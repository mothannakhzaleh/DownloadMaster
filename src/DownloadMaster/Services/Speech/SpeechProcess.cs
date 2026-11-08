using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DownloadMaster.Services.Speech;

internal sealed record SpeechProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string ErrorSummary(string tool)
    {
        var detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
        detail = detail.Trim();
        if (detail.Length > 400)
            detail = detail[^400..];

        return string.IsNullOrWhiteSpace(detail)
            ? $"{tool} failed with exit code {ExitCode}."
            : $"{tool} failed with exit code {ExitCode}: {detail}";
    }
}

/// <summary>Tracks the one live (spoken-aloud) utterance of a process engine so Stop can kill it.</summary>
internal sealed class LiveSpeechSlot
{
    private readonly object _sync = new();
    private CancellationTokenSource? _current;

    public async Task RunAsync(CancellationToken cancellationToken, Func<CancellationToken, Task> speak)
    {
        using var live = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sync)
        {
            _current?.Cancel();
            _current = live;
        }

        try
        {
            await speak(live.Token);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_current, live))
                    _current = null;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
            _current?.Cancel();
    }
}

/// <summary>Runs command-line speech tools (say, espeak-ng, spd-say, ffmpeg) without a shell.</summary>
internal static class SpeechProcess
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Start <paramref name="fileName"/> with <paramref name="arguments"/>, optionally feed
    /// <paramref name="standardInput"/>, and wait. Cancelling kills the process tree and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static async Task<SpeechProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (standardInput is not null)
            startInfo.StandardInputEncoding = Utf8NoBom;

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        await using var registration = cancellationToken.Register(() => TryKill(process));

        if (standardInput is not null)
        {
            try
            {
                await process.StandardInput.WriteAsync(standardInput);
                await process.StandardInput.FlushAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                // Process exited early (killed or failed); its exit code tells the story.
            }
            finally
            {
                try { process.StandardInput.Close(); } catch { /* ignore */ }
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        cancellationToken.ThrowIfCancellationRequested();
        return new SpeechProcessResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>Run and throw <see cref="InvalidOperationException"/> on a non-zero exit code.</summary>
    public static async Task<SpeechProcessResult> RunCheckedAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(fileName, arguments, standardInput, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.ErrorSummary(Path.GetFileName(fileName)));

        return result;
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
            // already gone
        }
    }

    /// <summary>Find a command on PATH or in the usual Unix install folders (GUI apps may lack the shell PATH).</summary>
    public static string? FindExecutable(params string[] names)
    {
        var suffix = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var name in names)
        {
            foreach (var dir in SearchFolders())
            {
                var candidate = Path.Combine(dir, name + suffix);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> SearchFolders()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(dir))
                yield return dir;
        }

        if (OperatingSystem.IsWindows())
            yield break;

        string[] extra =
        [
            "/usr/bin",
            "/usr/local/bin",
            "/opt/homebrew/bin",
            "/snap/bin",
            "/run/current-system/sw/bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")
        ];

        foreach (var dir in extra)
        {
            if (seen.Add(dir))
                yield return dir;
        }
    }

    /// <summary>"en_US" / "en-us" / "ar_001" → localized culture name; falls back to the raw code.</summary>
    public static string DescribeLocale(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var normalized = code.Replace('_', '-');
        try
        {
            var culture = CultureInfo.GetCultureInfo(normalized);
            if (!string.IsNullOrWhiteSpace(culture.DisplayName) && culture.ThreeLetterISOLanguageName != "ivl")
                return culture.DisplayName;
        }
        catch (CultureNotFoundException)
        {
            // espeak uses some codes .NET does not know (e.g. "en-us-nyc", "art-lfn").
        }

        var dash = normalized.IndexOf('-');
        if (dash > 0)
        {
            try
            {
                var parent = CultureInfo.GetCultureInfo(normalized[..dash]);
                return $"{parent.DisplayName} ({normalized[(dash + 1)..]})";
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return code;
    }
}
