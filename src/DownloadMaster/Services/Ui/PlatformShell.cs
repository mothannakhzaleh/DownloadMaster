using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DownloadMaster.Services.Ui;

/// <summary>Open files, folders and links with the OS default handler (Windows, macOS, Linux).</summary>
public static class PlatformShell
{
    public static void OpenFile(string path) => Launch(path);

    public static void OpenFolder(string folder) => Launch(folder);

    public static void OpenUrl(string url) => Launch(url);

    /// <summary>Open the containing folder and select the file where the OS supports it.</summary>
    public static void RevealInFolder(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                OpenFolder(dir);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add($"/select,{filePath}");
            Process.Start(psi);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add("-R");
            psi.ArgumentList.Add(filePath);
            Process.Start(psi);
            return;
        }

        // Linux: no portable "select" — open the folder.
        OpenFolder(Path.GetDirectoryName(filePath)!);
    }

    public static async Task CopyTextAsync(Visual visual, string text)
    {
        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard
            ?? Dialogs.FindOwner()?.Clipboard
            ?? throw new InvalidOperationException("Clipboard is not available.");
        await clipboard.SetTextAsync(text);
    }

    private static void Launch(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return;
        }

        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
        psi.ArgumentList.Add(target);
        Process.Start(psi);
    }
}
