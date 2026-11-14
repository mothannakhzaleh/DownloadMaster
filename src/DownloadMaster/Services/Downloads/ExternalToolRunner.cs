using System.Diagnostics;
using System.Text;

namespace DownloadMaster.Services;

/// <summary>
/// Runs a helper program (7-Zip, an antivirus scanner) without a console window. Arguments go
/// through ArgumentList (file names come from the web), stdin is closed at once so a prompt such as
/// "Enter password" fails instead of hanging, and stdout/stderr are drained so the child never blocks.
/// </summary>
internal static class ExternalToolRunner
{
    public sealed record Result(int ExitCode, string Output, bool TimedOut);

    public static ProcessStartInfo CreateStartInfo(string exe, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        return info;
    }

    public static async Task<Result> RunAsync(
        string exe,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var process = Process.Start(CreateStartInfo(exe, arguments))
            ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested)
                throw;
            timedOut = true;
        }

        var output = await SafeRead(stdout).ConfigureAwait(false) + await SafeRead(stderr).ConfigureAwait(false);
        return new Result(timedOut ? -1 : process.ExitCode, output, timedOut);
    }

    public static void Kill(Process process)
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

    /// <summary>Last non-empty line of a tool's output (for a short status message).</summary>
    public static string LastLine(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? string.Empty : lines[^1];
    }

    private static async Task<string> SafeRead(Task<string> read)
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
