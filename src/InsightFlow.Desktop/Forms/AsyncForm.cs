using InsightFlow.Desktop.Presentation;

namespace InsightFlow.Desktop.Forms;

internal class AsyncForm : Form
{
    private readonly FormWorkScope _work = new();
    private bool _waitingToClose;
    private bool _closeReady;
    // Tamanho mínimo projetado da janela, em pixels lógicos (96 DPI), guardado antes de a escala ser aplicada.
    private Size _designMinimum;
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
        _designMinimum = MinimumSize;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        ApplyDesignMinimum();
        var area = Screen.FromControl(this).WorkingArea;
        if (Width > area.Width || Height > area.Height)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        }
        base.OnLoad(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Ao mudar para um monitor com outra escala, os mínimos são refeitos a partir do tamanho projetado.
        ApplyDesignMinimum();
    }

    // Em telas pequenas ou com escala alta, a janela (e o seu tamanho mínimo) não pode exceder a área útil.
    // Quando nem o mínimo projetado cabe, o conteúdo o mantém e a janela ganha barras de rolagem, em vez de
    // espremer e sobrepor os controles. O mínimo é reescalado aqui, e não pelo Windows Forms, que em escalas
    // altas já o entrega limitado ao tamanho da tela.
    private void ApplyDesignMinimum()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var designed = LogicalToDeviceUnits(_designMinimum);
        AutoScrollMinSize = designed - (Size - ClientSize);
        AutoScroll = true;
        MinimumSize = new Size(Math.Min(designed.Width, area.Width), Math.Min(designed.Height, area.Height));
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
