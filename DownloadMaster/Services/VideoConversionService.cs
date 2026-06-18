using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class VideoConversionService
{
    public async Task<VideoConversionResult> ConvertAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        FfmpegToolHelper.EnsureAvailable();

        if (!File.Exists(settings.InputPath))
            throw new FileNotFoundException("Video file not found.", settings.InputPath);

        if (!VideoFormatCatalog.IsSupportedVideo(settings.InputPath))
            throw new InvalidOperationException("Unsupported video format.");

        return settings.OperationMode switch
        {
            VideoOperationMode.ToGif => await ConvertToGifAsync(settings, progress, cancellationToken),
            VideoOperationMode.ToFrameTextures => await ExtractFrameTexturesAsync(settings, progress, cancellationToken),
            _ => await ConvertOrOptimizeVideoAsync(settings, progress, cancellationToken)
        };
    }

    private async Task<VideoConversionResult> ConvertOrOptimizeVideoAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var sourceInfo = new FileInfo(settings.InputPath);
        var originalSize = sourceInfo.Length;
        var sourceExtension = Path.GetExtension(settings.InputPath);

        var targetDefinition = settings.OperationMode == VideoOperationMode.Optimize
            ? VideoFormatCatalog.ResolveFromExtension(sourceExtension) ??
              VideoFormatCatalog.GetDefinition(VideoFormat.Mp4)
            : VideoFormatCatalog.GetDefinition(settings.TargetFormat);

        var outputPath = ResolveVideoOutputPath(settings, targetDefinition.Extension);
        var inPlaceOverwrite = settings.OutputLocation == VideoOutputLocation.SameFolder &&
            (settings.OperationMode == VideoOperationMode.Optimize ||
             targetDefinition.MatchesExtension(sourceExtension)) &&
            string.Equals(
                Path.GetFullPath(settings.InputPath),
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase);

        var writePath = inPlaceOverwrite
            ? Path.Combine(Path.GetDirectoryName(settings.InputPath)!, $".vidcvt_{Guid.NewGuid():N}.tmp{targetDefinition.Extension}")
            : outputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);

        try
        {
            var args = BuildVideoArguments(settings, targetDefinition, writePath);
            await RunFfmpegAsync(args, progress, cancellationToken);

            if (inPlaceOverwrite)
            {
                File.Move(writePath, settings.InputPath, overwrite: true);
                outputPath = settings.InputPath;
            }
            else
            {
                outputPath = writePath;
            }

            return SuccessResult(settings.InputPath, outputPath, originalSize);
        }
        catch
        {
            if (File.Exists(writePath) && inPlaceOverwrite)
            {
                try { File.Delete(writePath); } catch { /* ignore */ }
            }

            throw;
        }
    }

    private async Task<VideoConversionResult> ConvertToGifAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var originalSize = new FileInfo(settings.InputPath).Length;
        var outputPath = ResolveVideoOutputPath(settings, ".gif");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var fps = Math.Clamp(settings.GifFps, 1, 60);
        var preFilters = new List<string> { $"fps={fps}" };
        var scale = settings.Resolution.ToScaleFilterSegment();
        if (scale != null)
            preFilters.Add(scale);

        var filter =
            $"{string.Join(",", preFilters)},split[s0][s1];" +
            "[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer";

        var args =
            $"-hide_banner -y -i \"{settings.InputPath}\" -vf \"{filter}\" -loop 0 \"{outputPath}\"";

        await RunFfmpegAsync(args, progress, cancellationToken);
        return SuccessResult(settings.InputPath, outputPath, originalSize);
    }

    private async Task<VideoConversionResult> ExtractFrameTexturesAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var originalSize = new FileInfo(settings.InputPath).Length;
        var outputDir = ResolveFrameOutputDirectory(settings);
        Directory.CreateDirectory(outputDir);

        var extension = GetFrameExtension(settings.FrameTextureFormat);
        var outputPattern = Path.Combine(outputDir, $"frame_%04d{extension}");

        var builder = new StringBuilder();
        builder.Append("-hide_banner -y ");
        builder.Append(CultureInfo.InvariantCulture, $"-i \"{settings.InputPath}\" ");

        var filterParts = new List<string>();
        if (settings.FrameExtractMode == FrameExtractMode.TargetFps)
        {
            var fps = Math.Clamp(settings.FrameExtractFps, 1, 120);
            filterParts.Add($"fps={fps}");
        }

        var scale = settings.Resolution.ToScaleFilterSegment();
        if (scale != null)
            filterParts.Add(scale);

        if (filterParts.Count > 0)
            builder.Append(CultureInfo.InvariantCulture, $"-vf \"{string.Join(",", filterParts)}\" ");

        if (settings.FrameExtractMode == FrameExtractMode.EveryFrame)
            builder.Append("-vsync 0 ");

        builder.Append(CultureInfo.InvariantCulture, $"\"{outputPattern}\"");

        await RunFfmpegAsync(builder.ToString(), progress, cancellationToken);

        var frameFiles = Directory
            .EnumerateFiles(outputDir, $"frame_*{extension}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (frameFiles.Count == 0)
            throw new InvalidOperationException("No frames were extracted. Check the source video has a video stream.");

        var outputSize = frameFiles.Sum(path => new FileInfo(path).Length);

        return new VideoConversionResult
        {
            SourcePath = settings.InputPath,
            OutputPath = outputDir,
            OriginalSizeBytes = originalSize,
            OutputSizeBytes = outputSize,
            Success = true,
            ExtractedFrameCount = frameFiles.Count
        };
    }

    private static string ResolveVideoOutputPath(VideoConversionSettings settings, string extension)
    {
        var fileName = Path.GetFileNameWithoutExtension(settings.InputPath) + extension;

        if (settings.OutputLocation == VideoOutputLocation.CustomFolder &&
            !string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            return Path.Combine(settings.OutputFolder, fileName);
        }

        return Path.Combine(Path.GetDirectoryName(settings.InputPath)!, fileName);
    }

    private static string ResolveFrameOutputDirectory(VideoConversionSettings settings)
    {
        var folderName = $"{Path.GetFileNameWithoutExtension(settings.InputPath)}_frames";

        if (settings.OutputLocation == VideoOutputLocation.CustomFolder &&
            !string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            return Path.Combine(settings.OutputFolder, folderName);
        }

        return Path.Combine(Path.GetDirectoryName(settings.InputPath)!, folderName);
    }

    private static string GetFrameExtension(FrameTextureFormat format) => format switch
    {
        FrameTextureFormat.Jpg => ".jpg",
        FrameTextureFormat.Bmp => ".bmp",
        FrameTextureFormat.Tga => ".tga",
        _ => ".png"
    };

    private static string BuildVideoArguments(
        VideoConversionSettings settings,
        VideoFormatDefinition targetDefinition,
        string outputPath)
    {
        var builder = new StringBuilder();
        builder.Append("-hide_banner -y ");
        builder.Append(CultureInfo.InvariantCulture, $"-i \"{settings.InputPath}\" ");

        var scaleFilter = settings.Resolution.ToScaleFilterSegment();
        if (scaleFilter != null)
            builder.Append(CultureInfo.InvariantCulture, $"-vf \"{scaleFilter}\" ");

        if (settings.OperationMode == VideoOperationMode.Optimize)
            AppendOptimizeArgs(builder, targetDefinition);
        else
            AppendConvertArgs(builder, targetDefinition);

        if (!string.IsNullOrWhiteSpace(targetDefinition.ExtraOutputArgs))
            builder.Append(targetDefinition.ExtraOutputArgs).Append(' ');

        builder.Append(CultureInfo.InvariantCulture, $"-c:v {targetDefinition.VideoCodec} ");
        builder.Append(CultureInfo.InvariantCulture, $"-c:a {targetDefinition.AudioCodec} ");
        builder.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");

        return builder.ToString();
    }

    private static VideoConversionResult SuccessResult(string sourcePath, string outputPath, long originalSize)
    {
        var outputSize = File.Exists(outputPath)
            ? new FileInfo(outputPath).Length
            : Directory.Exists(outputPath)
                ? Directory.EnumerateFiles(outputPath, "*.*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length)
                : 0;

        return new VideoConversionResult
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            OriginalSizeBytes = originalSize,
            OutputSizeBytes = outputSize,
            Success = true
        };
    }

    private static void AppendOptimizeArgs(StringBuilder builder, VideoFormatDefinition definition)
    {
        switch (definition.VideoCodec)
        {
            case "libx264":
                builder.Append("-preset slow -crf 23 ");
                break;
            case "libx265":
                builder.Append("-preset slow -crf 28 ");
                break;
            case "libvpx-vp9":
                builder.Append("-crf 31 -b:v 0 ");
                break;
            case "libtheora":
                builder.Append("-q:v 7 ");
                break;
            case "mpeg2video":
                builder.Append("-q:v 4 ");
                break;
            case "wmv2":
                builder.Append("-q:v 7 ");
                break;
            default:
                builder.Append("-crf 23 ");
                break;
        }

        if (definition.AudioCodec is "aac" or "libopus" or "libmp3lame" or "libvorbis" or "wmav2" or "mp2")
            builder.Append("-b:a 128k ");
    }

    private static void AppendConvertArgs(StringBuilder builder, VideoFormatDefinition definition)
    {
        switch (definition.VideoCodec)
        {
            case "libx264":
            case "libx265":
                builder.Append("-preset medium -crf 23 ");
                break;
            case "libvpx-vp9":
                builder.Append("-crf 31 -b:v 0 ");
                break;
            case "libtheora":
                builder.Append("-q:v 6 ");
                break;
            case "mpeg2video":
                builder.Append("-q:v 4 ");
                break;
            case "wmv2":
                builder.Append("-q:v 7 ");
                break;
        }

        if (definition.AudioCodec is "aac" or "libopus" or "libmp3lame" or "libvorbis" or "wmav2" or "mp2")
            builder.Append("-b:a 160k ");
    }

    private static async Task RunFfmpegAsync(
        string arguments,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = FfmpegToolHelper.FfmpegExePath;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Start();

        var durationSeconds = 0.0;
        var frameCount = 0;
        var totalFrames = 0;

        while (!process.StandardError.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (durationSeconds <= 0)
            {
                var durationMatch = System.Text.RegularExpressions.Regex.Match(line, @"Duration:\s(\d+):(\d+):(\d+\.\d+)");
                if (durationMatch.Success)
                {
                    durationSeconds =
                        int.Parse(durationMatch.Groups[1].Value) * 3600 +
                        int.Parse(durationMatch.Groups[2].Value) * 60 +
                        double.Parse(durationMatch.Groups[3].Value, CultureInfo.InvariantCulture);
                }
            }

            var frameMatch = System.Text.RegularExpressions.Regex.Match(line, @"frame=\s*(\d+)");
            if (frameMatch.Success)
            {
                frameCount = int.Parse(frameMatch.Groups[1].Value);
                if (totalFrames <= 0 && durationSeconds > 0)
                    totalFrames = (int)Math.Ceiling(durationSeconds * 30);

                if (totalFrames > 0)
                    progress?.Report(Math.Clamp((double)frameCount / totalFrames * 100.0, 0, 100));
            }

            var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d+):(\d+):(\d+\.\d+)");
            if (timeMatch.Success && durationSeconds > 0)
            {
                var currentSeconds =
                    int.Parse(timeMatch.Groups[1].Value) * 3600 +
                    int.Parse(timeMatch.Groups[2].Value) * 60 +
                    double.Parse(timeMatch.Groups[3].Value, CultureInfo.InvariantCulture);

                progress?.Report(Math.Clamp(currentSeconds / durationSeconds * 100.0, 0, 100));
            }
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}.");
    }
}
