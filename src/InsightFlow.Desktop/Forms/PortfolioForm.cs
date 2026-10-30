using System.Globalization;
using System.Net;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed class PortfolioForm : AsyncForm
{
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly int _sessionVersion;
    private readonly Dictionary<Guid, AssetResponse> _assets;
    private readonly Dictionary<Guid, string> _tickers;
    private readonly DataGridView _positions = UiTheme.Grid();
    private readonly DataGridView _operations = UiTheme.Grid();
    private readonly DataGridView _distribution = UiTheme.Grid();
    private readonly Label _costBase = Metric();
    private readonly Label _marketValue = Metric();
    private readonly Label _unrealized = Metric();
    private readonly Label _realized = Metric();
    private readonly Label _return = Metric();
    private readonly Label _positionStatus = UiTheme.Label("Carregando posições…", 9);
    private readonly Label _operationStatus = UiTheme.Label("Carregando operações…", 9);
    private readonly Label _distributionStatus = UiTheme.Label("Carregando distribuição…", 9);
    private readonly ComboBox _asset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _quantity = new() { DecimalPlaces = 8, Minimum = 0, Maximum = decimal.MaxValue, ThousandsSeparator = true, Dock = DockStyle.Fill };
    private readonly NumericUpDown _unitPrice = new() { DecimalPlaces = 8, Minimum = 0, Maximum = decimal.MaxValue, ThousandsSeparator = true, Dock = DockStyle.Fill };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Dock = DockStyle.Fill };
    private readonly Button _registerButton = UiTheme.Button("&Registrar operação", true);
    private readonly Button _deleteButton = UiTheme.Button("&Excluir operação");
    private readonly Button _refreshButton = UiTheme.Button("&Atualizar");
    private readonly Label _comparison = UiTheme.Label("Carregando comparação com CDI e Ibovespa…", 10);
    private readonly Label _assetHint = UiTheme.Label("", 8.5f);
    private readonly Label _total = UiTheme.Label("", 10, true);
    private readonly TextBox _summaryText = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly Label _summaryNotice = UiTheme.Label("", 8.5f);
    private readonly Label _summaryStatus = UiTheme.Label("Carregando resumo diário…", 9);
    // Cotações consultadas nesta tela, por ativo: variação do dia das posições e sugestão de preço da operação.
    private readonly Dictionary<Guid, QuoteResponse> _quotes = [];
    // Quantidade em carteira por ativo; nula enquanto as posições não foram carregadas.
    private Dictionary<Guid, decimal>? _held;
    private CancellationTokenSource? _assetQuoteCancellation;
    private decimal? _suggestedPrice;
    private bool _rebindingAssets;
    private int _positionsVersion;
    private readonly Label _status = UiTheme.Label("", 9);
    private int _loading;
    private bool _writing;

    public bool SessionExpired { get; private set; }
    private bool Busy => _loading > 0 || _writing;
    private OperationResponse? SelectedOperation => _operations.CurrentRow?.Tag as OperationResponse;
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public PortfolioForm(InsightFlowApiClient api, UserSession session, IEnumerable<AssetResponse> assets)
    {
        _api = api;
        _session = session;
        _sessionVersion = session.Version;
        _assets = assets.GroupBy(asset => asset.Id).ToDictionary(group => group.Key, group => group.First());
        _tickers = _assets.ToDictionary(pair => pair.Key, pair => pair.Value.Ticker);
        BuildLayout();
        _refreshButton.Click += (_, _) => LoadAll();
        _registerButton.Click += (_, _) => Register();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _operations.SelectionChanged += (_, _) => UpdateActions();
        // O total acompanha a digitação, antes de o campo confirmar o valor.
        _quantity.TextChanged += (_, _) => ShowTotal();
        _unitPrice.TextChanged += (_, _) => ShowTotal();
        _asset.SelectedIndexChanged += (_, _) =>
        {
            if (_rebindingAssets) return;
            UpdateActions();
            // O preço que estava no campo era de outro ativo: sai, e a cotação do ativo escolhido entra no lugar.
            _unitPrice.Value = 0;
            _suggestedPrice = null;
            LoadAssetQuote();
        };
        Shown += (_, _) =>
        {
            LoadAll();
            LoadAssetQuote();
            LoadCatalog();
        };
        FormClosed += (_, _) =>
        {
            _positions.Rows.Clear();
            _operations.Rows.Clear();
            _distribution.Rows.Clear();
            _summaryText.Clear();
            _quotes.Clear();
        };
        UpdateActions();
        CompleteLayout();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Portfólio";
        ClientSize = new Size(1200, 700);
        MinimumSize = new Size(1080, 600);
        BackColor = UiTheme.Background;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), BackColor = UiTheme.Background };
        // Sem estilo, a coluna cresce com o conteúdo mais largo e a janela menor corta a borda direita.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        titles.Controls.Add(UiTheme.Label("Portfólio", 20, true));
        var subtitle = UiTheme.Label("Operações simuladas registradas na sua conta. Posições e rentabilidade são calculadas pela API.", 9);
        subtitle.ForeColor = UiTheme.InkMuted;
        titles.Controls.Add(subtitle);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_refreshButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = UiTheme.Font(9.5f, true), Padding = new Point(16, 6) };
        tabs.TabPages.Add(Page("Posições", BuildPositionsTab()));
        tabs.TabPages.Add(Page("Operações", BuildOperationsTab()));
        tabs.TabPages.Add(Page("Distribuição e benchmark", BuildDistributionTab()));
        tabs.TabPages.Add(Page("Resumo diário", BuildSummaryTab()));
        root.Controls.Add(tabs, 0, 1);
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(0, 12, 0, 0);
        _status.ForeColor = UiTheme.InkMuted;
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
    }

    private Control BuildPositionsTab()
    {
        var panel = Card(4);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var metrics = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 10) };
        metrics.Controls.Add(MetricBlock("CUSTO BASE", _costBase));
        metrics.Controls.Add(MetricBlock("VALOR DE MERCADO", _marketValue));
        metrics.Controls.Add(MetricBlock("LUCRO NÃO REALIZADO", _unrealized));
        metrics.Controls.Add(MetricBlock("LUCRO REALIZADO", _realized));
        metrics.Controls.Add(MetricBlock("RENTABILIDADE", _return));
        panel.Controls.Add(metrics, 0, 0);
        _positions.Columns.Add(GridCells.Column("Ativo", 50f));
        _positions.Columns.Add(GridCells.Column("Quantidade", 78f, typeof(decimal), GridCells.Number, true));
        foreach (var (name, weight) in new[]
                 {
                     ("Preço médio", 100f), ("Cotação", 100f), ("Var. dia", 62f), ("Valor de mercado", 96f),
                     ("Valor (BRL)", 96f), ("Lucro não realizado", 92f), ("Resultado", 84f), ("Lucro realizado", 86f)
                 })
        {
            // A moeda varia por ativo: o formato monetário é definido em cada linha.
            _positions.Columns.Add(GridCells.Column(name, weight, typeof(decimal),
                name is "Var. dia" or "Resultado" ? GridCells.SignedPercent : null, true));
        }
        _positions.AccessibleName = "Posições da carteira";
        panel.Controls.Add(_positions, 0, 1);
        _positionStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_positionStatus, 0, 2);
        return panel;
    }

    private Control BuildOperationsTab()
    {
        _operations.Columns.Add(GridCells.Column("Data", 70f, typeof(DateTime), GridCells.Date));
        _operations.Columns.Add(GridCells.Column("Ativo", 60f));
        _operations.Columns.Add(GridCells.Column("Tipo", 55f));
        _operations.Columns.Add(GridCells.Column("Quantidade", 90f, typeof(decimal), GridCells.Number, true));
        _operations.Columns.Add(GridCells.Column("Preço unitário", 85f, typeof(decimal), alignRight: true));
        _operations.Columns.Add(GridCells.Column("Total", 85f, typeof(decimal), alignRight: true));
        _operations.AccessibleName = "Operações registradas";

        var left = Card(2);
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_operations, 0, 0);
        _operationStatus.Dock = DockStyle.Fill;
        left.Controls.Add(_operationStatus, 0, 1);

        UiTheme.StyleCombo(_asset);
        UiTheme.StyleCombo(_type);
        _asset.DisplayMember = nameof(AssetChoice.Label);
        foreach (var asset in _assets.Values.OrderBy(asset => asset.Ticker)) _asset.Items.Add(new AssetChoice(asset));
        if (_asset.Items.Count > 0) _asset.SelectedIndex = 0;
        _type.DisplayMember = nameof(TypeChoice.Label);
        _type.Items.AddRange([new TypeChoice(InsightFlowApiClient.OperationBuy, "Compra"), new TypeChoice(InsightFlowApiClient.OperationSell, "Venda")]);
        _type.SelectedIndex = 0;
        foreach (var input in new[] { _quantity, _unitPrice })
        {
            input.BackColor = UiTheme.Elevated;
            input.ForeColor = UiTheme.Ink;
        }
        _date.MaxDate = DateTime.Today;
        _date.Value = DateTime.Today;

        // Campos curtos lado a lado: as ações ficam visíveis sem rolagem no tamanho padrão da janela.
        var editor = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, BackColor = UiTheme.Surface };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        void AddWide(Control control, int row)
        {
            editor.Controls.Add(control, 0, row);
            editor.SetColumnSpan(control, 2);
        }
        AddWide(UiTheme.Label("NOVA OPERAÇÃO", 9, true), 0);
        AddWide(FieldLabel("Ativo"), 1);
        AddWide(_asset, 2);
        // Cotação atual (sugerida como preço unitário) e quantidade em carteira do ativo escolhido.
        _assetHint.ForeColor = UiTheme.InkMuted;
        _assetHint.Margin = new Padding(3, 4, 0, 0);
        _assetHint.MaximumSize = new Size(290, 0);
        AddWide(_assetHint, 3);
        editor.Controls.Add(FieldLabel("Tipo"), 0, 4);
        editor.Controls.Add(FieldLabel("Data"), 1, 4);
        _type.Margin = new Padding(3, 3, 8, 3);
        editor.Controls.Add(_type, 0, 5);
        editor.Controls.Add(_date, 1, 5);
        editor.Controls.Add(FieldLabel("Quantidade"), 0, 6);
        editor.Controls.Add(FieldLabel("Preço unitário"), 1, 6);
        _quantity.Margin = new Padding(3, 3, 8, 3);
        editor.Controls.Add(_quantity, 0, 7);
        editor.Controls.Add(_unitPrice, 1, 7);
        _total.Margin = new Padding(3, 10, 0, 0);
        AddWide(_total, 8);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 16, 0, 8) };
        actions.Controls.Add(_registerButton);
        actions.Controls.Add(_deleteButton);
        AddWide(actions, 9);
        var note = UiTheme.Label("A lista traz o catálogo de ativos do servidor e a sua watchlist. O preço é informado na moeda do ativo. Vendas acima da quantidade em carteira são recusadas pela API.", 8.5f);
        note.ForeColor = UiTheme.InkMuted;
        note.MaximumSize = new Size(290, 0);
        AddWide(note, 10);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Size = new Size(1120, 500), SplitterDistance = 770, Panel1MinSize = 420, Panel2MinSize = 320, BackColor = UiTheme.Border };
        split.Panel1.BackColor = UiTheme.Surface;
        split.Panel1.Controls.Add(left);
        split.Panel2.BackColor = UiTheme.Surface;
        split.Panel2.Padding = new Padding(18);
        split.Panel2.AutoScroll = true;
        split.Panel2.Controls.Add(editor);
        return split;
    }

    private Control BuildDistributionTab()
    {
        var panel = Card(4);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // CDI e Ibovespa são consultados junto com o restante da tela, como no cliente web.
        var benchmarkTitle = UiTheme.Label("CARTEIRA E BENCHMARKS", 9, true);
        benchmarkTitle.ForeColor = UiTheme.InkSecondary;
        panel.Controls.Add(benchmarkTitle, 0, 0);
        _comparison.Dock = DockStyle.Fill;
        _comparison.ForeColor = UiTheme.InkSecondary;
        _comparison.Margin = new Padding(0, 6, 0, 16);
        panel.Controls.Add(_comparison, 0, 1);
        var distributionTitle = UiTheme.Label("DISTRIBUIÇÃO", 9, true);
        distributionTitle.ForeColor = UiTheme.InkSecondary;
        panel.Controls.Add(distributionTitle, 0, 2);
        _distribution.Columns.Add(GridCells.Column("Agrupamento", 80f));
        _distribution.Columns.Add(GridCells.Column("Categoria", 140f));
        _distribution.Columns.Add(GridCells.Column("Participação", 80f, typeof(decimal), GridCells.Share, true));
        _distribution.AccessibleName = "Distribuição da carteira";
        panel.Controls.Add(_distribution, 0, 3);
        _distributionStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_distributionStatus, 0, 4);
        return panel;
    }

    private Control BuildSummaryTab()
    {
        var panel = Card(4);
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = UiTheme.Label("RESUMO DIÁRIO DA CARTEIRA · IA", 9, true);
        title.ForeColor = UiTheme.Violet;
        panel.Controls.Add(title, 0, 0);
        _summaryText.BackColor = UiTheme.Surface;
        _summaryText.ForeColor = UiTheme.Ink;
        _summaryText.Font = UiTheme.Font(10);
        _summaryText.AccessibleName = "Resumo diário da carteira gerado por IA";
        panel.Controls.Add(_summaryText, 0, 1);
        _summaryNotice.Dock = DockStyle.Fill;
        _summaryNotice.ForeColor = UiTheme.InkMuted;
        _summaryNotice.Margin = new Padding(0, 8, 0, 0);
        panel.Controls.Add(_summaryNotice, 0, 2);
        _summaryStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_summaryStatus, 0, 3);
        return panel;
    }

    private void LoadAll()
    {
        if (Busy || !IsCurrentSession) return;
        _loading = 6;
        UpdateActions();
        SetStatus(_positionStatus, "Carregando posições…");
        SetStatus(_operationStatus, "Carregando operações…");
        SetStatus(_distributionStatus, "Carregando distribuição…");
        SetStatus(_comparison, "Carregando comparação com CDI e Ibovespa…");
        SetStatus(_summaryStatus, "Carregando resumo diário…");
        // Cada seção falha de forma independente: uma fonte indisponível não esconde as demais.
        LoadSection(async token => BindPositions(await _api.GetPositionsAsync(token)), _positionStatus, ForgetHoldings);
        LoadSection(async token => BindProfitability(await _api.GetProfitabilityAsync(token)), _positionStatus, ClearProfitability);
        LoadSection(async token => BindOperations(await _api.GetOperationsAsync(token)), _operationStatus);
        LoadSection(async token => BindDistribution(await _api.GetDistributionAsync(token)), _distributionStatus);
        LoadSection(LoadBenchmarksAsync, _comparison);
        LoadSection(async token => BindSummary(await _api.GetNotificationsAsync(cancellationToken: token)), _summaryStatus, ClearSummary);
    }

    private void LoadSection(Func<CancellationToken, Task> load, Label status, Action? onFailure = null)
    {
        Run(async cancellationToken =>
        {
            try
            {
                await load(cancellationToken);
            }
            finally
            {
                if (IsCurrentSession)
                {
                    _loading--;
                    UpdateActions();
                }
            }
        }, exception =>
        {
            if (IsCurrentSession) onFailure?.Invoke();
            HandleError(exception, status);
        });
    }

    private void BindPositions(IReadOnlyList<PositionResponse> positions)
    {
        if (!IsCurrentSession) return;
        var version = ++_positionsVersion;
        _positions.Rows.Clear();
        foreach (var position in positions.OrderByDescending(position => position.ValorMercadoBrl))
        {
            _tickers[position.AtivoId] = position.Ticker;
            var cost = position.Quantidade * position.PrecoMedio;
            // Lucro não realizado sobre o custo da posição, em pontos percentuais.
            decimal? result = cost > 0 ? position.LucroNaoRealizado / cost * 100 : null;
            // "Var. dia" fica vazia até a cotação do ativo chegar.
            var row = _positions.Rows[_positions.Rows.Add(position.Ticker, position.Quantidade, position.PrecoMedio, position.CotacaoAtual)];
            row.Cells["Valor de mercado"].Value = position.ValorMercado;
            row.Cells["Valor (BRL)"].Value = position.ValorMercadoBrl;
            row.Cells["Lucro não realizado"].Value = position.LucroNaoRealizado;
            row.Cells["Resultado"].Value = result;
            row.Cells["Lucro realizado"].Value = position.LucroRealizado;
            row.SetFormat(GridCells.Money(Currency(position.AtivoId)), "Preço médio", "Cotação", "Valor de mercado", "Lucro não realizado", "Lucro realizado");
            row.SetFormat(GridCells.Money("BRL"), "Valor (BRL)");
            row.Cells["Lucro não realizado"].Style.ForeColor = row.Cells["Resultado"].Style.ForeColor = Tone(position.LucroNaoRealizado);
            row.Tag = position;
        }
        // As posições informam o ticker de ativos que já saíram da watchlist.
        foreach (DataGridViewRow row in _operations.Rows) row.Cells["Ativo"].Value = Ticker(((OperationResponse)row.Tag!).AtivoId);
        _held = positions.GroupBy(position => position.AtivoId).ToDictionary(group => group.Key, group => group.Sum(position => position.Quantidade));
        ShowAssetHint();
        SetStatus(_positionStatus, PositionSummary(positions.Count, null));
        LoadPositionQuotes(positions, version);
    }

    private static string PositionSummary(int count, decimal? dayChange) => count == 0
        ? "Nenhuma posição aberta. Registre operações na aba Operações."
        : $"{count} posição(ões) aberta(s)" +
          (dayChange is { } change ? $" · Variação do dia: {change.ToString(GridCells.SignedPercent, CultureInfo.CurrentCulture)}, ponderada pelo valor em BRL" : "") +
          ". Ativos sem cotação disponível são omitidos pela API.";

    // A posição não informa a variação do dia: ela vem da cotação de cada ativo, como no cliente web.
    private void LoadPositionQuotes(IReadOnlyList<PositionResponse> positions, int version)
    {
        if (positions.Count == 0) return;
        Run(async cancellationToken =>
        {
            var quotes = await Task.WhenAll(positions.Select(position => _api.GetQuoteAsync(position.AtivoId, cancellationToken).OrNullAsync(cancellationToken)));
            if (!IsCurrentSession || version != _positionsVersion) return;
            decimal weighted = 0, weight = 0;
            for (var i = 0; i < positions.Count; i++)
            {
                if (quotes[i] is not { } quote)
                {
                    _quotes.Remove(positions[i].AtivoId);
                    continue;
                }
                _quotes[positions[i].AtivoId] = quote;
                weighted += quote.VariacaoPercentual * positions[i].ValorMercadoBrl;
                weight += positions[i].ValorMercadoBrl;
            }
            foreach (DataGridViewRow row in _positions.Rows)
            {
                var cell = row.Cells["Var. dia"];
                cell.Value = _quotes.TryGetValue(((PositionResponse)row.Tag!).AtivoId, out var quote) ? quote.VariacaoPercentual : null;
                cell.Style.ForeColor = cell.Value is decimal change ? Tone(change) : Color.Empty;
            }
            ShowAssetHint();
            // Uma falha já exibida nesta linha (posições ou rentabilidade) não é substituída pelo resumo.
            if (weight > 0 && _positionStatus.ForeColor != UiTheme.Error)
                SetStatus(_positionStatus, PositionSummary(positions.Count, weighted / weight));
        }, exception => HandleError(exception, _positionStatus));
    }

    private void ForgetHoldings()
    {
        _held = null;
        ShowAssetHint();
    }

    private void LoadCatalog()
    {
        if (!IsCurrentSession) return;
        Run(async cancellationToken =>
        {
            var catalog = await _api.GetCatalogAsync(cancellationToken);
            if (IsCurrentSession) AddAssets(catalog);
        }, exception => HandleError(exception, _status));
    }

    private void AddAssets(IEnumerable<AssetResponse> assets)
    {
        var added = false;
        foreach (var asset in assets)
        {
            if (!_assets.TryAdd(asset.Id, asset)) continue;
            _tickers.TryAdd(asset.Id, asset.Ticker);
            added = true;
        }
        if (!added) return;

        var selected = (_asset.SelectedItem as AssetChoice)?.Asset.Id;
        _rebindingAssets = true;
        try
        {
            _asset.BeginUpdate();
            _asset.Items.Clear();
            foreach (var asset in _assets.Values.OrderBy(asset => asset.Ticker)) _asset.Items.Add(new AssetChoice(asset));
            _asset.EndUpdate();
            if (selected is { } id) _asset.SelectedIndex = _asset.Items.Cast<AssetChoice>().ToList().FindIndex(choice => choice.Asset.Id == id);
        }
        finally
        {
            _rebindingAssets = false;
        }
        foreach (DataGridViewRow row in _operations.Rows) row.Cells["Ativo"].Value = Ticker(((OperationResponse)row.Tag!).AtivoId);
        if (selected is null && _asset.Items.Count > 0) _asset.SelectedIndex = 0;
        UpdateActions();
    }

    private void LoadAssetQuote()
    {
        _assetQuoteCancellation?.Cancel();
        ShowAssetHint();
        if (_asset.SelectedItem is not AssetChoice choice || !IsCurrentSession) return;
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _assetQuoteCancellation = request;
            try
            {
                var quote = await _api.GetQuoteAsync(choice.Asset.Id, request.Token).OrNullAsync(request.Token);
                if (quote is null || !IsCurrentSession || request.IsCancellationRequested) return;
                _quotes[choice.Asset.Id] = quote;
                ShowAssetHint();
                // A cotação atual só substitui o preço enquanto ele estiver vazio ou for a sugestão anterior.
                if (_unitPrice.Value == 0 || _unitPrice.Value == _suggestedPrice)
                {
                    _unitPrice.Value = quote.Preco;
                    _suggestedPrice = _unitPrice.Value;
                }
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_assetQuoteCancellation, request)) _assetQuoteCancellation = null;
            }
        }, exception => HandleError(exception, _status));
    }

    private void ShowTotal()
    {
        var total = Typed(_quantity) * Typed(_unitPrice);
        _total.Text = total > 0 && _asset.SelectedItem is AssetChoice choice ? $"Valor total: {Money(choice.Asset.Moeda, total)}" : "";
    }

    private static decimal Typed(NumericUpDown input) =>
        decimal.TryParse(input.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && value > 0 ? value : 0;

    private void ShowAssetHint()
    {
        var parts = new List<string>();
        if (_asset.SelectedItem is AssetChoice choice)
        {
            if (_quotes.TryGetValue(choice.Asset.Id, out var quote)) parts.Add($"Cotação atual: {Money(choice.Asset.Moeda, quote.Preco)}");
            if (_held is not null) parts.Add($"Em carteira: {_held.GetValueOrDefault(choice.Asset.Id).ToString(GridCells.Number, CultureInfo.CurrentCulture)}");
        }
        _assetHint.Text = string.Join("   ·   ", parts);
    }

    private async Task LoadBenchmarksAsync(CancellationToken cancellationToken)
    {
        BenchmarkComparisonResponse cdi, ibovespa;
        try
        {
            // As duas comparações cobrem o mesmo período: desde a primeira operação registrada.
            var cdiTask = _api.GetBenchmarkComparisonAsync(InsightFlowApiClient.BenchmarkCdi, cancellationToken);
            var ibovespaTask = _api.GetBenchmarkComparisonAsync(InsightFlowApiClient.BenchmarkIbovespa, cancellationToken);
            cdi = await cdiTask;
            ibovespa = await ibovespaTask;
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            if (IsCurrentSession) SetStatus(_comparison, "Registre ao menos uma operação para comparar a carteira com o CDI e o Ibovespa.");
            return;
        }
        if (!IsCurrentSession) return;
        static string Index(BenchmarkComparisonResponse comparison) =>
            comparison.RentabilidadeBenchmarkPercentual is { } value ? Percent(value) : "indisponível no momento";
        _comparison.ForeColor = UiTheme.InkSecondary;
        _comparison.Text = $"Carteira: {Percent(cdi.RentabilidadeCarteiraPercentual)}   ·   CDI: {Index(cdi)}   ·   Ibovespa: {Index(ibovespa)}\n" +
                           "Período desde a primeira operação registrada.";
    }

    // O resumo diário é gerado pelo agendador do backend e entregue como notificação RESUMO_DIARIO.
    private void BindSummary(IReadOnlyList<NotificationResponse> notifications)
    {
        if (!IsCurrentSession) return;
        var latest = notifications.Where(item => item.Tipo == "RESUMO_DIARIO").MaxBy(item => item.CriadoEm);
        if (latest is null)
        {
            ClearSummary();
            SetStatus(_summaryStatus, "Nenhum resumo gerado ainda. O servidor gera o resumo diário da carteira por IA ao fim do pregão e ele aparece aqui.");
            return;
        }
        _summaryText.Text = (NotificationsForm.ContextText(latest.Contexto, "texto") ?? latest.Mensagem).ReplaceLineEndings("\r\n");
        _summaryNotice.Text = AiNotice.OrDefault(NotificationsForm.ContextText(latest.Contexto, "aviso_legal"));
        SetStatus(_summaryStatus, $"Gerado pelo servidor em {latest.CriadoEm.LocalDateTime:dd/MM/yyyy HH:mm}. Também aparece em Notificações.");
    }

    private void ClearSummary()
    {
        _summaryText.Clear();
        _summaryNotice.Text = "";
    }

    private void BindProfitability(ProfitabilityResponse profitability)
    {
        if (!IsCurrentSession) return;
        _costBase.Text = Money("BRL", profitability.CustoBaseBrl);
        _marketValue.Text = Money("BRL", profitability.ValorMercadoBrl);
        _unrealized.Text = Money("BRL", profitability.LucroNaoRealizadoBrl);
        _unrealized.ForeColor = Tone(profitability.LucroNaoRealizadoBrl);
        _realized.Text = Money("BRL", profitability.LucroRealizadoBrl);
        _realized.ForeColor = Tone(profitability.LucroRealizadoBrl);
        _return.Text = Percent(profitability.Percentual);
        _return.ForeColor = Tone(profitability.Percentual);
    }

    // Uma atualização que falha não deixa os totais anteriores na tela como se fossem atuais.
    private void ClearProfitability()
    {
        foreach (var metric in new[] { _costBase, _marketValue, _unrealized, _realized, _return })
        {
            metric.Text = "Indisponível";
            metric.ForeColor = UiTheme.InkMuted;
        }
    }

    private void BindOperations(IEnumerable<OperationResponse> operations, Guid? preferredId = null)
    {
        if (!IsCurrentSession) return;
        var list = operations.OrderByDescending(operation => operation.Data).ThenByDescending(operation => operation.CriadoEm).ToArray();
        _operations.Rows.Clear();
        foreach (var operation in list)
        {
            var index = _operations.Rows.Add(operation.Data.ToDateTime(TimeOnly.MinValue), Ticker(operation.AtivoId),
                operation.Tipo == InsightFlowApiClient.OperationBuy ? "Compra" : "Venda", operation.Quantidade,
                operation.PrecoUnitario, operation.Quantidade * operation.PrecoUnitario);
            _operations.Rows[index].SetFormat(GridCells.Money(Currency(operation.AtivoId)), "Preço unitário", "Total");
            _operations.Rows[index].Tag = operation;
        }
        var selected = _operations.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ((OperationResponse)row.Tag!).Id == preferredId)
                       ?? _operations.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (selected is not null) _operations.CurrentCell = selected.Cells[0];
        SetStatus(_operationStatus, list.Length == 0 ? "Nenhuma operação registrada." : $"{list.Length} operação(ões) registrada(s).");
        UpdateActions();
    }

    private void BindDistribution(DistributionResponse distribution)
    {
        if (!IsCurrentSession) return;
        _distribution.Rows.Clear();
        foreach (var (group, values) in new[] { ("Classe", distribution.PorClasse), ("Setor", distribution.PorSetor), ("Moeda", distribution.PorMoeda) })
        {
            foreach (var (category, share) in values.OrderByDescending(pair => pair.Value))
            {
                _distribution.Rows.Add(group, group == "Setor" && category == "N/A" ? "Setor não informado" : category, share * 100);
            }
        }
        SetStatus(_distributionStatus, _distribution.Rows.Count == 0
            ? "Sem posições com cotação para calcular a distribuição."
            : "Participação sobre o valor de mercado em BRL das posições com cotação disponível.");
    }

    private void Register()
    {
        if (Busy || !IsCurrentSession) return;
        if (_asset.SelectedItem is not AssetChoice asset || _type.SelectedItem is not TypeChoice type)
        {
            SetStatus(_status, "Selecione o ativo e o tipo da operação.", true);
            return;
        }
        if (_quantity.Value <= 0 || _unitPrice.Value <= 0)
        {
            SetStatus(_status, "Informe quantidade e preço unitário maiores que zero.", true);
            return;
        }

        var quantity = _quantity.Value;
        var price = _unitPrice.Value;
        var date = DateOnly.FromDateTime(_date.Value);
        _writing = true;
        UpdateActions();
        SetStatus(_status, "Registrando operação…");
        Run(async cancellationToken =>
        {
            try
            {
                var saved = await _api.RegisterOperationAsync(asset.Asset.Id, type.Value, quantity, price, date, cancellationToken);
                if (!IsCurrentSession) return;
                SetStatus(_status, $"{(saved.Tipo == InsightFlowApiClient.OperationBuy ? "Compra" : "Venda")} de {asset.Asset.Ticker} registrada.");
                var operations = _operations.Rows.Cast<DataGridViewRow>().Select(row => (OperationResponse)row.Tag!).Append(saved);
                BindOperations(operations, saved.Id);
                _quantity.Value = 0;
            }
            finally
            {
                if (IsCurrentSession)
                {
                    _writing = false;
                    UpdateActions();
                }
            }
            // As posições dependem da nova operação; falhas dessa leitura ficam nas seções, não no status do salvamento.
            if (IsCurrentSession) LoadAll();
        }, exception => HandleError(exception, _status,
            $"A venda ultrapassa a quantidade de {asset.Asset.Ticker} em carteira na data informada."));
    }

    private void DeleteSelected()
    {
        var selected = SelectedOperation;
        if (Busy || selected is null || !IsCurrentSession) return;
        var description = $"{(selected.Tipo == InsightFlowApiClient.OperationBuy ? "compra" : "venda")} de {Ticker(selected.AtivoId)} em {selected.Data:dd/MM/yyyy}";
        if (MessageBox.Show(this, $"Excluir a {description}?", "Excluir operação",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _writing = true;
        UpdateActions();
        SetStatus(_status, "Excluindo operação…");
        Run(async cancellationToken =>
        {
            try
            {
                await _api.DeleteOperationAsync(selected.Id, cancellationToken);
                if (!IsCurrentSession) return;
                SetStatus(_status, $"Operação excluída: {description}.");
                BindOperations(_operations.Rows.Cast<DataGridViewRow>().Select(row => (OperationResponse)row.Tag!)
                    .Where(operation => operation.Id != selected.Id).ToArray());
            }
            finally
            {
                if (IsCurrentSession)
                {
                    _writing = false;
                    UpdateActions();
                }
            }
            if (IsCurrentSession) LoadAll();
        }, exception => HandleError(exception, _status,
            $"Sem essa operação, uma venda posterior de {Ticker(selected.AtivoId)} ficaria maior que a quantidade em carteira."));
    }

    private void UpdateActions()
    {
        UiTheme.SetButtonEnabled(_refreshButton, !Busy);
        UiTheme.SetButtonEnabled(_registerButton, !Busy && _asset.SelectedItem is AssetChoice, true);
        UiTheme.SetButtonEnabled(_deleteButton, !Busy && SelectedOperation is not null);
        _operations.Enabled = !_writing;
    }

    private void HandleError(Exception exception, Label target, string? insufficientQuantityMessage = null)
    {
        if (!IsCurrentSession) return;
        if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            SessionExpired = true;
            _session.Clear();
            Close();
            return;
        }
        // Quantidade insuficiente chega como 422 com apenas o UUID do ativo no detail.
        var message = insufficientQuantityMessage is not null
                      && exception is ApiException { StatusCode: HttpStatusCode.UnprocessableEntity } api
                      && Guid.TryParse(api.Message, out _)
            ? insufficientQuantityMessage
            : UiErrors.Message(exception);
        SetStatus(target, message, true);
    }

    private string Currency(Guid assetId) => _assets.TryGetValue(assetId, out var asset) ? asset.Moeda : "";
    private string Ticker(Guid assetId) => _tickers.TryGetValue(assetId, out var ticker) ? ticker : $"Ativo {assetId.ToString()[..8]}";

    private static string Money(string currency, decimal value) => value.ToString(GridCells.Money(currency), CultureInfo.CurrentCulture);
    // A API envia frações (0,12 = 12%).
    private static string Percent(decimal fraction) => (fraction * 100).ToString(GridCells.SignedPercent, CultureInfo.CurrentCulture);
    private static Color Tone(decimal value) => value > 0 ? UiTheme.Up : value < 0 ? UiTheme.Down : UiTheme.Ink;

    private static void SetStatus(Label label, string text, bool error = false)
    {
        label.Text = text;
        label.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }

    private static Label Metric()
    {
        var label = UiTheme.Label("—", 14, true);
        label.Margin = new Padding(0, 2, 0, 0);
        return label;
    }

    private static Control MetricBlock(string title, Label value)
    {
        var block = new TableLayoutPanel { AutoSize = true, RowCount = 2, BackColor = UiTheme.Elevated, Padding = new Padding(12, 8, 18, 8), Margin = new Padding(0, 0, 10, 0) };
        var caption = UiTheme.Label(title, 7.5f, true);
        caption.ForeColor = UiTheme.InkMuted;
        block.Controls.Add(caption, 0, 0);
        block.Controls.Add(value, 0, 1);
        return block;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = UiTheme.Surface, Padding = new Padding(12) };
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel Card(int rows)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            BackColor = UiTheme.Surface,
            Padding = new Padding(8),
            Margin = Padding.Empty
        };
        // Sem estilo, a coluna cresce com a tabela e a última coluna some atrás do painel ao lado.
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return card;
    }

    private static Label FieldLabel(string text)
    {
        var label = UiTheme.Label(text, 8.5f, true);
        label.ForeColor = UiTheme.InkSecondary;
        label.Margin = new Padding(0, 12, 0, 4);
        return label;
    }

    private sealed record AssetChoice(AssetResponse Asset)
    {
        public string Label => $"{Asset.Ticker} — {Asset.Nome}";
    }

    private sealed record TypeChoice(string Value, string Label);
}
