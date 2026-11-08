using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

/// <summary>What the browser extension handed over, normalized for the popup.</summary>
public sealed class CaughtDownload
{
    public bool IsMedia { get; init; }
    public string Url { get; init; } = string.Empty;
    public string SuggestedName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string? MimeType { get; init; }
    public string? SourcePage { get; init; }
    public string? StreamType { get; init; }
    public string? QualityLabel { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

/// <summary>The user's choices in the popup.</summary>
public sealed record CaughtDownloadChoice(
    CaughtDownload Source,
    string FileName,
    string Folder,
    int Connections,
    string Quality,
    string Format);

/// <summary>"New download caught" popup. Opens on top of the browser even while the main window is in the tray.</summary>
public partial class DownloadCaughtWindow : Window
{
    private readonly CaughtDownload _download;
    private readonly LocalizationService _loc;
    private readonly Func<CaughtDownloadChoice, Task<bool>>? _onDownload;

    // Required by the Avalonia XAML loader / designer.
    public DownloadCaughtWindow() : this(new CaughtDownload(), new LocalizationService(), string.Empty, 8, null)
    {
    }

    public DownloadCaughtWindow(
        CaughtDownload download,
        LocalizationService loc,
        string defaultFolder,
        int defaultConnections,
        Func<CaughtDownloadChoice, Task<bool>>? onDownload)
    {
        InitializeComponent();
        _download = download;
        _loc = loc;
        _onDownload = onDownload;

        Title = _loc.Get(download.IsMedia ? "CaughtMediaTitle" : "CaughtFileTitle");
        HeaderText.Text = Title;
        UrlLabel.Text = _loc.Get("CaughtAddress");
        FileNameLabel.Text = _loc.Get("CaughtSaveAs");
        FolderLabel.Text = _loc.Get("SaveTo");
        ConnectionsLabel.Text = _loc.Get("FileDownloadConnections");
        QualityLabel.Text = _loc.Get("Quality");
        FormatLabel.Text = _loc.Get("Format");
        CancelButton.Content = _loc.Get("Cancel");
        DownloadButton.Content = _loc.Get("Download");
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        SourceText.Text = string.IsNullOrWhiteSpace(download.SourcePage)
            ? string.Empty
            : string.Format(_loc.Get("CaughtFromPage"), download.SourcePage);
        UrlText.Text = download.Url;
        FileNameBox.Text = download.SuggestedName;
        FolderBox.Text = defaultFolder;
        ConnectionsCombo.SelectedIndex = defaultConnections switch { <= 4 => 0, <= 8 => 1, <= 12 => 2, _ => 3 };

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(download.StreamType))
            details.Add(download.StreamType.ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(download.QualityLabel))
            details.Add(download.QualityLabel);
        if (download.FileSize > 0)
            details.Add(FormatHelpers.FormatBytes(download.FileSize));
        if (!string.IsNullOrWhiteSpace(download.MimeType))
            details.Add(download.MimeType);
        DetailsText.Text = string.Join(" · ", details);
        DetailsText.IsVisible = details.Count > 0;

        var cookieCount = download.Headers.TryGetValue("Cookie", out var cookie)
            ? cookie.Split(';', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;
        CookiesText.Text = cookieCount > 0
            ? string.Format(_loc.Get("CaughtCookies"), cookieCount)
            : _loc.Get("CaughtNoCookies");

        FileOptionsPanel.IsVisible = !download.IsMedia;
        MediaOptionsPanel.IsVisible = download.IsMedia;
        if (download.IsMedia)
        {
            QualityCombo.ItemsSource = VideoFormatAnalyzer.DefaultQualities;
            QualityCombo.SelectedItem = PickQuality(download.QualityLabel);
            FormatCombo.ItemsSource = VideoFormatAnalyzer.DefaultFormats;
            FormatCombo.SelectedItem = VideoFormatAnalyzer.DefaultFormats.FirstOrDefault();
        }

        Opened += (_, _) =>
        {
            Activate();
            FileNameBox.Focus();
        };
    }

    private static string? PickQuality(string? label)
    {
        var qualities = VideoFormatAnalyzer.DefaultQualities;
        if (!string.IsNullOrWhiteSpace(label))
        {
            var match = qualities.FirstOrDefault(q => label.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return qualities.Contains("best") ? "best" : qualities.LastOrDefault();
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("SavePath"), FolderBox.Text);
        if (folder is not null)
            FolderBox.Text = folder;
    }

    private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            await Dialogs.ShowAsync(this, _loc.Get("CaughtNoFolder"), Title ?? string.Empty, DialogKind.Warning);
            return;
        }

        var connections = ConnectionsCombo.SelectedItem is ComboBoxItem { Content: string text } && int.TryParse(text, out var n) ? n : 8;
        var choice = new CaughtDownloadChoice(
            _download,
            SanitizeFileName(FileNameBox.Text),
            folder,
            connections,
            QualityCombo.SelectedItem?.ToString() ?? "best",
            FormatCombo.SelectedItem?.ToString() ?? "mp4");

        DownloadButton.IsEnabled = false;
        var ok = _onDownload is null || await _onDownload(choice);
        DownloadButton.IsEnabled = true;
        if (ok)
            Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>Keeps names valid on Windows, macOS and Linux.</summary>
    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '|', '?', '*' };
        invalid.Add((char)92);
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        cleaned = cleaned.Trim(' ', '.');
        return cleaned.Length > 180 ? cleaned[..180] : cleaned;
    }
}
