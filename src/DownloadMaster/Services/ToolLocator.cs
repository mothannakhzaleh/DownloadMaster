using System.IO;

namespace DownloadMaster.Services;

/// <summary>
/// Finds yt-dlp, ffmpeg and ffprobe on Windows, macOS and Linux.
/// Order: bundled tools folder next to the app, then PATH and common install folders
/// (Homebrew, /usr/local/bin, /usr/bin, snap).
/// </summary>
public static class ToolLocator
{
    public static string ExeSuffix => OperatingSystem.IsWindows() ? ".exe" : string.Empty;

    /// <summary>Bundled tools folder copied next to the app on build/publish.</summary>
    public static string BundledToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    public static string BundledFfmpegDir => Path.Combine(BundledToolsDir, "ffmpeg");

    public static string BundledYtDlpPath => Path.Combine(BundledToolsDir, "yt-dlp" + ExeSuffix);

    private static string? _ffmpegFolder;
    private static string? _ytDlpPath;

    /// <summary>Folder that holds both ffmpeg and ffprobe.</summary>
    public static string? FfmpegFolder => _ffmpegFolder;
    public static string? YtDlpExecutable => _ytDlpPath;
    public static bool HasFfmpeg => _ffmpegFolder is not null;
    public static bool HasYtDlp => _ytDlpPath is not null;

    public static string? FfmpegExePath =>
        _ffmpegFolder is null ? null : Path.Combine(_ffmpegFolder, "ffmpeg" + ExeSuffix);

    public static string? FfprobeExePath =>
        _ffmpegFolder is null ? null : Path.Combine(_ffmpegFolder, "ffprobe" + ExeSuffix);

    /// <summary>Install hint shown when a tool is missing.</summary>
    public static string InstallHint =>
        OperatingSystem.IsWindows()
            ? "Run setup-tools.bat or place ffmpeg.exe, ffprobe.exe and yt-dlp.exe in the tools folder next to DownloadMaster."
            : OperatingSystem.IsMacOS()
                ? "Install with Homebrew: brew install ffmpeg yt-dlp (or run ./setup-tools.sh)."
                : "Install with your package manager, e.g. sudo apt install ffmpeg yt-dlp (or run ./setup-tools.sh).";

    public static bool ConfigureBundled()
    {
        _ffmpegFolder = ResolveFfmpegFolder();
        _ytDlpPath = ResolveYtDlp();
        return _ytDlpPath is not null;
    }

    private static string? ResolveFfmpegFolder()
    {
        if (HasBoth(BundledFfmpegDir))
        {
            EnsureExecutable(Path.Combine(BundledFfmpegDir, "ffmpeg" + ExeSuffix));
            EnsureExecutable(Path.Combine(BundledFfmpegDir, "ffprobe" + ExeSuffix));
            return BundledFfmpegDir;
        }

        return SearchFolders().FirstOrDefault(HasBoth);

        static bool HasBoth(string dir) =>
            File.Exists(Path.Combine(dir, "ffmpeg" + ExeSuffix))
            && File.Exists(Path.Combine(dir, "ffprobe" + ExeSuffix));
    }

    private static string? ResolveYtDlp()
    {
        // Release builds ship platform-specific yt-dlp names; accept all of them.
        var bundledNames = OperatingSystem.IsWindows()
            ? new[] { "yt-dlp.exe" }
            : OperatingSystem.IsMacOS()
                ? new[] { "yt-dlp", "yt-dlp_macos" }
                : new[] { "yt-dlp", "yt-dlp_linux", "yt-dlp_linux_aarch64" };

        foreach (var name in bundledNames)
        {
            var path = Path.Combine(BundledToolsDir, name);
            if (File.Exists(path))
            {
                EnsureExecutable(path);
                return Path.GetFullPath(path);
            }
        }

        return SearchFolders()
            .Select(dir => Path.Combine(dir, "yt-dlp" + ExeSuffix))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> SearchFolders()
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (seen.Add(dir))
                yield return dir;
        }

        if (OperatingSystem.IsWindows())
            yield break;

        // GUI apps on macOS do not inherit the shell PATH, so check the usual places.
        string[] extra =
        [
            "/opt/homebrew/bin",
            "/usr/local/bin",
            "/usr/bin",
            "/snap/bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")
        ];

        foreach (var dir in extra)
        {
            if (seen.Add(dir))
                yield return dir;
        }
    }

    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & UnixFileMode.UserExecute) == 0)
                File.SetUnixFileMode(path, mode | exec);
        }
        catch
        {
            // read-only install location — the tool may already be executable
        }
    }
}
