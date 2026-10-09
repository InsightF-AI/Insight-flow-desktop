using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Forms;

internal class AsyncForm : Form
{
    private readonly FormWorkScope _work = new();
    private bool _waitingToClose;
    private bool _closeReady;
    protected bool IsClosing => _work.IsClosing;

    protected AsyncForm()
    {
        // A escala de DPI é aplicada uma única vez, no primeiro layout. Ele fica suspenso até a tela terminar
        // de criar seus controles (CompleteLayout); do contrário só as fontes cresceriam e os tamanhos fixos não.
        SuspendLayout();
        Font = new Font("Segoe UI", 10);
        Icon = AppIcon.Window;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        StartPosition = FormStartPosition.CenterScreen;
    }

    // Toda tela derivada chama este método ao fim do construtor.
    protected void CompleteLayout()
    {
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        // Em telas pequenas ou com escala alta, a janela (e o seu tamanho mínimo) não pode exceder a área útil.
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (Width > area.Width || Height > area.Height)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        }
        base.OnLoad(e);
    }

    protected void Run(Func<CancellationToken, Task> operation, Action<Exception> onError) =>
        _work.Run(operation, onError);

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_closeReady)
        {
            base.OnFormClosing(e);
            return;
        }

        _work.Cancel();
        if (!_work.HasPending)
        {
            _closeReady = true;
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        base.OnFormClosing(e);
        if (_waitingToClose) return;
        _waitingToClose = true;
        Enabled = false;
        await _work.DrainAsync();
        _closeReady = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _work.Dispose();
        base.Dispose(disposing);
    }
}
