namespace DownloadMaster.Services;

/// <summary>Guesses whether a link is a plain file (multi-connection engine) or a page/video (yt-dlp).</summary>
public static class LinkKind
{
    private static readonly string[] FileExtensions =
    [
        ".zip", ".7z", ".rar", ".exe", ".msi", ".iso", ".dmg", ".pkg", ".deb", ".rpm", ".appimage",
        ".tar", ".gz", ".xz", ".bz2", ".tgz", ".img", ".pdf", ".epub", ".mp3", ".flac", ".wav", ".apk", ".bin"
    ];

    public static bool LooksLikeDirectFile(Uri uri) =>
        FileExtensions.Any(ext => uri.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        || uri.Host.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeDirectFile(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && LooksLikeDirectFile(uri);
}
