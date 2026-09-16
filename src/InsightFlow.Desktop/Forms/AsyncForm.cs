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
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        StartPosition = FormStartPosition.CenterScreen;
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
