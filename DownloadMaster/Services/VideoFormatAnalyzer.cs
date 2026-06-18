using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public static class VideoFormatAnalyzer
{
    public static readonly string[] DefaultQualities = ["144p", "240p", "360p", "480p", "720p", "1080p", "1440p", "2160p", "best"];
    public static readonly string[] DefaultFormats = ["mp4", "mkv", "webm", "mp3"];

    private static readonly int[] StandardHeights = [144, 240, 360, 480, 720, 1080, 1440, 2160];

    public static void PopulateFromJson(VideoInfo info, JsonElement root, string preferredFormat)
    {
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
        {
            ApplyDefaults(info, preferredFormat);
            return;
        }

        var heights = new HashSet<int>();
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fmt in formats.EnumerateArray())
        {
            var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
            var hasVideo = vcodec is not null and not "none";

            if (hasVideo && fmt.TryGetProperty("height", out var h) && h.TryGetInt32(out var height) && height > 0)
                heights.Add(height);

            if (fmt.TryGetProperty("ext", out var extProp))
            {
                var ext = extProp.GetString();
                if (string.IsNullOrEmpty(ext)) continue;
                if (ext.Equals("m4a", StringComparison.OrdinalIgnoreCase))
                    exts.Add("mp4");
                else if (ext is "mp4" or "mkv" or "webm")
                    exts.Add(ext);
            }
        }

        if (heights.Count == 0)
        {
            info.MaxHeight = 0;
            info.RecommendedQuality = "best";
            info.AvailableQualities = ["best"];
            info.AvailableFormats = exts.Count > 0 ? [.. exts, "mp3"] : ["mp3"];
            info.RecommendedFormat = info.AvailableFormats.Contains("mp3") ? "mp3" : info.AvailableFormats[0];
            return;
        }

        info.MaxHeight = heights.Max();
        info.RecommendedQuality = HeightToQualityLabel(info.MaxHeight);

        var qualities = new List<string>();
        foreach (var std in StandardHeights)
        {
            if (heights.Any(h => h >= std))
                qualities.Add($"{std}p");
        }

        if (qualities.Count == 0)
            qualities.Add(info.RecommendedQuality);

        qualities.Add("best");
        info.AvailableQualities = qualities;

        var videoFormats = exts.Where(e => !e.Equals("mp3", StringComparison.OrdinalIgnoreCase)).ToList();
        if (videoFormats.Count == 0)
            videoFormats.Add("mp4");

        if (!videoFormats.Contains("mp3"))
            videoFormats.Add("mp3");

        info.AvailableFormats = videoFormats;
        info.RecommendedFormat = videoFormats.Contains(preferredFormat, StringComparer.OrdinalIgnoreCase)
            ? preferredFormat
            : videoFormats.Contains("mp4", StringComparer.OrdinalIgnoreCase) ? "mp4" : videoFormats[0];
    }

    public static void ApplyDefaults(VideoInfo info, string preferredFormat)
    {
        info.MaxHeight = 0;
        info.RecommendedQuality = "1080p";
        info.AvailableQualities = [.. DefaultQualities];
        info.AvailableFormats = [.. DefaultFormats];
        info.RecommendedFormat = preferredFormat;
    }

    private static string HeightToQualityLabel(int height)
    {
        var match = StandardHeights.Where(h => h <= height).DefaultIfEmpty(144).Max();
        return $"{match}p";
    }
}
