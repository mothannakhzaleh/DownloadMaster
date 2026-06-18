using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ObservableCollection<DownloadItem> _items = [];

    public MainWindow()
    {
        InitializeComponent();
        _settings.Load();
        _downloads = new DownloadManager(_settings);
        DownloadList.ItemsSource = _items;
        DownloadList.SelectionChanged += (_, _) => EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        QualityCombo.ItemsSource = VideoFormatAnalyzer.DefaultQualities;
        QualityCombo.SelectedItem = _settings.Current.DefaultQuality;
        FormatCombo.ItemsSource = VideoFormatAnalyzer.DefaultFormats;
        FormatCombo.SelectedItem = _settings.Current.PreferredFormat;
        SavePathBox.Text = _settings.Current.DefaultSavePath;
        BindLanguageCombo();
        LanguageCombo.SelectedValue = _settings.Current.Language;

        _theme.Apply(_settings.Current.Theme);
        _loc.SetLanguage(_settings.Current.Language);
        ApplyLocalization();
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
        EmptyText.Text = _loc.Get("NoDownloads");
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
    }

    private void RefreshAllItemStatusText()
    {
        foreach (var item in _items)
            RefreshItemStatusText(item);
    }

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
            DownloadStatus.Failed => _loc.Get("StatusFailed"),
            DownloadStatus.Cancelled => _loc.Get("StatusCancelled"),
            DownloadStatus.Fetching => _loc.Get("StatusFetching"),
            DownloadStatus.Downloading => _loc.Get("StatusDownloading"),
            DownloadStatus.Queued => _loc.Get("StatusQueued"),
            _ => item.StatusText
        };
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
            if (e.PropertyName is nameof(DownloadItem.Status) or nameof(DownloadItem.OutputPath))
                RefreshItemStatusText(item);
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
            SavePathBox.Text = dlg.FolderName;
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
            SaveFolder = string.IsNullOrWhiteSpace(SavePathBox.Text)
                ? _settings.Current.DefaultSavePath
                : SavePathBox.Text.Trim()
        };
        _items.Insert(0, item);
        AttachItemHandlers(item);
        EmptyText.Visibility = Visibility.Collapsed;
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

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings.Current, _loc) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultSettings is not null)
        {
            _settings.Save(dlg.ResultSettings);
            SavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
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
}
