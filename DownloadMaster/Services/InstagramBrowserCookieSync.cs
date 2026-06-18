using System.Diagnostics;
using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public static class InstagramBrowserCookieSync
{
    public static async Task<(bool Success, string? Error)> TrySyncAsync(
        InstagramBrowser browser,
        string outputPath,
        LocalizationService loc,
        CancellationToken ct = default)
    {
        if (ToolLocator.YtDlpExecutable is null)
            return (false, loc.Get("ToolsMissingYtDlp"));

        if (InstagramBrowserProfiles.IsBrowserRunning(browser))
        {
            return (false, browser switch
            {
                InstagramBrowser.Edge => loc.Get("InstagramSyncCloseEdge"),
                _ => loc.Get("InstagramSyncCloseChrome")
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
