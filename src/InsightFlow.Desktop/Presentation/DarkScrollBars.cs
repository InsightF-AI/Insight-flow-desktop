using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InsightFlow.Desktop.Presentation;

// O Windows desenha as barras de rolagem no tema claro, que destoa do fundo escuro das telas. O tema
// "DarkMode_Explorer" do próprio Windows (10 1809 ou mais novo; nos anteriores o pedido é ignorado) as deixa escuras.
internal static class DarkScrollBars
{
    private static readonly ConditionalWeakTable<Control, object> Attached = new();

    // Vale para o controle e todos os seus filhos, inclusive os adicionados depois e os que têm a janela recriada.
    public static void Attach(Control control)
    {
        if (!Attached.TryAdd(control, Attached)) return;
        control.HandleCreated += (_, _) => Apply(control);
        control.ControlAdded += (_, e) =>
        {
            if (e.Control is { } child) Attach(child);
        };
        if (control.IsHandleCreated) Apply(control);
        foreach (Control child in control.Controls) Attach(child);
    }

    // Só os controles que exibem barras de rolagem: nos demais, o tema mudaria o desenho nativo sem necessidade.
    // As barras das tabelas são filhas delas (ScrollBar); as da janela e dos painéis, da área não cliente.
    public static bool Scrolls(Control control) =>
        control is ScrollableControl or ScrollBar or ListBox or TextBoxBase { Multiline: true };

    private static void Apply(Control control)
    {
        if (Scrolls(control)) SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
}
