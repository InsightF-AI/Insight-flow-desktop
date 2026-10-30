using System.Net;
using System.Reflection;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Configuration;
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
    public Task Chart_draws_candles_indicators_and_rsi_and_zooms_around_a_record() => OnUiThread(() =>
    {
        using var chart = new PriceHistoryChart { Size = new Size(700, 360), Candles = true, ShowBollinger = true, ShowRsi = true };
        chart.CreateControl();
        using var bitmap = new Bitmap(700, 360);
        chart.SetData(Enumerable.Range(0, 80).Select(i => new HistoryPointResponse
        {
            Data = DateTimeOffset.Parse("2026-06-01T03:00:00Z").AddDays(i),
            Abertura = 30 + i % 7, Fechamento = 31 + i % 5, Minima = 29, Maxima = 38, Volume = 100 + i
        }), "BRL");
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);

        // O detalhe do registro traz os indicadores ligados que já têm valor naquela data.
        Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.End));
        foreach (var indicator in new[] { "SMA 20:", "SMA 50:", "Bollinger:", "RSI (14):" }) Assert.Contains(indicator, chart.AccessibleDescription);
        Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.Home));
        Assert.DoesNotContain("SMA 20:", chart.AccessibleDescription);

        // O zoom mantém o registro de referência na tela: aproximar no último deixa a janela no fim da série.
        chart.Zoom(true, 79);
        Assert.Equal(60, Field<int>(chart, "_count"));
        Assert.Equal(20, Field<int>(chart, "_first"));
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        // Pelo teclado, a janela acompanha o registro escolhido; Esc volta à série inteira.
        Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.Home));
        Assert.Equal(0, Field<int>(chart, "_first"));
        Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.Escape));
        Assert.Equal(0, Field<int>(chart, "_count"));

        chart.Candles = chart.ShowRsi = chart.ShowSma20 = false;
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        Invoke(chart, "OnKeyDown", new KeyEventArgs(Keys.End));
        Assert.DoesNotContain("SMA 20:", chart.AccessibleDescription);
        Assert.Contains("SMA 50:", chart.AccessibleDescription);

        // Sem altura para os dois painéis, o gráfico mostra só o de preço; abaixo disso, pede uma janela maior.
        var priceArea = typeof(PriceHistoryChart).GetProperty("PriceArea", BindingFlags.Instance | BindingFlags.NonPublic)!;
        chart.Size = new Size(700, 360);
        var tall = ((RectangleF)priceArea.GetValue(chart)!).Height;
        chart.Size = new Size(700, 130);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
        Assert.Equal(130 - 62, ((RectangleF)priceArea.GetValue(chart)!).Height);
        Assert.True(tall > 130);
        chart.Size = new Size(700, 100);
        chart.DrawToBitmap(bitmap, chart.ClientRectangle);
    });

    [Fact]
    public Task Alert_creation_uses_the_write_response_without_a_second_get_or_duplicate_post() => OnUiThread(() =>
    {
        var saved = Alert();
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request switch
        {
            { Method.Method: "POST" } => RecordingHttpMessageHandler.Json(JsonSerializer.Serialize(saved), HttpStatusCode.Created),
            _ when request.Uri.AbsolutePath.EndsWith("/cotacao") => RecordingHttpMessageHandler.Json(
                """{"ticker":"PETR4","preco":"800000001.2","variacao":"0","variacao_percentual":"0","maxima_dia":"0","minima_dia":"0","volume":"0"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession();
        session.Start("test-token");
        using var form = new AlertsForm(new InsightFlowApiClient(http, session), session);
        form.CreateControl();
        Field<TextBox>(form, "_ticker").Text = "PETR4";
        Field<NumericUpDown>(form, "_target").Value = saved.ValorAlvo;
        Invoke(form, "Save");
        PumpUntil(() => !Work(form).HasPending);
        // Depois do salvamento, a única outra chamada é a cotação do ativo novo; a lista de alertas não é consultada de novo.
        Assert.Equal(HttpMethod.Post, Assert.Single(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/alertas").Method);
        Assert.Equal($"/api/v1/ativos/{saved.Ativo.Id}/cotacao", Assert.Single(handler.Requests, request => request.Method == HttpMethod.Get).Uri.AbsolutePath);
        var row = Field<DataGridView>(form, "_grid").Rows[0];
        Assert.Equal(saved.Id, ((AlertResponse)row.Tag!).Id);
        Assert.Equal("BRL 800.000.001,20", row.Cells["Preço atual"].FormattedValue);
        Assert.Equal("+25,00%", row.Cells["Distância"].FormattedValue);
        Assert.Equal("Monitorando", row.Cells["Estado"].Value);
        Assert.Contains("salvo", Field<Label>(form, "_status").Text);
        Assert.Contains("BRL 800.000.001,20", Field<Label>(form, "_currentPrice").Text);
        Assert.Equal(saved.ValorAlvo, Field<NumericUpDown>(form, "_target").Value);
        Invoke(form, "Save");
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(2, handler.Requests.Count);
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
            new NotificationsForm(api, session, new Dictionary<Guid, string>()), new PortfolioForm(api, session, []), new ChatForm(api, session),
            new SettingsForm(UserPreferences.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), TestStartup(out _))
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
    public Task Window_panel_text_and_grid_scroll_bars_get_the_dark_theme_including_controls_added_later() => OnUiThread(() =>
    {
        using var http = new HttpClient(RecordingHttpMessageHandler.ReturningJson("[]")) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new PortfolioForm(new InsightFlowApiClient(http, session), session, []);
        form.CreateControl();
        var scrolling = Descendants(form).Prepend(form).Where(DarkScrollBars.Scrolls).ToList();
        // A janela, os painéis, o texto do resumo diário e as barras das tabelas, que são filhas delas.
        Assert.Contains(form, scrolling);
        Assert.Contains(Field<TextBox>(form, "_summaryText"), scrolling);
        Assert.Contains(scrolling, control => control is ScrollBar && control.Parent is DataGridView);
        Assert.All(scrolling, control => Assert.True(HasDarkScrollBars(control)));
        Assert.False(DarkScrollBars.Scrolls(new TextBox()));

        var late = new Panel();
        late.Controls.Add(UiTheme.Grid());
        form.Controls[0].Controls.Add(late);
        Assert.All(Descendants(late).Prepend(late).Where(DarkScrollBars.Scrolls), control => Assert.True(HasDarkScrollBars(control)));
    });

    [Fact]
    public Task Portfolio_grids_sort_by_value_and_a_failed_refresh_does_not_keep_old_totals() => OnUiThread(() =>
    {
        var petr = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" };
        var vivt = new AssetResponse { Id = Guid.NewGuid(), Ticker = "VIVT3", Nome = "Telefonica Brasil", Tipo = "ACAO", Moeda = "BRL" };
        var valeId = Guid.NewGuid();
        var profitabilityFails = false;
        string Operation(Guid assetId, string date, string quantity, string price) =>
            $$"""{"id":"{{Guid.NewGuid()}}","ativo_id":"{{assetId}}","tipo":"COMPRA","quantidade":"{{quantity}}","preco_unitario":"{{price}}","data":"{{date}}","criado_em":"{{date}}T18:00:00Z"}""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/v1/portfolio/benchmark" when request.Uri.Query.Contains("CDI") => RecordingHttpMessageHandler.Json(
                """{"benchmark":"CDI","rentabilidade_carteira_percentual":"0.0457","rentabilidade_benchmark_percentual":"0.012"}"""),
            "/api/v1/portfolio/benchmark" => RecordingHttpMessageHandler.Json(
                """{"benchmark":"IBOVESPA","rentabilidade_carteira_percentual":"0.0457","rentabilidade_benchmark_percentual":null}"""),
            "/api/v1/notificacoes" => RecordingHttpMessageHandler.Json($$"""
                [{"id":"{{Guid.NewGuid()}}","ativo_id":null,"tipo":"RESUMO_DIARIO","mensagem":"Resumo antigo","contexto":{"texto":"Texto antigo","aviso_legal":"Aviso"},"lida":true,"criado_em":"2026-10-07T21:30:00Z"},
                 {"id":"{{Guid.NewGuid()}}","ativo_id":"{{petr.Id}}","tipo":"SINAL_ATIVADO","mensagem":"Sinal","contexto":{},"lida":false,"criado_em":"2026-10-09T12:00:00Z"},
                 {"id":"{{Guid.NewGuid()}}","ativo_id":null,"tipo":"RESUMO_DIARIO","mensagem":"Resumo diario da carteira","contexto":{"texto":"A carteira subiu no dia.","aviso_legal":"Aviso legal da API"},"lida":false,"criado_em":"2026-10-08T21:30:00Z"}]
                """),
            // Só PETR4 tem cotação: VALE3 fica sem variação do dia e fora da média ponderada.
            var path when path == $"/api/v1/ativos/{petr.Id}/cotacao" => RecordingHttpMessageHandler.Json(
                """{"ticker":"PETR4","preco":"40","variacao":"0.78","variacao_percentual":"2","maxima_dia":"40.5","minima_dia":"39","volume":"150000"}"""),
            var path when path == $"/api/v1/ativos/{vivt.Id}/cotacao" => RecordingHttpMessageHandler.Json(
                """{"ticker":"VIVT3","preco":"30.25","variacao":"0","variacao_percentual":"0","maxima_dia":"30.5","minima_dia":"30","volume":"1000"}"""),
            var path when path.EndsWith("/cotacao") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
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
        using var form = new PortfolioForm(new InsightFlowApiClient(http, session), session, [petr, vivt]);
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

        // O que o cliente web passou a mostrar com a mesma API: variação do dia, resultado, benchmarks e resumo diário.
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(["+2,00%", "—"], positions.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["Var. dia"].FormattedValue!));
        Assert.Equal(["+4,58%", "0,00%"], positions.Rows.Cast<DataGridViewRow>().Select(row => (string)row.Cells["Resultado"].FormattedValue!));
        Assert.Contains("Variação do dia: +2,00%", Field<Label>(form, "_positionStatus").Text);
        var comparison = Field<Label>(form, "_comparison").Text;
        Assert.Contains("Carteira: +4,57%", comparison);
        Assert.Contains("CDI: +1,20%", comparison);
        Assert.Contains("Ibovespa: indisponível", comparison);
        Assert.Equal("A carteira subiu no dia.", Field<TextBox>(form, "_summaryText").Text);
        Assert.Equal("Aviso legal da API", Field<Label>(form, "_summaryNotice").Text);
        Assert.Contains("08/10/2026", Field<Label>(form, "_summaryStatus").Text);

        // A cotação do ativo escolhido vira sugestão de preço; um preço digitado não é sobrescrito.
        Invoke(form, "LoadAssetQuote");
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(40m, Field<NumericUpDown>(form, "_unitPrice").Value);
        Assert.Contains("Cotação atual: BRL 40,00", Field<Label>(form, "_assetHint").Text);
        Assert.Contains("Em carteira: 10", Field<Label>(form, "_assetHint").Text);
        // O total acompanha quantidade e preço unitário.
        Field<NumericUpDown>(form, "_quantity").Value = 3;
        Assert.Equal("Valor total: BRL 120,00", Field<Label>(form, "_total").Text);
        Field<NumericUpDown>(form, "_unitPrice").Value = 39.5m;
        Invoke(form, "LoadAssetQuote");
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(39.5m, Field<NumericUpDown>(form, "_unitPrice").Value);

        // Ao trocar de ativo, o preço do anterior sai e entra a cotação do escolhido, mesmo que tivesse sido digitado.
        Field<ComboBox>(form, "_asset").SelectedIndex = 1;
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(30.25m, Field<NumericUpDown>(form, "_unitPrice").Value);
        Assert.Equal("Valor total: BRL 90,75", Field<Label>(form, "_total").Text);
        Assert.Contains("Cotação atual: BRL 30,25", Field<Label>(form, "_assetHint").Text);
        Assert.Contains("Em carteira: 0", Field<Label>(form, "_assetHint").Text);
        Field<ComboBox>(form, "_asset").SelectedIndex = 0;
        PumpUntil(() => !Work(form).HasPending);
        Assert.Equal(40m, Field<NumericUpDown>(form, "_unitPrice").Value);

        profitabilityFails = true;
        Invoke(form, "LoadAll");
        PumpUntil(() => Field<int>(form, "_loading") == 0 && !Work(form).HasPending);
        Assert.Equal("Indisponível", Field<Label>(form, "_return").Text);
        Assert.Equal("Indisponível", Field<Label>(form, "_marketValue").Text);
        Assert.Equal(2, positions.Rows.Count);
    });

    [Fact]
    public Task A_new_user_without_watchlist_picks_a_catalog_asset_and_gets_its_current_price() => OnUiThread(() =>
    {
        var petr = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" };
        var btcId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/v1/ativos/catalogo" => RecordingHttpMessageHandler.Json($$"""
                [{"id":"{{btcId}}","ticker":"BTC","nome":"Bitcoin","tipo":"CRIPTO","setor":null,"moeda":"BRL"},
                 {"id":"{{petr.Id}}","ticker":"PETR4","nome":"Petrobras","tipo":"ACAO","setor":null,"moeda":"BRL"}]
                """),
            var path when path == $"/api/v1/ativos/{btcId}/cotacao" => RecordingHttpMessageHandler.Json(
                """{"ticker":"BTC","preco":"416915","variacao":"0","variacao_percentual":"0","maxima_dia":"420000","minima_dia":"410000","volume":"10"}"""),
            _ => RecordingHttpMessageHandler.Json("[]")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");

        using var empty = new PortfolioForm(new InsightFlowApiClient(http, session), session, []);
        empty.CreateControl();
        Invoke(empty, "LoadCatalog");
        PumpUntil(() => !Work(empty).HasPending);
        var assets = Field<ComboBox>(empty, "_asset");
        Assert.Equal(["BTC", "PETR4"], assets.Items.Cast<object>().Select(item => assets.GetItemText(item).Split(' ')[0]));
        Assert.Equal(0, assets.SelectedIndex);
        Assert.Equal(416915m, Field<NumericUpDown>(empty, "_unitPrice").Value);

        using var withWatchlist = new PortfolioForm(new InsightFlowApiClient(http, session), session, [petr]);
        withWatchlist.CreateControl();
        var chosen = Field<ComboBox>(withWatchlist, "_asset");
        Field<NumericUpDown>(withWatchlist, "_unitPrice").Value = 38.5m;
        Invoke(withWatchlist, "LoadCatalog");
        PumpUntil(() => !Work(withWatchlist).HasPending);
        Assert.Equal(2, chosen.Items.Count);
        Assert.StartsWith("PETR4", chosen.GetItemText(chosen.SelectedItem));
        Assert.Equal(38.5m, Field<NumericUpDown>(withWatchlist, "_unitPrice").Value);
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

    [Fact]
    public Task Watchlist_rows_show_price_day_change_and_active_signals_and_stay_blank_without_a_quote() => OnUiThread(() =>
    {
        var petr = Guid.NewGuid();
        var btc = Guid.NewGuid();
        string Item(Guid id, string ticker, string type) =>
            $$"""{"ativo":{"id":"{{id}}","ticker":"{{ticker}}","nome":"{{ticker}}","tipo":"{{type}}","setor":null,"moeda":"BRL"},"notificar":true,"adicionado_em":"2026-09-16T12:00:00Z"}""";
        string Signal(string deactivation) =>
            $$"""{"id":"{{Guid.NewGuid()}}","ativo_id":"{{petr}}","regra_id":"{{Guid.NewGuid()}}","regra_nome":"Sobrecompra RSI","regra_descricao":"RSI(14) acima de 70","data_ativacao":"2026-10-09T22:15:08Z","contexto":{"valor":"74.3"},"data_desativacao":{{deactivation}}}""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath switch
        {
            "/api/v1/watchlist" => RecordingHttpMessageHandler.Json($"[{Item(petr, "PETR4", "ACAO")},{Item(btc, "BTC", "CRIPTO")}]"),
            var path when path == $"/api/v1/ativos/{petr}/cotacao" => RecordingHttpMessageHandler.Json(
                """{"ticker":"PETR4","preco":"56","variacao":"0.52","variacao_percentual":"0.94","maxima_dia":"56.8","minima_dia":"55.1","volume":"49760700"}"""),
            // A fonte de mercado indisponível para um ativo não esconde os dados dos demais.
            var path when path.EndsWith("/cotacao") => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            var path when path == $"/api/v1/ativos/{petr}/sinais" => RecordingHttpMessageHandler.Json(
                $"[{Signal("null")},{Signal("\"2026-10-09T23:00:00Z\"")}]"),
            var path when path.EndsWith("/backtest") => new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => RecordingHttpMessageHandler.Json("[]")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress);
        form.CreateControl();
        Invoke(form, "LoadWatchlist");
        PumpUntil(() => !Work(form).HasPending);

        var rows = Field<DataGridView>(form, "_watchGrid").Rows.Cast<DataGridViewRow>().ToDictionary(row => (string)row.Cells["Ticker"].Value!);
        Assert.Equal("BRL 56,00", rows["PETR4"].Cells["Preço"].FormattedValue);
        Assert.Equal("+0,94%", rows["PETR4"].Cells["Var. dia"].FormattedValue);
        // Só o sinal vigente conta; o desativado continua na aba de sinais do ativo.
        Assert.Equal(1, rows["PETR4"].Cells["Sinais"].Value);
        Assert.Equal("—", rows["BTC"].Cells["Preço"].FormattedValue);
        Assert.Equal("—", rows["BTC"].Cells["Var. dia"].FormattedValue);
        Assert.Equal(0, rows["BTC"].Cells["Sinais"].Value);
        // O ativo selecionado pede o período exibido e o histórico mais longo, que alimenta as médias do gráfico.
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/historico") && request.Uri.Query == "?periodo=1M");
        Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("/historico") && request.Uri.Query == "?periodo=5A");
        Assert.NotNull(session.AccessToken);
    });

    [Fact]
    public Task Marking_all_notifications_as_read_keeps_the_ones_the_server_refused_as_unread() => OnUiThread(() =>
    {
        var accepted = Guid.NewGuid();
        var refused = Guid.NewGuid();
        var alreadyRead = Guid.NewGuid();
        string Notification(Guid id, bool read) =>
            $$"""{"id":"{{id}}","ativo_id":null,"tipo":"ALERTA_DISPARADO","mensagem":"PETR4 atingiu o alvo","contexto":{},"lida":{{(read ? "true" : "false")}},"criado_em":"2026-10-09T15:00:00Z"}""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request switch
        {
            { Method.Method: "PATCH" } when request.Uri.AbsolutePath.Contains(accepted.ToString()) => RecordingHttpMessageHandler.Json(Notification(accepted, true)),
            { Method.Method: "PATCH" } => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => RecordingHttpMessageHandler.Json($"[{Notification(accepted, false)},{Notification(refused, false)},{Notification(alreadyRead, true)}]")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new NotificationsForm(new InsightFlowApiClient(http, session), session, new Dictionary<Guid, string>());
        form.CreateControl();
        Invoke(form, "LoadNotifications");
        PumpUntil(() => !Work(form).HasPending);

        Invoke(form, "MarkAllAsRead");
        PumpUntil(() => !Work(form).HasPending);

        // A que já estava lida não é reenviada.
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Patch));
        var state = Field<DataGridView>(form, "_grid").Rows.Cast<DataGridViewRow>()
            .ToDictionary(row => ((NotificationResponse)row.Tag!).Id, row => ((NotificationResponse)row.Tag!).Lida);
        Assert.True(state[accepted]);
        Assert.False(state[refused]);
        Assert.True(state[alreadyRead]);
        Assert.Contains("1 notificação(ões) marcada(s)", Field<Label>(form, "_status").Text);
        Assert.True(Field<Button>(form, "_markAllButton").Enabled);
        Assert.NotNull(session.AccessToken);
    });

    [Fact]
    public Task Screens_with_ai_text_show_the_mandatory_notice_even_when_the_api_does_not_send_it() => OnUiThread(() =>
    {
        const string notice = "Análise gerada por IA. Não constitui recomendação de investimento.";
        // Resumo diário sem "aviso_legal" no contexto.
        var summary = $$"""[{"id":"{{Guid.NewGuid()}}","ativo_id":null,"tipo":"RESUMO_DIARIO","mensagem":"Resumo diario da carteira","contexto":{"texto":"A carteira subiu no dia."},"lida":false,"criado_em":"2026-10-08T21:30:00Z"}]""";
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            RecordingHttpMessageHandler.Json(request.Uri.AbsolutePath == "/api/v1/notificacoes" ? summary : "[]")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        var api = new InsightFlowApiClient(http, session);

        using var chat = new ChatForm(api, session);
        Assert.Equal(notice, Field<Label>(chat, "_notice").Text);

        using var notifications = new NotificationsForm(api, session, new Dictionary<Guid, string>());
        notifications.CreateControl();
        Invoke(notifications, "LoadNotifications");
        PumpUntil(() => !Work(notifications).HasPending);
        Assert.Contains("A carteira subiu no dia.", Field<TextBox>(notifications, "_detail").Text);
        Assert.EndsWith(notice, Field<TextBox>(notifications, "_detail").Text);

        using var portfolio = new PortfolioForm(api, session, []);
        portfolio.CreateControl();
        Invoke(portfolio, "LoadAll");
        PumpUntil(() => Field<int>(portfolio, "_loading") == 0);
        Assert.Equal("A carteira subiu no dia.", Field<TextBox>(portfolio, "_summaryText").Text);
        Assert.Equal(notice, Field<Label>(portfolio, "_summaryNotice").Text);
    });

    [Fact]
    public Task Logging_out_clears_the_local_session_and_revokes_the_refresh_token_on_the_server() => OnUiThread(() =>
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath == "/api/v1/auth/logout"
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : RecordingHttpMessageHandler.Json("[]")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token", "test-refresh-token", DateTimeOffset.UtcNow.AddMinutes(30));
        using var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress);
        form.CreateControl();

        Invoke(form, "EndSession", false);
        PumpUntil(() => form.Revocation.IsCompleted);

        Assert.True(form.LogoutRequested);
        Assert.False(form.SessionExpired);
        Assert.Null(session.AccessToken);
        Assert.Null(session.RefreshToken);
        var logout = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/auth/logout", logout.Uri.AbsolutePath);
        Assert.Null(logout.Authorization);
        Assert.Contains("test-refresh-token", logout.Body);
    });

    [Fact]
    public Task Closing_the_main_window_keeps_the_session_in_the_tray_until_exit_is_chosen() => OnUiThread(() =>
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(request.Uri.AbsolutePath == "/api/v1/auth/logout"
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : RecordingHttpMessageHandler.Json("[]")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token", "test-refresh-token", DateTimeOffset.UtcNow.AddMinutes(30));
        var directory = Directory.CreateTempSubdirectory("insightflow-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "preferences.json");
            // Com o aviso de segundo plano já exibido, o teste não dispara um balão do Windows.
            File.WriteAllText(path, """{"KeepRunningInTray":true,"BackgroundNoticeShown":true}""");
            var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress, null, UserPreferences.Load(path), TestStartup(out _));
            ShowOffScreen(form);
            PumpUntil(() => Field<NotifyIcon>(form, "_tray").Visible && !Work(form).HasPending);

            form.Close();
            Assert.False(form.Visible);
            Assert.False(form.IsDisposed);
            Assert.NotNull(session.RefreshToken);
            // Na bandeja, a consulta das notificações continua.
            int Polls() { lock (handler.Requests) return handler.Requests.Count(request => request.Uri.AbsolutePath == "/api/v1/notificacoes"); }
            var polls = Polls();
            Invoke(form, "PollNotifications");
            PumpUntil(() => !Work(form).HasPending);
            Assert.Equal(polls + 1, Polls());
            lock (handler.Requests) Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/auth/logout");

            form.ShowFromTray();
            Assert.True(form.Visible);

            Invoke(form, "Exit");
            PumpUntil(() => form.IsDisposed && form.Revocation.IsCompleted);
            Assert.False(form.LogoutRequested);
            Assert.Null(session.RefreshToken);
            lock (handler.Requests) Assert.Contains(handler.Requests, request => request.Uri.AbsolutePath == "/api/v1/auth/logout" && request.Body!.Contains("test-refresh-token"));
        }
        finally
        {
            directory.Delete(true);
        }
    });

    [Fact]
    public Task Without_the_tray_preference_closing_the_main_window_ends_the_app() => OnUiThread(() =>
    {
        using var http = new HttpClient(RecordingHttpMessageHandler.ReturningJson("[]")) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        var directory = Directory.CreateTempSubdirectory("insightflow-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "preferences.json");
            File.WriteAllText(path, """{"KeepRunningInTray":false}""");
            var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress, null, UserPreferences.Load(path), TestStartup(out _));
            ShowOffScreen(form);
            PumpUntil(() => Field<NotifyIcon>(form, "_tray").Visible && !Work(form).HasPending);

            form.Close();
            PumpUntil(() => form.IsDisposed);

            Assert.False(form.LogoutRequested);
            Assert.Null(session.AccessToken);
        }
        finally
        {
            directory.Delete(true);
        }
    });

    [Fact]
    public Task Settings_save_the_tray_preference_and_the_windows_startup_entry() => OnUiThread(() =>
    {
        var directory = Directory.CreateTempSubdirectory("insightflow-tests");
        var startup = TestStartup(out var keyPath);
        try
        {
            var path = Path.Combine(directory.FullName, "preferences.json");
            var preferences = UserPreferences.Load(path);
            using var form = new SettingsForm(preferences, startup);
            form.CreateControl();
            var keepInTray = Field<CheckBox>(form, "_keepInTray");
            var startWithWindows = Field<CheckBox>(form, "_startWithWindows");
            var save = Field<Button>(form, "_save");
            Assert.True(keepInTray.Checked);
            Assert.False(startWithWindows.Checked);
            Assert.False(save.Enabled);

            keepInTray.Checked = false;
            startWithWindows.Checked = true;
            Assert.True(save.Enabled);
            Invoke(form, "Save");

            Assert.False(preferences.KeepRunningInTray);
            Assert.False(UserPreferences.Load(path).KeepRunningInTray);
            Assert.True(startup.IsEnabled);
            Assert.False(save.Enabled);
            Assert.Contains("salvas", Field<Label>(form, "_status").Text);

            startWithWindows.Checked = false;
            Invoke(form, "Save");
            Assert.False(startup.IsEnabled);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            directory.Delete(true);
        }
    });

    [Fact]
    public Task A_short_main_window_goes_compact_and_shows_the_search_list_only_when_it_has_results() => OnUiThread(() =>
    {
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(RecordingHttpMessageHandler.Json(
            request.Uri.AbsolutePath == "/api/v1/ativos/buscar" ? """[{"ticker":"PETR4","nome":"Petrobras","tipo":"ACAO","moeda":"BRL"}]""" : "[]")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test") };
        var session = new UserSession(); session.Start("test-token");
        using var form = new MainForm(new InsightFlowApiClient(http, session), session, http.BaseAddress);
        ShowOffScreen(form);
        PumpUntil(() => Field<NotifyIcon>(form, "_tray").Visible && !Work(form).HasPending);

        // É a altura que sobra com 150% de escala num monitor 1080p.
        form.Height = form.MinimumSize.Height;
        var results = Field<Panel>(form, "_searchResultsHost");
        var hints = Field<List<Control>>(form, "_compactHidden");
        Assert.True(Field<bool>(form, "_compact"));
        Assert.False(results.Visible);
        Assert.All(hints, hint => Assert.False(hint.Visible));
        // A watchlist e o gráfico ficam com a altura liberada.
        Assert.True(Field<DataGridView>(form, "_watchGrid").Height >= form.LogicalToDeviceUnits(150));
        Assert.True(Field<PriceHistoryChart>(form, "_historyChart").Height >= form.LogicalToDeviceUnits(110));

        Field<TextBox>(form, "_search").Text = "PETR";
        Invoke(form, "Search");
        PumpUntil(() => !Work(form).HasPending);
        Assert.True(results.Visible);
        Assert.Single(Field<ListBox>(form, "_searchResults").Items);

        // Apagar a busca devolve a altura à watchlist.
        Field<TextBox>(form, "_search").Text = "";
        Assert.False(results.Visible);
        Invoke(form, "Exit");
        PumpUntil(() => form.IsDisposed);
    });

    [Fact]
    public Task A_screen_smaller_than_its_designed_minimum_keeps_the_layout_and_scrolls() => OnUiThread(() =>
    {
        var startup = TestStartup(out _);
        using var form = new SettingsForm(UserPreferences.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), startup);
        var designed = form.MinimumSize;
        ShowOffScreen(form);
        PumpUntil(() => form.Visible);
        var content = form.Controls[0];
        var minimum = form.AutoScrollMinSize;
        Assert.True(form.AutoScroll);
        Assert.Equal(form.LogicalToDeviceUnits(designed) - (form.Size - form.ClientSize), minimum);

        // No tamanho mínimo projetado não há rolagem: o conteúdo ocupa exatamente a área da janela.
        form.Size = form.MinimumSize;
        Assert.Equal(form.ClientSize, content.Size);

        // Tela pequena ou escala alta: a janela encolhe, o conteúdo mantém o mínimo projetado e passa a rolar.
        form.MinimumSize = new Size(320, 240);
        form.Size = new Size(320, 240);
        Assert.Equal(minimum, content.Size);
        Assert.True(form.ClientSize.Width < minimum.Width && form.ClientSize.Height < minimum.Height);
    });

    private static AlertResponse Alert() => new()
    {
        Id = Guid.NewGuid(), Ativo = new AssetResponse { Id = Guid.NewGuid(), Ticker = "PETR4", Nome = "Petrobras", Tipo = "ACAO", Moeda = "BRL" },
        TipoCondicao = InsightFlowApiClient.PriceGreaterThanOrEqual, ValorAlvo = 1_000_000_001.5m,
        MoedaAlvo = "BRL", Habilitado = true, UltimoEstado = false, CriadoEm = DateTimeOffset.UtcNow, AtualizadoEm = DateTimeOffset.UtcNow
    };

    // Entrada de inicialização numa chave de teste: a chave Run do usuário não é tocada.
    private static WindowsStartup TestStartup(out string keyPath)
    {
        keyPath = $@"Software\InsightFlow.Tests.{Guid.NewGuid():N}";
        return new WindowsStartup("\"InsightFlow.Desktop.exe\"", keyPath);
    }

    // Exibe a janela de verdade, fora da área visível: só assim o ícone da bandeja e o fechamento se comportam como no uso.
    private static void ShowOffScreen(Form form)
    {
        form.ShowInTaskbar = false;
        form.Opacity = 0;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);
        form.Show();
    }

    private static FormWorkScope Work(Form form) =>
        (FormWorkScope)typeof(AsyncForm).GetField("_work", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static bool HasDarkScrollBars(Control control) =>
        ((System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>)typeof(DarkScrollBars)
            .GetField("Attached", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).TryGetValue(control, out _);
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
