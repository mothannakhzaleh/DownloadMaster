using System.Diagnostics;
using System.IO;
using DownloadMaster.Models;
using DownloadMaster.Services.Bridge;

namespace DownloadMaster.Services;

public static class InstagramBrowserCookieSync
{
    /// <summary>How long "Sync from browser" waits for the extension (includes time to log in).</summary>
    public static readonly TimeSpan ExtensionWait = TimeSpan.FromMinutes(2);

    public static async Task<(bool Success, string? Error)> TrySyncAsync(
        InstagramBrowser browser,
        string outputPath,
        LocalizationService loc,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        // Chrome/Edge 127+ on Windows lock their cookies to the browser (app-bound encryption), so
        // yt-dlp can't read them even with the browser closed. Ask the extension instead.
        var canUseExtension = InstagramBrowserProfiles.IsChromiumFamily(browser) && ExtensionCookieRelay.BridgeAvailable();
        if (canUseExtension && OperatingSystem.IsWindows())
            return await SyncThroughExtensionAsync(browser, outputPath, loc, progress, ct);

        var viaYtDlp = await SyncWithYtDlpAsync(browser, outputPath, loc, ct);
        if (viaYtDlp.Success || !canUseExtension)
            return viaYtDlp;

        var viaExtension = await SyncThroughExtensionAsync(browser, outputPath, loc, progress, ct);
        return viaExtension.Success ? viaExtension : viaYtDlp;
    }

    private static async Task<(bool Success, string? Error)> SyncThroughExtensionAsync(
        InstagramBrowser browser,
        string outputPath,
        LocalizationService loc,
        Action<string>? progress,
        CancellationToken ct)
    {
        var name = InstagramBrowserProfiles.DisplayName(browser);

        // Start listening before the tab opens so a fast answer isn't missed.
        var wait = ExtensionCookieRelay.WaitAsync(ExtensionWait, ct);
        InstagramBrowserProfiles.OpenUrl(browser, ExtensionCookieRelay.SyncUrl);
        progress?.Invoke(string.Format(loc.Get("InstagramSyncWaitingExtension"), name));

        var answer = await wait;
        if (answer is null)
            return (false, string.Format(loc.Get("InstagramSyncNoExtension"), name));

        var content = ExtensionCookieRelay.ToNetscape(answer.Cookies);
        if (!InstagramCookieSession.ValidateCookiesContent(content))
            return (false, string.Format(loc.Get("InstagramSyncExtensionNoSession"), name));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, content, ct);
        return InstagramCookieSession.ValidateCookiesFile(outputPath)
            ? (true, null)
            : (false, string.Format(loc.Get("InstagramSyncExtensionNoSession"), name));
    }

    private static async Task<(bool Success, string? Error)> SyncWithYtDlpAsync(
        InstagramBrowser browser,
        string outputPath,
        LocalizationService loc,
        CancellationToken ct)
    {
        if (ToolLocator.YtDlpExecutable is null)
            return (false, loc.Get("ToolsMissingYtDlp"));

        if (InstagramBrowserProfiles.MustCloseBeforeSync(browser))
        {
            return (false, browser switch
            {
                InstagramBrowser.Edge => loc.Get("InstagramSyncCloseEdge"),
                InstagramBrowser.Chrome => loc.Get("InstagramSyncCloseChrome"),
                _ => string.Format(loc.Get("InstagramSyncCloseBrowser"), InstagramBrowserProfiles.DisplayName(browser))
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        string? lastError = null;
        foreach (var profile in InstagramBrowserProfiles.GetProfilesToTry(browser))
        {
            if (!InstagramBrowserProfiles.ProfileHasCookieDatabase(browser, profile))
                continue;

            var result = await RunExportAsync(browser, profile, outputPath, loc, ct);
            if (result.Success)
                return (true, null);

            lastError = result.Error;
        }

        if (lastError is not null)
            return (false, CleanYtDlpError(lastError));

        return (false, loc.Get("InstagramSyncNoDatabase"));
    }

    private static async Task<(bool Success, string? Error)> RunExportAsync(
        InstagramBrowser browser,
        string profile,
        string outputPath,
        LocalizationService loc,
        CancellationToken ct)
    {
        var browserArg = InstagramBrowserProfiles.BuildYtDlpBrowserArg(browser, profile);
        var startInfo = new ProcessStartInfo
        {
            FileName = ToolLocator.YtDlpExecutable!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--cookies-from-browser");
        startInfo.ArgumentList.Add(browserArg);
        startInfo.ArgumentList.Add("--cookies");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--ignore-errors");
        startInfo.ArgumentList.Add("--skip-download");
        startInfo.ArgumentList.Add("https://www.instagram.com/");

        using var process = Process.Start(startInfo);
        if (process is null)
            return (false, "Failed to start yt-dlp.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        InstagramCookiesBuilder.RepairSavedCookiesFile();
        if (File.Exists(outputPath) && InstagramCookieSession.ValidateCookiesFile(outputPath))
            return (true, null);

        if (File.Exists(outputPath))
            return (false, loc.Get("InstagramSyncNoSession"));

        if (process.ExitCode != 0)
            return (false, string.IsNullOrWhiteSpace(stderr) ? "yt-dlp could not read browser cookies." : CleanYtDlpError(stderr));

        return (false, loc.Get("InstagramSyncNoSession"));
    }

    private static string CleanYtDlpError(string error)
    {
        var lines = error
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : error.Trim();
    }
}
