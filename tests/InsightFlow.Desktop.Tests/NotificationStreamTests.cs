using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Tests;

public sealed class NotificationStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const WebSocketCloseStatus Unauthenticated = (WebSocketCloseStatus)4401;
    private const string RenewedTokens =
        """{"access_token":"access-2","refresh_token":"refresh-2","token_type":"bearer","expires_in":1800}""";

    [Fact]
    public async Task Authenticates_in_the_first_message_and_signals_each_notification_but_not_pings()
    {
        using var server = new FakeNotificationServer();
        using var http = CreateHttpClient(new RecordingHttpMessageHandler((_, _) => throw new InvalidOperationException("Nenhuma chamada HTTP era esperada.")));
        var session = StartedSession(DateTimeOffset.UtcNow.AddMinutes(30));
        var stream = CreateStream(server, session, http);
        using var news = new SemaphoreSlim(0);
        using var cancellation = new CancellationTokenSource();

        var run = stream.RunAsync(() => news.Release(), cancellation.Token);
        var socket = await server.AcceptAsync();
        using (var first = await ReceiveAsync(socket))
        {
            Assert.Equal("autenticar", first.RootElement.GetProperty("tipo").GetString());
            Assert.Equal("access-1", first.RootElement.GetProperty("token").GetString());
        }

        // Antes da confirmação o cliente ainda não sincronizou nada.
        Assert.Equal(0, news.CurrentCount);
        await SendAsync(socket, """{"tipo":"autenticado"}""");
        Assert.True(await news.WaitAsync(Timeout));
        await SendAsync(socket, """{"tipo":"ping"}""");
        await SendAsync(socket, """{"tipo":"notificacao","notificacao":{"id":"5c3d1f7e-2a4b-4c6d-8e9f-0a1b2c3d4e5f","ativo_id":null,"tipo":"RESUMO_DIARIO","mensagem":"Resumo","contexto":{},"lida":false,"criado_em":"2026-10-07T21:30:00Z"}}""");
        Assert.True(await news.WaitAsync(Timeout));
        Assert.Equal(0, news.CurrentCount);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
    }

    [Fact]
    public async Task A_4401_close_renews_the_session_and_reconnects_with_the_new_token()
    {
        using var server = new FakeNotificationServer();
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(RenewedTokens)));
        using var http = CreateHttpClient(handler);
        // Sem validade conhecida, o cliente só descobre o vencimento pelo fechamento do servidor.
        var session = StartedSession(null);
        var stream = CreateStream(server, session, http);
        using var cancellation = new CancellationTokenSource();

        var run = stream.RunAsync(() => { }, cancellation.Token);
        var socket = await server.AcceptAsync();
        (await ReceiveAsync(socket)).Dispose();
        await SendAsync(socket, """{"tipo":"autenticado"}""");
        await socket.CloseAsync(Unauthenticated, "expirado", CancellationToken.None).WaitAsync(Timeout);

        var reconnected = await server.AcceptAsync();
        using (var first = await ReceiveAsync(reconnected))
        {
            Assert.Equal("access-2", first.RootElement.GetProperty("token").GetString());
        }
        var refresh = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/auth/refresh", refresh.Uri.AbsolutePath);
        Assert.Contains("refresh-1", refresh.Body);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
    }

    [Fact]
    public async Task A_refused_refresh_after_4401_stops_the_stream_and_asks_for_a_check_that_ends_the_session()
    {
        using var server = new FakeNotificationServer();
        using var http = CreateHttpClient(new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var stream = CreateStream(server, StartedSession(null), http);
        var signals = 0;

        var run = stream.RunAsync(() => Interlocked.Increment(ref signals), CancellationToken.None);
        var socket = await server.AcceptAsync();
        (await ReceiveAsync(socket)).Dispose();
        await socket.CloseAsync(Unauthenticated, "token invalido", CancellationToken.None).WaitAsync(Timeout);

        await run.WaitAsync(Timeout);
        Assert.Equal(1, signals);
    }

    [Fact]
    public async Task Before_the_token_expires_it_is_renewed_and_sent_again_on_the_same_connection()
    {
        using var server = new FakeNotificationServer();
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(RecordingHttpMessageHandler.Json(RenewedTokens)));
        using var http = CreateHttpClient(handler);
        var session = StartedSession(DateTimeOffset.UtcNow.AddMilliseconds(400));
        var stream = new NotificationStream(server.BaseUrl, session, new InsightFlowApiClient(http, session))
        {
            InitialRetryDelay = TimeSpan.FromMilliseconds(20),
            RenewalMargin = TimeSpan.FromMilliseconds(200)
        };
        using var cancellation = new CancellationTokenSource();

        var run = stream.RunAsync(() => { }, cancellation.Token);
        var socket = await server.AcceptAsync();
        (await ReceiveAsync(socket)).Dispose();
        await SendAsync(socket, """{"tipo":"autenticado"}""");

        using (var again = await ReceiveAsync(socket))
        {
            Assert.Equal("autenticar", again.RootElement.GetProperty("tipo").GetString());
            Assert.Equal("access-2", again.RootElement.GetProperty("token").GetString());
        }
        Assert.Single(handler.Requests);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(Timeout));
    }

    private static NotificationStream CreateStream(FakeNotificationServer server, UserSession session, HttpClient http) =>
        new(server.BaseUrl, session, new InsightFlowApiClient(http, session)) { InitialRetryDelay = TimeSpan.FromMilliseconds(20) };

    private static UserSession StartedSession(DateTimeOffset? expiresAt)
    {
        var session = new UserSession();
        session.Start("access-1", "refresh-1", expiresAt);
        return session;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.example.test/")
    };

    private static async Task<JsonDocument> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(Timeout);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return JsonDocument.Parse(buffer.AsMemory(0, result.Count));
    }

    private static Task SendAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None).WaitAsync(Timeout);

    // Servidor WebSocket local que faz o papel de /api/v1/notificacoes/ws do backend.
    private sealed class FakeNotificationServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public Uri BaseUrl { get; }

        public FakeNotificationServer()
        {
            using (var probe = new TcpListener(IPAddress.Loopback, 0))
            {
                probe.Start();
                BaseUrl = new Uri($"http://localhost:{((IPEndPoint)probe.LocalEndpoint).Port}/");
            }
            _listener.Prefixes.Add(BaseUrl.AbsoluteUri);
            _listener.Start();
        }

        public async Task<WebSocket> AcceptAsync()
        {
            var context = await _listener.GetContextAsync().WaitAsync(Timeout);
            Assert.True(context.Request.IsWebSocketRequest);
            Assert.Equal("/api/v1/notificacoes/ws", context.Request.Url!.AbsolutePath);
            // O token não pode vazar para a URL nem para cabeçalhos.
            Assert.Empty(context.Request.Url.Query);
            Assert.Null(context.Request.Headers["Authorization"]);
            return (await context.AcceptWebSocketAsync(null).WaitAsync(Timeout)).WebSocket;
        }

        public void Dispose() => _listener.Close();
    }
}
