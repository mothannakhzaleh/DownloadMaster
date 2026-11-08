using System.IO;
using System.Net;

namespace DownloadMaster.Services;

public static class InstagramCookieFileReader
{
    public static bool TryRead(string cookieFilePath, out CookieContainer container, out string? csrfToken, out string? error)
    {
        container = new CookieContainer();
        csrfToken = null;
        error = null;

        if (!File.Exists(cookieFilePath))
        {
            error = "Cookies file not found.";
            return false;
        }

        foreach (var rawLine in File.ReadAllLines(cookieFilePath))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith('#'))
                continue;

            var parts = rawLine.Split('\t');
            if (parts.Length < 7)
                continue;

            if (!parts[0].Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = parts[5].Trim();
            var value = parts[6].Trim().Trim('"');
            if (name.Length == 0)
                continue;

            if (name.Equals("csrftoken", StringComparison.OrdinalIgnoreCase))
                csrfToken = value;

            container.Add(new Cookie(name, value, "/", ".instagram.com"));
        }

        if (string.IsNullOrWhiteSpace(csrfToken))
        {
            error = "csrftoken cookie is missing.";
            return false;
        }

        return container.GetCookies(new Uri("https://www.instagram.com/")).Count > 0;
    }

    public static string BuildCookieHeader(string cookieFilePath)
    {
        if (!TryRead(cookieFilePath, out var container, out _, out _))
            return string.Empty;

        return string.Join("; ", container.GetCookies(new Uri("https://www.instagram.com/"))
            .Cast<Cookie>()
            .Select(cookie => $"{cookie.Name}={cookie.Value}"));
    }
}
