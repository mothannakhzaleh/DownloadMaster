namespace DownloadMaster.Models;

using System.IO;

public enum VideoFormat
{
    Mp4,
    Mp4H265,
    Mkv,
    WebM,
    Avi,
    Mov,
    Wmv,
    Flv,
    Mpeg,
    M4v,
    Ts,
    ThreeGp,
    Ogv,
    Asf
}

public sealed class VideoFormatDefinition
{
    public required VideoFormat Format { get; init; }
    public required string DisplayName { get; init; }
    public required string Extension { get; init; }
    public required string VideoCodec { get; init; }
    public required string AudioCodec { get; init; }
    public string? ExtraOutputArgs { get; init; }

    public bool MatchesExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return Extension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
               AlternateExtensions.Any(a => a.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> AlternateExtensions { get; init; } = [];
}

public static class VideoFormatCatalog
{
    public static IReadOnlyList<VideoFormatDefinition> OutputFormats { get; } =
    [
        new() { Format = VideoFormat.Mp4, DisplayName = "MP4 (H.264 + AAC)", Extension = ".mp4", VideoCodec = "libx264", AudioCodec = "aac", ExtraOutputArgs = "-movflags +faststart" },
        new() { Format = VideoFormat.Mp4H265, DisplayName = "MP4 (H.265 / HEVC + AAC)", Extension = ".mp4", VideoCodec = "libx265", AudioCodec = "aac", ExtraOutputArgs = "-movflags +faststart -tag:v hvc1" },
        new() { Format = VideoFormat.Mkv, DisplayName = "MKV (H.264 + AAC)", Extension = ".mkv", VideoCodec = "libx264", AudioCodec = "aac" },
        new() { Format = VideoFormat.WebM, DisplayName = "WebM (VP9 + Opus)", Extension = ".webm", VideoCodec = "libvpx-vp9", AudioCodec = "libopus" },
        new() { Format = VideoFormat.Avi, DisplayName = "AVI (H.264 + MP3)", Extension = ".avi", VideoCodec = "libx264", AudioCodec = "libmp3lame" },
        new() { Format = VideoFormat.Mov, DisplayName = "MOV (H.264 + AAC)", Extension = ".mov", VideoCodec = "libx264", AudioCodec = "aac" },
        new() { Format = VideoFormat.Wmv, DisplayName = "WMV (WMV2 + WMA)", Extension = ".wmv", VideoCodec = "wmv2", AudioCodec = "wmav2" },
        new() { Format = VideoFormat.Flv, DisplayName = "FLV (H.264 + AAC)", Extension = ".flv", VideoCodec = "libx264", AudioCodec = "aac" },
        new() { Format = VideoFormat.Mpeg, DisplayName = "MPEG (MPEG-2 + MP2)", Extension = ".mpg", VideoCodec = "mpeg2video", AudioCodec = "mp2", AlternateExtensions = [".mpeg"] },
        new() { Format = VideoFormat.M4v, DisplayName = "M4V (H.264 + AAC)", Extension = ".m4v", VideoCodec = "libx264", AudioCodec = "aac", ExtraOutputArgs = "-movflags +faststart" },
        new() { Format = VideoFormat.Ts, DisplayName = "MPEG-TS (H.264 + AAC)", Extension = ".ts", VideoCodec = "libx264", AudioCodec = "aac", AlternateExtensions = [".mts", ".m2ts"] },
        new() { Format = VideoFormat.ThreeGp, DisplayName = "3GP (H.264 + AAC)", Extension = ".3gp", VideoCodec = "libx264", AudioCodec = "aac" },
        new() { Format = VideoFormat.Ogv, DisplayName = "OGV (Theora + Vorbis)", Extension = ".ogv", VideoCodec = "libtheora", AudioCodec = "libvorbis" },
        new() { Format = VideoFormat.Asf, DisplayName = "ASF (WMV2 + WMA)", Extension = ".asf", VideoCodec = "wmv2", AudioCodec = "wmav2" }
    ];

    private static readonly HashSet<string> SupportedInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".flv", ".mpg", ".mpeg",
        ".m4v", ".ts", ".mts", ".m2ts", ".3gp", ".3g2", ".ogv", ".ogm", ".asf",
        ".divx", ".vob", ".rm", ".rmvb", ".f4v", ".mxf", ".dv", ".wtv"
    };

    public static bool IsSupportedVideo(string path) =>
        SupportedInputExtensions.Contains(Path.GetExtension(path));

    public static VideoFormatDefinition GetDefinition(VideoFormat format) =>
        OutputFormats.First(f => f.Format == format);

    public static VideoFormatDefinition? ResolveFromExtension(string extension)
    {
        foreach (var definition in OutputFormats)
        {
            if (definition.MatchesExtension(extension))
                return definition;
        }

        return null;
    }

    public static string BuildInputFilter() =>
        string.Join(";", SupportedInputExtensions.Select(e => $"*{e}")) + "|All files|*.*";
}
