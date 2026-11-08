using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DownloadMaster.Services.Ui;

/// <summary>Cross-platform file/folder pickers (replace WPF OpenFileDialog / OpenFolderDialog / SaveFileDialog).</summary>
public static class Pickers
{
    /// <summary>Build a filter, e.g. Filter("Video files", "mp4", "mkv"). Use "*" for all files.</summary>
    public static FilePickerFileType Filter(string name, params string[] extensions) =>
        new(name)
        {
            Patterns = extensions
                .Select(ext => ext == "*" ? "*" : "*." + ext.TrimStart('*', '.'))
                .ToList()
        };

    public static FilePickerFileType AllFiles(string name = "All files") => Filter(name, "*");

    public static async Task<string?> PickFolderAsync(Visual owner, string title, string? initialDirectory = null)
    {
        var storage = GetStorage(owner);
        if (storage is null)
            return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDirectory)
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickFileAsync(
        Visual owner,
        string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDirectory = null)
    {
        var files = await PickFilesCoreAsync(owner, title, filters, initialDirectory, allowMultiple: false);
        return files.FirstOrDefault();
    }

    public static Task<IReadOnlyList<string>> PickFilesAsync(
        Visual owner,
        string title,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDirectory = null) =>
        PickFilesCoreAsync(owner, title, filters, initialDirectory, allowMultiple: true);

    public static async Task<string?> SaveFileAsync(
        Visual owner,
        string title,
        string? suggestedFileName = null,
        string? defaultExtension = null,
        IReadOnlyList<FilePickerFileType>? filters = null,
        string? initialDirectory = null)
    {
        var storage = GetStorage(owner);
        if (storage is null)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension?.TrimStart('.'),
            FileTypeChoices = filters,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDirectory)
        });

        return file?.TryGetLocalPath();
    }

    private static async Task<IReadOnlyList<string>> PickFilesCoreAsync(
        Visual owner,
        string title,
        IReadOnlyList<FilePickerFileType>? filters,
        string? initialDirectory,
        bool allowMultiple)
    {
        var storage = GetStorage(owner);
        if (storage is null)
            return [];

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = filters,
            SuggestedStartLocation = await TryGetFolderAsync(storage, initialDirectory)
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();
    }

    private static IStorageProvider? GetStorage(Visual owner) =>
        TopLevel.GetTopLevel(owner)?.StorageProvider
        ?? Dialogs.FindOwner()?.StorageProvider;

    private static async Task<IStorageFolder?> TryGetFolderAsync(IStorageProvider storage, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (File.Exists(path))
                path = Path.GetDirectoryName(path);

            return path is not null && Directory.Exists(path)
                ? await storage.TryGetFolderFromPathAsync(path)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
