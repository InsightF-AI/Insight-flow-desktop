using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Api;

public sealed class InsightFlowApiClient
{
    private const int MaximumErrorBodyBytes = 16 * 1024;
    public const string PriceGreaterThanOrEqual = "PRECO_MAIOR_IGUAL";
    public const string PriceLessThanOrEqual = "PRECO_MENOR_IGUAL";
    public const string OperationBuy = "COMPRA";
    public const string OperationSell = "VENDA";
    public const string BenchmarkCdi = "CDI";
    public const string BenchmarkIbovespa = "IBOVESPA";
    public const string ChatUser = "usuario";
    public const string ChatAssistant = "assistente";
    // Padrões do backend (chat_max_mensagens / chat_max_caracteres_mensagem); o servidor continua sendo a autoridade.
    public const int ChatMaxMessages = 20;
    public const int ChatMaxCharacters = 2000;
    // O access token é renovado um pouco antes de expirar, evitando um 401 a cada meia hora de uso.
    internal static readonly TimeSpan RenewalMargin = TimeSpan.FromSeconds(60);
    // O provedor de IA do backend leva de segundos a perto de um minuto; os prazos são os mesmos do cliente web.
    internal static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(70);
    internal static readonly TimeSpan ChatTimeout = TimeSpan.FromSeconds(90);
    private readonly HttpClient _httpClient;
    private readonly UserSession _session;
    private readonly TimeSpan? _requestTimeout;
    // O backend trata o reuso de um refresh token como roubo e revoga o login: só uma renovação por vez.
    private readonly SemaphoreSlim _renewalGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // Com "requestTimeout", o prazo é contado por chamada e as de IA ganham um prazo maior; o Timeout do
    // HttpClient vale para todas e deve então ser infinito. Sem ele, só o Timeout do HttpClient se aplica.
    public InsightFlowApiClient(HttpClient httpClient, UserSession session, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(session);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("Defina a URL base do servidor no HttpClient.", nameof(httpClient));
        }
        if (requestTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "O prazo das chamadas deve ser maior que zero.");
        }

        _httpClient = httpClient;
        _session = session;
        _requestTimeout = requestTimeout;
    }

    public Task CheckHealthAsync(CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Get, "/health", null, false, cancellationToken);

    public Task<TokenResponse> LoginAsync(string email, string senha, CancellationToken cancellationToken = default) =>
        SendAsync<TokenResponse>(HttpMethod.Post, "/api/v1/auth/login", new { email, senha }, false, cancellationToken);

    public Task<TokenResponse> RegisterAsync(string nome, string email, string senha, CancellationToken cancellationToken = default) =>
        SendAsync<TokenResponse>(HttpMethod.Post, "/api/v1/usuarios", new { nome, email, senha }, false, cancellationToken);

    // Revoga no servidor o login do refresh token informado. O access token já emitido vale até expirar.
    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return SendWithoutResponseAsync(HttpMethod.Post, "/api/v1/auth/logout", new { refresh_token = refreshToken }, false, cancellationToken);
    }

    // Garante que a sessão tenha um access token diferente de "staleAccessToken", renovando-o se preciso.
    // Falso significa que o login acabou (refresh recusado ou sessão encerrada); falhas de rede são lançadas.
    public async Task<bool> RenewSessionAsync(string staleAccessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staleAccessToken);
        using var deadline = Deadline(cancellationToken);
        if (deadline is not null) cancellationToken = deadline.Token;
        await _renewalGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _session.Snapshot();
            if (current.AccessToken is null) return false;
            // Outra chamada já renovou enquanto esta aguardava.
            if (current.AccessToken != staleAccessToken) return true;
            if (current.RefreshToken is null) return false;

            using var request = CreateRequest(HttpMethod.Post, "/api/v1/auth/refresh", new { refresh_token = current.RefreshToken }, null);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return false;
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var tokens = await ReadAsync<TokenResponse>(response, cancellationToken).ConfigureAwait(false);
            return _session.Renew(current.Version, tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn));
        }
        finally
        {
            _renewalGate.Release();
        }
    }

    public async Task<IReadOnlyList<SearchAssetResponse>> SearchAssetsAsync(string termo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(termo);
        return await SendAsync<List<SearchAssetResponse>>(
            HttpMethod.Get, $"/api/v1/ativos/buscar?termo={Uri.EscapeDataString(termo.Trim())}", null, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetResponse>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<AssetResponse>>(HttpMethod.Get, "/api/v1/ativos/catalogo", null, true, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<WatchlistItemResponse>> GetWatchlistAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<WatchlistItemResponse>>(HttpMethod.Get, "/api/v1/watchlist", null, true, cancellationToken).ConfigureAwait(false);

    public Task<WatchlistItemResponse> AddToWatchlistAsync(string ticker, CancellationToken cancellationToken = default) =>
        SendAsync<WatchlistItemResponse>(HttpMethod.Post, "/api/v1/watchlist", new { ticker }, true, cancellationToken);

    public Task RemoveFromWatchlistAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Delete,
            $"/api/v1/watchlist/{RequiredId(ativoId, nameof(ativoId)):D}", null, true, cancellationToken);

    public Task<WatchlistItemResponse> SetNotificationsAsync(Guid ativoId, bool notificar, CancellationToken cancellationToken = default) =>
        SendAsync<WatchlistItemResponse>(HttpMethod.Patch,
            $"/api/v1/watchlist/{RequiredId(ativoId, nameof(ativoId)):D}/notificacao", new { notificar }, true, cancellationToken);

    public Task<QuoteResponse> GetQuoteAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        SendAsync<QuoteResponse>(HttpMethod.Get,
            $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/cotacao", null, true, cancellationToken);

    public async Task<IReadOnlyList<HistoryPointResponse>> GetHistoryAsync(Guid ativoId, string periodo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodo);
        return await SendAsync<List<HistoryPointResponse>>(HttpMethod.Get,
            $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/historico?periodo={Uri.EscapeDataString(periodo)}",
            null, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IndicatorResponse>> GetIndicatorsAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        await SendAsync<List<IndicatorResponse>>(
            HttpMethod.Get, $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/indicadores", null, true, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SignalResponse>> GetSignalsAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        await SendAsync<List<SignalResponse>>(
            HttpMethod.Get, $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/sinais", null, true, cancellationToken).ConfigureAwait(false);

    public Task<BacktestResponse> GetBacktestAsync(Guid ativoId, Guid regraId, CancellationToken cancellationToken = default) =>
        SendAsync<BacktestResponse>(HttpMethod.Get,
            $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/sinais/{RequiredId(regraId, nameof(regraId)):D}/backtest",
            null, true, cancellationToken);

    public async Task<IReadOnlyList<AlertResponse>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<AlertResponse>>(HttpMethod.Get, "/api/v1/alertas", null, true, cancellationToken).ConfigureAwait(false);

    public Task<AlertResponse> CreateAlertAsync(
        string ticker,
        string tipoCondicao,
        decimal valorAlvo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ValidateAlertCondition(tipoCondicao);
        ValidateTargetValue(valorAlvo);
        return SendAsync<AlertResponse>(HttpMethod.Post, "/api/v1/alertas",
            new { ticker = ticker.Trim(), tipo_condicao = tipoCondicao, valor_alvo = valorAlvo }, true, cancellationToken);
    }

    public Task<AlertResponse> UpdateAlertAsync(
        Guid alertaId,
        string? tipoCondicao = null,
        decimal? valorAlvo = null,
        bool? ativo = null,
        CancellationToken cancellationToken = default)
    {
        RequiredId(alertaId, nameof(alertaId));
        if (tipoCondicao is null && valorAlvo is null && ativo is null)
        {
            throw new ArgumentException("Informe ao menos uma alteração para o alerta.");
        }

        var body = new Dictionary<string, object>();
        if (tipoCondicao is not null)
        {
            ValidateAlertCondition(tipoCondicao);
            body["tipo_condicao"] = tipoCondicao;
        }
        if (valorAlvo is not null)
        {
            ValidateTargetValue(valorAlvo.Value);
            body["valor_alvo"] = valorAlvo.Value;
        }
        if (ativo is not null) body["ativo"] = ativo.Value;

        return SendAsync<AlertResponse>(HttpMethod.Patch, $"/api/v1/alertas/{alertaId:D}", body, true, cancellationToken);
    }

    public Task DeleteAlertAsync(Guid alertaId, CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Delete,
            $"/api/v1/alertas/{RequiredId(alertaId, nameof(alertaId)):D}", null, true, cancellationToken);

    public async Task<IReadOnlyList<NotificationResponse>> GetNotificationsAsync(bool onlyUnread = false, CancellationToken cancellationToken = default) =>
        await SendAsync<List<NotificationResponse>>(HttpMethod.Get,
            onlyUnread ? "/api/v1/notificacoes?apenas_nao_lidas=true" : "/api/v1/notificacoes",
            null, true, cancellationToken).ConfigureAwait(false);

    public Task<NotificationResponse> MarkNotificationReadAsync(Guid notificacaoId, CancellationToken cancellationToken = default) =>
        SendAsync<NotificationResponse>(HttpMethod.Patch,
            $"/api/v1/notificacoes/{RequiredId(notificacaoId, nameof(notificacaoId)):D}/lida", null, true, cancellationToken);

    public Task<OperationResponse> RegisterOperationAsync(
        Guid ativoId,
        string tipo,
        decimal quantidade,
        decimal precoUnitario,
        DateOnly data,
        CancellationToken cancellationToken = default)
    {
        RequiredId(ativoId, nameof(ativoId));
        if (tipo is not OperationBuy and not OperationSell)
        {
            throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de operação não reconhecido.");
        }
        if (quantidade <= 0) throw new ArgumentOutOfRangeException(nameof(quantidade), "A quantidade deve ser maior que zero.");
        if (precoUnitario <= 0) throw new ArgumentOutOfRangeException(nameof(precoUnitario), "O preço unitário deve ser maior que zero.");
        // O backend compara com a data UTC atual.
        if (data > DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentOutOfRangeException(nameof(data), "A data da operação não pode ser futura.");

        return SendAsync<OperationResponse>(HttpMethod.Post, "/api/v1/operacoes",
            new { ativo_id = ativoId, tipo, quantidade, preco_unitario = precoUnitario, data }, true, cancellationToken);
    }

    public async Task<IReadOnlyList<OperationResponse>> GetOperationsAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<OperationResponse>>(HttpMethod.Get, "/api/v1/operacoes", null, true, cancellationToken).ConfigureAwait(false);

    public Task DeleteOperationAsync(Guid operacaoId, CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Delete,
            $"/api/v1/operacoes/{RequiredId(operacaoId, nameof(operacaoId)):D}", null, true, cancellationToken);

    public async Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<PositionResponse>>(HttpMethod.Get, "/api/v1/portfolio/posicoes", null, true, cancellationToken).ConfigureAwait(false);

    public Task<ProfitabilityResponse> GetProfitabilityAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ProfitabilityResponse>(HttpMethod.Get, "/api/v1/portfolio/rentabilidade", null, true, cancellationToken);

    public Task<BenchmarkComparisonResponse> GetBenchmarkComparisonAsync(string benchmark, CancellationToken cancellationToken = default)
    {
        if (benchmark is not BenchmarkCdi and not BenchmarkIbovespa)
        {
            throw new ArgumentOutOfRangeException(nameof(benchmark), "Benchmark não reconhecido.");
        }
        return SendAsync<BenchmarkComparisonResponse>(HttpMethod.Get, $"/api/v1/portfolio/benchmark?benchmark={benchmark}", null, true, cancellationToken);
    }

    public Task<DistributionResponse> GetDistributionAsync(CancellationToken cancellationToken = default) =>
        SendAsync<DistributionResponse>(HttpMethod.Get, "/api/v1/portfolio/distribuicao", null, true, cancellationToken);

    public Task<AiAnalysisResponse> GetAiAnalysisAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        SendAsync<AiAnalysisResponse>(HttpMethod.Get,
            $"/api/v1/ativos/{RequiredId(ativoId, nameof(ativoId)):D}/analise", null, true, cancellationToken, AnalysisTimeout);

    public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> mensagens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mensagens);
        if (mensagens.Count == 0) throw new ArgumentException("Envie ao menos uma mensagem.", nameof(mensagens));
        if (mensagens.Count > ChatMaxMessages) throw new ArgumentException($"Envie no máximo {ChatMaxMessages} mensagens.", nameof(mensagens));
        if (mensagens[^1].Papel != ChatUser) throw new ArgumentException("A última mensagem deve ser do usuário.", nameof(mensagens));
        foreach (var mensagem in mensagens)
        {
            if (mensagem.Papel is not ChatUser and not ChatAssistant) throw new ArgumentException("Papel de mensagem não reconhecido.", nameof(mensagens));
            if (string.IsNullOrWhiteSpace(mensagem.Texto)) throw new ArgumentException("As mensagens não podem ser vazias.", nameof(mensagens));
            if (mensagem.Texto.Length > ChatMaxCharacters) throw new ArgumentException($"Cada mensagem deve ter até {ChatMaxCharacters} caracteres.", nameof(mensagens));
        }

        return SendAsync<ChatResponse>(HttpMethod.Post, "/api/v1/chat", new { mensagens }, true, cancellationToken, ChatTimeout);
    }

    // Prazo da chamada inteira, da requisição à leitura da resposta. "minimum" alonga o das chamadas de IA.
    private CancellationTokenSource? Deadline(CancellationToken cancellationToken, TimeSpan minimum = default)
    {
        if (_requestTimeout is not { } timeout) return null;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout > minimum ? timeout : minimum);
        return source;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    // Rotas autenticadas renovam o access token perto do vencimento e, diante de um 401, renovam e repetem uma única vez.
    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        if (!authenticated) return await SendOnceAsync(method, path, body, null, cancellationToken).ConfigureAwait(false);

        var session = _session.Snapshot();
        var renewable = session is { AccessToken: not null, RefreshToken: not null };
        if (renewable && session.ExpiresAt - DateTimeOffset.UtcNow < RenewalMargin)
        {
            renewable = await RenewSessionAsync(session.AccessToken!, cancellationToken).ConfigureAwait(false);
        }

        var token = _session.AccessToken;
        var response = await SendOnceAsync(method, path, body, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !renewable || token is null) return response;

        try
        {
            if (!await RenewSessionAsync(token, cancellationToken).ConfigureAwait(false)) return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }

        response.Dispose();
        return await SendOnceAsync(method, path, body, _session.AccessToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body, token);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken, TimeSpan minimumTimeout = default)
    {
        using var deadline = Deadline(cancellationToken, minimumTimeout);
        if (deadline is not null) cancellationToken = deadline.Token;
        using var response = await SendRequestAsync(method, path, body, authenticated, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                throw new ApiException(response.StatusCode, "A API retornou uma resposta vazia ou incompatível.");
            }

            ValidateContract(result, response.StatusCode);
            return result;
        }
        catch (JsonException)
        {
            throw new ApiException(response.StatusCode, "A API retornou dados em um formato incompatível.");
        }
    }

    private static void ValidateContract<T>(T result, HttpStatusCode statusCode)
    {
        var valid = result switch
        {
            TokenResponse token => ValidToken(token.AccessToken) && ValidToken(token.RefreshToken) && token.ExpiresIn > 0 &&
                                   string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase),
            WatchlistItemResponse item => ValidWatchlistItem(item),
            List<WatchlistItemResponse> items => items.All(ValidWatchlistItem),
            SearchAssetResponse asset => ValidSearchAsset(asset),
            List<SearchAssetResponse> assets => assets.All(ValidSearchAsset),
            QuoteResponse quote => !string.IsNullOrWhiteSpace(quote.Ticker),
            List<IndicatorResponse> indicators => indicators.All(ValidIndicator),
            List<SignalResponse> signals => signals.All(ValidSignal),
            BacktestResponse backtest => backtest.RegraId != Guid.Empty && backtest.AtivoId != Guid.Empty && backtest.TotalOcorrencias >= 0,
            AlertResponse alert => ValidAlert(alert),
            List<AlertResponse> alerts => alerts.All(ValidAlert),
            NotificationResponse notification => ValidNotification(notification),
            List<NotificationResponse> notifications => notifications.All(ValidNotification),
            OperationResponse operation => ValidOperation(operation),
            List<OperationResponse> operations => operations.All(ValidOperation),
            List<PositionResponse> positions => positions.All(position => position.AtivoId != Guid.Empty && !string.IsNullOrWhiteSpace(position.Ticker)),
            BenchmarkComparisonResponse comparison => comparison.Benchmark is BenchmarkCdi or BenchmarkIbovespa,
            AiAnalysisResponse analysis => analysis.AtivoId != Guid.Empty && !string.IsNullOrWhiteSpace(analysis.Texto) && !string.IsNullOrWhiteSpace(analysis.AvisoLegal),
            ChatResponse chat => !string.IsNullOrWhiteSpace(chat.Texto) && !string.IsNullOrWhiteSpace(chat.AvisoLegal),
            _ => true
        };

        if (!valid)
        {
            throw new ApiException(statusCode, "A API retornou dados em um formato incompatível.");
        }
    }

    private static bool ValidToken(string token) => !string.IsNullOrWhiteSpace(token) && !token.Any(char.IsWhiteSpace);

    private static bool ValidSearchAsset(SearchAssetResponse asset) =>
        !string.IsNullOrWhiteSpace(asset.Ticker) &&
        !string.IsNullOrWhiteSpace(asset.Nome) &&
        !string.IsNullOrWhiteSpace(asset.Tipo) &&
        !string.IsNullOrWhiteSpace(asset.Moeda);

    private static bool ValidWatchlistItem(WatchlistItemResponse item) =>
        item.Ativo is not null &&
        item.Ativo.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(item.Ativo.Ticker) &&
        !string.IsNullOrWhiteSpace(item.Ativo.Nome) &&
        !string.IsNullOrWhiteSpace(item.Ativo.Tipo) &&
        !string.IsNullOrWhiteSpace(item.Ativo.Moeda);

    private static bool ValidIndicator(IndicatorResponse indicator) =>
        !string.IsNullOrWhiteSpace(indicator.Tipo) &&
        indicator.Parametros.ValueKind == JsonValueKind.Object &&
        indicator.ValoresAuxiliares is null or { ValueKind: JsonValueKind.Object };

    private static bool ValidSignal(SignalResponse signal) =>
        signal.Id != Guid.Empty && signal.AtivoId != Guid.Empty && signal.RegraId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(signal.RegraNome) &&
        !string.IsNullOrWhiteSpace(signal.RegraDescricao) &&
        signal.Contexto.ValueKind == JsonValueKind.Object;

    private static bool ValidAlert(AlertResponse alert) =>
        alert.Id != Guid.Empty && alert.Ativo is not null && alert.Ativo.Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(alert.Ativo.Ticker) &&
        (alert.TipoCondicao is PriceGreaterThanOrEqual or PriceLessThanOrEqual) &&
        alert.ValorAlvo > 0 && !string.IsNullOrWhiteSpace(alert.MoedaAlvo);

    private static bool ValidNotification(NotificationResponse notification) =>
        notification.Id != Guid.Empty && !string.IsNullOrWhiteSpace(notification.Tipo) &&
        !string.IsNullOrWhiteSpace(notification.Mensagem) && notification.Contexto.ValueKind == JsonValueKind.Object;

    private static bool ValidOperation(OperationResponse operation) =>
        operation.Id != Guid.Empty && operation.AtivoId != Guid.Empty &&
        (operation.Tipo is OperationBuy or OperationSell) && operation.Quantidade > 0 && operation.PrecoUnitario > 0;

    private static Guid RequiredId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("O identificador deve ser um UUID válido.", parameterName) : id;

    private static void ValidateAlertCondition(string condition)
    {
        if (condition is not PriceGreaterThanOrEqual and not PriceLessThanOrEqual)
        {
            throw new ArgumentOutOfRangeException(nameof(condition), "Condição de alerta não reconhecida.");
        }
    }

    private static void ValidateTargetValue(decimal value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "O preço-alvo deve ser maior que zero.");
    }

    private async Task SendWithoutResponseAsync(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        using var deadline = Deadline(cancellationToken);
        if (deadline is not null) cancellationToken = deadline.Token;
        using var response = await SendRequestAsync(method, path, body, authenticated, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = DefaultErrorMessage(response.StatusCode);
        // Falhas do servidor podem trazer detalhes internos: só os erros de negócio e de validação são exibidos.
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            message = await ReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false) ?? message;
        }

        throw new ApiException(response.StatusCode, message, RetryAfter(response));
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static async Task<string?> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaximumErrorBodyBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            length += count;
        }

        if (length == 0 || length > MaximumErrorBodyBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("detail", out var detail))
            {
                return null;
            }

            if (detail.ValueKind == JsonValueKind.String)
            {
                return SafeDetail(detail.GetString());
            }

            if (detail.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var messages = new List<string>();
            foreach (var issue in detail.EnumerateArray())
            {
                if (issue.ValueKind != JsonValueKind.Object ||
                    !issue.TryGetProperty("msg", out var issueMessage) ||
                    issueMessage.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = SafeDetail(issueMessage.GetString());
                if (text is null)
                {
                    continue;
                }

                var field = ValidationField(issue);
                messages.Add(field is null ? text : $"{field}: {text}");
                if (messages.Count == 3)
                {
                    break;
                }
            }

            return messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? SafeDetail(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 300 ||
            message.IndexOfAny(['<', '>']) >= 0 || message.Any(char.IsControl))
        {
            return null;
        }

        return message.Trim();
    }

    private static string? ValidationField(JsonElement issue)
    {
        if (!issue.TryGetProperty("loc", out var location) || location.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var part in location.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var label = part.GetString() switch
            {
                "nome" => "Nome",
                "email" => "E-mail",
                "senha" => "Senha",
                "ticker" => "Ticker",
                "termo" => "Busca",
                "periodo" => "Período",
                "valor_alvo" => "Preço-alvo",
                "tipo_condicao" => "Condição",
                "notificar" => "Notificações",
                "ativo_id" => "Ativo",
                "tipo" => "Tipo",
                "quantidade" => "Quantidade",
                "preco_unitario" => "Preço unitário",
                "data" => "Data",
                "benchmark" => "Benchmark",
                "mensagens" => "Mensagens",
                _ => null
            };
            if (label is not null)
            {
                return label;
            }
        }

        return null;
    }

    private static string DefaultErrorMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "As credenciais ou a sessão não são válidas. Faça login novamente.",
        HttpStatusCode.Forbidden => "A API não autorizou esta solicitação. Verifique sua sessão.",
        HttpStatusCode.NotFound => "O recurso solicitado não foi encontrado.",
        HttpStatusCode.Conflict => "Este cadastro ou item já existe.",
        HttpStatusCode.UnprocessableEntity => "Confira os dados informados e tente novamente.",
        HttpStatusCode.TooManyRequests => "Muitas solicitações. Aguarde um pouco e tente novamente.",
        HttpStatusCode.BadGateway => "O servidor não conseguiu gerar uma resposta válida. Tente novamente.",
        HttpStatusCode.ServiceUnavailable => "O serviço está indisponível. Tente novamente mais tarde.",
        _ => "Não foi possível concluir a solicitação à API. Tente novamente."
    };
}
