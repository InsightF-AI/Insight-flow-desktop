using System.Net;
using System.Text;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Tests;

public sealed class ApiClientContractTests
{
    private static readonly Guid AssetId = Guid.Parse("d8910231-79ac-4482-b74e-9f2922e4c102");

    [Fact]
    public async Task Login_sends_documented_credentials_without_reusing_a_previous_bearer_token()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"access_token":"new-test-token","token_type":"bearer"}""");
        using var http = CreateHttpClient(handler);
        var session = new UserSession();
        session.Start("previous-test-token");
        var api = new InsightFlowApiClient(http, session);

        var result = await api.LoginAsync("test@example.com", "test-password", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/auth/login", request.Uri.AbsolutePath);
        Assert.Null(request.Authorization);
        Assert.Equal("application/json", request.ContentType);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal("test@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("test-password", body.RootElement.GetProperty("senha").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        Assert.Equal("new-test-token", result.AccessToken);
    }

    [Fact]
    public async Task Registration_sends_name_and_returns_the_token_without_an_extra_login_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"access_token":"registration-test-token","token_type":"bearer"}""",
            HttpStatusCode.Created);
        using var http = CreateHttpClient(handler);
        var session = new UserSession();
        session.Start("previous-test-token");
        var api = new InsightFlowApiClient(http, session);

        var result = await api.RegisterAsync("Usuário Teste", "test@example.com", "test-password", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/usuarios", request.Uri.AbsolutePath);
        Assert.Null(request.Authorization);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal("Usuário Teste", body.RootElement.GetProperty("nome").GetString());
        Assert.Equal("test@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("test-password", body.RootElement.GetProperty("senha").GetString());
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        Assert.Equal("registration-test-token", result.AccessToken);
    }

    [Fact]
    public async Task Clearing_a_session_removes_bearer_from_subsequent_requests()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson("[]");
        using var http = CreateHttpClient(handler);
        var session = new UserSession();
        session.Start("first-user-test-token");
        var api = new InsightFlowApiClient(http, session);

        await api.GetWatchlistAsync(CancellationToken.None);
        session.Clear();
        await api.GetWatchlistAsync(CancellationToken.None);
        session.Start("second-user-test-token");
        await api.GetWatchlistAsync(CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("Bearer first-user-test-token", handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Equal("Bearer second-user-test-token", handler.Requests[2].Authorization);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/watchlist", request.Uri.AbsolutePath);
        });
    }

    [Fact]
    public async Task Search_encodes_the_entire_term_without_injecting_query_parameters()
    {
        const string term = "PETR4 & ação/ON?";
        var handler = RecordingHttpMessageHandler.ReturningJson("[]");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.SearchAssetsAsync(term, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https", request.Uri.Scheme);
        Assert.Equal("api.example.test", request.Uri.Host);
        Assert.Equal("/api/v1/ativos/buscar", request.Uri.AbsolutePath);
        Assert.Equal("?termo=" + Uri.EscapeDataString(term), request.Uri.Query);
    }

    [Fact]
    public async Task History_uses_asset_uuid_and_the_documented_period_literal()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson("[]");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.GetHistoryAsync(AssetId, "1A", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/api/v1/ativos/{AssetId}/historico", request.Uri.AbsolutePath);
        Assert.Equal("?periodo=1A", request.Uri.Query);
    }

    [Fact]
    public async Task Adding_watchlist_entry_sends_only_the_ticker()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(WatchlistItemJson(), HttpStatusCode.Created);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.AddToWatchlistAsync("PETR4", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/watchlist", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal("PETR4", body.RootElement.GetProperty("ticker").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Changing_watchlist_notifications_uses_the_asset_uuid_and_notificar_field()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(WatchlistItemJson());
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.SetNotificationsAsync(AssetId, false, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"/api/v1/watchlist/{AssetId}/notificacao", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.False(body.RootElement.GetProperty("notificar").GetBoolean());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Removing_watchlist_entry_accepts_204_without_attempting_to_read_json()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.RemoveFromWatchlistAsync(AssetId, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/api/v1/watchlist/{AssetId}", request.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData("{\"detail\":\"Ativo não encontrado\"}", "Ativo não encontrado")]
    [InlineData("{\"detail\":[{\"loc\":[\"body\",\"email\"],\"msg\":\"Email inválido\",\"type\":\"value_error\"}]}", "Email inválido")]
    public async Task Validation_errors_preserve_status_and_extract_message_from_both_detail_formats(
        string responseBody,
        string expectedMessage)
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(responseBody, HttpStatusCode.UnprocessableEntity);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.LoginAsync("test@example.com", "test-password", CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Contains(expectedMessage, error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Html_server_error_does_not_expose_internal_response_body_to_the_user()
    {
        const string internalMarker = "private-server-path-and-debug-trace";
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent($"<html>{internalMarker}</html>", Encoding.UTF8, "text/html")
            }));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetWatchlistAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.DoesNotContain(internalMarker, error.Message);
        Assert.DoesNotContain("<html>", error.Message);
    }

    [Fact]
    public async Task Quote_with_missing_required_fields_is_rejected_instead_of_becoming_zero_values()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson("{}");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.GetQuoteAsync(AssetId, CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Contains("formato incompatível", error.Message);
    }

    [Fact]
    public async Task Login_with_empty_token_is_rejected()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"access_token":"","token_type":"bearer"}""");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.LoginAsync("test@example.com", "test-password", CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Contains("formato incompatível", error.Message);
    }

    [Fact]
    public async Task Failed_post_is_not_retried_automatically()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"detail":"Serviço indisponível"}""", HttpStatusCode.ServiceUnavailable);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.AddToWatchlistAsync("PETR4", CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task Cancelling_an_inflight_post_propagates_cancellation_without_retry()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return RecordingHttpMessageHandler.Json("{}");
        });
        using var http = CreateHttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var api = new InsightFlowApiClient(http, new UserSession());

        var pending = api.AddToWatchlistAsync("PETR4", cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.example.test/")
    };

    private static string WatchlistItemJson() => $$"""
        {
          "ativo": {
            "id": "{{AssetId}}",
            "ticker": "PETR4",
            "nome": "Petrobras",
            "tipo": "ACAO",
            "setor": "Petróleo",
            "moeda": "BRL"
          },
          "notificar": false,
          "adicionado_em": "2026-09-16T12:00:00Z"
        }
        """;
}
