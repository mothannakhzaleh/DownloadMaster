using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class InstagramCookieSession : IDisposable
{
    private readonly string _tempDir;
    private bool _deleteTempOnDispose = true;

    public string CookieFilePath { get; }
    public bool HasCookies { get; private set; }
    public string? ExportError { get; private set; }

    private InstagramCookieSession(string tempDir, string cookieFilePath)
    {
        _tempDir = tempDir;
        CookieFilePath = cookieFilePath;
    }

    public string TempDirectory => _tempDir;

    public void DetachTempDirectory() => _deleteTempOnDispose = false;

    public static bool ValidateCookiesFile(string path)
    {
        if (!File.Exists(path))
            return false;

        return ValidateCookiesContent(File.ReadAllText(path));
    }

    public static bool ValidateCookiesContent(string content) =>
        !string.IsNullOrWhiteSpace(content)
        && content.Contains("instagram.com", StringComparison.OrdinalIgnoreCase)
        && content.Contains("sessionid", StringComparison.OrdinalIgnoreCase);

    public static InstagramCookieSession Create(string? sourceCookiesPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DownloadMaster", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cookieFilePath = Path.Combine(tempDir, "cookies.txt");
        var session = new InstagramCookieSession(tempDir, cookieFilePath);

        if (string.IsNullOrWhiteSpace(sourceCookiesPath) || !File.Exists(sourceCookiesPath))
        {
            session.ExportError = "Import an Instagram cookies.txt file first.";
            return session;
        }

        if (!ValidateCookiesFile(sourceCookiesPath))
        {
            session.ExportError = "The selected file does not look like a valid Instagram cookies.txt export.";
            return session;
        }

        File.Copy(sourceCookiesPath, cookieFilePath, true);
        StripUtf8BomIfPresent(cookieFilePath);
        session.HasCookies = true;
        return session;
    }

    public static (string CookieFilePath, string TempDirectory) CopyForDownload(string sourceCookieFile)
    {
        if (string.IsNullOrWhiteSpace(sourceCookieFile) || !File.Exists(sourceCookieFile))
            throw new InvalidOperationException("Instagram cookies are missing or expired.");

        var tempDir = Path.Combine(Path.GetTempPath(), "DownloadMaster", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cookieFilePath = Path.Combine(tempDir, "cookies.txt");
        File.Copy(sourceCookieFile, cookieFilePath, true);
        StripUtf8BomIfPresent(cookieFilePath);
        return (cookieFilePath, tempDir);
    }

    public string GetYtDlpArguments()
    {
        if (!HasCookies)
            throw new InvalidOperationException(ExportError ?? "No Instagram cookies found.");

        return $" --cookies \"{CookieFilePath}\"";
    }

    public static string FormatError(string rawMessage, LocalizationService loc)
    {
        var message = CleanYtDlpError(rawMessage);

        if (message.Contains("No video formats found", StringComparison.OrdinalIgnoreCase))
            return loc.Get("InstagramPhotoPostHint");

        if (message.Contains("Unable to extract data", StringComparison.OrdinalIgnoreCase)
            || message.Contains("marked as broken", StringComparison.OrdinalIgnoreCase))
        {
            return loc.Get("InstagramExtractError");
        }

        if (message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limiting", StringComparison.OrdinalIgnoreCase)
            || message.Contains("InstagramRateLimited", StringComparison.OrdinalIgnoreCase))
        {
            return loc.Get("InstagramRateLimited");
        }

        if (message.Contains("does not look like a Netscape format cookies file", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid length", StringComparison.OrdinalIgnoreCase))
        {
            return loc.Get("InstagramCookiesFileError");
        }

        if (message.Contains("Import an Instagram", StringComparison.OrdinalIgnoreCase)
            || message.Contains("No Instagram cookies", StringComparison.OrdinalIgnoreCase)
            || message.Contains("csrf token", StringComparison.OrdinalIgnoreCase)
            || message.Contains("login required", StringComparison.OrdinalIgnoreCase)
            || message.Contains("registered users who follow", StringComparison.OrdinalIgnoreCase))
        {
            return loc.Get("InstagramAuthError");
        }

        if (message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase))
            return loc.Get("InstagramRateLimited");

        return message;
    }

    private static void StripUtf8BomIfPresent(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
            return;

        File.WriteAllBytes(path, bytes.AsSpan(3).ToArray());
    }

    private static string CleanYtDlpError(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return rawMessage;

        var lines = rawMessage
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return lines.Count == 0 ? rawMessage.Trim() : string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        if (!_deleteTempOnDispose)
            return;

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
