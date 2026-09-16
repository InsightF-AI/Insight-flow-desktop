using System.Net.Mail;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed class LoginForm : AsyncForm
{
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly TextBox _name = new() { Dock = DockStyle.Fill, MaxLength = 255, AutoSize = false, Height = 38 };
    private readonly TextBox _email = new() { Dock = DockStyle.Fill, MaxLength = 254, AutoSize = false, Height = 38 };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 128, AutoSize = false, Height = 38 };
    private readonly CheckBox _register = new() { Text = "Criar uma nova conta", AutoSize = true };
    private readonly Label _nameLabel = UiTheme.Label("&NOME", 8.5f, true);
    private readonly Label _status = UiTheme.Label("");
    private readonly Button _submit = UiTheme.Button("&Entrar", true);
    private bool _busy;

    public bool Authenticated { get; private set; }

    public LoginForm(InsightFlowApiClient api, UserSession session, Uri apiUrl, string? notice = null)
    {
        _api = api;
        _session = session;
        Text = "InsightFlow — Entrar";
        ClientSize = new Size(1080, 680);
        MinimumSize = new Size(900, 620);
        BackColor = UiTheme.Background;

        UiTheme.StyleInput(_name);
        UiTheme.StyleInput(_email);
        UiTheme.StyleInput(_password);
        _register.ForeColor = UiTheme.InkSecondary;
        _register.BackColor = Color.Transparent;
        _register.Cursor = Cursors.Hand;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UiTheme.Background
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildHero(), 0, 0);
        shell.Controls.Add(BuildForm(apiUrl, notice), 1, 0);
        Controls.Add(shell);

        _name.AccessibleName = "Nome";
        _email.AccessibleName = "E-mail";
        _password.AccessibleName = "Senha";
        _register.TabIndex = 0;
        _name.TabIndex = 1;
        _email.TabIndex = 2;
        _password.TabIndex = 3;
        _submit.TabIndex = 4;
        AcceptButton = _submit;
        _register.CheckedChanged += (_, _) => UpdateMode();
        _submit.Click += (_, _) => Submit();
        Shown += (_, _) => _email.Focus();
        UpdateMode();
    }

    private static Control BuildHero()
    {
        var hero = new MarketHeroPanel { Dock = DockStyle.Fill, Padding = new Padding(48) };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 5
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 76));

        var brand = UiTheme.Label("●  InsightFlow", 16, true);
        brand.ForeColor = UiTheme.Ink;
        content.Controls.Add(brand, 0, 0);

        var title = UiTheme.Label("Entenda o mercado.\nDecida com contexto.", 25, true);
        title.ForeColor = UiTheme.Ink;
        title.MaximumSize = new Size(430, 0);
        content.Controls.Add(title, 0, 2);

        var subtitle = UiTheme.Label(
            "Indicadores técnicos, sinais e histórico dos seus ativos em uma experiência integrada.", 11);
        subtitle.ForeColor = UiTheme.InkSecondary;
        subtitle.MaximumSize = new Size(410, 0);
        subtitle.Margin = new Padding(0, 14, 0, 0);
        content.Controls.Add(subtitle, 0, 3);
        hero.Controls.Add(content);
        return hero;
    }

    private Control BuildForm(Uri apiUrl, string? notice)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface };
        var form = new TableLayoutPanel
        {
            Anchor = AnchorStyles.None,
            Width = 430,
            Height = 555,
            ColumnCount = 1,
            RowCount = 14,
            BackColor = UiTheme.Surface,
            Padding = new Padding(14)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < form.RowCount; i++) form.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var kicker = UiTheme.Label("ACESSO À PLATAFORMA", 8.5f, true);
        kicker.ForeColor = UiTheme.Accent;
        form.Controls.Add(kicker, 0, 0);
        form.Controls.Add(UiTheme.Label("Bem-vindo ao InsightFlow", 21, true), 0, 1);
        var intro = UiTheme.Label("Entre para acompanhar sua watchlist e consultar os dados de mercado.", 9.5f);
        intro.ForeColor = UiTheme.InkSecondary;
        intro.MaximumSize = new Size(390, 0);
        intro.Margin = new Padding(0, 0, 0, 18);
        form.Controls.Add(intro, 0, 2);
        form.Controls.Add(_register, 0, 3);
        _nameLabel.ForeColor = UiTheme.InkMuted;
        form.Controls.Add(_nameLabel, 0, 4);
        form.Controls.Add(_name, 0, 5);
        var emailLabel = UiTheme.Label("&E-MAIL", 8.5f, true);
        emailLabel.ForeColor = UiTheme.InkMuted;
        emailLabel.Margin = new Padding(0, 12, 0, 5);
        form.Controls.Add(emailLabel, 0, 6);
        form.Controls.Add(_email, 0, 7);
        var passwordLabel = UiTheme.Label("&SENHA", 8.5f, true);
        passwordLabel.ForeColor = UiTheme.InkMuted;
        passwordLabel.Margin = new Padding(0, 12, 0, 5);
        form.Controls.Add(passwordLabel, 0, 8);
        form.Controls.Add(_password, 0, 9);
        _submit.Margin = new Padding(0, 20, 0, 12);
        _submit.Dock = DockStyle.Fill;
        _submit.Height = 42;
        form.Controls.Add(_submit, 0, 10);
        _status.MaximumSize = new Size(390, 0);
        _status.Text = notice ?? "";
        _status.ForeColor = UiTheme.Error;
        _status.AccessibleName = "Status de autenticação";
        form.Controls.Add(_status, 0, 11);
        var endpoint = UiTheme.Label($"API  {apiUrl}", 8);
        endpoint.MaximumSize = new Size(390, 0);
        endpoint.ForeColor = UiTheme.InkMuted;
        endpoint.Margin = new Padding(0, 18, 0, 0);
        form.Controls.Add(endpoint, 0, 12);

        host.Controls.Add(form);
        host.Resize += (_, _) =>
        {
            form.Left = Math.Max(20, (host.ClientSize.Width - form.Width) / 2);
            form.Top = Math.Max(20, (host.ClientSize.Height - form.Height) / 2);
        };
        return host;
    }

    private void UpdateMode()
    {
        _name.Visible = _nameLabel.Visible = _register.Checked;
        _submit.Text = _register.Checked ? "&Criar conta" : "&Entrar";
        _password.PlaceholderText = _register.Checked ? "De 8 a 128 caracteres" : "Sua senha";
    }

    private void Submit()
    {
        if (_busy || IsClosing) return;
        var email = _email.Text.Trim();
        var name = _name.Text.Trim();
        var password = _password.Text;
        var register = _register.Checked;
        if (register && name.Length is < 1 or > 255)
        {
            ShowValidation("Informe um nome de 1 a 255 caracteres.", _name);
            return;
        }
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            ShowValidation("Informe um e-mail válido.", _email);
            return;
        }
        if (password.Length == 0 || (register && password.Length < 8))
        {
            ShowValidation(register ? "A senha deve ter de 8 a 128 caracteres." : "Informe sua senha.", _password);
            return;
        }

        SetBusy(true);
        _status.ForeColor = UiTheme.InkMuted;
        _status.Text = register ? "Criando sua conta…" : "Entrando…";
        Run(async cancellationToken =>
        {
            try
            {
                var response = register
                    ? await _api.RegisterAsync(name, email, password, cancellationToken)
                    : await _api.LoginAsync(email, password, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                _session.Start(response.AccessToken);
                _password.Clear();
                Authenticated = true;
                Close();
            }
            finally
            {
                if (!IsClosing) SetBusy(false);
            }
        }, exception =>
        {
            _status.ForeColor = UiTheme.Error;
            _status.Text = UiErrors.Message(exception);
            _password.SelectAll();
            _password.Focus();
        });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _name.Enabled = _email.Enabled = _password.Enabled = _register.Enabled = _submit.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowValidation(string message, Control control)
    {
        _status.ForeColor = UiTheme.Error;
        _status.Text = message;
        control.Focus();
    }
}
