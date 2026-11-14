using System.IO;
using System.Text.RegularExpressions;

namespace DownloadMaster.Services;

public enum ScanOutcome
{
    Clean,
    /// <summary>The scanner reported a threat. The file is kept (never deleted or quarantined by us).</summary>
    ThreatFound,
    NoScanner,
    /// <summary>The scanner ran but could not scan (disabled, error, timeout).</summary>
    Failed
}

/// <param name="Scanner">"Windows Defender" or "ClamAV".</param>
/// <param name="Detail">Threat name when found, error text when failed.</param>
public sealed record ScanResult(ScanOutcome Outcome, string Scanner = "", string Detail = "");

/// <summary>
/// Scans one finished download with the system antivirus: Windows Defender (MpCmdRun.exe) on
/// Windows, ClamAV (clamscan) on macOS / Linux. Report-only: remediation is disabled, so the file
/// stays where it is. UI-free; never throws except for cancellation.
/// </summary>
public static partial class AntivirusScanner
{
    public const string DefenderName = "Windows Defender";
    public const string ClamAvName = "ClamAV";

    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(30);

    // One scan at a time: scanners are CPU and disk heavy.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    // MpCmdRun: "Threat                  : Virus:DOS/EICAR_Test_File"
    [GeneratedRegex(@"^\s*Threat\s*:\s*(?<name>.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DefenderThreatRegex();

    // MpCmdRun when it could not run the scan: "CmdTool: Failed with hr = 0x80004005. Check ..."
    [GeneratedRegex(@"Failed with hr\s*=\s*(?<hr>0x[0-9A-Fa-f]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DefenderFailureRegex();

    // clamscan: "/path/file.zip: Win.Test.EICAR_HDB-1 FOUND"
    [GeneratedRegex(@":\s*(?<name>\S.*?)\s+FOUND\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ClamThreatRegex();

    /// <summary>Name of the scanner this platform would use, or null when none is installed.</summary>
    public static string? AvailableScanner =>
        OperatingSystem.IsWindows()
            ? FindDefender() is null ? null : DefenderName
            : FindClamScan() is null ? null : ClamAvName;

    /// <summary>
    /// MpCmdRun.exe: the newest %ProgramData%\Microsoft\Windows Defender\Platform\&lt;version&gt;\ (kept up
    /// to date by Windows Update), else %ProgramFiles%\Windows Defender.
    /// </summary>
    public static string? FindDefender()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var platform = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Microsoft", "Windows Defender", "Platform");
            if (Directory.Exists(platform))
            {
                var newest = Directory.EnumerateDirectories(platform)
                    .Select(dir => (Dir: dir, Version: ParseVersion(Path.GetFileName(dir))))
                    .Where(x => x.Version is not null && File.Exists(Path.Combine(x.Dir, "MpCmdRun.exe")))
                    .OrderByDescending(x => x.Version)
                    .FirstOrDefault();
                if (newest.Dir is not null)
                    return Path.Combine(newest.Dir, "MpCmdRun.exe");
            }
        }
        catch
        {
            // no access to ProgramData: fall back to Program Files
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var legacy = Path.Combine(programFiles, "Windows Defender", "MpCmdRun.exe");
        return File.Exists(legacy) ? legacy : null;
    }

    public static string? FindClamScan() =>
        OperatingSystem.IsWindows() ? null : ToolLocator.FindExecutable("clamscan");

    public static async Task<ScanResult> ScanAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return new ScanResult(ScanOutcome.Failed, Detail: "File not found.");

        string exe;
        string scanner;
        string[] arguments;
        if (OperatingSystem.IsWindows())
        {
            if (FindDefender() is not { } defender)
                return new ScanResult(ScanOutcome.NoScanner, DefenderName);
            exe = defender;
            scanner = DefenderName;
            // ScanType 3 = custom scan of one file; DisableRemediation = report only, keep the file.
            arguments = ["-Scan", "-ScanType", "3", "-File", Path.GetFullPath(path), "-DisableRemediation"];
        }
        else
        {
            if (FindClamScan() is not { } clam)
                return new ScanResult(ScanOutcome.NoScanner, ClamAvName);
            exe = clam;
            scanner = ClamAvName;
            // An absolute path always starts with '/', so it can never be read as an option.
            arguments = ["--no-summary", Path.GetFullPath(path)];
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ExternalToolRunner.RunAsync(exe, arguments, ScanTimeout, ct).ConfigureAwait(false);
            return Interpret(scanner, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ScanResult(ScanOutcome.Failed, scanner, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Maps a scanner's exit code: Defender 0 clean / 2 threat, clamscan 0 clean / 1 threat; anything else = could not scan.</summary>
    internal static ScanResult Interpret(string scanner, ExternalToolRunner.Result result)
    {
        if (result.TimedOut)
            return new ScanResult(ScanOutcome.Failed, scanner, "The scan timed out.");

        var threatCode = scanner == DefenderName ? 2 : 1;
        if (result.ExitCode == 0)
            return new ScanResult(ScanOutcome.Clean, scanner);

        // MpCmdRun also exits with 2 when it cannot scan at all (Defender turned off because another
        // antivirus is active, policy...): "CmdTool: Failed with hr = 0x80004005". That is no threat.
        if (scanner == DefenderName && DefenderFailureRegex().Match(result.Output) is { Success: true } failure)
            return new ScanResult(ScanOutcome.Failed, scanner,
                $"Windows Defender could not scan (hr {failure.Groups["hr"].Value}); it may be turned off");

        if (result.ExitCode == threatCode)
        {
            var regex = scanner == DefenderName ? DefenderThreatRegex() : ClamThreatRegex();
            var match = regex.Match(result.Output);
            return new ScanResult(ScanOutcome.ThreatFound, scanner, match.Success ? match.Groups["name"].Value : string.Empty);
        }

        var line = ExternalToolRunner.LastLine(result.Output);
        var detail = string.IsNullOrWhiteSpace(line) ? $"exit code {result.ExitCode}" : $"{line} (exit code {result.ExitCode})";
        return new ScanResult(ScanOutcome.Failed, scanner, detail);
    }

    private static Version? ParseVersion(string text)
    {
        // Folder names look like "4.18.25010.11-0".
        var dash = text.IndexOf('-');
        return Version.TryParse(dash > 0 ? text[..dash] : text, out var version) ? version : null;
    }
}
