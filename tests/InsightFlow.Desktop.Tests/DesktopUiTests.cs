using System.Net;
using System.Reflection;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Forms;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Tests;

public sealed class DesktopUiTests
{
    [Fact]
    public Task Chart_renders_empty_single_flat_and_intraday_series_and_clears_previous_data() => OnUiThread(() =>
    {
        using var chart = new PriceHistoryChart { Size = new Size(650, 320) };
        chart.CreateControl();
        using var bitmap = new Bitmap(650, 320);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        foreach (var count in new[] { 1, 4 })
        {
            chart.SetData(Enumerable.Range(0, count).Select(i => new HistoryPointResponse
            {
                Data = DateTimeOffset.Parse("2026-09-18T10:00:00-03:00").AddMinutes(i * 30),
                Fechamento = 30, Abertura = 29, Minima = 28, Maxima = 31, Volume = i * 100
            }), "BRL");
            chart.DrawToBitmap(bitmap, chart.ClientRectangle);
            Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.End));
            Assert.Contains("-03:00", chart.AccessibleDescription);
            Assert.Contains("BRL 30", chart.AccessibleDescription);
        }
        chart.ShowMessage("Carregando histórico…");
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        Assert.Equal("Carregando histórico…", chart.AccessibleDescription);
        Assert.Empty(Field<HistorySeries>(chart, "_series").Points);
    });

    [Fact]
    public Task Alert_creation_uses_the_write_response_without_a_second_get_or_duplicate_post() => OnUiThread(() =>
    {
        var saved = Alert();
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post
                ? RecordingHttpMessageHandler.Json(JsonSerializer.Serialize(saved), HttpStatusCode.Created)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession();
        session.Start("test-token");
        using var form = new AlertsForm(new InsightFlowApiClient(http, session), session);
        form.CreateControl();
        Field<TextBox>(form, "_ticker").Text = "PETR4";
        Field<NumericUpDown>(form, "_target").Value = saved.ValorAlvo;
        Invoke(form, "Save");
        PumpUntil(() => !Field<bool>(form, "_busy"));
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests).Method);
        Assert.Equal(saved.Id, ((AlertResponse)Field<DataGridView>(form, "_grid").Rows[0].Tag!).Id);
        Assert.Contains("salvo", Field<Label>(form, "_status").Text);
        Assert.Equal(saved.ValorAlvo, Field<NumericUpDown>(form, "_target").Value);
        Invoke(form, "Save");
        PumpUntil(() => !Field<bool>(form, "_busy"));
        Assert.Single(handler.Requests);
        Assert.Contains("Nenhuma alteração", Field<Label>(form, "_status").Text);
        var split = Descendants(form).OfType<SplitContainer>().Single();
        Assert.True(split.Panel2.Width >= 320);
    });

    [Fact]
    public Task A_late_backtest_failure_does_not_replace_the_result_for_the_current_signal() => OnUiThread(() =>
    {
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>();
        var firstRule = Guid.NewGuid();
        var secondRule = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler((request, _) => request.Uri.AbsolutePath.Contains(firstRule.ToString())
            ? oldResponse.Task
            : Task.FromResult(RecordingHttpMessageHandler.Json(JsonSerializer.Serialize(new BacktestResponse
            {
                AtivoId = assetId, RegraId = secondRule, TotalOcorrencias = 7, RetornoMedio5Pregoes = 2
            }))));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress);
        form.CreateControl();
        SetField(form, "_bindingWatchlist", true);
        var watch = Field<DataGridView>(form, "_watchGrid");
        var wi = watch.Rows.Add("PETR4", "Petrobras");
        watch.Rows[wi].Tag = new WatchlistItemResponse { Ativo = new AssetResponse { Id = assetId, Ticker = "PETR4", Moeda = "BRL" } };
        watch.CurrentCell = watch.Rows[wi].Cells[0];
        var signals = Field<DataGridView>(form, "_signals");
        SetField(form, "_bindingSignals", true);
        foreach (var rule in new[] { firstRule, secondRule })
        {
            var index = signals.Rows.Add("Regra", "Ativo", DateTime.Now, "Descrição");
            signals.Rows[index].Tag = new SignalResponse { AtivoId = assetId, RegraId = rule, RegraNome = "Regra" };
        }
        signals.CurrentCell = signals.Rows[0].Cells[0];
        Invoke(form, "LoadSelectedBacktest");
        signals.CurrentCell = signals.Rows[1].Cells[0];
        Invoke(form, "LoadSelectedBacktest");
        PumpUntil(() => Field<DataGridView>(form, "_backtest").Rows.Count == 1);
        var status = Field<Label>(form, "_backtestStatus").Text;
        oldResponse.SetException(new HttpRequestException("Obsolete request failed"));
        var work = typeof(AsyncForm).GetField("_work", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        PumpUntil(() => !((FormWorkScope)work).HasPending);
        Assert.Equal(status, Field<Label>(form, "_backtestStatus").Text);
        Assert.Equal(7, Field<DataGridView>(form, "_backtest").Rows[0].Cells[0].Value);
    });

    private static AlertResponse Alert() => new()
    {
        Id = Guid.NewGuid(), Ativo = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" },
        TipoCondicao = InsightFlowApiClient.PriceGreaterThanOrEqual, ValorAlvo = 1_000_000_001.5m,
        MoedaAlvo = "BRL", Habilitado = true, UltimoEstado = "NAO_DISPARADO", CriadoEm = DateTimeOffset.UtcNow, AtualizadoEm = DateTimeOffset.UtcNow
    };

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void SetField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);
    private static void Invoke(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments);

    private static void PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Assert.True(done(), "UI operation did not complete within five seconds.");
    }

    private static Task OnUiThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                action();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { SynchronizationContext.SetSynchronizationContext(null); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
