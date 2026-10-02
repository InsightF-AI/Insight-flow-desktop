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

    [Fact]
    public Task Every_screen_finishes_its_layout_so_dpi_scaling_reaches_all_controls() => OnUiThread(() =>
    {
        using var http = new HttpClient(RecordingHttpMessageHandler.ReturningJson("[]")) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        var api = new InsightFlowApiClient(http, session);
        Form[] screens =
        [
            new LoginForm(api, session, http.BaseAddress), new MainForm(api, session, http.BaseAddress), new AlertsForm(api, session),
            new NotificationsForm(api, session, new Dictionary<Guid, string>()), new PortfolioForm(api, session, []), new ChatForm(api, session)
        ];
        foreach (var screen in screens)
        {
            using (screen)
            {
                Assert.Equal(AutoScaleMode.Dpi, screen.AutoScaleMode);
                // Com o layout ainda suspenso, o painel raiz manteria o tamanho inicial em vez de ocupar a área da janela.
                Assert.Equal(screen.ClientSize, screen.Controls[0].Size);
            }
        }
    });

    [Fact]
    public Task Portfolio_grids_sort_by_value_and_a_failed_refresh_does_not_keep_old_totals() => OnUiThread(() =>
    {
        var petr = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" };
        var valeId = Guid.NewGuid();
        var profitabilityFails = false;
        string Operation(Guid assetId, string date, string quantity, string price) =>
            $$"""{"id":"{{Guid.NewGuid()}}","ativo_id":"{{assetId}}","tipo":"COMPRA","quantidade":"{{quantity}}","preco_unitario":"{{price}}","data":"{{date}}","criado_em":"{{date}}T18:00:00Z"}""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/v1/portfolio/posicoes" => RecordingHttpMessageHandler.Json($$"""
                [{"ativo_id":"{{petr.Id}}","ticker":"PETR4","quantidade":"10","preco_medio":"38.25","cotacao_atual":"40","valor_mercado":"400","valor_mercado_brl":"400","lucro_nao_realizado":"17.5","lucro_realizado":"0"},
                 {"ativo_id":"{{valeId}}","ticker":"VALE3","quantidade":"20","preco_medio":"65","cotacao_atual":"65","valor_mercado":"1300","valor_mercado_brl":"1300","lucro_nao_realizado":"0","lucro_realizado":"0"}]
                """),
            "/api/v1/portfolio/rentabilidade" when profitabilityFails => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "/api/v1/portfolio/rentabilidade" => RecordingHttpMessageHandler.Json(
                """{"custo_base_brl":"1682.50","valor_mercado_brl":"1700","lucro_nao_realizado_brl":"17.5","lucro_realizado_brl":"0","percentual":"0.0457"}"""),
            "/api/v1/operacoes" => RecordingHttpMessageHandler.Json(
                $"[{Operation(petr.Id, "2026-09-15", "10", "38.25")},{Operation(valeId, "2026-10-01", "20", "65")},{Operation(petr.Id, "2026-08-20", "2", "36")}]"),
            _ => RecordingHttpMessageHandler.Json("""{"por_classe":{"ACAO":"1"},"por_setor":{"Energia":"0.75","N/A":"0.25"},"por_moeda":{"BRL":"1"}}""")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new PortfolioForm(new InsightFlowApiClient(http, session), session, [petr]);
        form.CreateControl();
        Invoke(form, "LoadAll");
        PumpUntil(() => Field<int>(form, "_loading") == 0);

        var operations = Field<DataGridView>(form, "_operations");
        operations.Sort(operations.Columns["Data"]!, System.ComponentModel.ListSortDirection.Ascending);
        // Como texto, 01/10 viria antes de 20/08.
        Assert.Equal(["20/08/2026", "15/09/2026", "01/10/2026"], operations.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["Data"].FormattedValue!));
        Assert.Equal("BRL 382,50", operations.Rows[1].Cells["Total"].FormattedValue);
        // VALE3 não está na watchlist: o ticker vem das posições e a moeda não é presumida.
        Assert.Equal("VALE3", operations.Rows[2].Cells["Ativo"].Value);
        Assert.Equal("1.300,00", operations.Rows[2].Cells["Total"].FormattedValue);

        var positions = Field<DataGridView>(form, "_positions");
        positions.Sort(positions.Columns["Valor (BRL)"]!, System.ComponentModel.ListSortDirection.Ascending);
        // Como texto, "BRL 1.300,00" viria antes de "BRL 400,00".
        Assert.Equal(["BRL 400,00", "BRL 1.300,00"], positions.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["Valor (BRL)"].FormattedValue!));
        Assert.Contains("75,00%", Field<DataGridView>(form, "_distribution").Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["Participação"].FormattedValue!));
        Assert.Equal("+4,57%", Field<Label>(form, "_return").Text);

        profitabilityFails = true;
        Invoke(form, "LoadAll");
        PumpUntil(() => Field<int>(form, "_loading") == 0);
        Assert.Equal("Indisponível", Field<Label>(form, "_return").Text);
        Assert.Equal("Indisponível", Field<Label>(form, "_marketValue").Text);
        Assert.Equal(2, positions.Rows.Count);
    });

    [Fact]
    public Task Saving_the_notification_preference_updates_the_row_without_reloading_asset_details() => OnUiThread(() =>
    {
        var assetId = Guid.NewGuid();
        string Item(bool notify) =>
            $$"""{"ativo":{"id":"{{assetId}}","ticker":"PETR4","nome":"Petrobras","tipo":"ACAO","setor":null,"moeda":"BRL"},"notificar":{{(notify ? "true" : "false")}},"adicionado_em":"2026-09-16T12:00:00Z"}""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request switch
        {
            { Method.Method: "PATCH" } => RecordingHttpMessageHandler.Json(Item(true)),
            { Uri.AbsolutePath: "/api/v1/watchlist" } => RecordingHttpMessageHandler.Json($"[{Item(false)}]"),
            _ when request.Uri.AbsolutePath.EndsWith("/cotacao") => RecordingHttpMessageHandler.Json(
                """{"ticker":"PETR4","preco":"38.2","variacao":"0.5","variacao_percentual":"1.3","maxima_dia":"39","minima_dia":"37.7","volume":"150000"}"""),
            _ => RecordingHttpMessageHandler.Json("[]")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress);
        form.CreateControl();
        var work = (FormWorkScope)typeof(AsyncForm).GetField("_work", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        Invoke(form, "LoadWatchlist");
        PumpUntil(() => !work.HasPending);
        var watch = Field<DataGridView>(form, "_watchGrid");
        Assert.Contains("38,2", Field<Label>(form, "_quotePrice").Text);
        var requestsBefore = handler.Requests.Count;

        Field<CheckBox>(form, "_notify").Checked = true;
        Invoke(form, "SaveNotificationPreference");
        PumpUntil(() => !work.HasPending);

        var patch = Assert.Single(handler.Requests.Skip(requestsBefore));
        Assert.Equal(HttpMethod.Patch, patch.Method);
        Assert.True(((WatchlistItemResponse)watch.Rows[0].Tag!).Notificar);
        Assert.False(Field<Button>(form, "_saveNotification").Enabled);
        Assert.Contains("38,2", Field<Label>(form, "_quotePrice").Text);
    });

    private static AlertResponse Alert() => new()
    {
        Id = Guid.NewGuid(), Ativo = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" },
        TipoCondicao = InsightFlowApiClient.PriceGreaterThanOrEqual, ValorAlvo = 1_000_000_001.5m,
        MoedaAlvo = "BRL", Habilitado = true, UltimoEstado = false, CriadoEm = DateTimeOffset.UtcNow, AtualizadoEm = DateTimeOffset.UtcNow
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
                // O aplicativo fixa pt-BR na inicialização; os textos formatados não dependem da cultura da máquina de teste.
                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
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
