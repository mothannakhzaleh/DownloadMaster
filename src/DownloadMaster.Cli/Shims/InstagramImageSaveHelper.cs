using DownloadMaster.Models;

namespace DownloadMaster.Services;

/// <summary>
/// Command-line version of the app's image helper: saves Instagram images as downloaded (JPEG/WebP/…)
/// instead of re-encoding them to PNG, so the tool doesn't need the Magick.NET image library.
/// </summary>
internal static class InstagramImageSaveHelper
{
    public static async Task<string> SaveImageBytesAsync(byte[] bytes, string preferredPngPath, DownloadItem? item, CancellationToken ct)
    {
        var extension = GuessExtension(bytes);
        var path = Path.ChangeExtension(preferredPngPath, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
        item?.AppendDiagnostic($"Saved original image: {path}");
        return path;
    }

    private static string GuessExtension(byte[] b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8)
            return ".jpg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return ".png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            return ".webp";
        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
            return ".gif";
        return ".jpg";
    }
}
