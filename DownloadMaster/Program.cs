using Avalonia;
using DownloadMaster.Services.Bridge;

namespace DownloadMaster;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Already running? Hand the command line (e.g. a URL) to that instance and quit.
        if (SingleInstance.TryHandOff(args))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
