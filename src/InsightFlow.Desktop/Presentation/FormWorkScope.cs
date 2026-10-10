namespace InsightFlow.Desktop.Presentation;

// Toda operação da tela é observada e aguardada enquanto o laço de mensagens ainda está rodando.
internal sealed class FormWorkScope : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Task> _tasks = [];
    public bool IsClosing => _lifetime.IsCancellationRequested;
    public bool HasPending => _tasks.Any(task => !task.IsCompleted);

    public void Run(Func<CancellationToken, Task> operation, Action<Exception> onError)
    {
        if (IsClosing) return;
        _tasks.RemoveAll(task => task.IsCompleted);
        _tasks.Add(ObserveAsync(operation, onError));
    }

    private async Task ObserveAsync(Func<CancellationToken, Task> operation, Action<Exception> onError)
    {
        try
        {
            await operation(_lifetime.Token);
        }
        catch (OperationCanceledException) when (IsClosing) { }
        catch (Exception exception)
        {
            if (!IsClosing) onError(exception);
        }
    }

    public void Cancel() => _lifetime.Cancel();
    public Task DrainAsync() => Task.WhenAll(_tasks.ToArray());
    public void Dispose() => _lifetime.Dispose();
}
