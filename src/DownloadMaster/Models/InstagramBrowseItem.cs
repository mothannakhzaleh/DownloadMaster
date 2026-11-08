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

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
