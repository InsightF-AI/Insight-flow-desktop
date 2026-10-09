namespace InsightFlow.Desktop.Presentation;

// O mesmo .ico do executável, embutido para as janelas e para a bandeja.
internal static class AppIcon
{
    public static Icon Window { get; } = Load();
    public static Icon Tray { get; } = new(Window, SystemInformation.SmallIconSize);

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("InsightFlow.Desktop.insightflow.ico")
            ?? throw new InvalidOperationException("O ícone do aplicativo não foi embutido.");
        return new Icon(stream);
    }
}
