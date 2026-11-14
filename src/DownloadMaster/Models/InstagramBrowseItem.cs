using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DownloadMaster.Models;

public sealed class InstagramBrowseSection
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? BatchUrl { get; init; }
    public List<InstagramBrowseItem> Items { get; init; } = [];
}

/// <summary>One file on Instagram's CDN and the name to save it under (images arrive as WebP).</summary>
public sealed record InstagramDirectMedia(string Url, bool IsVideo, string FileName);

public sealed class InstagramBrowseItem : INotifyPropertyChanged
{
    private bool _selected = true;

    public required string SectionKey { get; init; }
    public required string SectionTitle { get; init; }
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Kind { get; init; }
    public string? MediaPk { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? HighlightStoryIndex { get; init; }
    public int? CarouselSlideIndex { get; set; }

    /// <summary>
    /// Files read from Instagram's page by the browser extension: downloaded straight from Instagram's
    /// CDN instead of through yt-dlp. A carousel or highlight "all" item lists every file.
    /// </summary>
    public IReadOnlyList<InstagramDirectMedia>? DirectMedia { get; init; }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
