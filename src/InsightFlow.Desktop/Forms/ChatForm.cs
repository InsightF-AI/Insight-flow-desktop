using System.Net;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed class ChatForm : AsyncForm
{
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly int _sessionVersion;
    // Somente trocas confirmadas pela API entram no histórico enviado nas próximas perguntas.
    private readonly List<ChatMessage> _history = [];
    private readonly RichTextBox _transcript = new() { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical };
    private readonly TextBox _input = new() { Multiline = true, Dock = DockStyle.Fill, MaxLength = InsightFlowApiClient.ChatMaxCharacters, ScrollBars = ScrollBars.Vertical, Height = 76 };
    private readonly Button _sendButton = UiTheme.Button("&Enviar", true);
    private readonly Button _newButton = UiTheme.Button("&Nova conversa");
    private readonly Label _notice = UiTheme.Label("As respostas são geradas por IA a partir dos dados da sua carteira e dos ativos cadastrados.", 8.5f);
    private readonly Label _status = UiTheme.Label("Pergunte sobre sua carteira ou sobre ativos acompanhados.", 9);
    private readonly Font _speakerFont = UiTheme.Font(9, true);
    private readonly Font _messageFont = UiTheme.Font(10);
    private bool _busy;

    public bool SessionExpired { get; private set; }
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public ChatForm(InsightFlowApiClient api, UserSession session)
    {
        _api = api;
        _session = session;
        _sessionVersion = session.Version;
        BuildLayout();
        _sendButton.Click += (_, _) => Send();
        _newButton.Click += (_, _) => NewConversation();
        _input.TextChanged += (_, _) => UpdateActions();
        _input.KeyDown += (_, e) =>
        {
            // Enter envia; Shift+Enter quebra a linha.
            if (e.KeyCode != Keys.Enter || e.Shift) return;
            e.SuppressKeyPress = true;
            Send();
        };
        Shown += (_, _) => _input.Focus();
        FormClosed += (_, _) => { _history.Clear(); _transcript.Clear(); };
        UpdateActions();
        CompleteLayout();
    }

    private void BuildLayout()
    {
        Text = "InsightFlow — Chat IA";
        ClientSize = new Size(860, 640);
        MinimumSize = new Size(640, 480);
        BackColor = UiTheme.Background;
        _transcript.BackColor = UiTheme.Surface;
        _transcript.ForeColor = UiTheme.InkSecondary;
        _transcript.Font = UiTheme.Font(10);
        _transcript.AccessibleName = "Conversa com a IA";
        UiTheme.StyleInput(_input);
        _input.AccessibleName = "Mensagem para a IA";
        _input.PlaceholderText = "Escreva sua pergunta (Enter envia, Shift+Enter quebra linha)";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22), BackColor = UiTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        titles.Controls.Add(UiTheme.Label("Chat IA", 20, true));
        var subtitle = UiTheme.Label("Conteúdo informativo; não é recomendação de investimento.", 9);
        subtitle.ForeColor = UiTheme.InkMuted;
        titles.Controls.Add(subtitle);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(_newButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        var transcriptCard = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(14) };
        transcriptCard.Controls.Add(_transcript);
        root.Controls.Add(transcriptCard, 0, 1);
        _notice.Dock = DockStyle.Fill;
        _notice.ForeColor = UiTheme.InkMuted;
        _notice.Margin = new Padding(0, 8, 0, 8);
        root.Controls.Add(_notice, 0, 2);

        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _input.Margin = new Padding(0, 0, 10, 0);
        composer.Controls.Add(_input, 0, 0);
        _sendButton.Anchor = AnchorStyles.Top;
        composer.Controls.Add(_sendButton, 1, 0);
        root.Controls.Add(composer, 0, 3);
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiTheme.InkMuted;
        _status.Margin = new Padding(0, 10, 0, 0);
        root.Controls.Add(_status, 0, 4);
        Controls.Add(root);
    }

    private void Send()
    {
        var text = _input.Text.Trim();
        if (_busy || !IsCurrentSession || text.Length == 0) return;

        var question = new ChatMessage(InsightFlowApiClient.ChatUser, text);
        var conversation = Window(_history, question);
        AppendMessage("Você", text, UiTheme.Accent);
        _input.Clear();
        SetBusy(true);
        SetStatus("Aguardando resposta da IA… Pode levar até 1 minuto.");
        Run(async cancellationToken =>
        {
            try
            {
                var answer = await _api.ChatAsync(conversation, cancellationToken);
                if (!IsCurrentSession) return;
                _history.Add(question);
                _history.Add(new ChatMessage(InsightFlowApiClient.ChatAssistant, answer.Texto));
                AppendMessage("InsightFlow IA", answer.Texto, UiTheme.Violet);
                _notice.Text = answer.AvisoLegal;
                SetStatus($"Resposta gerada por {answer.Modelo}.");
            }
            finally
            {
                if (IsCurrentSession) SetBusy(false);
            }
        }, exception => HandleError(exception, text));
    }

    // O backend aceita uma janela limitada; as mensagens mais antigas saem primeiro. O corte nunca deixa
    // uma resposta sem a pergunta que a originou no início da conversa enviada.
    internal static ChatMessage[] Window(IEnumerable<ChatMessage> history, ChatMessage question) =>
        history.Append(question).TakeLast(InsightFlowApiClient.ChatMaxMessages)
            .SkipWhile(message => message.Papel != InsightFlowApiClient.ChatUser).ToArray();

    private void NewConversation()
    {
        if (_busy) return;
        _history.Clear();
        _transcript.Clear();
        SetStatus("Nova conversa iniciada.");
        _input.Focus();
    }

    private void AppendMessage(string speaker, string text, Color color)
    {
        if (_transcript.TextLength > 0) _transcript.AppendText(Environment.NewLine + Environment.NewLine);
        _transcript.SelectionStart = _transcript.TextLength;
        _transcript.SelectionColor = color;
        _transcript.SelectionFont = _speakerFont;
        _transcript.AppendText(speaker + Environment.NewLine);
        _transcript.SelectionColor = UiTheme.Ink;
        _transcript.SelectionFont = _messageFont;
        _transcript.AppendText(text.ReplaceLineEndings(Environment.NewLine));
        _transcript.ScrollToCaret();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _input.ReadOnly = busy;
        UpdateActions();
    }

    private void UpdateActions()
    {
        UiTheme.SetButtonEnabled(_sendButton, !_busy && _input.Text.Trim().Length > 0, true);
        UiTheme.SetButtonEnabled(_newButton, !_busy);
    }

    private void HandleError(Exception exception, string unsentText)
    {
        if (!IsCurrentSession) return;
        if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            SessionExpired = true;
            _session.Clear();
            Close();
            return;
        }
        AppendMessage("Não enviada", UiErrors.AiMessage(exception), UiTheme.Down);
        SetBusy(false);
        // Devolve o texto para o usuário tentar novamente sem redigitar.
        if (_input.TextLength == 0) _input.Text = unsentText;
        SetStatus("A mensagem não entrou no histórico da conversa.", true);
    }

    private void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _speakerFont.Dispose();
            _messageFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
