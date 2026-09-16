using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Forms;

internal sealed partial class MainForm
{
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Buscar ativo por ticker ou nome", MaxLength = 120, AutoSize = false, Height = 38 };
    private readonly Button _searchButton = UiTheme.Button("&Buscar", true);
    private readonly Button _addButton = UiTheme.Button("&Adicionar à watchlist", true);
    private readonly ListBox _searchResults = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
    private readonly Label _searchStatus = UiTheme.Label("Pesquise um ativo para adicioná-lo.", 9);
    private readonly DataGridView _watchGrid = UiTheme.Grid();
    private readonly Button _refreshWatch = UiTheme.Button("Atualizar &lista");
    private readonly Button _removeButton = UiTheme.Button("&Remover");
    private readonly CheckBox _notify = new() { Text = "Receber notificações deste ativo", AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
    private readonly Button _saveNotification = UiTheme.Button("Salvar &preferência");
    private readonly Label _watchStatus = UiTheme.Label("Carregando watchlist…", 9);
    private readonly Label _connection = UiTheme.Label("Verificando API…", 9);
    private readonly Button _checkConnection = UiTheme.Button("Verificar API");
    private readonly Label _assetTitle = UiTheme.Label("Selecione um ativo", 20, true);
    private readonly Label _assetDescription = UiTheme.Label("Os detalhes dos ativos acompanhados aparecem aqui.", 9);
    private readonly Label _quotePrice = UiTheme.Label("—", 26, true);
    private readonly Label _quoteChange = UiTheme.Label("Variação: —", 11);
    private readonly Label _quoteRange = UiTheme.Label("Mínima / máxima do dia: —", 10);
    private readonly Label _quoteVolume = UiTheme.Label("Volume: —", 10);
    private readonly Label _quoteStatus = UiTheme.Label("", 9);
    private readonly Label _historyStatus = UiTheme.Label("Selecione um ativo da watchlist para consultar o histórico.", 9);
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly Button _refreshDetails = UiTheme.Button("Atualizar &dados");
    private readonly DataGridView _history = UiTheme.Grid();

    private void BuildLayout(Uri apiUrl)
    {
        Text = "InsightFlow — Acompanhamento de ativos";
        ClientSize = new Size(1360, 850);
        MinimumSize = new Size(1120, 740);
        BackColor = UiTheme.Background;
        UiTheme.StyleInput(_search);
        UiTheme.StyleCombo(_period);
        _searchResults.BackColor = UiTheme.Elevated;
        _searchResults.ForeColor = UiTheme.InkSecondary;
        _searchResults.Font = UiTheme.Font(9.5f);
        _searchResults.ItemHeight = 30;
        _notify.ForeColor = UiTheme.InkSecondary;
        _notify.BackColor = Color.Transparent;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UiTheme.Background
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildSidebar(), 0, 0);
        shell.Controls.Add(BuildWorkspace(apiUrl), 1, 0);
        Controls.Add(shell);
    }

