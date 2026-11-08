using System.Diagnostics;
using System.IO;
using ImageMagick;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

internal static class InstagramImageSaveHelper
{
    public static async Task SaveBytesAsPngAsync(byte[] bytes, string outputPath, CancellationToken ct) =>
        _ = await SaveImageBytesAsync(bytes, outputPath, null, ct);

    public static async Task<string> SaveImageBytesAsync(
        byte[] bytes,
        string preferredPngPath,
        DownloadItem? item,
        CancellationToken ct)
    {
        var extension = GuessImageExtension(bytes);
        item?.AppendDiagnostic($"Image format detected: {extension.TrimStart('.')}");

        if (extension == ".webp" && ToolLocator.FfmpegFolder is null)
        {
            item?.AppendDiagnostic("FFmpeg not installed — saving original WebP instead of PNG");
            return await SaveOriginalBytesAsync(bytes, preferredPngPath, extension, ct);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "DownloadMaster", $"{Guid.NewGuid():N}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        await File.WriteAllBytesAsync(tempPath, bytes, ct);

        try
        {
            try
            {
                await SaveFileAsPngAsync(tempPath, preferredPngPath, ct);
                item?.AppendDiagnostic($"Converted and saved PNG: {preferredPngPath}");
                return preferredPngPath;
            }
            catch (Exception ex)
            {
                item?.AppendDiagnostic($"PNG conversion failed: {ex.Message}");
                return await SaveOriginalBytesAsync(bytes, preferredPngPath, extension, ct);
            }
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static async Task<string> SaveOriginalBytesAsync(
        byte[] bytes,
        string preferredPngPath,
        string extension,
        CancellationToken ct)
    {
        var outputExtension = extension switch
        {
            ".webp" => ".webp",
            ".jpg" => ".jpg",
            ".jpeg" => ".jpg",
            ".png" => ".png",
            _ => ".jpg"
        };

        var outputPath = Path.ChangeExtension(preferredPngPath, outputExtension);
        await File.WriteAllBytesAsync(outputPath, bytes, ct);
        return outputPath;
    }

    private static string GuessImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            return ".webp";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'N'
            && bytes[3] == (byte)'G')
        {
            return ".png";
        }

        return ".bin";
    }

    public static async Task SaveFileAsPngAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        if (await TryConvertWithFfmpegAsync(inputPath, outputPath, ct))
            return;

        await Task.Run(() => ConvertWithMagick(inputPath, outputPath), ct);
    }

    private static async Task<bool> TryConvertWithFfmpegAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        var ffmpeg = ToolLocator.FfmpegExePath;
        if (ffmpeg is null || !File.Exists(ffmpeg))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-y", "-loglevel", "error", "-i", inputPath, "-frames:v", "1", outputPath })
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        _ = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 && File.Exists(outputPath);
    }

    private static void ConvertWithMagick(string inputPath, string outputPath)
    {
        using var image = new MagickImage(inputPath);
        image.Format = MagickFormat.Png;
        image.Write(outputPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
