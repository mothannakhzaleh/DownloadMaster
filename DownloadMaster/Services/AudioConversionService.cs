using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
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

        var sourceInfo = new FileInfo(settings.InputPath);
        var originalSize = sourceInfo.Length;
        var sourceExtension = Path.GetExtension(settings.InputPath);

        var targetDefinition = settings.OperationMode == AudioOperationMode.Optimize
            ? AudioFormatCatalog.ResolveFromExtension(sourceExtension) ??
              AudioFormatCatalog.GetDefinition(AudioFormat.Mp3)
            : AudioFormatCatalog.GetDefinition(settings.TargetFormat);

        var outputPath = ResolveAudioOutputPath(settings, targetDefinition.Extension);
        var inPlaceOverwrite = settings.OutputLocation == AudioOutputLocation.SameFolder &&
            (settings.OperationMode == AudioOperationMode.Optimize ||
             targetDefinition.MatchesExtension(sourceExtension)) &&
            string.Equals(
                Path.GetFullPath(settings.InputPath),
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase);

        var writePath = inPlaceOverwrite
            ? Path.Combine(Path.GetDirectoryName(settings.InputPath)!, $".audcvt_{Guid.NewGuid():N}.tmp{targetDefinition.Extension}")
            : outputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);

        try
        {
            var args = BuildAudioArguments(settings, targetDefinition, writePath, optimize: settings.OperationMode == AudioOperationMode.Optimize);
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

    private static string BuildAudioArguments(
        AudioConversionSettings settings,
        AudioFormatDefinition targetDefinition,
        string outputPath,
        bool optimize)
    {
        var builder = new StringBuilder();
        builder.Append("-hide_banner -y ");
        builder.Append(CultureInfo.InvariantCulture, $"-i \"{settings.InputPath}\" ");
        builder.Append("-vn ");
        builder.Append(CultureInfo.InvariantCulture, $"-c:a {targetDefinition.AudioCodec} ");

        AppendQualityArgs(builder, targetDefinition, settings.Quality, optimize);

        if (!string.IsNullOrWhiteSpace(targetDefinition.ExtraOutputArgs))
            builder.Append(targetDefinition.ExtraOutputArgs).Append(' ');

        builder.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");
        return builder.ToString();
    }

    private static void AppendQualityArgs(
        StringBuilder builder,
        AudioFormatDefinition definition,
        AudioQualityPreset quality,
        bool optimize)
    {
        if (!definition.UsesQuality)
            return;

        if (definition.Format == AudioFormat.Amr)
        {
            var amrBitrate = quality.BitrateKbps switch
            {
                <= 64 => "5.9",
                <= 128 => "7.95",
                <= 192 => "10.2",
                _ => "12.2"
            };

            builder.Append(CultureInfo.InvariantCulture, $"-ab {amrBitrate}k ");
            return;
        }

        var bitrate = optimize && quality.BitrateKbps > 128
            ? Math.Max(128, quality.BitrateKbps - 32)
            : quality.BitrateKbps;

        builder.Append(CultureInfo.InvariantCulture, $"-b:a {bitrate}k ");
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
