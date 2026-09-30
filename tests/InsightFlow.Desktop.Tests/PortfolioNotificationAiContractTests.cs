using System.Net;
using System.Text;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Tests;

public sealed class PortfolioNotificationAiContractTests
{
    private static readonly Guid AssetId = Guid.Parse("d8910231-79ac-4482-b74e-9f2922e4c102");
    private static readonly Guid NotificationId = Guid.Parse("5c3d1f7e-2a4b-4c6d-8e9f-0a1b2c3d4e5f");
    private static readonly Guid OperationId = Guid.Parse("7e2a9c4b-1f06-4d3c-9b8a-6f5e4d3c2b1a");

    [Fact]
    public async Task Alert_state_is_read_as_the_boolean_sent_by_the_backend()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson($$"""
            [{"id":"ee838914-2761-4f45-a8f3-f25bab79042f",
              "ativo":{"id":"{{AssetId}}","ticker":"PETR4","nome":"Petrobras","tipo":"ACAO","setor":null,"moeda":"BRL"},
              "tipo_condicao":"PRECO_MAIOR_IGUAL","valor_alvo":"42.50","moeda_alvo":"BRL","habilitado":true,
              "ultimo_estado":true,"disparado_em":"2026-09-29T15:00:00Z",
              "criado_em":"2026-09-16T12:00:00Z","atualizado_em":"2026-09-29T15:00:00Z"}]
            """);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var alert = Assert.Single(await api.GetAlertsAsync());

