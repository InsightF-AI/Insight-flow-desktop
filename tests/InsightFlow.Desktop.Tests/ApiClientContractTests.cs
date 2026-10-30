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
            """{"access_token":"new-test-token","refresh_token":"new-refresh-token","token_type":"bearer","expires_in":1800}""");
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
        Assert.Equal("new-refresh-token", result.RefreshToken);
        Assert.Equal(1800, result.ExpiresIn);
    }

    [Fact]
    public async Task Registration_sends_name_and_returns_the_token_without_an_extra_login_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"access_token":"registration-test-token","refresh_token":"registration-refresh-token","token_type":"bearer","expires_in":1800}""",
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
        Assert.Equal("registration-refresh-token", result.RefreshToken);
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
    public async Task Catalog_lists_registered_assets_with_their_internal_ids()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            $$"""[{"id":"{{AssetId}}","ticker":"BTC","nome":"Bitcoin","tipo":"CRIPTO","setor":null,"moeda":"BRL"}]""");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var catalog = await api.GetCatalogAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/ativos/catalogo", request.Uri.AbsolutePath);
        var asset = Assert.Single(catalog);
        Assert.Equal(AssetId, asset.Id);
        Assert.Equal("BTC", asset.Ticker);
        Assert.Null(asset.Setor);
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
    public async Task Technical_analysis_preserves_nullable_values_and_uses_documented_paths()
    {
        var ruleId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
        var signalId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000010");
        var responses = new Queue<HttpResponseMessage>(
        [
            RecordingHttpMessageHandler.Json("""[{"tipo":"RSI","parametros":{"periodo":14},"data_calculo":"2026-09-16T12:00:00Z","valor":"29.5","valores_auxiliares":null}]"""),
            RecordingHttpMessageHandler.Json($$"""[{"id":"{{signalId}}","ativo_id":"{{AssetId}}","regra_id":"{{ruleId}}","regra_nome":"Sobrevenda RSI","regra_descricao":"RSI abaixo de 30","data_ativacao":"2026-09-16T12:00:00Z","contexto":{"rsi":29.5},"data_desativacao":null}]"""),
            RecordingHttpMessageHandler.Json($$"""{"regra_id":"{{ruleId}}","ativo_id":"{{AssetId}}","total_ocorrencias":3,"retorno_medio_5_pregoes":null,"retorno_medio_20_pregoes":"1.25","retorno_medio_60_pregoes":null}""")
        ]);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(responses.Dequeue()));
        using var http = CreateHttpClient(handler);
        var session = new UserSession();
        session.Start("analysis-test-token");
        var api = new InsightFlowApiClient(http, session);

        var indicators = await api.GetIndicatorsAsync(AssetId);
        var signals = await api.GetSignalsAsync(AssetId);
        var backtest = await api.GetBacktestAsync(AssetId, ruleId);

        Assert.Equal(29.5m, Assert.Single(indicators).Valor);
        Assert.Null(Assert.Single(indicators).ValoresAuxiliares);
        Assert.Null(Assert.Single(signals).DataDesativacao);
        Assert.Null(backtest.RetornoMedio5Pregoes);
        Assert.Equal(1.25m, backtest.RetornoMedio20Pregoes);
        Assert.Equal($"/api/v1/ativos/{AssetId}/indicadores", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal($"/api/v1/ativos/{AssetId}/sinais", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal($"/api/v1/ativos/{AssetId}/sinais/{ruleId}/backtest", handler.Requests[2].Uri.AbsolutePath);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer analysis-test-token", request.Authorization));
    }

    [Fact]
    public async Task Creating_alert_sends_only_the_documented_fields()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(AlertJson(), HttpStatusCode.Created);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.CreateAlertAsync("PETR4", InsightFlowApiClient.PriceGreaterThanOrEqual, 42.50m);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/alertas", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal("PETR4", body.RootElement.GetProperty("ticker").GetString());
        Assert.Equal("PRECO_MAIOR_IGUAL", body.RootElement.GetProperty("tipo_condicao").GetString());
        Assert.Equal(42.50m, body.RootElement.GetProperty("valor_alvo").GetDecimal());
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Updating_alert_uses_ativo_for_enabled_state_and_omits_unchanged_fields()
    {
        var alertId = Guid.Parse("ee838914-2761-4f45-a8f3-f25bab79042f");
        var handler = RecordingHttpMessageHandler.ReturningJson(AlertJson(alertId));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.UpdateAlertAsync(alertId, ativo: false);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"/api/v1/alertas/{alertId}", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.False(body.RootElement.GetProperty("ativo").GetBoolean());
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Invalid_alert_value_is_rejected_before_sending_a_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(AlertJson());
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.CreateAlertAsync("PETR4", InsightFlowApiClient.PriceLessThanOrEqual, 0));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_empty_asset_uuid_is_rejected_before_sending_a_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson("[]");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await Assert.ThrowsAsync<ArgumentException>(() => api.GetQuoteAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetHistoryAsync(Guid.Empty, "1M"));
        await Assert.ThrowsAsync<ArgumentException>(() => api.RemoveFromWatchlistAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => api.SetNotificationsAsync(Guid.Empty, true));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deleting_alert_accepts_204_without_reading_json()
    {
        var alertId = Guid.Parse("ee838914-2761-4f45-a8f3-f25bab79042f");
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.DeleteAlertAsync(alertId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/api/v1/alertas/{alertId}", request.Uri.AbsolutePath);
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

    [Theory]
    [InlineData("""{"access_token":"","refresh_token":"refresh","token_type":"bearer","expires_in":1800}""")]
    [InlineData("""{"access_token":"access","refresh_token":"","token_type":"bearer","expires_in":1800}""")]
    [InlineData("""{"access_token":"access","refresh_token":"refresh","token_type":"bearer","expires_in":0}""")]
    // Formato anterior ao refresh token: sem ele a sessão cairia a cada vencimento do access token.
    [InlineData("""{"access_token":"access","token_type":"bearer"}""")]
    public async Task Login_with_an_empty_or_incomplete_token_pair_is_rejected(string body)
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(body);
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

    private static string AlertJson(Guid? alertId = null) => $$"""
        {
          "id": "{{alertId ?? Guid.Parse("ee838914-2761-4f45-a8f3-f25bab79042f")}}",
          "ativo": {
            "id": "{{AssetId}}",
            "ticker": "PETR4",
            "nome": "Petrobras",
            "tipo": "ACAO",
            "setor": "Petróleo",
            "moeda": "BRL"
          },
          "tipo_condicao": "PRECO_MAIOR_IGUAL",
          "valor_alvo": 42.50,
          "moeda_alvo": "BRL",
          "habilitado": true,
          "ultimo_estado": false,
          "disparado_em": null,
          "criado_em": "2026-09-16T12:00:00Z",
          "atualizado_em": "2026-09-16T12:00:00Z"
        }
        """;
}
