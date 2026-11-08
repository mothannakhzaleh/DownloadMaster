using System.IO;

namespace DownloadMaster.Services.Queue;

public enum DownloadCategory
{
    Video,
    Music,
    Programs,
    Compressed,
    Documents,
    Images,
    Other
}

/// <summary>
/// IDM-style auto categorization: picks a <c>Video</c>, <c>Music</c>, <c>Programs</c>, <c>Compressed</c>,
/// <c>Documents</c>, <c>Images</c> or <c>Other</c> sub-folder from a file extension or MIME type.
/// </summary>
public static class DownloadCategoryRouter
{
    private static readonly Dictionary<string, DownloadCategory> ByExtension = Build(
        (DownloadCategory.Video, ["mp4", "mkv", "avi", "mov", "webm", "wmv", "flv", "m4v", "mpg", "mpeg", "3gp", "ts", "m2ts", "ogv", "vob"]),
        (DownloadCategory.Music, ["mp3", "flac", "wav", "m4a", "aac", "ogg", "oga", "opus", "wma", "alac", "aiff", "aif", "mka", "mid", "midi"]),
        (DownloadCategory.Programs, ["exe", "msi", "msix", "appx", "dmg", "pkg", "deb", "rpm", "appimage", "apk", "xapk", "bat", "cmd", "sh", "run", "jar", "flatpak", "snap"]),
        (DownloadCategory.Compressed, ["zip", "7z", "rar", "tar", "gz", "tgz", "xz", "txz", "bz2", "tbz2", "zst", "lz", "lzma", "cab", "iso", "img", "r00", "r01", "z01", "001"]),
        (DownloadCategory.Documents, ["pdf", "epub", "mobi", "azw3", "doc", "docx", "odt", "rtf", "txt", "md", "xls", "xlsx", "ods", "csv", "ppt", "pptx", "odp", "djvu", "chm"]),
        (DownloadCategory.Images, ["jpg", "jpeg", "png", "gif", "webp", "bmp", "tif", "tiff", "svg", "heic", "heif", "avif", "ico", "psd", "raw", "cr2", "nef"]));

    /// <summary>yt-dlp output formats that produce audio only.</summary>
    private static readonly HashSet<string> AudioFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "aac", "opus", "flac", "wav", "ogg", "vorbis", "alac", "audio", "bestaudio"
    };

    public static IReadOnlyList<DownloadCategory> All { get; } = Enum.GetValues<DownloadCategory>();

    /// <summary>Folder name on disk (kept in English so paths stay stable when the UI language changes).</summary>
    public static string FolderName(DownloadCategory category) => category.ToString();

    public static DownloadCategory Classify(string? fileNameOrUrl, string? mimeType = null)
    {
        var ext = GetExtension(fileNameOrUrl);
        if (ext.Length > 0 && ByExtension.TryGetValue(ext, out var byExt))
            return byExt;

        // Split archives: name.part1.rar is caught above; name.r05 / name.7z.002 here.
        if (ext.Length == 3 && (ext[0] == 'r' || ext[0] == 'z') && char.IsDigit(ext[1]) && char.IsDigit(ext[2]))
            return DownloadCategory.Compressed;
        if (ext.Length == 3 && ext.All(char.IsDigit))
            return DownloadCategory.Compressed;

        return ClassifyMime(mimeType);
    }

    /// <summary>Category for a yt-dlp download: audio formats go to Music, everything else to Video.</summary>
    public static DownloadCategory ClassifyMediaFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && AudioFormats.Contains(format.Trim().TrimStart('.'))
            ? DownloadCategory.Music
            : DownloadCategory.Video;

    /// <summary>
    /// Returns <paramref name="baseFolder"/>/&lt;Category&gt; when <paramref name="enabled"/>, otherwise the base folder.
    /// A base folder that already is a category folder (user picked ".../Video") is not nested again.
    /// </summary>
    public static string ResolveFolder(string baseFolder, DownloadCategory category, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(baseFolder))
            return baseFolder;

        var trimmed = baseFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        if (All.Any(c => string.Equals(FolderName(c), leaf, StringComparison.OrdinalIgnoreCase)))
            return baseFolder;

        return Path.Combine(trimmed, FolderName(category));
    }

    public static string ResolveFileFolder(string baseFolder, string? fileNameOrUrl, string? mimeType, bool enabled) =>
        ResolveFolder(baseFolder, Classify(fileNameOrUrl, mimeType), enabled);

    public static string ResolveMediaFolder(string baseFolder, string? format, bool enabled) =>
        ResolveFolder(baseFolder, ClassifyMediaFormat(format), enabled);

    private static DownloadCategory ClassifyMime(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return DownloadCategory.Other;

        var mime = mimeType.Split(';')[0].Trim().ToLowerInvariant();
        if (mime.StartsWith("video/", StringComparison.Ordinal)) return DownloadCategory.Video;
        if (mime.StartsWith("audio/", StringComparison.Ordinal)) return DownloadCategory.Music;
        if (mime.StartsWith("image/", StringComparison.Ordinal)) return DownloadCategory.Images;
        if (mime.StartsWith("text/", StringComparison.Ordinal)) return DownloadCategory.Documents;

        return mime switch
        {
            "application/zip" or "application/x-zip-compressed" or "application/x-7z-compressed"
                or "application/x-rar-compressed" or "application/vnd.rar" or "application/x-tar"
                or "application/gzip" or "application/x-gzip" or "application/x-xz" or "application/x-bzip2"
                or "application/zstd" or "application/x-iso9660-image" => DownloadCategory.Compressed,
            "application/x-msdownload" or "application/x-msdos-program" or "application/x-msi"
                or "application/x-ms-installer" or "application/vnd.microsoft.portable-executable"
                or "application/x-apple-diskimage" or "application/vnd.debian.binary-package"
                or "application/x-rpm" or "application/x-redhat-package-manager"
                or "application/vnd.android.package-archive" or "application/x-executable"
                or "application/java-archive" => DownloadCategory.Programs,
            "application/pdf" or "application/epub+zip" or "application/msword" or "application/rtf"
                or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint"
                or "application/vnd.oasis.opendocument.text" => DownloadCategory.Documents,
            _ when mime.StartsWith("application/vnd.openxmlformats-officedocument", StringComparison.Ordinal) => DownloadCategory.Documents,
            _ => DownloadCategory.Other
        };
    }

    private static string GetExtension(string? fileNameOrUrl)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrUrl))
            return string.Empty;

        var value = fileNameOrUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            value = Uri.UnescapeDataString(uri.AbsolutePath);

        var queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            value = value[..queryIndex];

        var ext = Path.GetExtension(value);
        return string.IsNullOrEmpty(ext) ? string.Empty : ext.TrimStart('.').ToLowerInvariant();
    }

    private static Dictionary<string, DownloadCategory> Build(params (DownloadCategory Category, string[] Extensions)[] groups)
    {
        var map = new Dictionary<string, DownloadCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, extensions) in groups)
        {
            foreach (var ext in extensions)
                map.TryAdd(ext, category);
        }

        return map;
    }
}
