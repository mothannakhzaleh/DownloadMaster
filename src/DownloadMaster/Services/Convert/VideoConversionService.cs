using System.IO;
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

    private static async Task<VideoConversionResult> ConvertOrOptimizeVideoAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var originalSize = new FileInfo(settings.InputPath).Length;
        var sourceExtension = Path.GetExtension(settings.InputPath);

        var targetDefinition = settings.OperationMode == VideoOperationMode.Optimize
            ? VideoFormatCatalog.ResolveFromExtension(sourceExtension) ??
              VideoFormatCatalog.GetDefinition(VideoFormat.Mp4)
            : VideoFormatCatalog.GetDefinition(settings.TargetFormat);

        var outputPath = ResolveVideoOutputPath(settings, targetDefinition.Extension);

        // Case-insensitive on purpose: Windows and macOS file systems are case-insensitive by default,
        // so "clip.MP4" -> "clip.mp4" is the same file and must go through a temp file.
        var inPlaceOverwrite = string.Equals(
            Path.GetFullPath(settings.InputPath),
            Path.GetFullPath(outputPath),
            StringComparison.OrdinalIgnoreCase);

        var writePath = inPlaceOverwrite
            ? Path.Combine(Path.GetDirectoryName(settings.InputPath)!, $".vidcvt_{Guid.NewGuid():N}.tmp{targetDefinition.Extension}")
            : outputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        var startedUtc = DateTime.UtcNow;

        try
        {
            var args = BuildVideoArguments(settings, targetDefinition, writePath);
            await FfmpegProcessRunner.RunAsync(args, progress, cancellationToken);

            if (inPlaceOverwrite)
            {
                File.Move(writePath, settings.InputPath, overwrite: true);
                outputPath = settings.InputPath;
            }

            return SuccessResult(settings.InputPath, outputPath, originalSize);
        }
        catch
        {
            TryDeletePartialOutput(writePath, startedUtc);
            throw;
        }
    }

    private static async Task<VideoConversionResult> ConvertToGifAsync(
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

        // Two-pass palette in one graph: generate an optimized palette, then dither with it.
        var filter =
            $"{string.Join(",", preFilters)},split[s0][s1];" +
            "[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer";

        List<string> args = ["-i", settings.InputPath, "-vf", filter, "-loop", "0", outputPath];
        var startedUtc = DateTime.UtcNow;

        try
        {
            await FfmpegProcessRunner.RunAsync(args, progress, cancellationToken);
        }
        catch
        {
            TryDeletePartialOutput(outputPath, startedUtc);
            throw;
        }

        return SuccessResult(settings.InputPath, outputPath, originalSize);
    }

    private static async Task<VideoConversionResult> ExtractFrameTexturesAsync(
        VideoConversionSettings settings,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var originalSize = new FileInfo(settings.InputPath).Length;
        var outputDir = ResolveFrameOutputDirectory(settings);
        var createdDir = !Directory.Exists(outputDir);
        Directory.CreateDirectory(outputDir);

        var extension = GetFrameExtension(settings.FrameTextureFormat);
        var outputPattern = Path.Combine(outputDir, $"frame_%04d{extension}");
        var startedUtc = DateTime.UtcNow.AddSeconds(-2);

        List<string> args = ["-i", settings.InputPath];

        var filterParts = new List<string>();
        if (settings.FrameExtractMode == FrameExtractMode.TargetFps)
            filterParts.Add($"fps={Math.Clamp(settings.FrameExtractFps, 1, 120)}");

        var scale = settings.Resolution.ToScaleFilterSegment();
        if (scale != null)
            filterParts.Add(scale);

        if (filterParts.Count > 0)
            args.AddRange(["-vf", string.Join(",", filterParts)]);

        if (settings.FrameExtractMode == FrameExtractMode.EveryFrame)
            args.AddRange(["-vsync", "0"]);

        if (settings.FrameTextureFormat == FrameTextureFormat.Jpg)
            args.AddRange(["-q:v", "2"]);

        args.Add(outputPattern);

        try
        {
            await FfmpegProcessRunner.RunAsync(args, progress, cancellationToken);
        }
        catch
        {
            if (createdDir)
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { /* ignore */ }
            }

            throw;
        }

        // Count only frames written by this run (the folder may hold frames from an earlier run).
        var frameFiles = Directory
            .EnumerateFiles(outputDir, $"frame_*{extension}")
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTimeUtc >= startedUtc)
            .ToList();

        if (frameFiles.Count == 0)
            throw new InvalidOperationException("No frames were extracted. Check the source video has a video stream.");

        return new VideoConversionResult
        {
            SourcePath = settings.InputPath,
            OutputPath = outputDir,
            OriginalSizeBytes = originalSize,
            OutputSizeBytes = frameFiles.Sum(info => info.Length),
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

    private static List<string> BuildVideoArguments(
        VideoConversionSettings settings,
        VideoFormatDefinition targetDefinition,
        string outputPath)
    {
        List<string> args = ["-i", settings.InputPath];

        var scaleFilter = settings.Resolution.ToScaleFilterSegment();
        if (scaleFilter != null)
            args.AddRange(["-vf", scaleFilter]);

        if (settings.OperationMode == VideoOperationMode.Optimize)
            AppendOptimizeArgs(args, targetDefinition);
        else
            AppendConvertArgs(args, targetDefinition);

        // 8-bit 4:2:0 keeps H.264/H.265 output playable everywhere (screen captures are often 4:4:4/RGB).
        if (targetDefinition.VideoCodec is "libx264" or "libx265")
            args.AddRange(["-pix_fmt", "yuv420p"]);

        args.AddRange(FfmpegProcessRunner.SplitArgs(targetDefinition.ExtraOutputArgs));
        args.AddRange(["-c:v", targetDefinition.VideoCodec]);
        args.AddRange(["-c:a", targetDefinition.AudioCodec]);
        args.Add(outputPath);

        return args;
    }

    private static VideoConversionResult SuccessResult(string sourcePath, string outputPath, long originalSize)
    {
        var outputSize = File.Exists(outputPath)
            ? new FileInfo(outputPath).Length
            : Directory.Exists(outputPath)
                ? Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length)
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

    private static void AppendOptimizeArgs(List<string> args, VideoFormatDefinition definition)
    {
        switch (definition.VideoCodec)
        {
            case "libx264":
                args.AddRange(["-preset", "slow", "-crf", "23"]);
                break;
            case "libx265":
                args.AddRange(["-preset", "slow", "-crf", "28"]);
                break;
            case "libvpx-vp9":
                args.AddRange(["-crf", "31", "-b:v", "0", "-row-mt", "1"]);
                break;
            case "libtheora":
                args.AddRange(["-q:v", "7"]);
                break;
            case "mpeg2video":
                args.AddRange(["-q:v", "4"]);
                break;
            case "wmv2":
                args.AddRange(["-q:v", "7"]);
                break;
            default:
                args.AddRange(["-crf", "23"]);
                break;
        }

        if (UsesAudioBitrate(definition.AudioCodec))
            args.AddRange(["-b:a", "128k"]);
    }

    private static void AppendConvertArgs(List<string> args, VideoFormatDefinition definition)
    {
        switch (definition.VideoCodec)
        {
            case "libx264":
            case "libx265":
                args.AddRange(["-preset", "medium", "-crf", "23"]);
                break;
            case "libvpx-vp9":
                args.AddRange(["-crf", "31", "-b:v", "0", "-row-mt", "1"]);
                break;
            case "libtheora":
                args.AddRange(["-q:v", "6"]);
                break;
            case "mpeg2video":
                args.AddRange(["-q:v", "4"]);
                break;
            case "wmv2":
                args.AddRange(["-q:v", "7"]);
                break;
        }

        if (UsesAudioBitrate(definition.AudioCodec))
            args.AddRange(["-b:a", "160k"]);
    }

    private static bool UsesAudioBitrate(string audioCodec) =>
        audioCodec is "aac" or "libopus" or "libmp3lame" or "libvorbis" or "wmav2" or "mp2";

    /// <summary>Remove a half-written output, but never a file ffmpeg did not touch in this run.</summary>
    private static void TryDeletePartialOutput(string path, DateTime startedUtc)
    {
        try
        {
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= startedUtc.AddSeconds(-2))
                File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}