        Assert.True(alert.UltimoEstado);
        Assert.Equal(42.50m, alert.ValorAlvo);
    }

    [Fact]
    public async Task Notifications_filter_unread_and_preserve_optional_asset_and_context()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson($$"""
            [{"id":"{{NotificationId}}","ativo_id":null,"tipo":"RESUMO_DIARIO","mensagem":"Resumo diario da carteira",
              "contexto":{"texto":"Resumo","aviso_legal":"Aviso"},"lida":false,"criado_em":"2026-09-29T21:30:00Z"}]
            """);
        using var http = CreateHttpClient(handler);
        var session = new UserSession();
        session.Start("notification-test-token");
        var api = new InsightFlowApiClient(http, session);

        var notification = Assert.Single(await api.GetNotificationsAsync(onlyUnread: true));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/notificacoes", request.Uri.AbsolutePath);
        Assert.Equal("?apenas_nao_lidas=true", request.Uri.Query);
        Assert.Equal("Bearer notification-test-token", request.Authorization);
        Assert.Null(notification.AtivoId);
        Assert.Equal("Resumo", notification.Contexto.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task Marking_notification_as_read_uses_patch_without_a_body()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson($$"""
            {"id":"{{NotificationId}}","ativo_id":"{{AssetId}}","tipo":"ALERTA_DISPARADO","mensagem":"PETR4 atingiu o alvo",
             "contexto":{"alerta_id":"ee838914-2761-4f45-a8f3-f25bab79042f","preco":"43.10"},"lida":true,"criado_em":"2026-09-29T15:00:00Z"}
            """);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var result = await api.MarkNotificationReadAsync(NotificationId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal($"/api/v1/notificacoes/{NotificationId}/lida", request.Uri.AbsolutePath);
        Assert.Null(request.Body);
        Assert.True(result.Lida);
    }

    [Fact]
    public async Task Registering_operation_sends_the_documented_fields_with_an_iso_date()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(OperationJson(), HttpStatusCode.Created);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var result = await api.RegisterOperationAsync(AssetId, InsightFlowApiClient.OperationBuy, 10, 38.25m, new DateOnly(2026, 9, 15));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/operacoes", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal(AssetId, body.RootElement.GetProperty("ativo_id").GetGuid());
        Assert.Equal("COMPRA", body.RootElement.GetProperty("tipo").GetString());
        Assert.Equal(10m, body.RootElement.GetProperty("quantidade").GetDecimal());
        Assert.Equal(38.25m, body.RootElement.GetProperty("preco_unitario").GetDecimal());
        Assert.Equal("2026-09-15", body.RootElement.GetProperty("data").GetString());
        Assert.Equal(5, body.RootElement.EnumerateObject().Count());
        Assert.Equal(new DateOnly(2026, 9, 15), result.Data);
    }

    [Fact]
    public async Task Future_or_empty_operations_are_rejected_before_sending_a_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(OperationJson());
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.RegisterOperationAsync(AssetId, InsightFlowApiClient.OperationSell, 1, 10, tomorrow));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.RegisterOperationAsync(AssetId, InsightFlowApiClient.OperationSell, 0, 10, new DateOnly(2026, 9, 1)));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deleting_operation_accepts_204_without_reading_json()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await api.DeleteOperationAsync(OperationId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/api/v1/operacoes/{OperationId}", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Portfolio_reads_use_documented_paths_and_keep_unavailable_benchmark_as_null()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            RecordingHttpMessageHandler.Json($$"""
                [{"ativo_id":"{{AssetId}}","ticker":"PETR4","quantidade":"10","preco_medio":"38.25","cotacao_atual":"40.00",
                  "valor_mercado":"400.00","valor_mercado_brl":"400.00","lucro_nao_realizado":"17.50","lucro_realizado":"0"}]
                """),
            RecordingHttpMessageHandler.Json("""
                {"custo_base_brl":"382.50","valor_mercado_brl":"400.00","lucro_nao_realizado_brl":"17.50","lucro_realizado_brl":"0","percentual":"0.0457"}
                """),
            RecordingHttpMessageHandler.Json("""
                {"benchmark":"CDI","rentabilidade_carteira_percentual":"0.0457","rentabilidade_benchmark_percentual":null}
                """),
            RecordingHttpMessageHandler.Json("""
                {"por_classe":{"ACAO":"1"},"por_setor":{"Energia":"0.75","N/A":"0.25"},"por_moeda":{"BRL":"1"}}
                """)
        ]);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(responses.Dequeue()));
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var position = Assert.Single(await api.GetPositionsAsync());
        var profitability = await api.GetProfitabilityAsync();
        var comparison = await api.GetBenchmarkComparisonAsync(InsightFlowApiClient.BenchmarkCdi);
        var distribution = await api.GetDistributionAsync();

        Assert.Equal(17.50m, position.LucroNaoRealizado);
        Assert.Equal(0.0457m, profitability.Percentual);
        Assert.Null(comparison.RentabilidadeBenchmarkPercentual);
        Assert.Equal(0.75m, distribution.PorSetor["Energia"]);
        Assert.Equal("/api/v1/portfolio/posicoes", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("/api/v1/portfolio/rentabilidade", handler.Requests[1].Uri.AbsolutePath);
        Assert.Equal("/api/v1/portfolio/benchmark", handler.Requests[2].Uri.AbsolutePath);
        Assert.Equal("?benchmark=CDI", handler.Requests[2].Uri.Query);
        Assert.Equal("/api/v1/portfolio/distribuicao", handler.Requests[3].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Ai_analysis_uses_the_asset_uuid_and_requires_the_legal_notice()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson($$"""
            {"ativo_id":"{{AssetId}}","texto":"Análise","gerado_em":"2026-09-29T12:00:00Z","modelo":"gemini-2.5-flash","em_cache":true,"aviso_legal":""}
            """);
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetAiAnalysisAsync(AssetId));

        Assert.Equal($"/api/v1/ativos/{AssetId}/analise", Assert.Single(handler.Requests).Uri.AbsolutePath);
        Assert.Contains("formato incompatível", error.Message);
    }

    [Fact]
    public async Task Chat_sends_the_conversation_with_backend_roles()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson(
            """{"texto":"Resposta","modelo":"gemini-2.5-flash","aviso_legal":"Aviso"}""");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var result = await api.ChatAsync(
        [
            new ChatMessage(InsightFlowApiClient.ChatUser, "Olá"),
            new ChatMessage(InsightFlowApiClient.ChatAssistant, "Como posso ajudar?"),
            new ChatMessage(InsightFlowApiClient.ChatUser, "Como está minha carteira?")
        ]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/chat", request.Uri.AbsolutePath);
        using var body = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        var messages = body.RootElement.GetProperty("mensagens").EnumerateArray().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Equal("assistente", messages[1].GetProperty("papel").GetString());
        Assert.Equal("Como está minha carteira?", messages[2].GetProperty("texto").GetString());
        Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("Resposta", result.Texto);
    }

    [Fact]
    public async Task Invalid_chat_conversations_are_rejected_before_sending_a_request()
    {
        var handler = RecordingHttpMessageHandler.ReturningJson("{}");
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            api.ChatAsync([new ChatMessage(InsightFlowApiClient.ChatAssistant, "Sem pergunta")]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            api.ChatAsync([new ChatMessage(InsightFlowApiClient.ChatUser, new string('a', InsightFlowApiClient.ChatMaxCharacters + 1))]));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            api.ChatAsync(Enumerable.Repeat(new ChatMessage(InsightFlowApiClient.ChatUser, "Oi"), InsightFlowApiClient.ChatMaxMessages + 1).ToArray()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ai_quota_error_keeps_status_and_retry_after_without_exposing_server_detail()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"detail":"Cota do provedor de IA excedida"}""", Encoding.UTF8, "application/json")
            };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });
        using var http = CreateHttpClient(handler);
        var api = new InsightFlowApiClient(http, new UserSession());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            api.ChatAsync([new ChatMessage(InsightFlowApiClient.ChatUser, "Oi")]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://api.example.test/")
    };

    private static string OperationJson() => $$"""
        {"id":"{{OperationId}}","ativo_id":"{{AssetId}}","tipo":"COMPRA","quantidade":"10","preco_unitario":"38.25",
         "data":"2026-09-15","criado_em":"2026-09-15T18:00:00Z"}
        """;
}
