using System.Globalization;
using System.IO;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class AudioConversionService
{
    public async Task<AudioConversionResult> ConvertAsync(
        AudioConversionSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        FfmpegToolHelper.EnsureAvailable();

        if (!File.Exists(settings.InputPath))
            throw new FileNotFoundException("Audio file not found.", settings.InputPath);

        if (!AudioFormatCatalog.IsSupportedAudio(settings.InputPath))
            throw new InvalidOperationException("Unsupported audio or video format.");

        var originalSize = new FileInfo(settings.InputPath).Length;
        var sourceExtension = Path.GetExtension(settings.InputPath);

        var targetDefinition = settings.OperationMode == AudioOperationMode.Optimize
            ? AudioFormatCatalog.ResolveFromExtension(sourceExtension) ??
              AudioFormatCatalog.GetDefinition(AudioFormat.Mp3)
            : AudioFormatCatalog.GetDefinition(settings.TargetFormat);

        var outputPath = ResolveAudioOutputPath(settings, targetDefinition.Extension);

        // Case-insensitive on purpose: "song.MP3" -> "song.mp3" is the same file on Windows/macOS.
        var inPlaceOverwrite = string.Equals(
            Path.GetFullPath(settings.InputPath),
            Path.GetFullPath(outputPath),
            StringComparison.OrdinalIgnoreCase);

        var writePath = inPlaceOverwrite
            ? Path.Combine(Path.GetDirectoryName(settings.InputPath)!, $".audcvt_{Guid.NewGuid():N}.tmp{targetDefinition.Extension}")
            : outputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        var startedUtc = DateTime.UtcNow;

        try
        {
            var args = BuildAudioArguments(
                settings,
                targetDefinition,
                writePath,
                optimize: settings.OperationMode == AudioOperationMode.Optimize);

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

    private static string ResolveAudioOutputPath(AudioConversionSettings settings, string extension)
    {
        var fileName = Path.GetFileNameWithoutExtension(settings.InputPath) + extension;

        if (settings.OutputLocation == AudioOutputLocation.CustomFolder &&
            !string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            return Path.Combine(settings.OutputFolder, fileName);
        }

        return Path.Combine(Path.GetDirectoryName(settings.InputPath)!, fileName);
    }

    private static List<string> BuildAudioArguments(
        AudioConversionSettings settings,
        AudioFormatDefinition targetDefinition,
        string outputPath,
        bool optimize)
    {
        List<string> args = ["-i", settings.InputPath, "-vn", "-c:a", targetDefinition.AudioCodec];

        AppendQualityArgs(args, targetDefinition, settings.Quality, optimize);
        args.AddRange(FfmpegProcessRunner.SplitArgs(targetDefinition.ExtraOutputArgs));
        args.Add(outputPath);

        return args;
    }

    private static void AppendQualityArgs(
        List<string> args,
        AudioFormatDefinition definition,
        AudioQualityPreset quality,
        bool optimize)
    {
        if (!definition.UsesQuality)
            return;

        if (definition.Format == AudioFormat.Amr)
        {
            // AMR-NB only supports fixed modes (4.75k–12.2k).
            var amrBitrate = quality.BitrateKbps switch
            {
                <= 64 => "5.9k",
                <= 128 => "7.95k",
                <= 192 => "10.2k",
                _ => "12.2k"
            };

            args.AddRange(["-b:a", amrBitrate]);
            return;
        }

        var bitrate = optimize && quality.BitrateKbps > 128
            ? Math.Max(128, quality.BitrateKbps - 32)
            : quality.BitrateKbps;

        args.AddRange(["-b:a", bitrate.ToString(CultureInfo.InvariantCulture) + "k"]);
    }

    private static AudioConversionResult SuccessResult(string sourcePath, string outputPath, long originalSize)
    {
        var outputSize = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;

        return new AudioConversionResult
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            OriginalSizeBytes = originalSize,
            OutputSizeBytes = outputSize,
            Success = true
        };
    }

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
