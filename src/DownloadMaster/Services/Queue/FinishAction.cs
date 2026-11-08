using System.Diagnostics;
using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services.Queue;

/// <summary>What to do when every download in the queue has finished.</summary>
public enum FinishAction
{
    Nothing,
    Sleep,
    ShutDown,
    QuitApp
}

/// <summary>Runs a <see cref="FinishAction"/>. Injectable so tests never sleep or power off the machine.</summary>
public interface IFinishActionExecutor
{
    void Execute(FinishAction action);
}

/// <summary>OS commands for sleep / shut down. "Quit app" is delegated to the caller.</summary>
public sealed class SystemFinishActionExecutor(Action quitApp) : IFinishActionExecutor
{
    public void Execute(FinishAction action)
    {
        switch (action)
        {
            case FinishAction.QuitApp:
                quitApp();
                return;
            case FinishAction.Sleep:
            case FinishAction.ShutDown:
                if (BuildCommand(action) is { } psi)
                    Process.Start(psi);
                return;
        }
    }

    /// <summary>The command for the current OS, or null when there is none (exposed for tests).</summary>
    public static ProcessStartInfo? BuildCommand(FinishAction action)
    {
        static ProcessStartInfo Make(string file, params string[] args)
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            return psi;
        }

        if (OperatingSystem.IsWindows())
        {
            return action switch
            {
                FinishAction.ShutDown => Make("shutdown", "/s", "/t", "0"),
                FinishAction.Sleep => Make("rundll32", "powrprof.dll,SetSuspendState", "0,1,0"),
                _ => null
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return action switch
            {
                FinishAction.ShutDown => Make("osascript", "-e", "tell app \"System Events\" to shut down"),
                FinishAction.Sleep => Make("pmset", "sleepnow"),
                _ => null
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return action switch
            {
                FinishAction.ShutDown => Make("systemctl", "poweroff"),
                FinishAction.Sleep => Make("systemctl", "suspend"),
                _ => null
            };
        }

        return null;
    }
}

/// <summary>
/// Used for sandboxed runs (DOWNLOADMASTER_DATA_DIR set): logs what would have happened to
/// &lt;sandbox&gt;/finish-action.log instead of sleeping or shutting down the machine.
/// </summary>
public sealed class DryRunFinishActionExecutor(string logPath, Action? quitApp = null) : IFinishActionExecutor
{
    public List<FinishAction> Executed { get; } = [];

    public void Execute(FinishAction action)
    {
        Executed.Add(action);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var command = SystemFinishActionExecutor.BuildCommand(action);
            var line = command is null
                ? $"{DateTime.Now:O} {action}"
                : $"{DateTime.Now:O} {action}: {command.FileName} {string.Join(' ', command.ArgumentList)}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // logging only
        }

        if (action == FinishAction.QuitApp)
            quitApp?.Invoke();
    }
}

/// <summary>Decides when the queue counts as "all finished" for the finish action.</summary>
public static class QueueCompletion
{
    public static bool IsActive(DownloadItem item) =>
        item.Status is DownloadStatus.Queued or DownloadStatus.Fetching or DownloadStatus.Downloading or DownloadStatus.Paused;

    /// <summary>True when nothing is queued, running, paused or held and at least one item completed.</summary>
    public static bool IsAllFinished(IReadOnlyCollection<DownloadItem> items, int heldCount) =>
        heldCount == 0
        && items.Count > 0
        && !items.Any(IsActive)
        && items.Any(i => i.Status == DownloadStatus.Completed);
}
