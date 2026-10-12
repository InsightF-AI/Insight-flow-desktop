using System.Security;
using InsightFlow.Desktop.Configuration;
using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Forms;

internal sealed class SettingsForm : AsyncForm
{
    private readonly UserPreferences _preferences;
    private readonly WindowsStartup _startup;
    private readonly CheckBox _keepInTray = Option("&Continuar em segundo plano ao fechar a janela");
    private readonly CheckBox _startWithWindows = Option("&Abrir o InsightFlow ao iniciar o Windows");
    private readonly Button _save = UiTheme.Button("&Salvar", true);
    private readonly Button _close = UiTheme.Button("&Fechar");
    private readonly Label _status = UiTheme.Label("", 9);
    private bool _startupEnabled;

    public SettingsForm(UserPreferences preferences, WindowsStartup startup)
    {
        _preferences = preferences;
        _startup = startup;
        _keepInTray.Checked = preferences.KeepRunningInTray;
        try
        {
            _startupEnabled = startup.IsEnabled;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // Sem leitura do registro, a opção começa desmarcada e o erro aparece ao salvar.
        }
        _startWithWindows.Checked = _startupEnabled;
        BuildLayout();
        _keepInTray.CheckedChanged += (_, _) => UpdateActions();
        _startWithWindows.CheckedChanged += (_, _) => UpdateActions();
        _save.Click += (_, _) => Save();
        _close.Click += (_, _) => Close();
        AcceptButton = _save;
        CancelButton = _close;
        UpdateActions();
        CompleteLayout();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Configurações";
        ClientSize = new Size(640, 480);
        MinimumSize = new Size(560, 500);
        BackColor = UiTheme.Background;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22), BackColor = UiTheme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        titles.Controls.Add(UiTheme.Label("Configurações", 20, true));
        var subtitle = UiTheme.Label("Preferências deste computador. Elas valem para o seu usuário do Windows.", 9);
        subtitle.ForeColor = UiTheme.InkMuted;
        titles.Controls.Add(subtitle);
        root.Controls.Add(titles, 0, 0);

        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = UiTheme.Surface, Padding = new Padding(16), Margin = Padding.Empty };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < card.RowCount; i++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var section = UiTheme.Label("SEGUNDO PLANO E NOTIFICAÇÕES", 9, true);
        section.ForeColor = UiTheme.InkSecondary;
        card.Controls.Add(section, 0, 0);
        card.Controls.Add(_keepInTray, 0, 1);
        card.Controls.Add(Hint("O aplicativo fica na bandeja do Windows e continua entregando as notificações. " +
                               "Para encerrar, use o menu do ícone na bandeja."), 0, 2);
        card.Controls.Add(_startWithWindows, 0, 3);
        card.Controls.Add(Hint("O InsightFlow abre na tela de entrada quando você entra no Windows. " +
                               "A sessão fica só em memória: é preciso entrar de novo a cada inicialização."), 0, 4);
        var limit = Hint("Com o aplicativo encerrado não há entrega. As notificações não lidas aparecem na próxima entrada.");
        limit.Margin = new Padding(0, 14, 0, 0);
        card.Controls.Add(limit, 0, 5);
        root.Controls.Add(card, 0, 1);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiTheme.InkMuted;
        _status.Margin = new Padding(0, 10, 0, 4);
        _status.AccessibleName = "Status das configurações";
        root.Controls.Add(_status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, BackColor = Color.Transparent };
        _close.Margin = new Padding(8, 3, 0, 3);
        _save.Margin = new Padding(0, 3, 0, 3);
        actions.Controls.Add(_close);
        actions.Controls.Add(_save);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
    }

    private void Save()
    {
        if (!_save.Enabled) return;
        var keepInTray = _preferences.KeepRunningInTray;
        try
        {
            _preferences.KeepRunningInTray = _keepInTray.Checked;
            _preferences.Save();
            keepInTray = _preferences.KeepRunningInTray;
            if (_startWithWindows.Checked != _startupEnabled)
            {
                _startup.Set(_startWithWindows.Checked);
                _startupEnabled = _startWithWindows.Checked;
            }
            SetStatus("Preferências salvas.");
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            // O que não foi gravado não passa a valer nesta execução.
            _preferences.KeepRunningInTray = keepInTray;
            SetStatus("Não foi possível salvar as preferências neste computador.", true);
        }
        UpdateActions();
    }

    private void UpdateActions() => UiTheme.SetButtonEnabled(_save,
        _keepInTray.Checked != _preferences.KeepRunningInTray || _startWithWindows.Checked != _startupEnabled, true);

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }

    private static bool IsStorageFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private static CheckBox Option(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Ink,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 12, 0, 0)
    };

    private static Label Hint(string text)
    {
        var hint = UiTheme.Label(text, 9);
        hint.ForeColor = UiTheme.InkMuted;
        hint.Dock = DockStyle.Fill;
        // Alinha o texto com o rótulo da opção, depois da caixa de seleção.
        hint.Margin = new Padding(20, 2, 0, 0);
        return hint;
    }
}
