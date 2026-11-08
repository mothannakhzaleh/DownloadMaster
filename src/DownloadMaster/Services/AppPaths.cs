using System.IO;

namespace DownloadMaster.Services;

/// <summary>
/// Where DownloadMaster keeps its own data (settings, resume state, cookies, extension copy).
/// Set DOWNLOADMASTER_DATA_DIR to sandbox a run — tests use a folder inside the repo so nothing
/// touches the real profile or the user's Downloads/Videos folders.
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "DOWNLOADMASTER_DATA_DIR";

    public static string? SandboxDir
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(DataDirVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }
    }

    public static bool IsSandboxed => SandboxDir is not null;

    /// <summary>%APPDATA%\DownloadMaster, ~/.config/DownloadMaster, ~/Library/Application Support/DownloadMaster — or the sandbox.</summary>
    public static string DataDir { get; } = SandboxDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DownloadMaster");
}
