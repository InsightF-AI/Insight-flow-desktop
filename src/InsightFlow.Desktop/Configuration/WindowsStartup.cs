using Microsoft.Win32;

namespace InsightFlow.Desktop.Configuration;

// Entrada do aplicativo em HKCU\…\Run: o Windows o abre quando o usuário entra, sem exigir administrador.
// O instalador remove a entrada na desinstalação.
public sealed class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _command;
    private readonly string _keyPath;
    private readonly string _valueName;

    public WindowsStartup(string? command = null, string keyPath = RunKey, string valueName = "InsightFlow")
    {
        _command = command ?? Command(
            Environment.ProcessPath ?? throw new InvalidOperationException("O caminho do executável não está disponível."),
            Path.Combine(AppContext.BaseDirectory, "InsightFlow.Desktop.dll"));
        _keyPath = keyPath;
        _valueName = valueName;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
            return key?.GetValue(_valueName) is string { Length: > 0 };
        }
    }

    public void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        if (enabled) key.SetValue(_valueName, _command);
        else key.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    // Pelo host "dotnet" (build sem o executável), a linha de comando precisa levar a DLL do aplicativo.
    internal static string Command(string processPath, string applicationDll) =>
        Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{processPath}\" \"{applicationDll}\""
            : $"\"{processPath}\"";
}
