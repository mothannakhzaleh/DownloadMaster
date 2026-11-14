using System.IO;

namespace DownloadMaster.Services;

/// <summary>
/// Where DownloadMaster keeps its own data (settings, resume state, cookies, extension copy).
/// Resolved once, first match wins:
/// <list type="number">
///   <item><c>DOWNLOADMASTER_DATA_DIR</c> — sandbox; tests use a folder inside the repo so nothing touches the
///   real profile or the user's Downloads/Videos folders.</item>
///   <item>Portable mode — a <c>DownloadMaster.portable</c> file next to the executable keeps data in
///   <c>&lt;exe dir&gt;/data</c> (USB sticks, side-by-side copies). Only the data folder moves.</item>
///   <item>The per-user profile folder.</item>
/// </list>
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "DOWNLOADMASTER_DATA_DIR";

    /// <summary>Marker file that switches on portable mode when it sits next to the executable.</summary>
    public const string PortableMarkerName = "DownloadMaster.portable";

    public static string? SandboxDir
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(DataDirVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    public static bool IsSandboxed => SandboxDir is not null;

    /// <summary>
    /// True when the portable marker is next to the executable and no sandbox overrides it,
    /// i.e. <see cref="DataDir"/> is <c>&lt;exe dir&gt;/data</c>.
    /// </summary>
    public static bool IsPortable { get; } =
        !IsSandboxed && File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarkerName));

    /// <summary>
    /// The sandbox, the portable <c>data</c> folder, or %APPDATA%\DownloadMaster, ~/.config/DownloadMaster,
    /// ~/Library/Application Support/DownloadMaster.
    /// </summary>
    public static string DataDir { get; } =
        SandboxDir
        ?? (IsPortable
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DownloadMaster"));
}
