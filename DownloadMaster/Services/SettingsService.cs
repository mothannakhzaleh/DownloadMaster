using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DownloadMaster");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

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
            Current.DefaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DownloadMaster");

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
        DefaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DownloadMaster")
    };
}
