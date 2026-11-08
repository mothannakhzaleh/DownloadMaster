using System.IO;
using System.Text.Json;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public static class FileDownloadStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Saves come from the progress ticker and the worker shutdown path; never interleave them.
    private static readonly object SaveLock = new();

    public static string StoreDirectory { get; } = Path.Combine(AppPaths.DataDir, "file-downloads");

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

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FileDownloadState>(json);
        }
        catch (JsonException)
        {
            // A torn/corrupt state file cannot be trusted for offsets; start the file over.
            return null;
        }
    }

    public static void Save(FileDownloadState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var path = GetStatePath(state.Id);
        var temp = path + ".tmp";

        lock (SaveLock)
        {
            Directory.CreateDirectory(StoreDirectory);
            // Write-then-rename so a crash mid-save leaves the previous state intact.
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
    }

    public static void Delete(string itemId)
    {
        var path = GetStatePath(itemId);
        lock (SaveLock)
        {
            if (File.Exists(path))
                File.Delete(path);
            TryDeleteFile(path + ".tmp");
        }
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
