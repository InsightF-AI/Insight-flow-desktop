using System.Net;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Tests;

public sealed class SessionRenewalTests
{
    private const string RenewedTokens =
        """{"access_token":"access-2","refresh_token":"refresh-2","token_type":"bearer","expires_in":1800}""";

    [Fact]
    public async Task An_expired_access_token_is_renewed_and_the_request_repeated_once_within_the_same_login()
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request switch
        {
            { Uri.AbsolutePath: "/api/v1/auth/refresh" } => RecordingHttpMessageHandler.Json(RenewedTokens),
            { Authorization: "Bearer access-2" } => RecordingHttpMessageHandler.Json("[]"),
            _ => RecordingHttpMessageHandler.Json("""{"detail":"Token invalido"}""", HttpStatusCode.Unauthorized)
        }));
        using var http = CreateHttpClient(handler);
        var session = StartedSession();
        var login = session.Version;
        var api = new InsightFlowApiClient(http, session);

        Assert.Empty(await api.GetWatchlistAsync());

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("Bearer access-1", handler.Requests[0].Authorization);
        var refresh = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, refresh.Method);
        Assert.Null(refresh.Authorization);
        using var body = JsonDocument.Parse(Assert.IsType<string>(refresh.Body));
        Assert.Equal("refresh-1", body.RootElement.GetProperty("refresh_token").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("Bearer access-2", handler.Requests[2].Authorization);
        Assert.Equal("refresh-2", session.RefreshToken);
        // As telas abertas comparam a versão para saber se o login ainda é o mesmo.
        Assert.Equal(login, session.Version);
    }

    [Fact]
    public async Task Parallel_requests_share_a_single_refresh_because_the_backend_revokes_a_reused_refresh_token()
    {
        var refreshResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((request, _) => request switch
        {
            { Uri.AbsolutePath: "/api/v1/auth/refresh" } => refreshResponse.Task,
            { Authorization: "Bearer access-2" } => Task.FromResult(RecordingHttpMessageHandler.Json("[]")),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))
        });
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, StartedSession());

        Task[] requests = [api.GetWatchlistAsync(), api.GetAlertsAsync(), api.GetOperationsAsync()];
        refreshResponse.SetResult(RecordingHttpMessageHandler.Json(RenewedTokens));
        await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/auth/refresh");
        Assert.Equal(3, handler.Requests.Count(request => request.Authorization == "Bearer access-2"));
    }

    [Fact]
    public async Task A_refused_refresh_surfaces_the_401_without_repeating_the_request()
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
            request.Uri.AbsolutePath == "/api/v1/auth/refresh" ? """{"detail":"Refresh token invalido"}""" : """{"detail":"Token invalido"}""",
            HttpStatusCode.Unauthorized)));
        using var http = CreateHttpClient(handler);
        var session = StartedSession();
        var api = new InsightFlowApiClient(http, session);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetWatchlistAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(["/api/v1/watchlist", "/api/v1/auth/refresh"], handler.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.Equal("access-1", session.AccessToken);
    }

    [Fact]
    public async Task A_token_about_to_expire_is_renewed_before_the_request_is_sent()
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            RecordingHttpMessageHandler.Json(request.Uri.AbsolutePath == "/api/v1/auth/refresh" ? RenewedTokens : "[]")));
        using var http = CreateHttpClient(handler);
        var session = StartedSession(DateTimeOffset.UtcNow.AddSeconds(5));
        var api = new InsightFlowApiClient(http, session);

        await api.GetWatchlistAsync();

        Assert.Equal(["/api/v1/auth/refresh", "/api/v1/watchlist"], handler.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.Equal("Bearer access-2", handler.Requests[1].Authorization);
        Assert.True(session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(29));
    }

    [Fact]
    public async Task A_refresh_that_answers_after_logout_does_not_bring_the_session_back()
    {
        var refreshResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((_, _) => refreshResponse.Task);
        using var http = CreateHttpClient(handler);
        var session = StartedSession();
        var api = new InsightFlowApiClient(http, session);

        var renewal = api.RenewSessionAsync("access-1");
        session.Clear();
        refreshResponse.SetResult(RecordingHttpMessageHandler.Json(RenewedTokens));

        Assert.False(await renewal.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(session.AccessToken);
        Assert.Null(session.RefreshToken);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_without_sending_the_bearer()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, StartedSession());

        await api.LogoutAsync("refresh-1");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/auth/logout", request.Uri.AbsolutePath);
        Assert.Null(request.Authorization);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal("refresh-1", body.RootElement.GetProperty("refresh_token").GetString());
    }

    private static UserSession StartedSession(DateTimeOffset? expiresAt = null)
    {
        var session = new UserSession();
        session.Start("access-1", "refresh-1", expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(30));
        return session;
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.example.test/")
    };
}
