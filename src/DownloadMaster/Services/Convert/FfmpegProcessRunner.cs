using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DownloadMaster.Services;

/// <summary>
/// Runs ffmpeg with an argument list (no shell quoting, works on Windows, macOS and Linux),
/// reports progress from stderr, kills the process on cancel and surfaces ffmpeg's last error line.
/// </summary>
public static partial class FfmpegProcessRunner
{
    private const int ErrorTailLines = 12;

    public static async Task RunAsync(
        IEnumerable<string> arguments,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        FfmpegToolHelper.EnsureAvailable();
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegToolHelper.FfmpegExePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        // -nostdin: never wait on the terminal (matters on macOS/Linux when launched from a shell).
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-y");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
            throw new InvalidOperationException("Could not start FFmpeg. " + ToolLocator.InstallHint);

        process.OutputDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();

        await using var registration = cancellationToken.Register(() => TryKill(process));

        var durationSeconds = 0.0;
        var tail = new Queue<string>(ErrorTailLines);

        while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (durationSeconds <= 0)
            {
                var durationMatch = DurationRegex().Match(line);
                if (durationMatch.Success)
                    durationSeconds = ParseSeconds(durationMatch);
            }

            var timeMatch = TimeRegex().Match(line);
            if (timeMatch.Success)
            {
                if (durationSeconds > 0)
                    progress?.Report(Math.Clamp(ParseSeconds(timeMatch) / durationSeconds * 100.0, 0, 100));
                continue;
            }

            if (tail.Count == ErrorTailLines)
                tail.Dequeue();
            tail.Enqueue(line.Trim());
        }

        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var reason = PickErrorLine(tail);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(reason)
                    ? $"FFmpeg failed with exit code {process.ExitCode}."
                    : $"FFmpeg failed (exit code {process.ExitCode}): {reason}");
        }

        progress?.Report(100);
    }

    /// <summary>Split a catalog argument string such as "-movflags +faststart" into list items.</summary>
    public static IEnumerable<string> SplitArgs(string? args) =>
        string.IsNullOrWhiteSpace(args)
            ? []
            : args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string PickErrorLine(IEnumerable<string> tail)
    {
        var lines = tail.ToList();
        var error = lines.LastOrDefault(l =>
            l.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("unknown encoder", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("unable", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("no such", StringComparison.OrdinalIgnoreCase));

        // "Conversion failed!" is the generic closing line; prefer the specific cause before it.
        if (error is null || error.StartsWith("Conversion failed", StringComparison.OrdinalIgnoreCase))
            error = lines.LastOrDefault(l => !l.StartsWith("Conversion failed", StringComparison.OrdinalIgnoreCase)) ?? error;

        return error ?? string.Empty;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already exited
        }
    }

    private static double ParseSeconds(Match match) =>
        int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
        int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
        double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"time=\s*(\d+):(\d+):(\d+(?:\.\d+)?)")]
    private static partial Regex TimeRegex();
}
