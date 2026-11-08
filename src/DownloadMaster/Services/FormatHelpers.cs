using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DownloadMaster.Services;

public static partial class FormatHelpers
{
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var pow = (int)Math.Floor(Math.Log(bytes) / Math.Log(1024));
        pow = Math.Min(pow, units.Length - 1);
        return $"{bytes / Math.Pow(1024, pow):0.#} {units[pow]}";
    }

    public static string FormatSpeed(double bytesPerSec) => $"{FormatBytes((long)bytesPerSec)}/s";

    public static string FormatDuration(int totalSeconds)
    {
        if (totalSeconds <= 0) return "—";

        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "_").Trim();
        if (cleaned.Length == 0) cleaned = "download";
        return cleaned[..Math.Min(180, cleaned.Length)];
    }

    public static string ApplyTemplate(string template, string title, string quality, string ext)
    {
        var safe = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "download" : title);
        return template
            .Replace("{title}", safe)
            .Replace("{quality}", quality)
            .Replace("{ext}", ext.TrimStart('.'));
    }
}
