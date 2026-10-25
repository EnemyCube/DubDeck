using System.IO;
using System.Windows;

namespace DubDeck.App;

public partial class App : Application
{
    public static string? SmokeTestDirectory { get; private set; }
    public static bool SimulateVideoStartupStall { get; private set; }
    public static string? PerformanceTestDirectory { get; private set; }
    public static string? FixtureDirectory { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--initialize-vlc-cache")) { using var cacheEngine = new Media.MediaEngine(true); Shutdown(0); return; }
            string? startupPath = null;
            for (int i = 0; i < e.Args.Length; i++)
            {
                if (e.Args[i] == "--smoke-test" && i + 1 < e.Args.Length) SmokeTestDirectory = Path.GetFullPath(e.Args[++i]);
                else if (e.Args[i] == "--simulate-video-stall") SimulateVideoStartupStall = true;
                else if (e.Args[i] == "--performance-test" && i + 1 < e.Args.Length) PerformanceTestDirectory = Path.GetFullPath(e.Args[++i]);
                else if (e.Args[i] == "--fixtures" && i + 1 < e.Args.Length) FixtureDirectory = Path.GetFullPath(e.Args[++i]);
                else if (!e.Args[i].StartsWith("--")) startupPath = Path.GetFullPath(e.Args[i]);
            }
            MainWindow = new MainWindow(startupPath);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            if (SmokeTestDirectory is not null) { Directory.CreateDirectory(SmokeTestDirectory); File.WriteAllText(Path.Combine(SmokeTestDirectory, "failure.txt"), ex.ToString()); }
            else if (e.Args.Contains("--initialize-vlc-cache")) File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "cache-error.txt"), ex.ToString());
            else MessageBox.Show("DubDeck could not start.\n\n" + ex.Message, "DubDeck", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
