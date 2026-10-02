using System.Globalization;
using System.Net;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Forms;
using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Tests;

public sealed class PresentationTests
{
    private static readonly CultureInfo Brazilian = CultureInfo.GetCultureInfo("pt-BR");

    [Theory]
    [InlineData("Ativo ja esta na watchlist", "Este ativo já está na sua watchlist.")]
    [InlineData("Dados insuficientes para gerar a analise", "Ainda não há dados suficientes para gerar a análise deste ativo.")]
    [InlineData("Mensagem excede 2000 caracteres", "Mensagem excede 2000 caracteres")]
    public void Known_backend_details_get_desktop_wording_and_unknown_ones_are_kept(string detail, string expected)
    {
        Assert.Equal(expected, UiErrors.Message(new ApiException(HttpStatusCode.UnprocessableEntity, detail)));
    }

    [Fact]
    public void Ai_errors_distinguish_guardrail_quota_and_disabled_provider()
    {
        var blocked = UiErrors.AiMessage(new ApiException(HttpStatusCode.BadGateway, "x"));
        var quota = UiErrors.AiMessage(new ApiException(HttpStatusCode.ServiceUnavailable, "x", TimeSpan.FromSeconds(29.2)));
        var disabled = UiErrors.AiMessage(new ApiException(HttpStatusCode.ServiceUnavailable, "x"));

        Assert.Contains("bloqueada", blocked);
        Assert.Contains("30 s", quota);
        Assert.Contains("indisponível", disabled);
        Assert.DoesNotContain("cota", disabled);
    }

    [Theory]
    [InlineData("""{"periodo":14}""", "periodo: 14")]
    [InlineData("""{"linha_sinal":1.23456,"histograma":-0.5}""", "linha sinal: 1,2346 · histograma: -0,5")]
    [InlineData("""{"valor":"28.4"}""", "valor: 28,4")]
    [InlineData("""{"regra_id":"a1a1a1a1-0000-0000-0000-000000000001"}""", "regra id: a1a1a1a1-0000-0000-0000-000000000001")]
    [InlineData("{}", "—")]
    [InlineData("null", "—")]
    public void Auxiliary_json_is_shown_as_readable_pairs(string json, string expected) => WithBrazilianCulture(() =>
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, JsonText.Summary(document.RootElement));
    });

    [Fact]
    public void Long_or_missing_auxiliary_json_stays_within_the_cell()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { texto = new string('a', 400) }));

        var summary = JsonText.Summary(document.RootElement);

        Assert.Equal(120, summary.Length);
        Assert.EndsWith("…", summary);
        Assert.Equal("—", JsonText.Summary(null));
    }

    [Fact]
    public void Grid_formats_keep_the_stored_percentage_and_currency() => WithBrazilianCulture(() =>
    {
        // Um "%" sem aspas multiplicaria por 100 um valor que já está em pontos percentuais.
        Assert.Equal("12,50%", 12.5m.ToString(GridCells.Share, CultureInfo.CurrentCulture));
        Assert.Equal("+4,57%", 4.57m.ToString(GridCells.SignedPercent, CultureInfo.CurrentCulture));
        Assert.Equal("-1,00%", (-1m).ToString(GridCells.SignedPercent, CultureInfo.CurrentCulture));
        Assert.Equal("0,00%", 0m.ToString(GridCells.SignedPercent, CultureInfo.CurrentCulture));
        Assert.Equal("BRL 1.382,50", 1382.5m.ToString(GridCells.Money("BRL"), CultureInfo.CurrentCulture));
        Assert.Equal("BRL -2,50", (-2.5m).ToString(GridCells.Money("BRL"), CultureInfo.CurrentCulture));
        Assert.Equal("BRL 0,00", 0m.ToString(GridCells.Money("BRL"), CultureInfo.CurrentCulture));
        Assert.Equal("-20,00", (-20m).ToString(GridCells.Money(""), CultureInfo.CurrentCulture));
        Assert.Equal("0,00000012", 0.00000012m.ToString(GridCells.Money(""), CultureInfo.CurrentCulture));
        Assert.Equal("USD 10,00", 10m.ToString(GridCells.Money("US'D"), CultureInfo.CurrentCulture));
    });

    [Theory]
    [InlineData(38.624000001, 0.624, "38,62")]
    [InlineData(4.2, 1.5, "4,2")]
    [InlineData(125000, 25000, "125.000")]
    [InlineData(0.0005, 0.000002, "0,0005000")]
    [InlineData(1500000, 250000, "1,5E+6")]
    [InlineData(30, 0, "30,00")]
    public void Chart_axis_labels_follow_the_tick_interval(double value, double step, string expected) => WithBrazilianCulture(() =>
        Assert.Equal(expected, PriceHistoryChart.AxisNumber(value, step)));

    [Fact]
    public void Chat_window_never_starts_with_an_answer_whose_question_was_dropped()
    {
        var history = Enumerable.Range(0, InsightFlowApiClient.ChatMaxMessages)
            .Select(i => new ChatMessage(i % 2 == 0 ? InsightFlowApiClient.ChatUser : InsightFlowApiClient.ChatAssistant, $"m{i}"))
            .ToArray();
        var question = new ChatMessage(InsightFlowApiClient.ChatUser, "nova pergunta");

        var window = ChatForm.Window(history, question);

        Assert.True(window.Length <= InsightFlowApiClient.ChatMaxMessages);
        Assert.Equal(InsightFlowApiClient.ChatUser, window[0].Papel);
        Assert.Equal("m2", window[0].Texto);
        Assert.Same(question, window[^1]);
        Assert.Equal(new[] { history[0], history[1], question }, ChatForm.Window(history.Take(2), question));
    }

    private static void WithBrazilianCulture(Action test)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Brazilian;
        try { test(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
