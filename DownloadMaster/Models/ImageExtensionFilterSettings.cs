namespace DownloadMaster.Models;

public enum ImageExtensionFilterMode
{
    None,
    IncludeOnly,
    Exclude
}

/// <summary>Folder-mode filter: convert only, or skip, the selected file extensions.</summary>
public sealed class ImageExtensionFilterSettings
{
    public static ImageExtensionFilterSettings None { get; } = new();

    public ImageExtensionFilterMode Mode { get; init; } = ImageExtensionFilterMode.None;

    public IReadOnlySet<string> Extensions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsActive => Mode != ImageExtensionFilterMode.None && Extensions.Count > 0;

    public bool Matches(string path)
    {
        if (!IsActive)
            return true;

        var inSet = Extensions.Contains(Path.GetExtension(path));

        return Mode switch
        {
            ImageExtensionFilterMode.IncludeOnly => inSet,
            ImageExtensionFilterMode.Exclude => !inSet,
            _ => true
        };
    }
}
