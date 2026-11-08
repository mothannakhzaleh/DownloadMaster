using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DownloadMaster.Services;
using DownloadMaster.Services.Queue;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

/// <summary>Expands a wildcard link (part_[01-20].rar) and queues every result as a direct-file download.</summary>
public partial class BatchDownloadWindow : Window
{
    private const int PreviewLimit = 200;

    private readonly LocalizationService _loc;
    private readonly Func<IReadOnlyList<string>, Task> _queue;
    private IReadOnlyList<string> _expanded = [];

    // Required by the Avalonia XAML loader / designer.
    public BatchDownloadWindow() : this(new LocalizationService(), _ => Task.CompletedTask)
    {
    }

    public BatchDownloadWindow(LocalizationService loc, Func<IReadOnlyList<string>, Task> queue, string? initialPattern = null)
    {
        InitializeComponent();
        _loc = loc;
        _queue = queue;

        Title = _loc.Get("BatchTitle");
        HeaderText.Text = _loc.Get("BatchTitle");
        HintText.Text = _loc.Get("BatchHint");
        PatternBox.Watermark = _loc.Get("BatchPatternPlaceholder");
        PreviewButton.Content = _loc.Get("BatchPreview");
        QueueButton.Content = _loc.Get("BatchQueueAll");
        CancelButton.Content = _loc.Get("Cancel");
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // Links read left-to-right even in Arabic.
        PatternBox.FlowDirection = FlowDirection.LeftToRight;
        PreviewList.FlowDirection = FlowDirection.LeftToRight;

        if (!string.IsNullOrWhiteSpace(initialPattern))
            PatternBox.Text = initialPattern;
        Opened += (_, _) => PatternBox.Focus();
    }

    /// <summary>Expands and validates a pattern. Returns the links, or null with an error message.</summary>
    public static IReadOnlyList<string>? TryExpand(string? pattern, LocalizationService loc, out string message)
    {
        message = string.Empty;
        pattern = pattern?.Trim() ?? string.Empty;
        if (pattern.Length == 0)
            return null;

        if (!BatchPatternExpander.HasPattern(pattern))
        {
            message = loc.Get("BatchNoPattern");
            return null;
        }

        IReadOnlyList<string> links;
        try
        {
            links = BatchPatternExpander.Expand(pattern);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            message = string.Format(loc.Get("BatchInvalid"), ex.Message);
            return null;
        }

        if (links.Any(link => !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            message = loc.Get("BatchInvalidUrl");
            return null;
        }

        message = string.Format(loc.Get("BatchCount"), links.Count);
        return links;
    }

    private void UpdatePreview()
    {
        var links = TryExpand(PatternBox.Text, _loc, out var message);
        _expanded = links ?? [];
        CountText.Text = message;

        var preview = _expanded.Take(PreviewLimit).ToList();
        if (_expanded.Count > PreviewLimit)
            preview.Add(string.Format(_loc.Get("BatchMore"), _expanded.Count - PreviewLimit));

        PreviewList.ItemsSource = preview;
        QueueButton.IsEnabled = _expanded.Count > 0;
    }

    private void PatternBox_TextChanged(object? sender, TextChangedEventArgs e) => UpdatePreview();

    private void PreviewButton_Click(object? sender, RoutedEventArgs e) => UpdatePreview();

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private async void QueueButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdatePreview();
        if (_expanded.Count == 0)
            return;

        try
        {
            QueueButton.IsEnabled = false;
            await _queue(_expanded);
            Close(true);
        }
        catch (Exception ex)
        {
            QueueButton.IsEnabled = true;
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("BatchTitle"), DialogKind.Error);
        }
    }
}
