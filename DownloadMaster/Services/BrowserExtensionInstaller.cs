using System.Diagnostics;
using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>
/// Sets up the bundled Chrome/Edge extension. Chromium only allows "Load unpacked" from a folder the
/// user picks, so we copy the extension to a stable folder (survives app updates and works when the
/// app runs from a read-only .app bundle) and open the browser's extensions page.
/// </summary>
public static class BrowserExtensionInstaller
{
    public static IReadOnlyList<InstagramBrowser> SupportedBrowsers { get; } =
    [
        InstagramBrowser.Chrome,
        InstagramBrowser.Edge,
        InstagramBrowser.Brave,
        InstagramBrowser.Chromium
    ];

    public static string BundledDir => Path.Combine(AppContext.BaseDirectory, "browser-extension");

    public static string InstallDir => Path.Combine(AppPaths.DataDir, "browser-extension");

    public static bool IsBundled => File.Exists(Path.Combine(BundledDir, "manifest.json"));

    /// <summary>Copies the bundled extension to <see cref="InstallDir"/> (refreshing older copies).</summary>
    public static string PrepareInstallFolder()
    {
        if (!IsBundled)
            throw new DirectoryNotFoundException($"The browser extension was not found next to the app ({BundledDir}).");

        Directory.CreateDirectory(InstallDir);
        foreach (var source in Directory.EnumerateFiles(BundledDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(BundledDir, source);
            var target = Path.Combine(InstallDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        return InstallDir;
    }

    public static bool CanOpenBrowser(InstagramBrowser browser) =>
        InstagramBrowserProfiles.FindExecutable(browser) is not null;

    /// <summary>Opens chrome://extensions (or the Edge/Brave equivalent) in the chosen browser.</summary>
    public static void OpenExtensionsPage(InstagramBrowser browser)
    {
        var executable = InstagramBrowserProfiles.FindExecutable(browser)
            ?? throw new FileNotFoundException(
                $"{InstagramBrowserProfiles.DisplayName(browser)} was not found. Install it or choose another browser.");

        var page = browser switch
        {
            InstagramBrowser.Edge => "edge://extensions",
            InstagramBrowser.Brave => "brave://extensions",
            _ => "chrome://extensions"
        };

        var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
        psi.ArgumentList.Add(page);
        Process.Start(psi);
    }
}
