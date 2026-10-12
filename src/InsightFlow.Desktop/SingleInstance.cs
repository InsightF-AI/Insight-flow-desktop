using System.Runtime.InteropServices;

namespace InsightFlow.Desktop;

// Uma instância por usuário do Windows. Abrir o aplicativo de novo traz à frente a que já está rodando,
// inclusive quando ela está só na bandeja.
internal sealed class SingleInstance : IDisposable
{
    // O instalador usa o mesmo nome (AppMutex) para saber se o aplicativo está aberto.
    public const string DefaultName = "InsightFlow.Desktop";
    private const int AnyProcess = -1;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _listener;

    public bool IsFirst { get; }

    public SingleInstance(string name = DefaultName)
    {
        _mutex = new Mutex(false, name, out var created);
        IsFirst = created;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Ativar");
    }

    // "activate" roda numa thread do pool, a cada pedido de outra instância.
    public void Listen(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        _listener = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);
    }

    public void ActivateFirst()
    {
        // Só o processo em primeiro plano pode ceder o foco; sem isto, a janela da outra instância apenas piscaria na barra de tarefas.
        AllowSetForegroundWindow(AnyProcess);
        _activation.Set();
    }

    public void Dispose()
    {
        _listener?.Unregister(null);
        _activation.Dispose();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
