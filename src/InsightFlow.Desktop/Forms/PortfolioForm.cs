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
    private readonly IReadOnlyDictionary<Guid, AssetResponse> _assets;
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
    private readonly ComboBox _benchmark = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Button _compareButton = UiTheme.Button("&Comparar");
    private readonly Label _comparison = UiTheme.Label("Escolha um índice e compare com a rentabilidade da carteira.", 10);
    private readonly Label _status = UiTheme.Label("", 9);
    private int _loading;
    private bool _writing;
    private bool _comparing;

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
        BuildLayout();
        _refreshButton.Click += (_, _) => LoadAll();
        _registerButton.Click += (_, _) => Register();
        _deleteButton.Click += (_, _) => DeleteSelected();
        _compareButton.Click += (_, _) => Compare();
        _operations.SelectionChanged += (_, _) => UpdateActions();
        _asset.SelectedIndexChanged += (_, _) => UpdateActions();
        Shown += (_, _) => LoadAll();
        FormClosed += (_, _) => { _positions.Rows.Clear(); _operations.Rows.Clear(); _distribution.Rows.Clear(); };
        UpdateActions();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Portfólio";
        ClientSize = new Size(1120, 700);
        MinimumSize = new Size(940, 600);
        BackColor = UiTheme.Background;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), BackColor = UiTheme.Background };
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
        foreach (var (name, weight) in new[]
                 {
                     ("Ativo", 60f), ("Quantidade", 75f), ("Preço médio", 85f), ("Cotação", 80f), ("Valor de mercado", 95f),
                     ("Valor (BRL)", 90f), ("Lucro não realizado", 100f), ("Lucro realizado", 90f)
                 })
        {
            _positions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name.ToUpperInvariant(), FillWeight = weight,
                DefaultCellStyle = { Alignment = name == "Ativo" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight }
            });
        }
        _positions.AccessibleName = "Posições da carteira";
        panel.Controls.Add(_positions, 0, 1);
        _positionStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_positionStatus, 0, 2);
        return panel;
    }

    private Control BuildOperationsTab()
    {
        foreach (var (name, weight) in new[] { ("Data", 70f), ("Ativo", 60f), ("Tipo", 55f), ("Quantidade", 75f), ("Preço unitário", 85f), ("Total", 85f) })
        {
            _operations.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name.ToUpperInvariant(), FillWeight = weight,
                DefaultCellStyle = { Alignment = name is "Data" or "Ativo" or "Tipo" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight }
            });
        }
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

        var editor = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, BackColor = UiTheme.Surface };
        editor.Controls.Add(UiTheme.Label("NOVA OPERAÇÃO", 9, true));
        editor.Controls.Add(FieldLabel("Ativo"));
        editor.Controls.Add(_asset);
        editor.Controls.Add(FieldLabel("Tipo"));
        editor.Controls.Add(_type);
        editor.Controls.Add(FieldLabel("Quantidade"));
        editor.Controls.Add(_quantity);
        editor.Controls.Add(FieldLabel("Preço unitário (moeda do ativo)"));
        editor.Controls.Add(_unitPrice);
        editor.Controls.Add(FieldLabel("Data"));
        editor.Controls.Add(_date);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 16, 0, 8) };
        actions.Controls.Add(_registerButton);
        actions.Controls.Add(_deleteButton);
        editor.Controls.Add(actions);
        var note = UiTheme.Label(_assets.Count == 0
            ? "Adicione ativos à watchlist para registrar operações: a API exige o identificador interno do ativo."
            : "Somente ativos da sua watchlist aparecem na lista. Vendas acima da quantidade em carteira são recusadas pela API.", 8.5f);
        note.ForeColor = UiTheme.InkMuted;
        note.MaximumSize = new Size(290, 0);
        editor.Controls.Add(note);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Size = new Size(1040, 500), SplitterDistance = 690, Panel1MinSize = 420, Panel2MinSize = 320, BackColor = UiTheme.Border };
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
        var benchmarkRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.Transparent, WrapContents = true };
        var benchmarkTitle = UiTheme.Label("BENCHMARK", 9, true);
        benchmarkTitle.ForeColor = UiTheme.InkSecondary;
        benchmarkTitle.Margin = new Padding(0, 10, 12, 0);
        benchmarkRow.Controls.Add(benchmarkTitle);
        UiTheme.StyleCombo(_benchmark);
        _benchmark.DisplayMember = nameof(TypeChoice.Label);
        _benchmark.Items.AddRange([new TypeChoice(InsightFlowApiClient.BenchmarkCdi, "CDI"), new TypeChoice(InsightFlowApiClient.BenchmarkIbovespa, "Ibovespa")]);
        _benchmark.SelectedIndex = 0;
        _benchmark.Margin = new Padding(0, 7, 10, 0);
        benchmarkRow.Controls.Add(_benchmark);
        benchmarkRow.Controls.Add(_compareButton);
        panel.Controls.Add(benchmarkRow, 0, 0);
        _comparison.Dock = DockStyle.Fill;
        _comparison.ForeColor = UiTheme.InkSecondary;
        _comparison.Margin = new Padding(0, 6, 0, 16);
        panel.Controls.Add(_comparison, 0, 1);
        var distributionTitle = UiTheme.Label("DISTRIBUIÇÃO", 9, true);
        distributionTitle.ForeColor = UiTheme.InkSecondary;
        panel.Controls.Add(distributionTitle, 0, 2);
        foreach (var (name, weight) in new[] { ("Agrupamento", 80f), ("Categoria", 140f), ("Participação", 80f) })
        {
            _distribution.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name.ToUpperInvariant(), FillWeight = weight,
                DefaultCellStyle = { Alignment = name == "Participação" ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft }
            });
        }
        _distribution.AccessibleName = "Distribuição da carteira";
        panel.Controls.Add(_distribution, 0, 3);
        _distributionStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_distributionStatus, 0, 4);
        return panel;
    }

    private void LoadAll()
    {
        if (Busy || !IsCurrentSession) return;
        _loading = 4;
        UpdateActions();
        SetStatus(_positionStatus, "Carregando posições…");
        SetStatus(_operationStatus, "Carregando operações…");
        SetStatus(_distributionStatus, "Carregando distribuição…");
        // Cada seção falha de forma independente: uma fonte indisponível não esconde as demais.
        LoadSection(async token => BindPositions(await _api.GetPositionsAsync(token)), _positionStatus);
        LoadSection(async token => BindProfitability(await _api.GetProfitabilityAsync(token)), _positionStatus);
        LoadSection(async token => BindOperations(await _api.GetOperationsAsync(token)), _operationStatus);
        LoadSection(async token => BindDistribution(await _api.GetDistributionAsync(token)), _distributionStatus);
    }

    private void LoadSection(Func<CancellationToken, Task> load, Label status)
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
        }, exception => HandleError(exception, status));
    }

    private void BindPositions(IReadOnlyList<PositionResponse> positions)
    {
        if (!IsCurrentSession) return;
        _positions.Rows.Clear();
        foreach (var position in positions.OrderByDescending(position => position.ValorMercadoBrl))
        {
            var currency = Currency(position.AtivoId);
            var index = _positions.Rows.Add(position.Ticker, Number(position.Quantidade), Money(currency, position.PrecoMedio),
                Money(currency, position.CotacaoAtual), Money(currency, position.ValorMercado), Money("BRL", position.ValorMercadoBrl),
                Money(currency, position.LucroNaoRealizado), Money(currency, position.LucroRealizado));
            _positions.Rows[index].Cells["Lucro não realizado"].Style.ForeColor = Tone(position.LucroNaoRealizado);
            _positions.Rows[index].Tag = position;
        }
        SetStatus(_positionStatus, positions.Count == 0
            ? "Nenhuma posição aberta. Registre operações na aba Operações."
            : $"{positions.Count} posição(ões) aberta(s). Ativos sem cotação disponível são omitidos pela API.");
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

    private void BindOperations(IEnumerable<OperationResponse> operations, Guid? preferredId = null)
    {
        if (!IsCurrentSession) return;
        var list = operations.OrderByDescending(operation => operation.Data).ThenByDescending(operation => operation.CriadoEm).ToArray();
        _operations.Rows.Clear();
        foreach (var operation in list)
        {
            var currency = Currency(operation.AtivoId);
            var index = _operations.Rows.Add(operation.Data.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture), Ticker(operation.AtivoId),
                operation.Tipo == InsightFlowApiClient.OperationBuy ? "Compra" : "Venda", Number(operation.Quantidade),
                Money(currency, operation.PrecoUnitario), Money(currency, operation.Quantidade * operation.PrecoUnitario));
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
                _distribution.Rows.Add(group, group == "Setor" && category == "N/A" ? "Setor não informado" : category, Percent(share, false));
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
        }, exception => HandleError(exception, _status));
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
        }, exception => HandleError(exception, _status));
    }

    private void Compare()
    {
        if (_comparing || !IsCurrentSession || _benchmark.SelectedItem is not TypeChoice benchmark) return;
        _comparing = true;
        UpdateActions();
        _comparison.ForeColor = UiTheme.InkSecondary;
        _comparison.Text = $"Consultando {benchmark.Label}…";
        Run(async cancellationToken =>
        {
            try
            {
                var result = await _api.GetBenchmarkComparisonAsync(benchmark.Value, cancellationToken);
                if (!IsCurrentSession) return;
                var index = result.RentabilidadeBenchmarkPercentual is { } value
                    ? Percent(value)
                    : "indisponível no momento";
                _comparison.Text = $"Carteira: {Percent(result.RentabilidadeCarteiraPercentual)}   ·   {benchmark.Label}: {index}\n" +
                                   "Período desde a primeira operação registrada.";
            }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                if (IsCurrentSession) _comparison.Text = "Registre ao menos uma operação para comparar a carteira.";
            }
            finally
            {
                if (IsCurrentSession)
                {
                    _comparing = false;
                    UpdateActions();
                }
            }
        }, exception => HandleError(exception, _comparison));
    }

    private void UpdateActions()
    {
        UiTheme.SetButtonEnabled(_refreshButton, !Busy);
        UiTheme.SetButtonEnabled(_registerButton, !Busy && _asset.SelectedItem is AssetChoice, true);
        UiTheme.SetButtonEnabled(_deleteButton, !Busy && SelectedOperation is not null);
        UiTheme.SetButtonEnabled(_compareButton, !_comparing);
        _operations.Enabled = !_writing;
    }

    private void HandleError(Exception exception, Label target)
    {
        if (!IsCurrentSession) return;
        if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            SessionExpired = true;
            _session.Clear();
            Close();
            return;
        }
        SetStatus(target, UiErrors.Message(exception), true);
    }

    private string Currency(Guid assetId) => _assets.TryGetValue(assetId, out var asset) ? asset.Moeda : "";
    private string Ticker(Guid assetId) => _assets.TryGetValue(assetId, out var asset) ? asset.Ticker : $"Ativo {assetId.ToString()[..8]}";

    private static string Number(decimal value) => value.ToString("#,0.########", CultureInfo.CurrentCulture);
    private static string Money(string currency, decimal value) =>
        (currency.Length == 0 ? "" : currency + " ") + value.ToString("#,0.00######", CultureInfo.CurrentCulture);
    // A API envia frações (0,12 = 12%).
    private static string Percent(decimal fraction, bool signed = true) =>
        (fraction * 100).ToString(signed ? "+#,0.00;-#,0.00;0.00" : "#,0.00", CultureInfo.CurrentCulture) + "%";
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

    private static TableLayoutPanel Card(int rows) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = rows,
        BackColor = UiTheme.Surface,
        Padding = new Padding(8),
        Margin = Padding.Empty
    };

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
