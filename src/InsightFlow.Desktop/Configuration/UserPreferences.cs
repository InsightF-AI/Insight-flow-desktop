using System.Text.Json;

namespace InsightFlow.Desktop.Configuration;

// Preferências do usuário do Windows, sem segredos: ficam em %LOCALAPPDATA%\InsightFlow\preferences.json.
public sealed class UserPreferences
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly string _path;

    // Fechar a janela principal deixa o aplicativo na bandeja, recebendo notificações.
    public bool KeepRunningInTray { get; set; } = true;

    // O aviso de que o aplicativo continua na bandeja aparece só na primeira vez em que a janela é fechada.
    public bool BackgroundNoticeShown { get; set; }

    private UserPreferences(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InsightFlow", "preferences.json");

    // Arquivo ausente, ilegível ou inválido não impede o uso: valem os padrões.
    public static UserPreferences Load(string? path = null)
    {
        var preferences = new UserPreferences(path ?? DefaultPath);
        try
        {
            if (!File.Exists(preferences._path)) return preferences;
            using var document = JsonDocument.Parse(File.ReadAllText(preferences._path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return preferences;
            preferences.KeepRunningInTray = Flag(document.RootElement, nameof(KeepRunningInTray)) ?? preferences.KeepRunningInTray;
            preferences.BackgroundNoticeShown = Flag(document.RootElement, nameof(BackgroundNoticeShown)) ?? preferences.BackgroundNoticeShown;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return preferences;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new { KeepRunningInTray, BackgroundNoticeShown }, Indented));
    }

    private static bool? Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