    private Control BuildSidebar()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Surface,
            Padding = new Padding(10, 8, 10, 12),
            Margin = Padding.Empty
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));

        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var mark = UiTheme.Label("●", 17, true);
        mark.ForeColor = UiTheme.Accent;
        mark.Anchor = AnchorStyles.None;
        brand.Controls.Add(mark, 0, 0);
        var brandText = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        brandText.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var name = UiTheme.Label("InsightFlow", 12, true);
        name.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        var market = UiTheme.Label("B3 · CRIPTO", 7.5f, true);
        market.ForeColor = UiTheme.InkMuted;
        market.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        brandText.Controls.Add(name, 0, 0);
        brandText.Controls.Add(market, 0, 1);
        brand.Controls.Add(brandText, 1, 0);
        sidebar.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0)
        };
        AddNavHeading(nav, "PRINCIPAL");
        AddNavButton(nav, "  Visão geral", true);
        AddUnavailableNav(nav, "  Portfólio");
        AddNavHeading(nav, "MONITORAMENTO");
        AddUnavailableNav(nav, "  Alertas");
        var searchNav = AddNavButton(nav, "  Buscar ativos");
        searchNav.Click += (_, _) => _search.Focus();
        AddNavHeading(nav, "FERRAMENTAS");
        AddUnavailableNav(nav, "  Chat IA");
        AddUnavailableNav(nav, "  Configurações");
        sidebar.Controls.Add(nav, 0, 1);

        var profile = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = UiTheme.Elevated,
            Padding = new Padding(10, 8, 8, 8),
            Margin = new Padding(4)
        };
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profile.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var user = UiTheme.Label("Sessão ativa\nToken em memória", 8.5f, true);
        user.ForeColor = UiTheme.InkSecondary;
        var logout = UiTheme.Button("Sair");
        logout.MinimumSize = new Size(58, 34);
        logout.Click += (_, _) => EndSession(false);
        profile.Controls.Add(user, 0, 0);
        profile.Controls.Add(logout, 1, 0);
        sidebar.Controls.Add(profile, 0, 2);
        return sidebar;
    }

    private Control BuildWorkspace(Uri apiUrl)
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Background,
            Padding = new Padding(18, 14, 18, 8),
            Margin = Padding.Empty
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        workspace.Controls.Add(BuildTopbar(), 0, 0);
        workspace.Controls.Add(BuildMainContent(), 0, 1);
        var footer = UiTheme.Label($"API configurada: {apiUrl}", 8);
        footer.ForeColor = UiTheme.InkMuted;
        footer.Margin = new Padding(6, 6, 0, 0);
        workspace.Controls.Add(footer, 0, 2);
        return workspace;
    }

    private Control BuildTopbar()
    {
        var topbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = UiTheme.Surface,
            Padding = new Padding(14, 9, 10, 9),
            Margin = new Padding(0, 0, 0, 8)
        };
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _search.Margin = new Padding(0, 1, 10, 0);
        _search.AccessibleName = "Pesquisar ticker ou nome de ativo";
        topbar.Controls.Add(_search, 0, 0);
        topbar.Controls.Add(_searchButton, 1, 0);
        _connection.Margin = new Padding(10, 10, 8, 0);
        topbar.Controls.Add(_connection, 2, 0);
        _checkConnection.MinimumSize = new Size(105, 36);
        topbar.Controls.Add(_checkConnection, 3, 0);
        return topbar;
    }

    private Control BuildMainContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Background,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent, Padding = new Padding(4, 12, 0, 0) };
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.Controls.Add(UiTheme.Label("Visão geral", 20, true), 0, 0);
        var subtitle = UiTheme.Label("Acompanhe sua watchlist e consulte o histórico dos ativos.", 9.5f);
        subtitle.ForeColor = UiTheme.InkMuted;
        heading.Controls.Add(subtitle, 0, 1);
        content.Controls.Add(heading, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 14,
            BackColor = UiTheme.Background,
            Margin = Padding.Empty,
            Size = new Size(1040, 640),
            SplitterDistance = 400,
            Panel1MinSize = 340,
            Panel2MinSize = 440
        };
        split.Panel1.Controls.Add(BuildWatchPanel());
        split.Panel2.Controls.Add(BuildDetailPanel());
        content.Controls.Add(split, 0, 1);
        return content;
    }

    private Control BuildWatchPanel()
    {
        var panel = Card(12);
        panel.RowCount = 10;
        for (var i = 0; i < panel.RowCount; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles[1] = new RowStyle(SizeType.Absolute, 92);
        panel.RowStyles[6] = new RowStyle(SizeType.Percent, 100);
        panel.Controls.Add(SectionTitle("BUSCA DE ATIVOS", "Selecione um resultado antes de adicionar"), 0, 0);
        _searchResults.AccessibleName = "Resultados da busca";
        _searchResults.DisplayMember = "Display";
        panel.Controls.Add(_searchResults, 0, 1);
        _searchStatus.Dock = DockStyle.Fill;
        _searchStatus.MaximumSize = new Size(550, 0);
        panel.Controls.Add(_searchStatus, 0, 2);
        _addButton.Enabled = false;
        _addButton.Dock = DockStyle.Top;
        panel.Controls.Add(_addButton, 0, 3);

        var watchHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 14, 0, 6) };
        watchHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        watchHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        watchHeading.Controls.Add(UiTheme.Label("Watchlist", 13, true), 0, 0);
        watchHeading.Controls.Add(_refreshWatch, 1, 0);
        panel.Controls.Add(watchHeading, 0, 4);
        var hint = UiTheme.Label("Ativos acompanhados pela sua conta", 8.5f);
        hint.ForeColor = UiTheme.InkMuted;
        panel.Controls.Add(hint, 0, 5);
        _watchGrid.AccessibleName = "Ativos na sua watchlist";
        _watchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ticker", HeaderText = "ATIVO", FillWeight = 32 });
        _watchGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nome", HeaderText = "NOME", FillWeight = 68 });
        panel.Controls.Add(_watchGrid, 0, 6);
        _watchStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_watchStatus, 0, 7);
        panel.Controls.Add(_notify, 0, 8);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        actions.Controls.Add(_saveNotification);
        actions.Controls.Add(_removeButton);
        panel.Controls.Add(actions, 0, 9);
        return panel;
    }

    private Control BuildDetailPanel()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Background,
            Margin = Padding.Empty
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        container.RowStyles.Add(new RowStyle(SizeType.Absolute, 375));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        container.Controls.Add(BuildQuoteCard(), 0, 0);
        container.Controls.Add(BuildHistoryCard(), 0, 1);
        return container;
    }

    private Control BuildQuoteCard()
    {
        var panel = Card(18);
        panel.Margin = new Padding(0, 0, 0, 12);
        panel.RowCount = 7;
        for (var i = 0; i < panel.RowCount; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(_assetTitle, 0, 0);
        var pill = UiTheme.Label("COTAÇÃO", 8, true);
        pill.ForeColor = UiTheme.Accent;
        pill.BackColor = Color.FromArgb(13, 32, 56);
        pill.Padding = new Padding(8, 4, 8, 4);
        header.Controls.Add(pill, 1, 0);
        panel.Controls.Add(header, 0, 0);
        _assetDescription.Dock = DockStyle.Fill;
        _assetDescription.ForeColor = UiTheme.InkMuted;
        panel.Controls.Add(_assetDescription, 0, 1);
        panel.Controls.Add(_quotePrice, 0, 2);
        panel.Controls.Add(_quoteChange, 0, 3);
        var quoteInfo = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        _quoteRange.Margin = new Padding(0, 6, 20, 4);
        quoteInfo.Controls.Add(_quoteRange);
        quoteInfo.Controls.Add(_quoteVolume);
        panel.Controls.Add(quoteInfo, 0, 4);
        _quoteStatus.Dock = DockStyle.Fill;
        _quoteStatus.ForeColor = UiTheme.InkMuted;
        panel.Controls.Add(_quoteStatus, 0, 5);
        return panel;
    }

    private Control BuildHistoryCard()
    {
        var panel = Card(14);
        panel.RowCount = 3;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var historyHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 8), BackColor = Color.Transparent };
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        historyHeader.Controls.Add(SectionTitle("HISTÓRICO OHLCV", "Dados conforme recebidos da API"), 0, 0);
        _period.AccessibleName = "Período do histórico";
        _period.Margin = new Padding(0, 5, 10, 0);
        _period.DisplayMember = nameof(HistoryPeriod.Label);
        _period.Items.AddRange(new object[]
        {
            new HistoryPeriod("1D", "1 dia"), new HistoryPeriod("1S", "1 semana"),
            new HistoryPeriod("1M", "1 mês"), new HistoryPeriod("3M", "3 meses"),
            new HistoryPeriod("1A", "1 ano"), new HistoryPeriod("5A", "5 anos")
        });
        _period.SelectedIndex = 2;
        historyHeader.Controls.Add(_period, 1, 0);
        historyHeader.Controls.Add(_refreshDetails, 2, 0);
        panel.Controls.Add(historyHeader, 0, 0);
        _history.AccessibleName = "Histórico de preços e volume";
        foreach (var name in new[] { "Data", "Abertura", "Máxima", "Mínima", "Fechamento", "Volume" })
        {
            _history.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name.ToUpperInvariant(),
                MinimumWidth = name == "Data" ? 90 : 65,
                FillWeight = name == "Data" ? 115 : 100,
                ValueType = name == "Data" ? typeof(DateTime) : typeof(decimal),
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = name == "Data" ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleRight,
                    Format = name == "Data" ? "dd/MM/yyyy" : "#,0.########"
                }
            });
        }
        panel.Controls.Add(_history, 0, 1);
        _historyStatus.Dock = DockStyle.Fill;
        panel.Controls.Add(_historyStatus, 0, 2);
        return panel;
    }

    private static TableLayoutPanel Card(int padding) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        BackColor = UiTheme.Surface,
        Padding = new Padding(padding),
        Margin = Padding.Empty
    };

    private static Control SectionTitle(string title, string subtitle)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, AutoSize = true, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var titleLabel = UiTheme.Label(title, 9, true);
        titleLabel.ForeColor = UiTheme.InkSecondary;
        var subtitleLabel = UiTheme.Label(subtitle, 8);
        subtitleLabel.ForeColor = UiTheme.InkMuted;
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(subtitleLabel, 0, 1);
        return layout;
    }

    private static void AddNavHeading(FlowLayoutPanel nav, string text)
    {
        var heading = UiTheme.Label(text, 7.5f, true);
        heading.ForeColor = UiTheme.InkMuted;
        heading.Width = 190;
        heading.Margin = new Padding(12, 16, 0, 4);
        nav.Controls.Add(heading);
    }

    private static Button AddNavButton(FlowLayoutPanel nav, string text, bool active = false)
    {
        var button = UiTheme.NavigationButton(text, active);
        button.Width = 198;
        nav.Controls.Add(button);
        return button;
    }

    private static void AddUnavailableNav(FlowLayoutPanel nav, string text)
    {
        var button = AddNavButton(nav, text);
        button.BackColor = UiTheme.Surface;
        button.ForeColor = UiTheme.InkMuted;
        button.Enabled = false;
    }

    private sealed record HistoryPeriod(string Value, string Label);
}
