using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using DownloadMaster.Models;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings = new();
    private readonly ThemeService _theme = new();
    private readonly LocalizationService _loc = new();
    private readonly DownloadManager _downloads;
    private readonly YtDlpService _ytDlp = new();
    private readonly InstagramProfileService _instagramProfiles = new();
    private readonly ObservableCollection<DownloadItem> _items = [];
    private readonly ObservableCollection<InstagramBrowseItem> _instagramBrowseItems = [];
    private readonly Dictionary<string, string?> _instagramSectionBatchUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Expander> _instagramSectionExpanders = [];
    private InstagramCookieSession? _instagramBrowseSession;
    private readonly Dictionary<string, string> _instagramMediaPkByShortcode = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncingBrowseSelection;

    public MainWindow()
    {
        InitializeComponent();
        _settings.Load();
        _downloads = new DownloadManager(_settings);
        DownloadList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateQueueEmptyState();
        UpdateQueueEmptyState();
        ApplyDownloadQueueLayout();
        ApplyInstagramBrowseLayout();

        QualityCombo.ItemsSource = VideoFormatAnalyzer.DefaultQualities;
        QualityCombo.SelectedItem = _settings.Current.DefaultQuality;
        FormatCombo.ItemsSource = VideoFormatAnalyzer.DefaultFormats;
        FormatCombo.SelectedItem = _settings.Current.PreferredFormat;
        SavePathBox.Text = _settings.Current.DefaultSavePath;
        BindLanguageCombo();
        BindInstagramBrowserCombo();
        InstagramSavePathBox.Text = SavePathBox.Text;
        LanguageCombo.SelectedValue = _settings.Current.Language;

        if (string.IsNullOrWhiteSpace(_settings.Current.InstagramCookiesPath)
            && File.Exists(InstagramCookiesBuilder.SavedCookiesPath))
        {
            _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
        }

        InstagramCookiesBuilder.RepairSavedCookiesFile();

        _theme.Apply(_settings.Current.Theme);
        _loc.SetLanguage(_settings.Current.Language);
        ApplyLocalization();
        UpdateInstagramCookiesStatus();
    }

    private void UpdateInstagramCookiesStatus()
    {
        var path = _settings.Current.InstagramCookiesPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)
            || !InstagramCookieSession.ValidateCookiesFile(path))
        {
            InstagramCookiesStatusText.Text = _loc.Get("InstagramCookiesMissing");
            return;
        }

        InstagramCookiesStatusText.Text = _loc.Get("InstagramCookiesReady");
    }

    private void UpdateToolsStatus()
    {
        if (ToolLocator.HasYtDlp && ToolLocator.HasFfmpeg)
        {
            StatusText.Text = _loc.Get("ToolsReady");
            return;
        }

        var missing = new List<string>();
        if (!ToolLocator.HasYtDlp) missing.Add(_loc.Get("ToolsMissingYtDlp"));
        if (!ToolLocator.HasFfmpeg) missing.Add(_loc.Get("ToolsMissingFfmpeg"));
        StatusText.Text = string.Join(" · ", missing);
    }

    private void ApplyLocalization()
    {
        TitleText.Text = _loc.Get("AppTitle");
        SubtitleText.Text = _loc.Get("AppSubtitle");
        FetchButton.Content = _loc.Get("Fetch");
        DownloadButton.Content = _loc.Get("Download");
        QualityLabel.Text = _loc.Get("Quality");
        FormatLabel.Text = _loc.Get("Format");
        SavePathLabel.Text = _loc.Get("SavePath");
        SettingsButton.Content = _loc.Get("Settings");
        ThemeButton.Content = _loc.Get("Theme");
        AboutButton.Content = _loc.Get("About");
        EmptyText.Text = _loc.Get("NoDownloads");
        VideoTab.Header = _loc.Get("TabVideo");
        InstagramTab.Header = _loc.Get("TabInstagram");
        InstagramHintText.Text = _loc.Get("InstagramHint");
        InstagramFetchButton.Content = _loc.Get("Fetch");
        InstagramDownloadButton.Content = _loc.Get("Download");
        InstagramBrowserLabel.Text = _loc.Get("InstagramBrowser");
        InstagramLoginButton.Content = _loc.Get("InstagramLogin");
        InstagramImportCookiesButton.Content = _loc.Get("InstagramAddCookies");
        InstagramSyncCookiesButton.Content = _loc.Get("InstagramSyncCookies");
        InstagramClearCookiesButton.Content = _loc.Get("InstagramClearCookies");
        InstagramDownloadSelectedButton.Content = _loc.Get("InstagramDownloadSelected");
        InstagramSelectAllButton.Content = _loc.Get("InstagramSelectAll");
        InstagramExpandAllButton.Content = _loc.Get("InstagramExpandAll");
        InstagramCollapseAllButton.Content = _loc.Get("InstagramCollapseAll");
        InstagramClearBrowseButton.Content = _loc.Get("ClearList");
        DownloadQueueHeader.Text = _loc.Get("DownloadQueue");
        InstagramSavePathLabel.Text = _loc.Get("SavePath");
        UrlBox.Tag = _loc.Get("LinkPlaceholder");
        InstagramUrlBox.Tag = _loc.Get("InstagramUrlPlaceholder");
        ClearListButton.Content = _loc.Get("ClearList");
        BindInstagramBrowserCombo();
        UpdateInstagramCookiesStatus();
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        UpdateActionLabels();
        UpdateToolsStatus();
        RefreshAllItemStatusText();
    }

    private void UpdateActionLabels()
    {
        Resources["CancelLabel"] = _loc.Get("Cancel");
        Resources["OpenFolderLabel"] = _loc.Get("OpenFolder");
        Resources["PlayLabel"] = _loc.Get("Play");
        Resources["RemoveLabel"] = _loc.Get("Remove");
        Resources["CopyDetailsLabel"] = _loc.Get("CopyDetails");
        Resources["InstagramDownloadSectionLabel"] = _loc.Get("InstagramDownloadSection");
        Resources["InstagramDownloadOneLabel"] = _loc.Get("InstagramDownloadOne");
    }

    private void UpdateQueueEmptyState()
    {
        var isEmpty = _items.Count == 0;
        var browsing = InstagramBrowsePanel.Visibility == Visibility.Visible;

        EmptyText.Visibility = isEmpty && !browsing ? Visibility.Visible : Visibility.Collapsed;
        ClearListButton.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (_items.Count > 0)
            DownloadQueueExpander.IsExpanded = true;
        else if (browsing)
            DownloadQueueExpander.IsExpanded = false;

        DownloadQueueHeader.Text = isEmpty
            ? _loc.Get("DownloadQueue")
            : string.Format(_loc.Get("DownloadQueueCount"), _items.Count);

        ApplyDownloadQueueLayout();
    }

    private void ApplyDownloadQueueLayout()
    {
        if (!DownloadQueueExpander.IsExpanded)
        {
            DownloadQueueRow.Height = GridLength.Auto;
            DownloadQueueRow.MinHeight = 0;
            return;
        }

        if (_items.Count > 0)
        {
            DownloadQueueRow.Height = new GridLength(2, GridUnitType.Star);
            DownloadQueueRow.MinHeight = 160;
            return;
        }

        DownloadQueueRow.Height = GridLength.Auto;
        DownloadQueueRow.MinHeight = 120;
    }

    private void ApplyInstagramBrowseLayout()
    {
        if (InstagramBrowsePanel.Visibility == Visibility.Visible)
        {
            InstagramBrowseRow.Height = new GridLength(1, GridUnitType.Star);
            InstagramBrowseRow.MinHeight = 120;
            return;
        }

        InstagramBrowseRow.Height = new GridLength(0);
        InstagramBrowseRow.MinHeight = 0;
    }

    private void DownloadQueueExpander_Expanded(object sender, RoutedEventArgs e) => ApplyDownloadQueueLayout();

    private void DownloadQueueExpander_Collapsed(object sender, RoutedEventArgs e) => ApplyDownloadQueueLayout();

    private void RefreshAllItemStatusText()
    {
        foreach (var item in _items)
            RefreshItemStatusText(item);
    }

    private void BindInstagramBrowserCombo()
    {
        var selected = InstagramBrowserCombo.SelectedValue is InstagramBrowser browser
            ? browser
            : _settings.Current.InstagramBrowser;

        InstagramBrowserCombo.DisplayMemberPath = nameof(BrowserOption.DisplayName);
        InstagramBrowserCombo.SelectedValuePath = nameof(BrowserOption.Browser);
        InstagramBrowserCombo.ItemsSource = new[]
        {
            new BrowserOption(InstagramBrowser.Chrome, _loc.Get("BrowserChrome")),
            new BrowserOption(InstagramBrowser.Edge, _loc.Get("BrowserEdge"))
        };
        InstagramBrowserCombo.SelectedValue = selected;
    }

    private InstagramBrowser GetSelectedInstagramBrowser() =>
        InstagramBrowserCombo.SelectedValue is InstagramBrowser browser
            ? browser
            : _settings.Current.InstagramBrowser;

    private string GetSaveFolder() =>
        string.IsNullOrWhiteSpace(SavePathBox.Text)
            ? _settings.Current.DefaultSavePath
            : SavePathBox.Text.Trim();

    private void BindLanguageCombo()
    {
        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Language);
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();
    }

    private void RefreshItemStatusText(DownloadItem item)
    {
        item.StatusText = item.Status switch
        {
            DownloadStatus.Completed => FormatDoneStatus(item),
            DownloadStatus.Failed => FormatFailedStatus(item),
            DownloadStatus.Cancelled => _loc.Get("StatusCancelled"),
            DownloadStatus.Fetching => _loc.Get("StatusFetching"),
            DownloadStatus.Downloading => _loc.Get("StatusDownloading"),
            DownloadStatus.Queued => _loc.Get("StatusQueued"),
            _ => item.StatusText
        };
    }

    private string FormatFailedStatus(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ErrorMessage))
            return _loc.Get("StatusFailed");

        var message = item.ErrorMessage.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (message.Length > 140)
            message = message[..137] + "...";

        return $"{_loc.Get("StatusFailed")}: {message}";
    }

    private string FormatDoneStatus(DownloadItem item)
    {
        var done = _loc.Get("StatusDone");
        return !string.IsNullOrWhiteSpace(item.OutputPath)
            ? $"{done} — {Path.GetFileName(item.OutputPath)}"
            : done;
    }

    private void AttachItemHandlers(DownloadItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.OutputPath) or nameof(DownloadItem.ErrorMessage))
            {
                RefreshItemStatusText(item);
                item.NotifyDiagnosticsChanged();
            }
        };
    }

    private void ApplyVideoOptions(VideoInfo info)
    {
        if (info.IsPlaylist)
        {
            QualityCombo.ItemsSource = VideoFormatAnalyzer.DefaultQualities;
            QualityCombo.SelectedItem = _settings.Current.DefaultQuality;
            FormatCombo.ItemsSource = VideoFormatAnalyzer.DefaultFormats;
            FormatCombo.SelectedItem = _settings.Current.PreferredFormat;
            return;
        }

        QualityCombo.ItemsSource = info.AvailableQualities.Count > 0 ? info.AvailableQualities : VideoFormatAnalyzer.DefaultQualities;
        QualityCombo.SelectedItem = info.AvailableQualities.Contains(info.RecommendedQuality)
            ? info.RecommendedQuality
            : info.AvailableQualities.LastOrDefault() ?? info.RecommendedQuality;

        FormatCombo.ItemsSource = info.AvailableFormats.Count > 0 ? info.AvailableFormats : VideoFormatAnalyzer.DefaultFormats;
        FormatCombo.SelectedItem = info.AvailableFormats.Contains(info.RecommendedFormat)
            ? info.RecommendedFormat
            : info.AvailableFormats.FirstOrDefault() ?? _settings.Current.PreferredFormat;
    }

    private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = _loc.Get("SavePath"),
            InitialDirectory = Directory.Exists(SavePathBox.Text) ? SavePathBox.Text : null
        };
        if (dlg.ShowDialog() == true)
        {
            SavePathBox.Text = dlg.FolderName;
            InstagramSavePathBox.Text = dlg.FolderName;
        }
    }

    private async void FetchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text)) return;
        try
        {
            FetchButton.IsEnabled = false;
            PreviewText.Visibility = Visibility.Visible;
            PreviewText.Text = _loc.Get("Fetching");
            var info = await _ytDlp.FetchInfoAsync(UrlBox.Text.Trim(), _settings.Current.PreferredFormat);
            ApplyVideoOptions(info);
            PreviewText.Text = info.IsPlaylist
                ? $"{info.Title} — playlist ({info.PlaylistCount} videos)"
                : info.MaxHeight > 0
                    ? $"{info.Title} — {info.Uploader} — {FormatHelpers.FormatDuration(info.Duration)} — {info.RecommendedQuality}"
                    : $"{info.Title} — {info.Uploader} — {FormatHelpers.FormatDuration(info.Duration)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Fetch failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            PreviewText.Visibility = Visibility.Collapsed;
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text)) return;

        try
        {
            DownloadButton.IsEnabled = false;
            var url = UrlBox.Text.Trim();
            var info = await _ytDlp.FetchInfoAsync(url, _settings.Current.PreferredFormat);
            ApplyVideoOptions(info);

            if (info.IsPlaylist && info.Entries.Count > 0)
            {
                foreach (var entry in info.Entries.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
                    await AddDownloadAsync(entry.Url, entry.Title);
            }
            else
            {
                await AddDownloadAsync(url, info.Title);
            }

            UrlBox.Clear();
            PreviewText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Download failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private async Task AddDownloadAsync(string url, string title)
    {
        var item = new DownloadItem
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Quality = QualityCombo.SelectedItem?.ToString() ?? _settings.Current.DefaultQuality,
            Format = FormatCombo.SelectedItem?.ToString() ?? _settings.Current.PreferredFormat,
            SaveFolder = GetSaveFolder()
        };
        _items.Insert(0, item);
        AttachItemHandlers(item);
        UpdateQueueEmptyState();
        await _downloads.EnqueueAsync(item);
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item is not null) _downloads.Cancel(item);
        }
    }

    private void CopyDownloadDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null || string.IsNullOrWhiteSpace(item.DiagnosticsLog))
            return;

        try
        {
            Clipboard.SetText(item.BuildDiagnosticReport());
            MessageBox.Show(_loc.Get("CopyDetailsCopied"), _loc.Get("CopyDetails"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("CopyDetails"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null) return;

        _downloads.Cancel(item);
        _items.Remove(item);
        UpdateQueueEmptyState();
    }

    private void ClearListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;

        if (MessageBox.Show(
                _loc.Get("ConfirmClearList"),
                _loc.Get("ClearList"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var item in _items.ToList())
            _downloads.Cancel(item);

        _items.Clear();
        UpdateQueueEmptyState();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.OutputPath}\"",
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(item.SaveFolder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.SaveFolder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("OpenFolder"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PlayVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item is null || string.IsNullOrWhiteSpace(item.OutputPath) || !File.Exists(item.OutputPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.OutputPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("Play"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyrightLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }

    private string FormatInstagramPreview(VideoInfo info)
    {
        if (info.MaxHeight == 0
            && info.AvailableFormats.Any(format =>
                format.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || format.Equals("webp", StringComparison.OrdinalIgnoreCase)
                || format.Equals("png", StringComparison.OrdinalIgnoreCase)))
        {
            return $"{info.Title} — {info.Uploader} — photo";
        }

        return $"{info.Title} — {info.Uploader} — {FormatHelpers.FormatDuration(info.Duration)}";
    }

    private InstagramCookieSession RequireInstagramSession()
    {
        var session = InstagramCookieSession.Create(_settings.Current.InstagramCookiesPath);
        if (!session.HasCookies)
            throw new InvalidOperationException(session.ExportError ?? "No Instagram cookies found.");
        return session;
    }

    private void ClearInstagramBrowse()
    {
        _instagramBrowseItems.Clear();
        _instagramSectionBatchUrls.Clear();
        _instagramMediaPkByShortcode.Clear();
        _instagramSectionExpanders.Clear();
        InstagramSectionsPanel.Children.Clear();
        InstagramBrowsePanel.Visibility = Visibility.Collapsed;
        _instagramBrowseSession?.Dispose();
        _instagramBrowseSession = null;
        UpdateInstagramSelectAllButtonLabel();
        ApplyInstagramBrowseLayout();
        UpdateQueueEmptyState();
    }

    private void RenderInstagramBrowseSections(IReadOnlyList<InstagramBrowseSection> sections)
    {
        InstagramSectionsPanel.Children.Clear();
        _instagramSectionExpanders.Clear();

        foreach (var section in sections.Where(section => section.Items.Count > 0))
        {
            var header = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = string.Format(_loc.Get("InstagramSectionHeader"), section.Title, section.Items.Count),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            var downloadSection = new Button
            {
                Content = _loc.Get("InstagramDownloadSection"),
                Tag = section.Title,
                Style = (Style)FindResource("SecondaryButton"),
                Padding = new Thickness(10, 4, 10, 4)
            };
            downloadSection.Click += InstagramDownloadSection_Click;
            Grid.SetColumn(downloadSection, 1);
            header.Children.Add(downloadSection);

            var itemsPanel = new StackPanel { Margin = new Thickness(4, 6, 0, 4) };
            foreach (var item in section.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var checkBox = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(InstagramBrowseItem.Selected))
                {
                    Source = item,
                    Mode = BindingMode.TwoWay
                });
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(InstagramBrowseItem.Selected))
                        OnBrowseItemSelectionChanged(item);
                };
                Grid.SetColumn(checkBox, 0);
                row.Children.Add(checkBox);

                var thumbHost = new Border
                {
                    Width = 44,
                    Height = 44,
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("SurfaceElevatedBrush"),
                    ClipToBounds = true
                };
                var thumbImage = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                thumbHost.Child = thumbImage;
                LoadInstagramBrowseThumbnail(thumbImage, item.ThumbnailUrl, _instagramBrowseSession?.CookieFilePath);
                Grid.SetColumn(thumbHost, 1);
                row.Children.Add(thumbHost);

                var label = new TextBlock
                {
                    Text = item.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 2);
                row.Children.Add(label);

                var downloadOne = new Button
                {
                    Content = _loc.Get("InstagramDownloadOne"),
                    Tag = item,
                    Style = (Style)FindResource("SecondaryButton"),
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(8, 0, 0, 0)
                };
                downloadOne.Click += InstagramDownloadOne_Click;
                Grid.SetColumn(downloadOne, 3);
                row.Children.Add(downloadOne);

                itemsPanel.Children.Add(row);
            }

            var expander = new Expander
            {
                Header = header,
                Content = itemsPanel,
                Margin = new Thickness(0, 0, 0, 8),
                IsExpanded = section.Key == "stories"
                    || section.Key.StartsWith("highlight-", StringComparison.OrdinalIgnoreCase)
                    || (section.Key == "posts" && section.Items.Count <= 8)
            };

            InstagramSectionsPanel.Children.Add(expander);
            _instagramSectionExpanders.Add(expander);
        }
    }

    private void ShowInstagramBrowse(IReadOnlyList<InstagramBrowseSection> sections, InstagramCookieSession session, string username)
    {
        _instagramBrowseSession?.Dispose();
        _instagramBrowseSession = session;
        _instagramBrowseItems.Clear();
        _instagramSectionBatchUrls.Clear();
        _instagramMediaPkByShortcode.Clear();

        var total = 0;
        foreach (var section in sections)
        {
            if (section.Items.Count == 0)
                continue;

            _instagramSectionBatchUrls[section.Title] = section.BatchUrl;
            foreach (var item in section.Items)
            {
                _instagramBrowseItems.Add(item);
                IndexBrowseMediaPk(item);
                total++;
            }
        }

        RenderInstagramBrowseSections(sections);
        InstagramBrowsePanel.Visibility = _instagramBrowseItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InstagramPreviewText.Visibility = Visibility.Visible;
        InstagramPreviewText.Text = string.Format(_loc.Get("InstagramProfileLoaded"), total, username);
        ApplyInstagramBrowseLayout();
        UpdateQueueEmptyState();
        UpdateInstagramSelectAllButtonLabel();
    }

    private async Task EnqueueInstagramUrlAsync(
        InstagramUrlNormalization normalized,
        InstagramCookieSession session,
        string? mediaPk = null,
        int? slideIndex = null)
    {
        var playlistIndex = slideIndex ?? normalized.PlaylistItemIndex;
        await AddInstagramDownloadAsync(
            normalized.Url,
            normalized.NoPlaylist,
            playlistIndex,
            session.CookieFilePath,
            mediaPk);
    }

    private async Task EnqueueInstagramBrowseItemAsync(InstagramBrowseItem browseItem, InstagramCookieSession session)
    {
        var normalized = InstagramUrlHelper.NormalizeDetailed(browseItem.Url);
        await EnqueueInstagramUrlAsync(
            normalized,
            session,
            ResolveBrowseMediaPk(browseItem),
            browseItem.CarouselSlideIndex ?? browseItem.HighlightStoryIndex);
    }

    private async Task EnqueueInstagramUrlsAsync(IEnumerable<InstagramBrowseItem> items, InstagramCookieSession session, bool onlySelected = true)
    {
        var selectedItems = items.Where(x => !onlySelected || x.Selected).ToList();
        foreach (var item in FilterGroupedBrowseDownloads(selectedItems))
            await EnqueueInstagramBrowseItemAsync(item, session);
    }

    private static IEnumerable<InstagramBrowseItem> FilterGroupedBrowseDownloads(IReadOnlyList<InstagramBrowseItem> items)
    {
        var batchUrls = items
            .Where(item => item.Selected && IsGroupedBatchItem(item))
            .Select(item => item.Url)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!item.Selected)
                continue;

            if (IsGroupedChildItem(item) && batchUrls.Contains(item.Url))
                continue;

            yield return item;
        }
    }

    private static bool IsGroupedBatchItem(InstagramBrowseItem item) =>
        string.Equals(item.Kind, "carousel", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(item.Kind, "highlight", StringComparison.OrdinalIgnoreCase) && item.HighlightStoryIndex is null);

    private static bool IsGroupedChildItem(InstagramBrowseItem item) =>
        string.Equals(item.Kind, "carousel-slide", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.Kind, "highlight-story", StringComparison.OrdinalIgnoreCase);

    private void OnBrowseItemSelectionChanged(InstagramBrowseItem item)
    {
        if (_syncingBrowseSelection)
        {
            UpdateInstagramSelectAllButtonLabel();
            return;
        }

        _syncingBrowseSelection = true;
        try
        {
            if (string.Equals(item.Kind, "carousel", StringComparison.OrdinalIgnoreCase))
                SyncCarouselBatchSelection(item);
            else if (string.Equals(item.Kind, "carousel-slide", StringComparison.OrdinalIgnoreCase))
                SyncCarouselBatchFromSlides(item);
            else if (string.Equals(item.Kind, "highlight", StringComparison.OrdinalIgnoreCase) && item.HighlightStoryIndex is null)
                SyncHighlightBatchSelection(item);
            else if (string.Equals(item.Kind, "highlight-story", StringComparison.OrdinalIgnoreCase))
                SyncHighlightBatchFromStories(item);
        }
        finally
        {
            _syncingBrowseSelection = false;
        }

        UpdateInstagramSelectAllButtonLabel();
    }

    private void SyncCarouselBatchSelection(InstagramBrowseItem batch)
    {
        foreach (var slide in GetCarouselSlides(batch.Url))
            slide.Selected = batch.Selected;
    }

    private void SyncCarouselBatchFromSlides(InstagramBrowseItem slide)
    {
        var batch = GetCarouselBatch(slide.Url);
        if (batch is null)
            return;

        var slides = GetCarouselSlides(slide.Url).ToList();
        batch.Selected = slides.Count > 0 && slides.All(x => x.Selected);
    }

    private IEnumerable<InstagramBrowseItem> GetCarouselSlides(string url) =>
        _instagramBrowseItems.Where(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Kind, "carousel-slide", StringComparison.OrdinalIgnoreCase));

    private InstagramBrowseItem? GetCarouselBatch(string url) =>
        _instagramBrowseItems.FirstOrDefault(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Kind, "carousel", StringComparison.OrdinalIgnoreCase));

    private void SyncHighlightBatchSelection(InstagramBrowseItem batch)
    {
        foreach (var story in GetHighlightStories(batch.Url))
            story.Selected = batch.Selected;
    }

    private void SyncHighlightBatchFromStories(InstagramBrowseItem story)
    {
        var batch = GetHighlightBatch(story.Url);
        if (batch is null)
            return;

        var stories = GetHighlightStories(story.Url).ToList();
        batch.Selected = stories.Count > 0 && stories.All(x => x.Selected);
    }

    private IEnumerable<InstagramBrowseItem> GetHighlightStories(string url) =>
        _instagramBrowseItems.Where(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Kind, "highlight-story", StringComparison.OrdinalIgnoreCase));

    private InstagramBrowseItem? GetHighlightBatch(string url) =>
        _instagramBrowseItems.FirstOrDefault(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Kind, "highlight", StringComparison.OrdinalIgnoreCase)
            && item.HighlightStoryIndex is null);

    private async void InstagramFetchButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstagramUrlBox.Text)) return;

        InstagramCookieSession? session = null;
        try
        {
            InstagramFetchButton.IsEnabled = false;
            ClearInstagramBrowse();
            InstagramPreviewText.Visibility = Visibility.Visible;
            InstagramPreviewText.Text = _loc.Get("Fetching");

            var normalized = InstagramUrlHelper.NormalizeDetailed(InstagramUrlBox.Text);
            session = RequireInstagramSession();

            if (InstagramUrlHelper.IsProfileHomeUrl(normalized.Url))
            {
                var sections = await _instagramProfiles.FetchSectionsAsync(normalized.Url, session.CookieFilePath, _loc);
                if (!InstagramUrlHelper.TryGetUsername(normalized.Url, out var username))
                    username = normalized.Url;

                session.DetachTempDirectory();
                ShowInstagramBrowse(sections, session, username);
                if (!sections.Any(section => section.Key == "posts" && section.Items.Count > 0)
                    && !sections.Any(section => section.Key.StartsWith("highlight-", StringComparison.OrdinalIgnoreCase)))
                {
                    InstagramPreviewText.Text += Environment.NewLine + Environment.NewLine + _loc.Get("InstagramProfilePartial");
                }

                session = null;
                return;
            }

            var info = await _ytDlp.FetchInfoAsync(
                normalized.Url,
                "jpg",
                session,
                normalized.NoPlaylist,
                playlistItemIndex: normalized.PlaylistItemIndex);
            InstagramPreviewText.Text = info.IsPlaylist
                ? $"{info.Title} — carousel ({info.PlaylistCount} items)"
                : FormatInstagramPreview(info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Fetch"), MessageBoxButton.OK, MessageBoxImage.Warning);
            InstagramPreviewText.Visibility = Visibility.Collapsed;
            ClearInstagramBrowse();
        }
        finally
        {
            session?.Dispose();
            InstagramFetchButton.IsEnabled = true;
        }
    }

    private async void InstagramDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstagramUrlBox.Text) && _instagramBrowseItems.Count == 0) return;

        InstagramCookieSession? session = null;
        try
        {
            InstagramDownloadButton.IsEnabled = false;

            if (_instagramBrowseItems.Count > 0 && _instagramBrowseSession is not null)
            {
                await EnqueueInstagramUrlsAsync(_instagramBrowseItems.Where(x => x.Selected), _instagramBrowseSession);
                UpdateQueueEmptyState();
                return;
            }

            var normalized = InstagramUrlHelper.NormalizeDetailed(InstagramUrlBox.Text);
            session = RequireInstagramSession();
            var mediaPk = TryResolveBrowseMediaPk(normalized.Url);
            await EnqueueInstagramUrlAsync(normalized, session, mediaPk);
            session.DetachTempDirectory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            session?.Dispose();
            InstagramDownloadButton.IsEnabled = true;
        }
    }

    private async Task AddInstagramDownloadAsync(
        string url,
        bool noPlaylist,
        int? playlistItemIndex,
        string cookieFile,
        string? instagramMediaPk = null)
    {
        var sourceCookie = !string.IsNullOrWhiteSpace(cookieFile) && File.Exists(cookieFile)
            ? cookieFile
            : _settings.Current.InstagramCookiesPath;

        var (ownedCookieFile, ownedTempDir) = InstagramCookieSession.CopyForDownload(sourceCookie!);

        var item = new DownloadItem
        {
            Url = url,
            Title = string.Empty,
            Quality = "best",
            Format = "jpg",
            SaveFolder = GetSaveFolder(),
            IsInstagram = true,
            InstagramBrowser = GetSelectedInstagramBrowser(),
            NoPlaylist = noPlaylist,
            PlaylistItemIndex = playlistItemIndex,
            InstagramMediaPk = InstagramShortcodeHelper.IsLikelyValidMediaPk(instagramMediaPk?.Trim())
                ? instagramMediaPk!.Trim()
                : null,
            InstagramCookieFile = ownedCookieFile,
            InstagramCookieTempDir = ownedTempDir
        };
        _items.Insert(0, item);
        AttachItemHandlers(item);
        UpdateQueueEmptyState();
        await _downloads.EnqueueAsync(item);
    }

    private void InstagramLoginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstagramBrowserProfiles.OpenLoginBrowser(GetSelectedInstagramBrowser());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("InstagramLogin"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InstagramImportCookiesButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InstagramCookiesWindow(_loc) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
        _settings.Save(_settings.Current);
        UpdateInstagramCookiesStatus();
    }

    private async void InstagramSyncCookiesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstagramSyncCookiesButton.IsEnabled = false;
            var (success, error) = await InstagramBrowserCookieSync.TrySyncAsync(
                GetSelectedInstagramBrowser(),
                InstagramCookiesBuilder.SavedCookiesPath,
                _loc);

            if (!success)
            {
                MessageBox.Show(error, _loc.Get("InstagramSyncCookies"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
            _settings.Save(_settings.Current);
            UpdateInstagramCookiesStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("InstagramSyncCookies"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            InstagramSyncCookiesButton.IsEnabled = true;
        }
    }

    private void InstagramClearCookiesButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _settings.Current.InstagramCookiesPath;
        var hasCookies = (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            || InstagramCookiesBuilder.HasSavedCookies();

        if (!hasCookies)
        {
            UpdateInstagramCookiesStatus();
            return;
        }

        if (MessageBox.Show(
                _loc.Get("InstagramConfirmClearCookies"),
                _loc.Get("InstagramClearCookies"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!InstagramCookiesBuilder.TryClearSavedCookies(out var error))
        {
            MessageBox.Show(error, _loc.Get("InstagramClearCookies"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Current.InstagramCookiesPath = string.Empty;
        _settings.Save(_settings.Current);
        UpdateInstagramCookiesStatus();
    }

    private async void InstagramDownloadSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_instagramBrowseSession is null || _instagramBrowseItems.All(x => !x.Selected))
            return;

        try
        {
            InstagramDownloadSelectedButton.IsEnabled = false;
            await EnqueueInstagramUrlsAsync(_instagramBrowseItems.Where(x => x.Selected), _instagramBrowseSession);
            UpdateQueueEmptyState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            InstagramDownloadSelectedButton.IsEnabled = true;
        }
    }

    private void UpdateInstagramSelectAllButtonLabel()
    {
        if (_instagramBrowseItems.Count == 0)
        {
            InstagramSelectAllButton.Content = _loc.Get("InstagramSelectAll");
            return;
        }

        var allSelected = _instagramBrowseItems.All(x => x.Selected);
        InstagramSelectAllButton.Content = allSelected
            ? _loc.Get("InstagramDeselectAll")
            : _loc.Get("InstagramSelectAll");
    }

    private void InstagramSelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        var selectAll = _instagramBrowseItems.Any(x => !x.Selected);
        foreach (var item in _instagramBrowseItems)
            item.Selected = selectAll;

        UpdateInstagramSelectAllButtonLabel();
    }

    private void InstagramClearBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        ClearInstagramBrowse();
        InstagramPreviewText.Visibility = Visibility.Collapsed;
    }

    private void InstagramExpandAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var expander in _instagramSectionExpanders)
            expander.IsExpanded = true;
    }

    private void InstagramCollapseAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var expander in _instagramSectionExpanders)
            expander.IsExpanded = false;
    }

    private async void InstagramDownloadSection_Click(object sender, RoutedEventArgs e)
    {
        if (_instagramBrowseSession is null || sender is not Button { Tag: string sectionTitle })
            return;

        try
        {
            if (_instagramSectionBatchUrls.TryGetValue(sectionTitle, out var batchUrl)
                && !string.IsNullOrWhiteSpace(batchUrl))
            {
                await EnqueueInstagramUrlAsync(
                    InstagramUrlHelper.NormalizeDetailed(batchUrl),
                    _instagramBrowseSession);
                return;
            }

            var sectionItems = _instagramBrowseItems
                .Where(x => x.SectionTitle.Equals(sectionTitle, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(x.Kind, "carousel-slide", StringComparison.OrdinalIgnoreCase));
            await EnqueueInstagramUrlsAsync(sectionItems, _instagramBrowseSession, onlySelected: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void InstagramDownloadOne_Click(object sender, RoutedEventArgs e)
    {
        if (_instagramBrowseSession is null || sender is not Button { Tag: InstagramBrowseItem browseItem })
            return;

        try
        {
            await EnqueueInstagramBrowseItemAsync(browseItem, _instagramBrowseSession);
        }
        catch (Exception ex)
        {
            MessageBox.Show(InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InstagramBrowserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InstagramBrowserCombo.SelectedValue is InstagramBrowser browser)
        {
            _settings.Current.InstagramBrowser = browser;
            _settings.Save(_settings.Current);
        }
    }

    private void InstagramBrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = _loc.Get("SavePath"),
            InitialDirectory = Directory.Exists(InstagramSavePathBox.Text) ? InstagramSavePathBox.Text : null
        };
        if (dlg.ShowDialog() == true)
        {
            InstagramSavePathBox.Text = dlg.FolderName;
            SavePathBox.Text = dlg.FolderName;
        }
    }

    private void InstagramSavePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SavePathBox.Text != InstagramSavePathBox.Text)
            SavePathBox.Text = InstagramSavePathBox.Text;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AboutWindow(_loc) { Owner = this };
        dlg.ShowDialog();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings.Current, _loc) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultSettings is not null)
        {
            _settings.Save(dlg.ResultSettings);
            SavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
            InstagramSavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
            _theme.Apply(dlg.ResultSettings.Theme);
            _loc.SetLanguage(dlg.ResultSettings.Language);
            LanguageCombo.SelectedValue = dlg.ResultSettings.Language;
            ApplyLocalization();
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = _theme.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next);
        _settings.Current.Theme = next;
        _settings.Save(_settings.Current);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedValue is AppLanguage lang)
        {
            _loc.SetLanguage(lang);
            _settings.Current.Language = lang;
            _settings.Save(_settings.Current);
            ApplyLocalization();
        }
    }

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);
    private string? TryResolveBrowseMediaPk(string url)
    {
        if (InstagramShortcodeHelper.TryGetShortcodeFromUrl(url, out var shortcode)
            && _instagramMediaPkByShortcode.TryGetValue(shortcode, out var mappedPk))
        {
            return mappedPk;
        }

        if (InstagramShortcodeHelper.TryGetShortcodeFromUrl(url, out shortcode))
        {
            foreach (var item in _instagramBrowseItems)
            {
                if (!InstagramShortcodeHelper.TryGetShortcodeFromUrl(item.Url, out var itemCode))
                    continue;

                if (itemCode.Equals(shortcode, StringComparison.OrdinalIgnoreCase)
                    && InstagramShortcodeHelper.IsLikelyValidMediaPk(item.MediaPk))
                {
                    return item.MediaPk!.Trim();
                }
            }
        }

        var exact = _instagramBrowseItems.FirstOrDefault(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
        if (InstagramShortcodeHelper.IsLikelyValidMediaPk(exact?.MediaPk))
            return exact!.MediaPk!.Trim();

        return null;
    }

    private string? ResolveBrowseMediaPk(InstagramBrowseItem browseItem)
    {
        if (InstagramShortcodeHelper.IsLikelyValidMediaPk(browseItem.MediaPk))
            return browseItem.MediaPk!.Trim();

        return TryResolveBrowseMediaPk(browseItem.Url);
    }

    private void IndexBrowseMediaPk(InstagramBrowseItem item)
    {
        if (!InstagramShortcodeHelper.TryGetShortcodeFromUrl(item.Url, out var shortcode))
            return;

        if (InstagramShortcodeHelper.IsLikelyValidMediaPk(item.MediaPk))
            _instagramMediaPkByShortcode[shortcode] = item.MediaPk!.Trim();
    }

    private static void LoadInstagramBrowseThumbnail(Image image, string? url, string? cookieFilePath)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                byte[] bytes;
                if (!string.IsNullOrWhiteSpace(cookieFilePath)
                    && File.Exists(cookieFilePath)
                    && InstagramCookieFileReader.TryRead(cookieFilePath, out var cookies, out _, out _))
                {
                    using var handler = new HttpClientHandler { CookieContainer = cookies, AutomaticDecompression = DecompressionMethods.All };
                    using var client = new HttpClient(handler);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.instagram.com/");
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    bytes = await client.GetByteArrayAsync(url);
                }
                else
                {
                    using var client = new HttpClient();
                    bytes = await client.GetByteArrayAsync(url);
                }

                await image.Dispatcher.InvokeAsync(() =>
                {
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.DecodePixelWidth = 88;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image.Source = bitmap;
                });
            }
            catch
            {
                // thumbnail is optional
            }
        });
    }

    private sealed record BrowserOption(InstagramBrowser Browser, string DisplayName);
}
