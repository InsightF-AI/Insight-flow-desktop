using InsightFlow.Desktop.Configuration;
using Microsoft.Win32;

namespace InsightFlow.Desktop.Tests;

public sealed class LocalPreferencesTests
{
    [Fact]
    public void Preferences_default_to_the_tray_and_survive_a_round_trip()
    {
        var directory = Directory.CreateTempSubdirectory("insightflow-tests");
        try
        {
            // A pasta do aplicativo ainda não existe na primeira gravação.
            var path = Path.Combine(directory.FullName, "InsightFlow", "preferences.json");
            var preferences = UserPreferences.Load(path);
            Assert.True(preferences.KeepRunningInTray);
            Assert.False(preferences.BackgroundNoticeShown);

            preferences.KeepRunningInTray = false;
            preferences.BackgroundNoticeShown = true;
            preferences.Save();

            var reloaded = UserPreferences.Load(path);
            Assert.False(reloaded.KeepRunningInTray);
            Assert.True(reloaded.BackgroundNoticeShown);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("isto não é JSON")]
    [InlineData("[]")]
    [InlineData("""{"KeepRunningInTray":"sim","BackgroundNoticeShown":1}""")]
    public void An_unreadable_preferences_file_falls_back_to_the_defaults(string content)
    {
        var directory = Directory.CreateTempSubdirectory("insightflow-tests");
        try
        {
            var path = Path.Combine(directory.FullName, "preferences.json");
            File.WriteAllText(path, content);

            var preferences = UserPreferences.Load(path);

            Assert.True(preferences.KeepRunningInTray);
            Assert.False(preferences.BackgroundNoticeShown);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Starting_with_windows_writes_and_removes_only_the_app_entry()
    {
        var keyPath = $@"Software\InsightFlow.Tests.{Guid.NewGuid():N}";
        try
        {
            var startup = new WindowsStartup("\"C:\\Apps\\InsightFlow.Desktop.exe\"", keyPath);
            Assert.False(startup.IsEnabled);

            using (var key = Registry.CurrentUser.CreateSubKey(keyPath)) key.SetValue("Outro", "outro.exe");
            startup.Set(true);
            Assert.True(startup.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
            {
                Assert.Equal("\"C:\\Apps\\InsightFlow.Desktop.exe\"", key.GetValue("InsightFlow"));
            }

            startup.Set(false);
            startup.Set(false);
            Assert.False(startup.IsEnabled);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!) Assert.Equal("outro.exe", key.GetValue("Outro"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
        }
    }

    [Theory]
    [InlineData(@"C:\Apps\InsightFlow\InsightFlow.Desktop.exe", "\"C:\\Apps\\InsightFlow\\InsightFlow.Desktop.exe\"")]
    // Pelo host "dotnet", a linha de comando leva a DLL do aplicativo.
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\Apps\\InsightFlow\\InsightFlow.Desktop.dll\"")]
    public void The_startup_command_points_to_the_app_whether_it_runs_from_the_exe_or_the_dotnet_host(string processPath, string expected)
    {
        Assert.Equal(expected, WindowsStartup.Command(processPath, @"C:\Apps\InsightFlow\InsightFlow.Desktop.dll"));
    }

    [Fact]
    public void A_second_instance_is_detected_and_asks_the_first_one_to_come_forward()
    {
        var name = $"InsightFlow.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstance(name);
        using var activated = new ManualResetEventSlim();
        first.Listen(activated.Set);
        Assert.True(first.IsFirst);
        Assert.False(activated.IsSet);

        using (var second = new SingleInstance(name))
        {
            Assert.False(second.IsFirst);
            second.ActivateFirst();
        }

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }
}
