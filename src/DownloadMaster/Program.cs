using Avalonia;
using Avalonia.Threading;
using DownloadMaster.Services.Bridge;

namespace DownloadMaster;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Already running? Hand the command line (e.g. a URL or /d switches) to that instance and quit.
        if (SingleInstance.TryHandOff(args))
            return;

        // Item changes come from download threads; Avalonia bindings must hear them on the UI thread.
        DownloadMaster.Models.DownloadItem.NotifyDispatcher = notify =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                notify();
            else
                Dispatcher.UIThread.Post(notify);
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
