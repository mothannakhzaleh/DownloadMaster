using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public static class FileDownloadStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StoreDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DownloadMaster",
        "file-downloads");

    public static string GetStatePath(string itemId) => Path.Combine(StoreDirectory, $"{itemId}.json");

    public static bool ExistsForItem(string itemId)
    {
        var path = GetStatePath(itemId);
        if (!File.Exists(path))
            return false;

        try
        {
            var state = Load(itemId);
            return state is not null && File.Exists(state.PartPath);
        }
        catch
        {
            return false;
        }
    }

    public static FileDownloadState? Load(string itemId)
    {
        var path = GetStatePath(itemId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FileDownloadState>(json);
    }

    public static void Save(FileDownloadState state)
    {
        Directory.CreateDirectory(StoreDirectory);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(GetStatePath(state.Id), json);
    }

    public static void Delete(string itemId)
    {
        var path = GetStatePath(itemId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static void DeleteStateAndPart(FileDownloadState state)
    {
        Delete(state.Id);
        TryDeleteFile(state.PartPath);
    }

    public static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }
}
