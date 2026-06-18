using System.IO;
using System.Text;

namespace DownloadMaster.Services;

public static class InstagramCookiesBuilder
{
    public static string SavedCookiesPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DownloadMaster",
        "instagram-cookies.txt");

    public static bool HasSavedCookies() =>
        File.Exists(SavedCookiesPath) && InstagramCookieSession.ValidateCookiesFile(SavedCookiesPath);

    public static bool TryClearSavedCookies(out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(SavedCookiesPath))
                File.Delete(SavedCookiesPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void RepairSavedCookiesFile()
    {
        if (!File.Exists(SavedCookiesPath))
            return;

        var bytes = File.ReadAllBytes(SavedCookiesPath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            File.WriteAllBytes(SavedCookiesPath, bytes.AsSpan(3).ToArray());
    }

    public static bool TrySaveFromPaste(string paste, out string? error)
    {
        if (!TryBuildNetscapeFile(paste, out var content, out error))
            return false;

        if (!InstagramCookieSession.ValidateCookiesContent(content))
        {
            error = "Missing Instagram session cookies. Copy the full Cookie header from a request to instagram.com in DevTools → Network.";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SavedCookiesPath)!);
        WriteUtf8WithoutBom(SavedCookiesPath, content);
        return true;
    }

    public static bool TrySaveFromFile(string sourcePath, out string? error)
    {
        error = null;
        if (!File.Exists(sourcePath))
        {
            error = "File not found.";
            return false;
        }

        var content = File.ReadAllText(sourcePath);
        if (!InstagramCookieSession.ValidateCookiesContent(content))
        {
            error = "That file does not look like a valid Instagram cookies export.";
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SavedCookiesPath)!);
        WriteUtf8WithoutBom(SavedCookiesPath, NormalizeNetscapeContent(content));
        return true;
    }

    public static bool TryBuildNetscapeFile(string paste, out string content, out string? error)
    {
        content = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(paste))
        {
            error = "Paste the Cookie header from DevTools first.";
            return false;
        }

        paste = ExtractCookieHeader(paste).Trim();

        if (LooksLikeNetscapeFile(paste))
        {
            content = NormalizeNetscapeContent(paste);
            return true;
        }

        var cookies = ParseCookiePairs(paste);
        if (cookies.Count == 0)
        {
            error = "Could not read any cookies from that text. Paste the Cookie request header from DevTools → Network.";
            return false;
        }

        if (!cookies.ContainsKey("sessionid"))
        {
            error = "sessionid was not found. Make sure you copied the Cookie header from a request to instagram.com while logged in.";
            return false;
        }

        content = BuildNetscapeFile(cookies);
        return true;
    }

    private static string ExtractCookieHeader(string paste)
    {
        foreach (var rawLine in paste.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("cookie\t", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }
        }

        return paste.Trim();
    }

    private static bool LooksLikeNetscapeFile(string text) =>
        text.Contains("# Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase)
        || text.Split('\n').Any(line =>
            line.Contains('\t')
            && line.Contains("instagram.com", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> ParseCookiePairs(string paste)
    {
        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (paste.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
            paste = paste["cookie:".Length..].Trim();

        foreach (var rawLine in paste.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.Contains('\t'))
            {
                TryAddNetscapeLine(line, cookies);
                continue;
            }

            if (line.Contains(';'))
            {
                foreach (var pair in ParseSemicolonPairs(line))
                    cookies[pair.Key] = pair.Value;
                continue;
            }

            TryAddNameValueLine(line, cookies);
        }

        return cookies;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseSemicolonPairs(string header)
    {
        foreach (var segment in header.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (TrySplitNameValue(trimmed, out var name, out var value))
                yield return new KeyValuePair<string, string>(name, value);
        }
    }

    private static void TryAddNetscapeLine(string line, Dictionary<string, string> cookies)
    {
        var parts = line.Split('\t');
        if (parts.Length < 7)
            return;

        if (!parts[0].Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
            return;

        cookies[parts[5]] = parts[6];
    }

    private static void TryAddNameValueLine(string line, Dictionary<string, string> cookies)
    {
        if (TrySplitNameValue(line, out var name, out var value))
            cookies[name] = value;
    }

    private static bool TrySplitNameValue(string text, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        var separator = text.IndexOf('=');
        if (separator <= 0)
            separator = text.IndexOf(':');

        if (separator <= 0)
            return false;

        name = text[..separator].Trim().Trim('"');
        value = DecodeCookieValue(text[(separator + 1)..].Trim().Trim('"'));
        return name.Length > 0 && value.Length > 0;
    }

    private static string DecodeCookieValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string BuildNetscapeFile(IReadOnlyDictionary<string, string> cookies)
    {
        var expiry = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        sb.AppendLine("# Generated by DownloadMaster");

        foreach (var (name, value) in cookies.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var secure = name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) ? "TRUE" : "FALSE";
            sb.AppendLine($".instagram.com\tTRUE\t/\t{secure}\t{expiry}\t{name}\t{value}");
        }

        return sb.ToString();
    }

    private static string NormalizeNetscapeContent(string content)
    {
        if (content.Contains("# Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase))
            return content.TrimEnd() + Environment.NewLine;

        var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains('\t'))
                TryAddNetscapeLine(line, cookies);
        }

        return cookies.Count > 0 ? BuildNetscapeFile(cookies) : content.TrimEnd() + Environment.NewLine;
    }

    private static void WriteUtf8WithoutBom(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
