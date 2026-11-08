using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Queue;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

public partial class MainWindow : Window
{
    private const int DownloadQueueRowIndex = 2;
    private const int InstagramBrowseRowIndex = 6;

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
    private bool _initializing = true;

    // Queue features: scheduler, finish action, clipboard monitor.
    private readonly QueueScheduler _scheduler;
    private readonly DispatcherTimer _schedulerTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _clipboardTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private ClipboardUrlWatcher _clipboardWatcher = new();
    private bool _clipboardPolling;
    private bool _clipboardMonitorWasOn;
    private FinishAction _finishAction = FinishAction.Nothing;
    private bool _finishPending;
    private bool _queueHadWork;
    private bool _bindingFinishCombo;

    public MainWindow()
    {
        InitializeComponent();
        _settings.Load();
        _downloads = new DownloadManager(_settings);
        _downloads.Localize = key => _loc.Get(key);
        _scheduler = new QueueScheduler(
            item => _downloads.EnqueueAsync(item),
            item => _downloads.Pause(item),
            item =>
            {
                _downloads.Resume(item);
                RefreshItemStatusText(item);
            },
            () => _items);
        _scheduler.Changed += OnSchedulerChanged;
        FinishActions = AppPaths.SandboxDir is { } sandbox
            ? new DryRunFinishActionExecutor(Path.Combine(sandbox, "finish-action.log"), QuitApp)
            : new SystemFinishActionExecutor(QuitApp);
        _schedulerTimer.Tick += (_, _) => _scheduler.Tick();
        _clipboardTimer.Tick += ClipboardTimer_Tick;
        Closed += (_, _) =>
        {
            _schedulerTimer.Stop();
            _clipboardTimer.Stop();
        };
        DownloadList.ItemsSource = _items;
        _items.CollectionChanged += (_, _) => UpdateQueueEmptyState();
        DownloadQueueExpander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty)
                ApplyDownloadQueueLayout();
        };
        UpdateQueueEmptyState();
        ApplyDownloadQueueLayout();
        ApplyInstagramBrowseLayout();
        UpdateDownloadQueueVisibility();

        QualityCombo.ItemsSource = VideoFormatAnalyzer.DefaultQualities;
        QualityCombo.SelectedItem = _settings.Current.DefaultQuality;
        FormatCombo.ItemsSource = VideoFormatAnalyzer.DefaultFormats;
        FormatCombo.SelectedItem = _settings.Current.PreferredFormat;
        SavePathBox.Text = _settings.Current.DefaultSavePath;
        BindLanguageCombo();
        BindInstagramBrowserCombo();
        InstagramSavePathBox.Text = SavePathBox.Text;
        FileDownloadSavePathBox.Text = SavePathBox.Text;
        FileDownloadConnectionsCombo.SelectedIndex = GetFileConnectionsIndex(_settings.Current.FileDownloadConnections);
        SelectLanguage(_settings.Current.Language);

        if (string.IsNullOrWhiteSpace(_settings.Current.InstagramCookiesPath)
            && File.Exists(InstagramCookiesBuilder.SavedCookiesPath))
        {
            _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
        }

        InstagramCookiesBuilder.RepairSavedCookiesFile();

        _theme.Apply(_settings.Current.Theme);
        _loc.SetLanguage(_settings.Current.Language);
        ApplySchedulerSettings();
        ApplyLocalization();
        UpdateInstagramCookiesStatus();
        _schedulerTimer.Start();
        ApplyClipboardMonitor();
        _initializing = false;
    }

    /// <summary>Used by App (extension installer, tray) to show text in the current language.</summary>
    public LocalizationService Localization => _loc;

    /// <summary>Runs the "when all downloads finish" action. Sandboxed runs use a dry-run logger; tests can replace it.</summary>
    public IFinishActionExecutor FinishActions { get; set; }

    /// <summary>The current "when all downloads finish" choice (in memory only — never persisted, so a restart never shuts the PC down).</summary>
    public FinishAction FinishAction
    {
        get => _finishAction;
        set
        {
            _finishAction = value;
            if (FinishActionCombo.ItemsSource is IEnumerable<FinishActionOption> options)
            {
                _bindingFinishCombo = true;
                FinishActionCombo.SelectedItem = options.FirstOrDefault(o => o.Action == value);
                _bindingFinishCombo = false;
            }
        }
    }

    private void QuitApp()
    {
        if (Avalonia.Application.Current is App app)
            app.ExitApplication();
        else
            CloseForExit();
    }

    private bool _closeForExit;

    public bool MinimizeToTrayEnabled => _settings.Current.MinimizeToTray;

    /// <summary>Tray "Exit": really close instead of hiding.</summary>
    public void CloseForExit()
    {
        _closeForExit = true;
        foreach (var item in _items.Where(i => i.CanCancel).ToList())
            _downloads.Cancel(item);
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The X button hides to the tray; downloads keep running. Exit from the tray menu quits.
        if (!_closeForExit && _settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void PauseAll()
    {
        foreach (var item in _items.Where(i => i.CanPause).ToList())
            _downloads.Pause(item);
    }

    public void ResumeAll()
    {
        foreach (var item in _items.Where(i => i.CanResume).ToList())
        {
            _downloads.Resume(item);
            RefreshItemStatusText(item);
        }
    }

    /// <summary>Tray "Add URL from clipboard": routes the link to the right tab.</summary>
    public async Task AddUrlFromClipboardAsync()
    {
        string? text = null;
        try
        {
            if (Clipboard is not null)
                text = (await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(Clipboard))?.Trim();
        }
        catch
        {
            // clipboard unavailable
        }

        if (!RouteUrl(text))
            await Dialogs.ShowAsync(this, _loc.Get("TrayClipboardNoUrl"), _loc.Get("TrayAddFromClipboard"), DialogKind.Warning);
    }

    /// <summary>Puts a link into the matching tab (Instagram, Files or Video). Returns false if it isn't an http(s) URL.</summary>
    public bool RouteUrl(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text)
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        if (uri.Host.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
        {
            SourceTabs.SelectedItem = InstagramTab;
            InstagramUrlBox.Text = text;
            InstagramUrlBox.Focus();
        }
        else if (LooksLikeDirectFile(uri))
        {
            SourceTabs.SelectedItem = FileDownloadTab;
            FileDownloadUrlBox.Text = text;
            FileDownloadUrlBox.Focus();
        }
        else
        {
            SourceTabs.SelectedItem = VideoTab;
            UrlBox.Text = text;
            UrlBox.Focus();
        }

        return true;
    }

    // ---- Browser extension bridge -------------------------------------------------------------

    public bool InterceptEnabled
    {
        get => _settings.Current.InterceptBrowserDownloads;
        set
        {
            _settings.Current.InterceptBrowserDownloads = value;
            _settings.Save(_settings.Current);
            (Avalonia.Application.Current as App)?.RefreshTrayText(_loc);
        }
    }

    public IReadOnlyList<string> InterceptFileTypes => _settings.Current.InterceptFileTypes;

    public int SpeedLimitKbps => _settings.Current.SpeedLimitKbps;

    /// <summary>Global speed limit (0 = unlimited) for file downloads now and yt-dlp downloads started later.</summary>
    public void SetSpeedLimit(int kbps)
    {
        _settings.Current.SpeedLimitKbps = Math.Max(0, kbps);
        _settings.Save(_settings.Current);
        _downloads.SetSpeedLimit(_settings.Current.SpeedLimitKbps);
        (Avalonia.Application.Current as App)?.RefreshTrayText(_loc);
    }

    public void ReportBridgeError(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            StatusText.Text = $"{StatusText.Text} · {_loc.Get("BridgeUnavailable")} {error}";
    }

    /// <summary>A browser download handed over by the extension (POST /api/v1/download).</summary>
    public async void ShowCaughtDownload(Services.Bridge.BridgeDownloadRequest request)
    {
        var name = !string.IsNullOrWhiteSpace(request.FileName)
            ? Path.GetFileName(request.FileName)
            : FileNameFromUrl(request.Url);
        var headers = Services.Bridge.BridgeHeaders.Build(request.Cookies, request.Referrer, request.UserAgent, request.Headers);

        // Same file as a failed/expired download? Offer to continue it with the browser's fresh link (IDM "refresh address").
        if (_downloads.FindRefreshCandidate(name, request.FileSize) is { } existing)
        {
            var resume = await Dialogs.ConfirmAsync(
                this,
                string.Format(_loc.Get("EngineRefreshAddressPrompt"), existing.Title),
                _loc.Get("CaughtFileTitle"));
            if (resume)
            {
                try
                {
                    await _downloads.RefreshAddressAsync(existing, request.Url, headers);
                    return;
                }
                catch (Exception ex)
                {
                    await Dialogs.ShowAsync(this, ex.Message, _loc.Get("CaughtFileTitle"), DialogKind.Warning);
                }
            }
        }

        ShowCaughtWindow(new CaughtDownload
        {
            IsMedia = false,
            Url = request.Url,
            SuggestedName = DownloadCaughtWindow.SanitizeFileName(name),
            FileSize = request.FileSize,
            MimeType = request.MimeType,
            SourcePage = request.Referrer,
            Headers = Services.Bridge.BridgeHeaders.Build(request.Cookies, request.Referrer, request.UserAgent, request.Headers)
        });
    }

    /// <summary>A sniffed stream or a whole page from the extension (POST /api/v1/media-stream).</summary>
    public void ShowCaughtMedia(Services.Bridge.BridgeMediaRequest request)
    {
        var type = (request.StreamType ?? "page").ToLowerInvariant();
        var title = DownloadCaughtWindow.SanitizeFileName(request.PageTitle);
        var headers = Services.Bridge.BridgeHeaders.Build(request.Cookies, request.Referrer ?? request.PageUrl, request.UserAgent);

        // Plain MP4/WebM/audio files go through the multi-connection file engine; HLS/DASH/pages through yt-dlp.
        if (type is "video" or "audio")
        {
            var urlName = FileNameFromUrl(request.StreamUrl);
            var ext = Path.GetExtension(urlName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = type == "audio" ? ".m4a" : ".mp4";

            ShowCaughtWindow(new CaughtDownload
            {
                IsMedia = false,
                Url = request.StreamUrl,
                SuggestedName = string.IsNullOrWhiteSpace(title) ? urlName : title + ext,
                FileSize = request.FileSize,
                StreamType = type,
                QualityLabel = request.QualityLabel,
                SourcePage = request.PageUrl,
                Headers = headers
            });
            return;
        }

        ShowCaughtWindow(new CaughtDownload
        {
            IsMedia = true,
            Url = request.StreamUrl,
            SuggestedName = title,
            StreamType = type,
            QualityLabel = request.QualityLabel,
            SourcePage = request.PageUrl,
            Headers = headers
        });
    }

    private void ShowCaughtWindow(CaughtDownload download)
    {
        var window = new DownloadCaughtWindow(
            download,
            _loc,
            GetSaveFolder(),
            _settings.Current.FileDownloadConnections,
            EnqueueCaughtAsync);
        window.Show();
        window.Activate();
    }

    private async Task<bool> EnqueueCaughtAsync(CaughtDownloadChoice choice)
    {
        try
        {
            var source = choice.Source;
            var sort = _settings.Current.SortIntoCategoryFolders;
            var folder = source.IsMedia
                ? DownloadCategoryRouter.ResolveMediaFolder(choice.Folder, choice.Format, sort)
                : DownloadCategoryRouter.ResolveFileFolder(
                    choice.Folder,
                    string.IsNullOrWhiteSpace(choice.FileName) ? source.Url : choice.FileName,
                    source.MimeType,
                    sort);
            Directory.CreateDirectory(folder);
            DownloadItem item;
            if (source.IsMedia)
            {
                item = new DownloadItem
                {
                    Url = source.Url,
                    // Empty title = let yt-dlp read the real title (with the browser's cookies).
                    Title = choice.FileName,
                    Quality = choice.Quality,
                    Format = choice.Format,
                    SaveFolder = folder,
                    NoPlaylist = true,
                    RequestHeaders = source.Headers
                };
            }
            else
            {
                DirectDownloadLinkResolver.EnsureSupported(source.Url);
                item = new DownloadItem
                {
                    Url = source.Url,
                    Kind = DownloadKind.DirectFile,
                    Title = string.IsNullOrWhiteSpace(choice.FileName) ? source.Url : choice.FileName,
                    DesiredFileName = string.IsNullOrWhiteSpace(choice.FileName) ? null : choice.FileName,
                    ConnectionCount = choice.Connections,
                    SaveFolder = folder,
                    RequestHeaders = source.Headers
                };
            }

            _items.Insert(0, item);
            AttachItemHandlers(item);
            UpdateQueueEmptyState();
            if (!ReferenceEquals(SourceTabs.SelectedItem, ConvertTab))
                DownloadQueueExpander.IsExpanded = true;
            await StartOrScheduleAsync(item);
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(ex.Message, _loc.Get("Download"), DialogKind.Error);
            return false;
        }
    }

    private static string FileNameFromUrl(string url)
    {
        try
        {
            var name = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
            return string.IsNullOrWhiteSpace(name) ? "download" : DownloadCaughtWindow.SanitizeFileName(name);
        }
        catch
        {
            return "download";
        }
    }

    private static bool LooksLikeDirectFile(Uri uri)
    {
        string[] fileExtensions =
        [
            ".zip", ".7z", ".rar", ".exe", ".msi", ".iso", ".dmg", ".pkg", ".deb", ".rpm", ".appimage",
            ".tar", ".gz", ".xz", ".pdf", ".mp3", ".flac", ".apk"
        ];
        var path = uri.AbsolutePath;
        return fileExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            || uri.Host.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase);
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
        }
        else
        {
            var missing = new List<string>();
            if (!ToolLocator.HasYtDlp) missing.Add(_loc.Get("ToolsMissingYtDlp"));
            if (!ToolLocator.HasFfmpeg) missing.Add(_loc.Get("ToolsMissingFfmpeg"));
            missing.Add(ToolLocator.InstallHint);
            StatusText.Text = string.Join(" · ", missing);
        }

        VideoConvertorPanel.RefreshFfmpegStatus();
        AudioConvertorPanel.RefreshFfmpegStatus();
    }

    private void ApplyLocalization()
    {
        TitleText.Text = _loc.Get("AppTitle");
        SubtitleText.Text = _loc.Get("AppSubtitle");
        FetchButton.Content = _loc.Get("Fetch");
        DownloadButton.Content = _loc.Get("Download");
        QualityLabel.Text = _loc.Get("Quality");
        FormatLabel.Text = _loc.Get("Format");
        SavePathLabel.Text = _loc.Get("SaveTo");
        SettingsButton.Content = _loc.Get("Settings");
        ThemeButton.Content = _loc.Get("Theme");
        AboutButton.Content = _loc.Get("About");
        EmptyText.Text = _loc.Get("NoDownloads");
        VideoTab.Header = _loc.Get("TabVideo");
        InstagramTab.Header = _loc.Get("TabInstagram");
        FileDownloadTab.Header = _loc.Get("TabFileDownload");
        ConvertTab.Header = _loc.Get("TabConvert");
        VideoConvertSubTab.Header = _loc.Get("TabVideoConvert");
        ImageConvertSubTab.Header = _loc.Get("TabImageConvert");
        AudioConvertSubTab.Header = _loc.Get("TabAudioConvert");
        SpeechConvertSubTab.Header = _loc.Get("TabSpeechConvert");
        VideoConvertorPanel.ApplyLocalization(_loc);
        ImageConvertorPanel.ApplyLocalization(_loc);
        AudioConvertorPanel.ApplyLocalization(_loc);
        SpeechConvertorPanel.ApplyLocalization(_loc);
        FileDownloadHintText.Text = _loc.Get("FileDownloadHint");
        FileDownloadButton.Content = _loc.Get("Download");
        FileDownloadNameLabel.Text = _loc.Get("FileDownloadName");
        FileDownloadConnectionsLabel.Text = _loc.Get("FileDownloadConnections");
        FileDownloadSavePathLabel.Text = _loc.Get("SaveTo");
        FileDownloadUrlBox.Watermark = _loc.Get("FileDownloadUrlPlaceholder");
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
        InstagramSavePathLabel.Text = _loc.Get("SaveTo");
        UrlBox.Watermark = _loc.Get("LinkPlaceholder");
        InstagramUrlBox.Watermark = _loc.Get("InstagramUrlPlaceholder");
        ClearListButton.Content = _loc.Get("ClearList");
        BatchDownloadButton.Content = _loc.Get("BatchButton");
        FinishActionLabel.Text = _loc.Get("QueueWhenDone");
        StartScheduledNowButton.Content = _loc.Get("QueueStartNow");
        BindFinishActionCombo();
        UpdateSchedulerStatus();
        Dialogs.OkLabel = _loc.Get("DialogOk");
        Dialogs.YesLabel = _loc.Get("DialogYes");
        Dialogs.NoLabel = _loc.Get("DialogNo");
        (Avalonia.Application.Current as App)?.RefreshTrayText(_loc);
        BindInstagramBrowserCombo();
        UpdateInstagramCookiesStatus();
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        UpdateActionLabels();
        UpdateToolsStatus();
        RefreshAllItemStatusText();
        UpdateQueueEmptyState();
    }

    private void UpdateActionLabels()
    {
        Resources["CancelLabel"] = _loc.Get("Cancel");
        Resources["OpenFolderLabel"] = _loc.Get("OpenFolder");
        Resources["PlayLabel"] = _loc.Get("Play");
        Resources["RemoveLabel"] = _loc.Get("Remove");
        Resources["CopyDetailsLabel"] = _loc.Get("CopyDetails");
        Resources["CopySha256Label"] = _loc.Get("CopySha256");
        Resources["PauseLabel"] = _loc.Get("Pause");
        Resources["ResumeLabel"] = _loc.Get("Resume");
        Resources["RetryLabel"] = _loc.Get("Retry");
        Resources["InstagramDownloadSectionLabel"] = _loc.Get("InstagramDownloadSection");
        Resources["InstagramDownloadOneLabel"] = _loc.Get("InstagramDownloadOne");
    }

    private void UpdateQueueEmptyState()
    {
        var isEmpty = _items.Count == 0;
        var browsing = InstagramBrowsePanel.IsVisible;

        EmptyText.IsVisible = isEmpty && !browsing;
        ClearListButton.IsVisible = !isEmpty;

        if (_items.Count > 0)
            DownloadQueueExpander.IsExpanded = true;
        else if (browsing)
            DownloadQueueExpander.IsExpanded = false;

        DownloadQueueHeader.Text = isEmpty
            ? _loc.Get("DownloadQueue")
            : string.Format(_loc.Get("DownloadQueueCount"), _items.Count);

        UpdateDownloadQueueVisibility();
    }

    private void SourceTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, SourceTabs))
            return;

        UpdateDownloadQueueVisibility();
    }

    private RowDefinition DownloadQueueRow => RootLayoutGrid.RowDefinitions[DownloadQueueRowIndex];

    private void UpdateDownloadQueueVisibility()
    {
        var hideQueue = ReferenceEquals(SourceTabs.SelectedItem, ConvertTab);
        DownloadQueueExpander.IsVisible = !hideQueue;

        if (hideQueue)
        {
            DownloadQueueRow.Height = new GridLength(0);
            DownloadQueueRow.MinHeight = 0;
            return;
        }

        ApplyDownloadQueueLayout();
    }

    private void ApplyDownloadQueueLayout()
    {
        if (ReferenceEquals(SourceTabs.SelectedItem, ConvertTab))
            return;

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
        var row = InstagramGrid.RowDefinitions[InstagramBrowseRowIndex];
        if (InstagramBrowsePanel.IsVisible)
        {
            row.Height = new GridLength(1, GridUnitType.Star);
            row.MinHeight = 120;
            return;
        }

        row.Height = new GridLength(0);
        row.MinHeight = 0;
    }

    private void RefreshAllItemStatusText()
    {
        foreach (var item in _items)
            RefreshItemStatusText(item);
    }

    private void BindInstagramBrowserCombo()
    {
        var selected = InstagramBrowserCombo.SelectedItem is BrowserOption current
            ? current.Browser
            : _settings.Current.InstagramBrowser;

        var options = InstagramBrowserProfiles.AvailableBrowsers
            .Select(b => new BrowserOption(b, _loc.Get(b switch
            {
                InstagramBrowser.Edge => "BrowserEdge",
                InstagramBrowser.Chromium => "BrowserChromium",
                InstagramBrowser.Brave => "BrowserBrave",
                InstagramBrowser.Firefox => "BrowserFirefox",
                _ => "BrowserChrome"
            })))
            .ToList();

        _initializing = true;
        try
        {
            InstagramBrowserCombo.ItemsSource = options;
            InstagramBrowserCombo.SelectedItem = options.FirstOrDefault(o => o.Browser == selected) ?? options.FirstOrDefault();
        }
        finally
        {
            _initializing = false;
        }
    }

    private InstagramBrowser GetSelectedInstagramBrowser() =>
        InstagramBrowserCombo.SelectedItem is BrowserOption option
            ? option.Browser
            : _settings.Current.InstagramBrowser;

    private string GetSaveFolder() =>
        string.IsNullOrWhiteSpace(SavePathBox.Text)
            ? _settings.Current.DefaultSavePath
            : SavePathBox.Text.Trim();

    private void BindLanguageCombo()
    {
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();
    }

    private void SelectLanguage(AppLanguage language)
    {
        if (LanguageCombo.ItemsSource is IEnumerable<LanguageOption> options)
            LanguageCombo.SelectedItem = options.FirstOrDefault(o => o.Language == language);
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
            DownloadStatus.Paused => _loc.Get("StatusPaused"),
            DownloadStatus.Queued => _scheduler.IsHeld(item)
                ? string.Format(_loc.Get("QueueScheduledFor"), FormatNextScheduledStart())
                : _loc.Get("StatusQueued"),
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

            if (e.PropertyName == nameof(DownloadItem.Status))
            {
                if (item.Status is DownloadStatus.Fetching or DownloadStatus.Downloading)
                    _queueHadWork = true;
                CheckQueueFinished();
            }
        };
    }

    // ---- Queue: scheduler, finish action, categories, batch, clipboard ----------------------------

    /// <summary>Every new download goes through here: starts now, or waits for the scheduler window.</summary>
    private async Task StartOrScheduleAsync(DownloadItem item)
    {
        if (await _scheduler.StartOrHoldAsync(item))
            RefreshItemStatusText(item);
    }

    private void ApplySchedulerSettings()
    {
        var s = _settings.Current;
        var schedule = QueueSchedule.From(s.SchedulerStartEnabled, s.SchedulerStartTime, s.SchedulerStopEnabled, s.SchedulerStopTime);
        _ = _scheduler.ConfigureAsync(schedule);
        UpdateSchedulerStatus();
    }

    private void OnSchedulerChanged()
    {
        RefreshAllItemStatusText();
        UpdateSchedulerStatus();
    }

    private string FormatNextScheduledStart()
    {
        var next = _scheduler.NextStart;
        var time = QueueSchedule.FormatTime(next.TimeOfDay);
        return next.Date > DateTime.Today.AddDays(1) ? $"{next:ddd} {time}" : time;
    }

    private void UpdateSchedulerStatus()
    {
        var schedule = _scheduler.Schedule;
        if (!schedule.IsEnabled)
        {
            SchedulerStatusText.IsVisible = false;
            StartScheduledNowButton.IsVisible = false;
            return;
        }

        var start = QueueSchedule.FormatTime(schedule.Start ?? TimeSpan.Zero);
        var stop = schedule.Stop is { } s ? QueueSchedule.FormatTime(s) : "24:00";
        var text = string.Format(_loc.Get("QueueScheduleWindow"), $"{start}–{stop}");
        if (_scheduler.HeldCount > 0)
            text += " · " + string.Format(_loc.Get("QueueScheduleHeld"), _scheduler.HeldCount, FormatNextScheduledStart());

        SchedulerStatusText.Text = text;
        SchedulerStatusText.IsVisible = true;
        StartScheduledNowButton.IsVisible = _scheduler.HeldCount > 0;
    }

    private async void StartScheduledNowButton_Click(object? sender, RoutedEventArgs e) =>
        await _scheduler.ReleaseAllAsync();

    private void BindFinishActionCombo()
    {
        var options = new List<FinishActionOption>
        {
            new(FinishAction.Nothing, _loc.Get("FinishNothing")),
            new(FinishAction.Sleep, _loc.Get("FinishSleep")),
            new(FinishAction.ShutDown, _loc.Get("FinishShutDown")),
            new(FinishAction.QuitApp, _loc.Get("FinishQuitApp"))
        };

        _bindingFinishCombo = true;
        try
        {
            FinishActionCombo.ItemsSource = options;
            FinishActionCombo.SelectedItem = options.First(o => o.Action == _finishAction);
        }
        finally
        {
            _bindingFinishCombo = false;
        }
    }

    private void FinishActionCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_bindingFinishCombo || FinishActionCombo.SelectedItem is not FinishActionOption option)
            return;

        _finishAction = option.Action;
        // Choosing an action while downloads run arms it for this batch.
        if (_items.Any(QueueCompletion.IsActive))
            _queueHadWork = true;
    }

    /// <summary>When the whole queue just finished, counts down 60 s (cancellable) and runs the chosen action.</summary>
    private async void CheckQueueFinished()
    {
        if (_finishAction == FinishAction.Nothing || _finishPending || !_queueHadWork)
            return;

        if (!QueueCompletion.IsAllFinished(_items, _scheduler.HeldCount))
            return;

        _queueHadWork = false;
        _finishPending = true;
        var action = _finishAction;
        try
        {
            var run = await new FinishCountdownWindow(_loc, action).RunAsync();
            // New downloads added during the countdown cancel it.
            if (!run || _items.Any(QueueCompletion.IsActive))
                return;

            FinishActions.Execute(action);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(string.Format(_loc.Get("FinishActionFailed"), ex.Message), _loc.Get("FinishCountdownTitle"), DialogKind.Error);
        }
        finally
        {
            _finishPending = false;
        }
    }

    private string CategoryFolder(string baseFolder, DownloadCategory category) =>
        DownloadCategoryRouter.ResolveFolder(baseFolder, category, _settings.Current.SortIntoCategoryFolders);

    private async void CopySha256_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string hash } || string.IsNullOrWhiteSpace(hash))
            return;

        try
        {
            await PlatformShell.CopyTextAsync(this, hash);
            await Dialogs.ShowAsync(this, $"{_loc.Get("CopySha256Copied")}\n{hash}", _loc.Get("CopySha256"));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("CopySha256"), DialogKind.Warning);
        }
    }

    private async void BatchDownloadButton_Click(object? sender, RoutedEventArgs e) =>
        await OpenBatchWindowAsync(FileDownloadUrlBox.Text?.Trim());

    private async Task OpenBatchWindowAsync(string? initialPattern)
    {
        var prefill = BatchPatternExpander.HasPattern(initialPattern) ? initialPattern : null;
        var window = new BatchDownloadWindow(_loc, QueueBatchAsync, prefill);
        if (await window.ShowDialog<bool>(this) && prefill is not null)
            FileDownloadUrlBox.Text = string.Empty;
    }

    /// <summary>Queues every expanded link as a direct-file download with the Files tab folder and connections.</summary>
    private async Task QueueBatchAsync(IReadOnlyList<string> urls)
    {
        foreach (var url in urls)
            DirectDownloadLinkResolver.EnsureSupported(url);

        var folder = GetFileSaveFolder();
        var connections = GetFileConnections();
        foreach (var url in urls)
            await AddFileDownloadAsync(url, null, folder, connections);

        DownloadQueueExpander.IsExpanded = true;
    }

    private void ApplyClipboardMonitor()
    {
        var on = _settings.Current.ClipboardMonitor;
        if (on && !_clipboardMonitorWasOn)
        {
            // Fresh baseline: whatever is on the clipboard right now is not a "new" link.
            _clipboardWatcher = new ClipboardUrlWatcher();
            _clipboardTimer.Start();
        }
        else if (!on)
        {
            _clipboardTimer.Stop();
        }

        _clipboardMonitorWasOn = on;
    }

    /// <summary>Avalonia has no clipboard-changed event, so the text is polled every 1.5 s.</summary>
    private async void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        if (_clipboardPolling || !_settings.Current.ClipboardMonitor || Clipboard is null)
            return;

        _clipboardPolling = true;
        try
        {
            string? text;
            try
            {
                text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(Clipboard);
            }
            catch
            {
                return; // clipboard busy (another app holds it) — try again next tick
            }

            var url = _clipboardWatcher.Observe(text, _settings.Current.InterceptFileTypes);
            if (url is not null)
            {
                ShowCaughtDownload(new Services.Bridge.BridgeDownloadRequest
                {
                    Url = url,
                    FileName = ClipboardUrlWatcher.FileNameFromUrl(url)
                });
            }
        }
        finally
        {
            _clipboardPolling = false;
        }
    }

    private sealed record FinishActionOption(FinishAction Action, string DisplayName)
    {
        public override string ToString() => DisplayName;
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

    private async void BrowseSavePath_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("SavePath"), SavePathBox.Text);
        if (folder is null)
            return;

        SavePathBox.Text = folder;
        InstagramSavePathBox.Text = folder;
    }

    private async void FetchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlBox.Text)) return;
        try
        {
            FetchButton.IsEnabled = false;
            PreviewText.IsVisible = true;
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
            PreviewText.IsVisible = false;
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("Fetch"), DialogKind.Warning);
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
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

            UrlBox.Text = string.Empty;
            PreviewText.IsVisible = false;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("Download"), DialogKind.Error);
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private async Task AddDownloadAsync(string url, string title)
    {
        var format = FormatCombo.SelectedItem?.ToString() ?? _settings.Current.PreferredFormat;
        var item = new DownloadItem
        {
            Url = url,
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Quality = QualityCombo.SelectedItem?.ToString() ?? _settings.Current.DefaultQuality,
            Format = format,
            SaveFolder = CategoryFolder(GetSaveFolder(), DownloadCategoryRouter.ClassifyMediaFormat(format))
        };
        _items.Insert(0, item);
        AttachItemHandlers(item);
        UpdateQueueEmptyState();
        await StartOrScheduleAsync(item);
    }

    private DownloadItem? FindItem(object? sender) =>
        sender is Control { Tag: string id } ? _items.FirstOrDefault(x => x.Id == id) : null;

    private void CancelDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (FindItem(sender) is not { } item)
            return;

        _downloads.Cancel(item);
        _scheduler.Forget(item);
    }

    private void PauseDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (FindItem(sender) is { } item)
            _downloads.Pause(item);
    }

    private void ResumeDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (FindItem(sender) is not { } item)
            return;

        _downloads.Resume(item);
        RefreshItemStatusText(item);
    }

    private async void RetryDownload_Click(object? sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item is null || !item.CanRetry) return;

        item.ErrorMessage = string.Empty;
        item.Progress = 0;
        item.SpeedText = string.Empty;
        item.EtaText = string.Empty;
        item.SizeText = string.Empty;
        item.Status = DownloadStatus.Queued;
        item.StatusText = _loc.Get("StatusQueued");
        await StartOrScheduleAsync(item);
    }

    private async void FileDownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        var url = FileDownloadUrlBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
            return;

        // part_[01-20].rar → open the batch downloader with the pattern.
        if (BatchPatternExpander.HasPattern(url))
        {
            await OpenBatchWindowAsync(url);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            await Dialogs.ShowAsync(this, _loc.Get("FileDownloadInvalidUrl"), _loc.Get("TabFileDownload"), DialogKind.Warning);
            return;
        }

        try
        {
            DirectDownloadLinkResolver.EnsureSupported(url);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("TabFileDownload"), DialogKind.Warning);
            return;
        }

        try
        {
            FileDownloadButton.IsEnabled = false;
            var name = FileDownloadNameBox.Text?.Trim();
            await AddFileDownloadAsync(url, name, GetFileSaveFolder(), GetFileConnections());
            FileDownloadUrlBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("TabFileDownload"), DialogKind.Error);
        }
        finally
        {
            FileDownloadButton.IsEnabled = true;
        }
    }

    private string GetFileSaveFolder() =>
        string.IsNullOrWhiteSpace(FileDownloadSavePathBox.Text)
            ? _settings.Current.DefaultSavePath
            : FileDownloadSavePathBox.Text.Trim();

    private int GetFileConnections() =>
        FileDownloadConnectionsCombo.SelectedItem is ComboBoxItem { Content: string text }
        && int.TryParse(text, out var parsed)
            ? parsed
            : _settings.Current.FileDownloadConnections;

    private async Task AddFileDownloadAsync(string url, string? name, string baseFolder, int connections)
    {
        var saveFolder = CategoryFolder(
            baseFolder,
            DownloadCategoryRouter.Classify(string.IsNullOrWhiteSpace(name) ? url : name));

        Directory.CreateDirectory(saveFolder);

        var item = new DownloadItem
        {
            Url = url,
            Kind = DownloadKind.DirectFile,
            Title = string.IsNullOrWhiteSpace(name) ? url : name,
            DesiredFileName = string.IsNullOrWhiteSpace(name) ? null : name,
            ConnectionCount = connections,
            SaveFolder = saveFolder
        };

        _items.Insert(0, item);
        AttachItemHandlers(item);
        UpdateQueueEmptyState();
        await StartOrScheduleAsync(item);
    }

    private async void FileDownloadBrowseSavePath_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("SavePath"), FileDownloadSavePathBox.Text);
        if (folder is not null)
            FileDownloadSavePathBox.Text = folder;
    }

    private static int GetFileConnectionsIndex(int connections) => connections switch
    {
        <= 4 => 0,
        <= 8 => 1,
        <= 12 => 2,
        _ => 3
    };

    private async void CopyDownloadDetails_Click(object? sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item is null || string.IsNullOrWhiteSpace(item.DiagnosticsLog))
            return;

        try
        {
            await PlatformShell.CopyTextAsync(this, item.BuildDiagnosticReport());
            await Dialogs.ShowAsync(this, _loc.Get("CopyDetailsCopied"), _loc.Get("CopyDetails"));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("CopyDetails"), DialogKind.Warning);
        }
    }

    private void RemoveDownload_Click(object? sender, RoutedEventArgs e)
    {
        if (FindItem(sender) is not { } item) return;

        _downloads.Cancel(item);
        _downloads.Untrack(item);
        _scheduler.Forget(item);
        _items.Remove(item);
        UpdateQueueEmptyState();
    }

    private async void ClearListButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;

        if (!await Dialogs.ConfirmAsync(this, _loc.Get("ConfirmClearList"), _loc.Get("ClearList")))
            return;

        foreach (var item in _items.ToList())
        {
            _downloads.Cancel(item);
            _downloads.Untrack(item);
            _scheduler.Forget(item);
        }

        _items.Clear();
        UpdateQueueEmptyState();
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (FindItem(sender) is not { } item) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(item.OutputPath) && File.Exists(item.OutputPath))
                PlatformShell.RevealInFolder(item.OutputPath);
            else if (Directory.Exists(item.SaveFolder))
                PlatformShell.OpenFolder(item.SaveFolder);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("OpenFolder"), DialogKind.Warning);
        }
    }

    private async void PlayVideo_Click(object? sender, RoutedEventArgs e)
    {
        var item = FindItem(sender);
        if (item is null || string.IsNullOrWhiteSpace(item.OutputPath) || !File.Exists(item.OutputPath)) return;

        try
        {
            PlatformShell.OpenFile(item.OutputPath);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("Play"), DialogKind.Warning);
        }
    }

    private async void Link_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string url })
            return;

        try
        {
            PlatformShell.OpenUrl(url);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, "Link", DialogKind.Warning);
        }
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
        InstagramBrowsePanel.IsVisible = false;
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
            var header = new Grid { Margin = new Thickness(0, 0, 8, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var title = new TextBlock
            {
                Text = string.Format(_loc.Get("InstagramSectionHeader"), section.Title, section.Items.Count),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            var downloadSection = new Button
            {
                Content = _loc.Get("InstagramDownloadSection"),
                Tag = section.Title,
                Padding = new Thickness(10, 4, 10, 4)
            };
            downloadSection.Classes.Add("secondary");
            downloadSection.Click += InstagramDownloadSection_Click;
            Grid.SetColumn(downloadSection, 1);
            header.Children.Add(downloadSection);

            var itemsPanel = new StackPanel { Margin = new Thickness(4, 6, 0, 4) };
            foreach (var item in section.Items)
            {
                var row = new Grid
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto")
                };

                var checkBox = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                checkBox.Bind(ToggleButtonIsChecked, new Binding(nameof(InstagramBrowseItem.Selected))
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

                var thumbImage = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var thumbHost = new Border
                {
                    Width = 44,
                    Height = 44,
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Child = thumbImage
                };
                thumbHost.Bind(Border.BackgroundProperty, thumbHost.GetResourceObservable("SurfaceBrush"));
                LoadInstagramBrowseThumbnail(thumbImage, item.ThumbnailUrl, _instagramBrowseSession?.CookieFilePath);
                Grid.SetColumn(thumbHost, 1);
                row.Children.Add(thumbHost);

                var label = new TextBlock
                {
                    Text = item.Title,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(label, 2);
                row.Children.Add(label);

                var downloadOne = new Button
                {
                    Content = _loc.Get("InstagramDownloadOne"),
                    Tag = item,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(8, 0, 0, 0)
                };
                downloadOne.Classes.Add("secondary");
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

    private static readonly AvaloniaProperty<bool?> ToggleButtonIsChecked =
        Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty;

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
        InstagramBrowsePanel.IsVisible = _instagramBrowseItems.Count > 0;
        InstagramPreviewText.IsVisible = true;
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

    private async void InstagramFetchButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InstagramUrlBox.Text)) return;

        InstagramCookieSession? session = null;
        try
        {
            InstagramFetchButton.IsEnabled = false;
            ClearInstagramBrowse();
            InstagramPreviewText.IsVisible = true;
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
            InstagramPreviewText.IsVisible = false;
            ClearInstagramBrowse();
            await Dialogs.ShowAsync(this, InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Fetch"), DialogKind.Warning);
        }
        finally
        {
            session?.Dispose();
            InstagramFetchButton.IsEnabled = true;
        }
    }

    private async void InstagramDownloadButton_Click(object? sender, RoutedEventArgs e)
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

            var normalized = InstagramUrlHelper.NormalizeDetailed(InstagramUrlBox.Text ?? string.Empty);
            session = RequireInstagramSession();
            var mediaPk = TryResolveBrowseMediaPk(normalized.Url);
            await EnqueueInstagramUrlAsync(normalized, session, mediaPk);
            session.DetachTempDirectory();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), DialogKind.Error);
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
            Kind = DownloadKind.Instagram,
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
        await StartOrScheduleAsync(item);
    }

    private async void InstagramLoginButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            InstagramBrowserProfiles.OpenLoginBrowser(GetSelectedInstagramBrowser());
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("InstagramLogin"), DialogKind.Warning);
        }
    }

    private async void InstagramImportCookiesButton_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new InstagramCookiesWindow(_loc);
        if (!await dlg.ShowDialog<bool>(this))
            return;

        _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
        _settings.Save(_settings.Current);
        UpdateInstagramCookiesStatus();
    }

    private async void InstagramSyncCookiesButton_Click(object? sender, RoutedEventArgs e)
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
                await Dialogs.ShowAsync(this, error, _loc.Get("InstagramSyncCookies"), DialogKind.Warning);
                return;
            }

            _settings.Current.InstagramCookiesPath = InstagramCookiesBuilder.SavedCookiesPath;
            _settings.Save(_settings.Current);
            UpdateInstagramCookiesStatus();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("InstagramSyncCookies"), DialogKind.Warning);
        }
        finally
        {
            InstagramSyncCookiesButton.IsEnabled = true;
        }
    }

    private async void InstagramClearCookiesButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = _settings.Current.InstagramCookiesPath;
        var hasCookies = (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            || InstagramCookiesBuilder.HasSavedCookies();

        if (!hasCookies)
        {
            UpdateInstagramCookiesStatus();
            return;
        }

        if (!await Dialogs.ConfirmAsync(this, _loc.Get("InstagramConfirmClearCookies"), _loc.Get("InstagramClearCookies")))
            return;

        if (!InstagramCookiesBuilder.TryClearSavedCookies(out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("InstagramClearCookies"), DialogKind.Warning);
            return;
        }

        _settings.Current.InstagramCookiesPath = string.Empty;
        _settings.Save(_settings.Current);
        UpdateInstagramCookiesStatus();
    }

    private async void InstagramDownloadSelectedButton_Click(object? sender, RoutedEventArgs e)
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
            await Dialogs.ShowAsync(this, InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), DialogKind.Error);
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

    private void InstagramSelectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectAll = _instagramBrowseItems.Any(x => !x.Selected);
        foreach (var item in _instagramBrowseItems)
            item.Selected = selectAll;

        UpdateInstagramSelectAllButtonLabel();
    }

    private void InstagramClearBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        ClearInstagramBrowse();
        InstagramPreviewText.IsVisible = false;
    }

    private void InstagramExpandAllButton_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var expander in _instagramSectionExpanders)
            expander.IsExpanded = true;
    }

    private void InstagramCollapseAllButton_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var expander in _instagramSectionExpanders)
            expander.IsExpanded = false;
    }

    private async void InstagramDownloadSection_Click(object? sender, RoutedEventArgs e)
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
            await Dialogs.ShowAsync(this, InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), DialogKind.Error);
        }
    }

    private async void InstagramDownloadOne_Click(object? sender, RoutedEventArgs e)
    {
        if (_instagramBrowseSession is null || sender is not Button { Tag: InstagramBrowseItem browseItem })
            return;

        try
        {
            await EnqueueInstagramBrowseItemAsync(browseItem, _instagramBrowseSession);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, InstagramCookieSession.FormatError(ex.Message, _loc), _loc.Get("Download"), DialogKind.Error);
        }
    }

    private void InstagramBrowserCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || InstagramBrowserCombo.SelectedItem is not BrowserOption option)
            return;

        _settings.Current.InstagramBrowser = option.Browser;
        _settings.Save(_settings.Current);
    }

    private async void InstagramBrowseSavePath_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("SavePath"), InstagramSavePathBox.Text);
        if (folder is null)
            return;

        InstagramSavePathBox.Text = folder;
        SavePathBox.Text = folder;
    }

    private void InstagramSavePathBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (SavePathBox is not null && SavePathBox.Text != InstagramSavePathBox.Text)
            SavePathBox.Text = InstagramSavePathBox.Text;
    }

    private async void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        await new AboutWindow(_loc).ShowDialog(this);
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings.Current, _loc);
        if (!await dlg.ShowDialog<bool>(this) || dlg.ResultSettings is null)
            return;

        _settings.Save(dlg.ResultSettings);
        ApplySchedulerSettings();
        ApplyClipboardMonitor();
        SavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
        InstagramSavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
        FileDownloadSavePathBox.Text = dlg.ResultSettings.DefaultSavePath;
        FileDownloadConnectionsCombo.SelectedIndex = GetFileConnectionsIndex(dlg.ResultSettings.FileDownloadConnections);
        _theme.Apply(dlg.ResultSettings.Theme);
        _loc.SetLanguage(dlg.ResultSettings.Language);
        _initializing = true;
        SelectLanguage(dlg.ResultSettings.Language);
        _initializing = false;
        ApplyLocalization();
    }

    private void ThemeButton_Click(object? sender, RoutedEventArgs e)
    {
        var next = _theme.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next);
        _settings.Current.Theme = next;
        _settings.Save(_settings.Current);
    }

    private void LanguageCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageCombo.SelectedItem is not LanguageOption option)
            return;

        _loc.SetLanguage(option.Language);
        _settings.Current.Language = option.Language;
        _settings.Save(_settings.Current);
        ApplyLocalization();
    }

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

                using var stream = new MemoryStream(bytes);
                var bitmap = Bitmap.DecodeToWidth(stream, 88);
                await Dispatcher.UIThread.InvokeAsync(() => image.Source = bitmap);
            }
            catch
            {
                // thumbnail is optional
            }
        });
    }

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);

    private sealed record BrowserOption(InstagramBrowser Browser, string DisplayName);
}
