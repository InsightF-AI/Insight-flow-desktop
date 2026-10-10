using System.Net;
using System.Text;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed class NotificationsForm : AsyncForm
{
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly int _sessionVersion;
    private readonly IReadOnlyDictionary<Guid, string> _tickers;
    private readonly DataGridView _grid = UiTheme.Grid();
    private readonly CheckBox _onlyUnread = new() { Text = "Somente não lidas", AutoSize = true, Margin = new Padding(0, 10, 12, 0) };
    private readonly Button _refreshButton = UiTheme.Button("&Atualizar");
    private readonly Button _markReadButton = UiTheme.Button("&Marcar como lida", true);
    private readonly Button _markAllButton = UiTheme.Button("Marcar &todas como lidas");
    private readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly Label _status = UiTheme.Label("Carregando notificações…", 9);
    private bool _busy;
    private bool _binding;

    public bool SessionExpired { get; private set; }
    private NotificationResponse? SelectedNotification => _grid.CurrentRow?.Tag as NotificationResponse;
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public NotificationsForm(InsightFlowApiClient api, UserSession session, IReadOnlyDictionary<Guid, string> tickers)
    {
        _api = api;
        _session = session;
        _sessionVersion = session.Version;
        _tickers = tickers;
        BuildLayout();
        _grid.SelectionChanged += (_, _) => { if (!_binding) BindSelection(); };
        _refreshButton.Click += (_, _) => LoadNotifications();
        _onlyUnread.CheckedChanged += (_, _) => LoadNotifications();
        _markReadButton.Click += (_, _) => MarkSelectedAsRead();
        _markAllButton.Click += (_, _) => MarkAllAsRead();
        Shown += (_, _) => LoadNotifications();
        FormClosed += (_, _) => { _grid.Rows.Clear(); _detail.Clear(); };
        UpdateActions();
        CompleteLayout();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Notificações";
        ClientSize = new Size(1000, 620);
        MinimumSize = new Size(820, 500);
        BackColor = UiTheme.Background;
        _onlyUnread.ForeColor = UiTheme.InkSecondary;
        _onlyUnread.BackColor = Color.Transparent;
        _detail.BackColor = UiTheme.Surface;
        _detail.ForeColor = UiTheme.InkSecondary;
        _detail.Font = UiTheme.Font(9.5f);
        _detail.AccessibleName = "Detalhes da notificação selecionada";

        _grid.Columns.Add(GridCells.Column("Data", 125f, typeof(DateTime), GridCells.DateTime));
        foreach (var (name, weight) in new[] { ("Tipo", 115f), ("Ativo", 65f), ("Mensagem", 190f), ("Situação", 90f) })
        {
            _grid.Columns.Add(GridCells.Column(name, weight));
        }
        _grid.AccessibleName = "Notificações recebidas";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(22), BackColor = UiTheme.Background };
        // Sem estilo, a coluna cresce até o tamanho inicial da divisão e a janela menor corta o painel da direita.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        titles.Controls.Add(UiTheme.Label("Notificações", 20, true));
        var subtitle = UiTheme.Label("Sinais ativados, alertas disparados e resumos diários gerados pelo backend.", 9);
        subtitle.ForeColor = UiTheme.InkMuted;
        titles.Controls.Add(subtitle);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_onlyUnread, 1, 0);
        header.Controls.Add(_refreshButton, 2, 0);
        root.Controls.Add(header, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Size = new Size(950, 450), SplitterDistance = 610, Panel1MinSize = 420, Panel2MinSize = 280, BackColor = UiTheme.Border };
        split.Panel1.BackColor = UiTheme.Surface;
        split.Panel1.Padding = new Padding(12);
        split.Panel1.Controls.Add(_grid);
        split.Panel2.BackColor = UiTheme.Surface;
        split.Panel2.Padding = new Padding(18);
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiTheme.Surface };
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detail.Controls.Add(UiTheme.Label("DETALHES", 9, true), 0, 0);
        detail.Controls.Add(_detail, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        actions.Controls.Add(_markReadButton);
        actions.Controls.Add(_markAllButton);
        detail.Controls.Add(actions, 0, 2);
        split.Panel2.Controls.Add(detail);
        root.Controls.Add(split, 0, 1);
        _status.Dock = DockStyle.Fill;
        _status.Margin = new Padding(0, 12, 0, 0);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);
    }

    private void LoadNotifications()
    {
        if (_busy || !IsCurrentSession) return;
        var onlyUnread = _onlyUnread.Checked;
        var selectedId = SelectedNotification?.Id;
        SetBusy(true);
        SetStatus("Atualizando notificações…");
        Run(async cancellationToken =>
        {
            try
            {
                var notifications = await _api.GetNotificationsAsync(onlyUnread, cancellationToken);
                if (!IsCurrentSession) return;
                Bind(notifications, selectedId);
                var unread = notifications.Count(item => !item.Lida);
                SetStatus(notifications.Count == 0
                    ? (onlyUnread ? "Nenhuma notificação não lida." : "Nenhuma notificação recebida.")
                    : $"{notifications.Count} notificação(ões) · {unread} não lida(s).");
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    private void Bind(IEnumerable<NotificationResponse> notifications, Guid? preferredId)
    {
        _binding = true;
        try
        {
            _grid.Rows.Clear();
            // A API já ordena da mais recente para a mais antiga; a ordenação local mantém isso após atualizações.
            foreach (var notification in notifications.OrderByDescending(item => item.CriadoEm))
            {
                var index = _grid.Rows.Add(notification.CriadoEm.LocalDateTime, TypeLabel(notification.Tipo),
                    Ticker(notification.AtivoId), notification.Mensagem, notification.Lida ? "Lida" : "Não lida");
                var row = _grid.Rows[index];
                row.Tag = notification;
                if (!notification.Lida) row.DefaultCellStyle.ForeColor = UiTheme.Ink;
            }
            var selected = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ((NotificationResponse)row.Tag!).Id == preferredId)
                           ?? _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (selected is not null) _grid.CurrentCell = selected.Cells[0];
        }
        finally
        {
            _binding = false;
        }
        BindSelection();
    }

    private void BindSelection()
    {
        var notification = SelectedNotification;
        _detail.Text = notification is null ? "Selecione uma notificação para ver os detalhes." : Describe(notification);
        UpdateActions();
    }

    private void MarkSelectedAsRead()
    {
        var selected = SelectedNotification;
        if (_busy || selected is null || selected.Lida || !IsCurrentSession) return;
        SetBusy(true);
        SetStatus("Marcando como lida…");
        Run(async cancellationToken =>
        {
            try
            {
                var updated = await _api.MarkNotificationReadAsync(selected.Id, cancellationToken);
                if (!IsCurrentSession) return;
                var remaining = _grid.Rows.Cast<DataGridViewRow>().Select(row => (NotificationResponse)row.Tag!)
                    .Where(item => item.Id != updated.Id);
                // No filtro de não lidas, o item confirmado sai da lista.
                Bind(_onlyUnread.Checked ? remaining.ToArray() : remaining.Append(updated).ToArray(), updated.Id);
                SetStatus("Notificação marcada como lida.");
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    private void MarkAllAsRead()
    {
        var unread = Notifications().Where(item => !item.Lida).ToArray();
        if (_busy || unread.Length == 0 || !IsCurrentSession) return;
        var selectedId = SelectedNotification?.Id;
        SetBusy(true);
        SetStatus($"Marcando {unread.Length} notificação(ões) como lida(s)…");
        Run(async cancellationToken =>
        {
            try
            {
                // A API marca uma notificação por chamada; as que falharem continuam como não lidas na lista.
                var results = await Task.WhenAll(unread.Select(item => _api.MarkNotificationReadAsync(item.Id, cancellationToken).OrNullAsync(cancellationToken)));
                if (!IsCurrentSession) return;
                var updated = results.OfType<NotificationResponse>().ToDictionary(item => item.Id);
                var all = Notifications().Select(item => updated.GetValueOrDefault(item.Id, item));
                Bind(_onlyUnread.Checked ? all.Where(item => !item.Lida).ToArray() : all.ToArray(), selectedId);
                var failed = unread.Length - updated.Count;
                SetStatus(failed == 0
                    ? $"{updated.Count} notificação(ões) marcada(s) como lida(s)."
                    : $"{updated.Count} notificação(ões) marcada(s) como lida(s); {failed} continua(m) como não lida(s). Tente novamente.", failed > 0);
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, HandleError);
    }

    private IEnumerable<NotificationResponse> Notifications() =>
        _grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag).OfType<NotificationResponse>();

    private string Describe(NotificationResponse notification)
    {
        var text = new StringBuilder();
        text.AppendLine(notification.Mensagem);
        text.AppendLine();
        text.AppendLine($"{TypeLabel(notification.Tipo)} · {notification.CriadoEm.LocalDateTime:dd/MM/yyyy HH:mm}");
        if (notification.AtivoId is { } assetId) text.AppendLine($"Ativo: {Ticker(assetId)}");
        switch (notification.Tipo)
        {
            case "ALERTA_DISPARADO" when ContextText(notification.Contexto, "preco") is { } price:
                text.AppendLine($"Preço na avaliação: {price}");
                break;
            case "RESUMO_DIARIO":
                if (ContextText(notification.Contexto, "texto") is { } summary)
                {
                    text.AppendLine();
                    text.AppendLine(summary);
                }
                // O resumo é texto de IA: o aviso aparece mesmo quando a notificação não o traz.
                text.AppendLine();
                text.AppendLine(AiNotice.OrDefault(ContextText(notification.Contexto, "aviso_legal")));
                break;
        }
        return text.ToString().TrimEnd().ReplaceLineEndings("\r\n");
    }

    private string Ticker(Guid? assetId) =>
        assetId is null ? "—" : _tickers.TryGetValue(assetId.Value, out var ticker) ? ticker : "Fora da watchlist";

    internal static string? ContextText(JsonElement context, string property) =>
        context.ValueKind == JsonValueKind.Object && context.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string TypeLabel(string type) => type switch
    {
        "SINAL_ATIVADO" => "Sinal técnico",
        "ALERTA_DISPARADO" => "Alerta de preço",
        "RESUMO_DIARIO" => "Resumo diário",
        _ => type
    };

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _grid.Enabled = !busy;
        _onlyUnread.Enabled = !busy;
        UpdateActions();
    }

    private void UpdateActions()
    {
        UiTheme.SetButtonEnabled(_refreshButton, !_busy);
        UiTheme.SetButtonEnabled(_markReadButton, !_busy && SelectedNotification is { Lida: false }, true);
        UiTheme.SetButtonEnabled(_markAllButton, !_busy && Notifications().Any(item => !item.Lida));
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

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }
}
