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
    private readonly HttpClient _httpClient;
    private readonly UserSession _session;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public InsightFlowApiClient(HttpClient httpClient, UserSession session)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(session);
        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException("Defina a URL base do servidor no HttpClient.", nameof(httpClient));
        }

        _httpClient = httpClient;
        _session = session;
    }

    public Task CheckHealthAsync(CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Get, "/health", null, false, cancellationToken);

    public Task<TokenResponse> LoginAsync(string email, string senha, CancellationToken cancellationToken = default) =>
        SendAsync<TokenResponse>(HttpMethod.Post, "/api/v1/auth/login", new { email, senha }, false, cancellationToken);

    public Task<TokenResponse> RegisterAsync(string nome, string email, string senha, CancellationToken cancellationToken = default) =>
        SendAsync<TokenResponse>(HttpMethod.Post, "/api/v1/usuarios", new { nome, email, senha }, false, cancellationToken);

    public async Task<IReadOnlyList<SearchAssetResponse>> SearchAssetsAsync(string termo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(termo);
        return await SendAsync<List<SearchAssetResponse>>(
            HttpMethod.Get, $"/api/v1/ativos/buscar?termo={Uri.EscapeDataString(termo.Trim())}", null, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WatchlistItemResponse>> GetWatchlistAsync(CancellationToken cancellationToken = default) =>
        await SendAsync<List<WatchlistItemResponse>>(HttpMethod.Get, "/api/v1/watchlist", null, true, cancellationToken).ConfigureAwait(false);

    public Task<WatchlistItemResponse> AddToWatchlistAsync(string ticker, CancellationToken cancellationToken = default) =>
        SendAsync<WatchlistItemResponse>(HttpMethod.Post, "/api/v1/watchlist", new { ticker }, true, cancellationToken);

    public Task RemoveFromWatchlistAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(HttpMethod.Delete, $"/api/v1/watchlist/{ativoId:D}", null, true, cancellationToken);

    public Task<WatchlistItemResponse> SetNotificationsAsync(Guid ativoId, bool notificar, CancellationToken cancellationToken = default) =>
        SendAsync<WatchlistItemResponse>(HttpMethod.Patch, $"/api/v1/watchlist/{ativoId:D}/notificacao", new { notificar }, true, cancellationToken);

    public Task<QuoteResponse> GetQuoteAsync(Guid ativoId, CancellationToken cancellationToken = default) =>
        SendAsync<QuoteResponse>(HttpMethod.Get, $"/api/v1/ativos/{ativoId:D}/cotacao", null, true, cancellationToken);

    public async Task<IReadOnlyList<HistoryPointResponse>> GetHistoryAsync(Guid ativoId, string periodo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(periodo);
        return await SendAsync<List<HistoryPointResponse>>(
            HttpMethod.Get, $"/api/v1/ativos/{ativoId:D}/historico?periodo={Uri.EscapeDataString(periodo)}", null, true, cancellationToken).ConfigureAwait(false);
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

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body, bool authenticated)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var token = _session.AccessToken;
        if (authenticated && !string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, body, authenticated);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

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
            TokenResponse token => !string.IsNullOrWhiteSpace(token.AccessToken) &&
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
            _ => true
        };

        if (!valid)
        {
            throw new ApiException(statusCode, "A API retornou dados em um formato incompatível.");
        }
    }

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
        using var request = CreateRequest(method, path, body, authenticated);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = DefaultErrorMessage(response.StatusCode);
        // Server failures may contain internal details. Only business/validation errors are presented.
        if ((int)response.StatusCode is >= 400 and < 500)
        {
            message = await ReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false) ?? message;
        }

        throw new ApiException(response.StatusCode, message);
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
        HttpStatusCode.ServiceUnavailable => "O serviço está indisponível. Tente novamente mais tarde.",
        _ => "Não foi possível concluir a solicitação à API. Tente novamente."
    };
}
