using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir = AppPaths.DataDir;

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    /// <summary>Videos/DownloadMaster (Movies on macOS); falls back to the home folder when the OS has no videos folder.</summary>
    public static string DefaultSaveFolder
    {
        get
        {
            // Sandboxed runs (tests) save inside the sandbox, never in the user's folders.
            if (AppPaths.SandboxDir is { } sandbox)
                return Path.Combine(sandbox, "downloads");

            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrWhiteSpace(videos))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                videos = Path.Combine(home, OperatingSystem.IsMacOS() ? "Movies" : "Videos");
            }

            return Path.Combine(videos, "DownloadMaster");
        }
    }

    public AppSettings Current { get; private set; } = CreateDefault();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
            }
            else
            {
                Current = CreateDefault();
            }
        }
        catch
        {
            Current = CreateDefault();
        }

        if (string.IsNullOrWhiteSpace(Current.DefaultSavePath))
            Current.DefaultSavePath = DefaultSaveFolder;

        Directory.CreateDirectory(Current.DefaultSavePath);
        ToolLocator.ConfigureBundled();
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(settings.DefaultSavePath);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        ToolLocator.ConfigureBundled();
    }

    private static AppSettings CreateDefault() => new()
    {
        DefaultSavePath = DefaultSaveFolder
    };
}
