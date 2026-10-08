using System.Net.WebSockets;
using System.Text.Json;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Api;

// Canal de tempo real das notificações (WS /api/v1/notificacoes/ws). Ele apenas avisa que há novidade:
// o servidor não reenvia o que chegou com a conexão caída, então quem consome consulta as não lidas a cada aviso.
public sealed class NotificationStream
{
    // Código de fechamento do backend para token ausente, inválido ou expirado.
    private const int Unauthenticated = 4401;
    private const int MaximumMessageBytes = 64 * 1024;
    private readonly Uri _endpoint;
    private readonly UserSession _session;
    private readonly InsightFlowApiClient _api;
    private volatile string? _sentToken;

    // O servidor envia um ping a cada 30 s; silêncio maior que isto indica conexão morta.
    internal TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(90);
    internal TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);
    internal TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    internal TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(60);
    internal TimeSpan RenewalMargin { get; init; } = InsightFlowApiClient.RenewalMargin;

    public NotificationStream(Uri baseUrl, UserSession session, InsightFlowApiClient api)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(api);
        _endpoint = new UriBuilder(baseUrl)
        {
            Scheme = baseUrl.Scheme == Uri.UriSchemeHttps ? Uri.UriSchemeWss : Uri.UriSchemeWs,
            Path = "/api/v1/notificacoes/ws"
        }.Uri;
        _session = session;
        _api = api;
    }

    // Mantém a conexão do login atual até o cancelamento ou o fim da sessão, reconectando com espera crescente.
    // "onNews" é chamado fora da thread de interface, a cada (re)conexão autenticada e a cada notificação recebida.
    public async Task RunAsync(Action onNews, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onNews);
        var version = _session.Version;
        var delay = InitialRetryDelay;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = _session.Snapshot();
            if (session.Version != version || session.AccessToken is null) return;

            var (authenticated, rejected) = await ListenAsync(
                session.AccessToken, session.ExpiresAt, version, onNews, cancellationToken).ConfigureAwait(false);
            if (rejected && !await RenewAfterRejectionAsync(cancellationToken).ConfigureAwait(false))
            {
                // O login acabou: a consulta seguinte recebe 401 e a tela encerra a sessão.
                onNews();
                return;
            }

            delay = authenticated ? InitialRetryDelay : delay;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            if (!authenticated && delay < MaximumRetryDelay) delay = delay * 2 < MaximumRetryDelay ? delay * 2 : MaximumRetryDelay;
        }
    }

    private async Task<bool> RenewAfterRejectionAsync(CancellationToken cancellationToken)
    {
        if (_sentToken is not { } sent) return true;
        try
        {
            return await _api.RenewSessionAsync(sent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            // Sem rede não dá para saber se o login acabou; a próxima tentativa de conexão decide.
            return true;
        }
    }

    private async Task<(bool Authenticated, bool Rejected)> ListenAsync(
        string token, DateTimeOffset? expiresAt, int version, Action onNews, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var authenticated = false;
        var renewal = Task.CompletedTask;
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(_endpoint, timeout.Token).ConfigureAwait(false);
            }

            await AuthenticateAsync(socket, token, cancellationToken).ConfigureAwait(false);
            while (await ReceiveTypeAsync(socket, cancellationToken).ConfigureAwait(false) is { } type)
            {
                if (type == "autenticado" && !authenticated)
                {
                    authenticated = true;
                    renewal = KeepAuthenticatedAsync(socket, expiresAt, version, connection.Token);
                    onNews();
                }
                else if (type == "notificacao" && authenticated)
                {
                    onNews();
                }
            }
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken)) { }
        finally
        {
            // A renovação é a única outra origem de envios; ela termina antes de a conexão ser fechada.
            connection.Cancel();
            await renewal.ConfigureAwait(false);
        }

        var rejected = (int?)socket.CloseStatus == Unauthenticated;
        if (socket.State == WebSocketState.CloseReceived)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException) { }
        }

        return (authenticated, rejected);
    }

    // Antes de o access token enviado expirar, renova-o e autentica de novo na mesma conexão.
    private async Task KeepAuthenticatedAsync(ClientWebSocket socket, DateTimeOffset? expiresAt, int version, CancellationToken cancellationToken)
    {
        try
        {
            while (expiresAt is { } expiration)
            {
                var wait = expiration - DateTimeOffset.UtcNow - RenewalMargin;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                var sent = _sentToken!;
                if (!await _api.RenewSessionAsync(sent, cancellationToken).ConfigureAwait(false)) return;
                var session = _session.Snapshot();
                if (session.Version != version || session.AccessToken is null || session.AccessToken == sent) return;
                await AuthenticateAsync(socket, session.AccessToken, cancellationToken).ConfigureAwait(false);
                expiresAt = session.ExpiresAt;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or HttpRequestException or ApiException)
        {
            // Se a renovação falhar, o servidor fecha com 4401 no vencimento e RunAsync trata a reconexão.
        }
    }

    private async Task AuthenticateAsync(ClientWebSocket socket, string token, CancellationToken cancellationToken)
    {
        // O token vai na primeira mensagem, nunca na URL nem em cabeçalho.
        var message = JsonSerializer.SerializeToUtf8Bytes(new { tipo = "autenticar", token });
        await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        _sentToken = token;
    }

    // Devolve o "tipo" da próxima mensagem, texto vazio para mensagens ignoradas e nulo quando o servidor fecha.
    private async Task<string?> ReceiveTypeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        silence.CancelAfter(SilenceTimeout);
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), silence.Token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (message.Length <= MaximumMessageBytes) message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            if (result.MessageType != WebSocketMessageType.Text || message.Length > MaximumMessageBytes) return string.Empty;

            try
            {
                using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
                return document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("tipo", out var type) &&
                       type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }
    }

    // Falhas de rede e tempos esgotados levam a uma nova tentativa; o cancelamento pedido por quem chamou não.
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        WebSocketException or HttpRequestException or ApiException or IOException => true,
        _ => false
    };
}
