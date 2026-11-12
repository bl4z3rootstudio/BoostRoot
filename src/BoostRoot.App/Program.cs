using Avalonia;

namespace BoostRoot.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--revert-all"))
        {
            // Used by the uninstaller: undo everything without showing the UI.
            try { WindowsAppServices.Create().RevertAll(); return 0; }
            catch (Exception) { return 1; }
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
