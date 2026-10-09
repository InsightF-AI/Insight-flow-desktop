using System.Globalization;
using System.Net;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed class AlertsForm : AsyncForm
{
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly int _sessionVersion;
    private readonly DataGridView _grid = UiTheme.Grid();
    private readonly TextBox _ticker = new() { MaxLength = 20, CharacterCasing = CharacterCasing.Upper, Dock = DockStyle.Fill };
    private readonly ComboBox _condition = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _target = new() { DecimalPlaces = 8, Minimum = 0, Maximum = decimal.MaxValue, ThousandsSeparator = true, Dock = DockStyle.Fill };
    private readonly CheckBox _enabled = new() { Text = "Alerta habilitado", AutoSize = true };
    private readonly Button _newButton = UiTheme.Button("&Novo");
    private readonly Button _saveButton = UiTheme.Button("&Criar alerta", true);
    private readonly Button _deleteButton = UiTheme.Button("&Excluir");
    private readonly Button _refreshButton = UiTheme.Button("&Atualizar");
    private readonly Label _currentPrice = UiTheme.Label("", 8.5f);
    private readonly Label _status = UiTheme.Label("Carregando alertas…", 9);
    // Preço atual por ativo, para mostrar a distância até o alvo; ativos sem cotação ficam fora.
    private readonly Dictionary<Guid, decimal> _prices = [];
    private bool _busy;
    private bool _binding;
    private bool _creating = true;
    private bool _summaryShown;

    public bool SessionExpired { get; private set; }
    private AlertResponse? SelectedAlert => _grid.CurrentRow?.Tag as AlertResponse;
    private AlertResponse? EditingAlert => _creating ? null : SelectedAlert;
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public AlertsForm(InsightFlowApiClient api, UserSession session)
    {
        _api = api;
        _session = session;
        _sessionVersion = session.Version;
        BuildLayout();
        _grid.SelectionChanged += (_, _) => { if (!_binding) BindSelection(); };
        _newButton.Click += (_, _) => BeginNew();
        _saveButton.Click += (_, _) => Save();
        _deleteButton.Click += (_, _) => Delete();
        _refreshButton.Click += (_, _) => LoadAlerts();
        Shown += (_, _) => LoadAlerts();
        FormClosed += (_, _) => { _grid.Rows.Clear(); _prices.Clear(); };
        BeginNew();
        CompleteLayout();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Alertas de preço";
        ClientSize = new Size(1100, 700);
        MinimumSize = new Size(1060, 520);
        BackColor = UiTheme.Background;
        UiTheme.StyleInput(_ticker);
        UiTheme.StyleCombo(_condition);
        _target.BackColor = UiTheme.Elevated;
        _target.ForeColor = UiTheme.Ink;
        _enabled.ForeColor = UiTheme.InkSecondary;
        _enabled.BackColor = Color.Transparent;
        _condition.DisplayMember = nameof(ConditionChoice.Label);
        _condition.Items.AddRange([
            new ConditionChoice(InsightFlowApiClient.PriceGreaterThanOrEqual, "Preço maior ou igual"),
            new ConditionChoice(InsightFlowApiClient.PriceLessThanOrEqual, "Preço menor ou igual")
        ]);
        _condition.SelectedIndex = 0;

        _grid.Columns.Add(GridCells.Column("Ativo", 64f));
        _grid.Columns.Add(GridCells.Column("Condição", 76f));
        _grid.Columns.Add(GridCells.Column("Preço-alvo", 122f, typeof(decimal)));
        _grid.Columns.Add(GridCells.Column("Preço atual", 122f, typeof(decimal)));
        // Quanto o preço atual precisa variar para chegar ao alvo.
        _grid.Columns.Add(GridCells.Column("Distância", 92f, typeof(decimal), GridCells.SignedPercent));
        _grid.Columns.Add(GridCells.Column("Estado", 108f));
        _grid.Columns.Add(GridCells.Column("Último disparo", 114f, typeof(DateTime), GridCells.DateTime));
        _grid.AccessibleName = "Alertas de preço cadastrados";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), BackColor = UiTheme.Background };
        // Sem estilo, a coluna cresce até o tamanho inicial da divisão e a janela menor corta o painel da direita.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = UiTheme.Label("Alertas de preço", 20, true);
        var subtitle = UiTheme.Label("Crie condições e mantenha os alertas que a API deve avaliar.", 9);
        subtitle.ForeColor = UiTheme.InkMuted;
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        titles.Controls.Add(title);
        titles.Controls.Add(subtitle);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_refreshButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Size = new Size(1050, 450), SplitterDistance = 720, Panel1MinSize = 370, Panel2MinSize = 320, BackColor = UiTheme.Border };
        split.Panel1.BackColor = UiTheme.Surface;
        split.Panel1.Padding = new Padding(12);
        split.Panel1.Controls.Add(_grid);
        split.Panel2.BackColor = UiTheme.Surface;
        split.Panel2.Padding = new Padding(18);
        // Em janelas baixas, as ações e a observação continuam alcançáveis por rolagem.
        split.Panel2.AutoScroll = true;
        split.Panel2.Controls.Add(BuildEditor());
        root.Controls.Add(split, 0, 1);
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(0, 12, 0, 0);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
        AcceptButton = _saveButton;
    }

    private Control BuildEditor()
    {
        var editor = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 11, AutoSize = true, BackColor = UiTheme.Surface };
        editor.Controls.Add(UiTheme.Label("DADOS DO ALERTA", 9, true));
        editor.Controls.Add(FieldLabel("Ticker"));
        editor.Controls.Add(_ticker);
        editor.Controls.Add(FieldLabel("Condição"));
        editor.Controls.Add(_condition);
        editor.Controls.Add(FieldLabel("Preço-alvo"));
        editor.Controls.Add(_target);
        _currentPrice.ForeColor = UiTheme.InkMuted;
        _currentPrice.Margin = new Padding(0, 4, 0, 0);
        editor.Controls.Add(_currentPrice);
        _enabled.Margin = new Padding(0, 14, 0, 10);
        editor.Controls.Add(_enabled);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        actions.Controls.Add(_saveButton);
        actions.Controls.Add(_newButton);
        actions.Controls.Add(_deleteButton);
        editor.Controls.Add(actions);
        var note = UiTheme.Label("O backend avalia os alertas habilitados periodicamente. Quando a condição é atingida, a ocorrência aparece em Notificações.", 8.5f);
        note.ForeColor = UiTheme.InkMuted;
        note.MaximumSize = new Size(270, 0);
        note.AutoSize = true;
        editor.Controls.Add(note);
        return editor;
    }

    private static Label FieldLabel(string text)
    {
        var label = UiTheme.Label(text, 8.5f, true);
        label.ForeColor = UiTheme.InkSecondary;
        label.Margin = new Padding(0, 12, 0, 4);
        return label;
    }

    private void LoadAlerts(Guid? preferredId = null)
    {
        if (_busy || !IsCurrentSession) return;
        SetBusy(true);
        SetStatus("Atualizando alertas…");
        Run(async cancellationToken =>
        {
            try
            {
                var alerts = await _api.GetAlertsAsync(cancellationToken);
                if (!IsCurrentSession) return;
                _prices.Clear();
                BindAlerts(alerts, preferredId);
                SetStatus(Summary(), summary: true);
                LoadPrices(alerts.Select(alert => alert.Ativo.Id));
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    private void BindAlerts(IReadOnlyList<AlertResponse> alerts, Guid? preferredId)
    {
        _binding = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var alert in alerts.OrderBy(alert => alert.Ativo.Ticker).ThenBy(alert => alert.CriadoEm))
            {
                var added = _grid.Rows[_grid.Rows.Add(alert.Ativo.Ticker, ConditionLabel(alert.TipoCondicao), alert.ValorAlvo)];
                added.Cells["Estado"].Value = StateLabel(alert);
                added.SetFormat(GridCells.Money(alert.MoedaAlvo), "Preço-alvo");
                // A cotação vem na moeda do ativo, que pode ser diferente da moeda do alvo.
                added.SetFormat(GridCells.Money(alert.Ativo.Moeda), "Preço atual");
                // Sem disparo, a célula fica vazia e a grade exibe "—".
                if (alert.DisparadoEm is { } fired) added.Cells["Último disparo"].Value = fired.LocalDateTime;
                added.Tag = alert;
                ShowPrice(added);
            }

            var row = preferredId is null
                ? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault()
                : _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item => ((AlertResponse)item.Tag!).Id == preferredId)
                  ?? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (row is not null) _grid.CurrentCell = row.Cells[0];
        }
        finally
        {
            _binding = false;
        }
        if (_grid.Rows.Count == 0) BeginNew(); else BindSelection();
    }

    private void BindSelection()
    {
        var alert = SelectedAlert;
        if (alert is null) return;
        _creating = false;
        _ticker.Text = alert.Ativo.Ticker;
        _ticker.Enabled = false;
        SelectCondition(alert.TipoCondicao);
        _target.Value = alert.ValorAlvo;
        _enabled.Checked = alert.Habilitado;
        _enabled.Enabled = true;
        _saveButton.Text = "&Salvar alterações";
        ShowCurrentPrice();
        UpdateActions();
    }

    private void BeginNew()
    {
        _creating = true;
        _binding = true;
        _grid.ClearSelection();
        _binding = false;
        _ticker.Enabled = true;
        _ticker.Clear();
        _condition.SelectedIndex = 0;
        _target.Value = 1;
        _enabled.Checked = true;
        _enabled.Enabled = false;
        _saveButton.Text = "&Criar alerta";
        ShowCurrentPrice();
        UpdateActions();
        if (Visible) _ticker.Focus();
    }

    private void Save()
    {
        if (_busy || !IsCurrentSession) return;
        var condition = (_condition.SelectedItem as ConditionChoice)?.Value;
        if (string.IsNullOrWhiteSpace(_ticker.Text) || condition is null || _target.Value <= 0)
        {
            SetStatus("Informe o ticker, a condição e um preço-alvo maior que zero.", true);
            return;
        }

        var selected = EditingAlert;
        SetBusy(true);
        SetStatus(selected is null ? "Criando alerta…" : "Salvando alterações…");
        Run(async cancellationToken =>
        {
            try
            {
                AlertResponse saved;
                if (selected is null)
                {
                    saved = await _api.CreateAlertAsync(_ticker.Text, condition, _target.Value, cancellationToken);
                }
                else
                {
                    var changedCondition = condition == selected.TipoCondicao ? null : condition;
                    decimal? changedTarget = _target.Value == selected.ValorAlvo ? null : _target.Value;
                    bool? changedEnabled = _enabled.Checked == selected.Habilitado ? null : _enabled.Checked;
                    if (changedCondition is null && changedTarget is null && changedEnabled is null)
                    {
                        SetStatus("Nenhuma alteração para salvar.");
                        return;
                    }
                    saved = await _api.UpdateAlertAsync(selected.Id, changedCondition, changedTarget, changedEnabled, cancellationToken);
                }
                if (IsCurrentSession)
                {
                    var alerts = _grid.Rows.Cast<DataGridViewRow>().Select(row => (AlertResponse)row.Tag!)
                        .Where(alert => alert.Id != saved.Id).Append(saved).ToArray();
                    BindAlerts(alerts, saved.Id);
                    SetStatus($"Alerta de {saved.Ativo.Ticker} salvo.");
                    // O alerta já está salvo: a cotação do ativo novo é só um complemento da linha.
                    if (!_prices.ContainsKey(saved.Ativo.Id)) LoadPrices([saved.Ativo.Id]);
                }
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    private void Delete()
    {
        var selected = EditingAlert;
        if (_busy || selected is null || !IsCurrentSession) return;
        if (MessageBox.Show(this, $"Excluir o alerta de {selected.Ativo.Ticker}?", "Excluir alerta",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SetBusy(true);
        SetStatus("Excluindo alerta…");
        Run(async cancellationToken =>
        {
            try
            {
                await _api.DeleteAlertAsync(selected.Id, cancellationToken);
                if (IsCurrentSession)
                {
                    var alerts = _grid.Rows.Cast<DataGridViewRow>().Select(row => (AlertResponse)row.Tag!)
                        .Where(alert => alert.Id != selected.Id).ToArray();
                    BindAlerts(alerts, null);
                    SetStatus($"Alerta de {selected.Ativo.Ticker} excluído.");
                }
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    // Uma cotação por ativo, não por alerta, como no cliente web. A falha de uma delas só deixa aquele preço em branco.
    private void LoadPrices(IEnumerable<Guid> assetIds)
    {
        var ids = assetIds.Distinct().ToArray();
        if (ids.Length == 0 || !IsCurrentSession) return;
        Run(async cancellationToken =>
        {
            var prices = await Task.WhenAll(ids.Select(id => PriceAsync(id, cancellationToken)));
            if (!IsCurrentSession) return;
            for (var i = 0; i < ids.Length; i++)
            {
                if (prices[i] is { } price) _prices[ids[i]] = price;
                else _prices.Remove(ids[i]);
            }
            foreach (DataGridViewRow row in _grid.Rows) ShowPrice(row);
            ShowCurrentPrice();
            // O resumo só é refeito se ainda for ele que está na tela, sem apagar o resultado de uma ação.
            if (_summaryShown) SetStatus(Summary(), summary: true);
        }, HandleError);
    }

    private async Task<decimal?> PriceAsync(Guid assetId, CancellationToken cancellationToken)
    {
        try
        {
            return (await _api.GetQuoteAsync(assetId, cancellationToken)).Preco;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                          exception is not ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            return null;
        }
    }

    private void ShowPrice(DataGridViewRow row)
    {
        var alert = (AlertResponse)row.Tag!;
        var price = ComparablePrice(alert);
        row.Cells["Preço atual"].Value = _prices.TryGetValue(alert.Ativo.Id, out var current) ? current : null;
        row.Cells["Distância"].Value = price is > 0 ? (alert.ValorAlvo / price.Value - 1) * 100 : null;
    }

    private void ShowCurrentPrice()
    {
        var alert = EditingAlert;
        _currentPrice.Text = alert is not null && _prices.TryGetValue(alert.Ativo.Id, out var price)
            ? $"Preço atual de {alert.Ativo.Ticker}: {price.ToString(GridCells.Money(alert.Ativo.Moeda), CultureInfo.CurrentCulture)}"
            : "";
    }

    // O preço atual só é comparado com o alvo quando os dois estão na mesma moeda.
    private decimal? ComparablePrice(AlertResponse alert) =>
        string.Equals(alert.MoedaAlvo, alert.Ativo.Moeda, StringComparison.OrdinalIgnoreCase) &&
        _prices.TryGetValue(alert.Ativo.Id, out var price)
            ? price
            : null;

    private string Summary()
    {
        var alerts = _grid.Rows.Cast<DataGridViewRow>().Select(row => (AlertResponse)row.Tag!).ToArray();
        if (alerts.Length == 0) return "Nenhum alerta cadastrado.";
        var summary = $"{alerts.Length} alerta(s) cadastrado(s) · {alerts.Count(alert => alert.Habilitado)} habilitado(s)";
        var met = alerts.Count(alert => alert.Habilitado && ComparablePrice(alert) is { } price &&
                                        (alert.TipoCondicao == InsightFlowApiClient.PriceGreaterThanOrEqual ? price >= alert.ValorAlvo : price <= alert.ValorAlvo));
        return _prices.Count == 0 ? summary + "." : $"{summary} · {met} com a condição satisfeita no preço atual.";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _grid.Enabled = !busy;
        _ticker.Enabled = !busy && _creating;
        _condition.Enabled = !busy;
        _target.Enabled = !busy;
        _enabled.Enabled = !busy && !_creating;
        UpdateActions();
    }

    private void UpdateActions()
    {
        UiTheme.SetButtonEnabled(_refreshButton, !_busy);
        UiTheme.SetButtonEnabled(_newButton, !_busy);
        UiTheme.SetButtonEnabled(_saveButton, !_busy, true);
        UiTheme.SetButtonEnabled(_deleteButton, !_busy && !_creating && SelectedAlert is not null);
    }

    private void HandleError(Exception exception)
    {
        if (!IsCurrentSession) return;
        if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            SessionExpired = true;
            _session.Clear();
            Close();
            return;
        }
        SetBusy(false);
        SetStatus(UiErrors.Message(exception), true);
    }

    private void SelectCondition(string value)
    {
        for (var i = 0; i < _condition.Items.Count; i++)
        {
            if (_condition.Items[i] is ConditionChoice choice && choice.Value == value)
            {
                _condition.SelectedIndex = i;
                return;
            }
        }
    }

    private void SetStatus(string text, bool error = false, bool summary = false)
    {
        _summaryShown = summary;
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }

    private static string ConditionLabel(string condition) => condition switch
    {
        InsightFlowApiClient.PriceGreaterThanOrEqual => "≥ alvo",
        InsightFlowApiClient.PriceLessThanOrEqual => "≤ alvo",
        _ => condition
    };

    private static string StateLabel(AlertResponse alert) =>
        !alert.Habilitado ? "Desabilitado" : alert.UltimoEstado ? "Condição atingida" : "Monitorando";

    private sealed record ConditionChoice(string Value, string Label);
}
