using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Queue;
using DownloadMaster.Services.Ui;

namespace DownloadMaster;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly LocalizationService _loc;
    private readonly List<LanguageOption> _languages;
    private readonly AutoStartService _autoStart;
    private readonly bool _autoStartWasEnabled;

    public AppSettings? ResultSettings { get; private set; }

    // Required by the Avalonia XAML loader / designer.
    public SettingsWindow() : this(new AppSettings(), new LocalizationService())
    {
    }

    public SettingsWindow(AppSettings current, LocalizationService loc, AutoStartService? autoStart = null)
    {
        InitializeComponent();
        _original = current;
        _loc = loc;
        _autoStart = autoStart ?? AutoStartService.CreateDefault();

        _languages = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();
        LanguageCombo.ItemsSource = _languages;

        ApplyLabels();
        LoadSettings(current);

        // The sign-in entry lives in the OS, not settings.json: show what is really there.
        StartWithOsCheck.IsVisible = AutoStartService.IsSupported;
        _autoStartWasEnabled = AutoStartService.IsSupported && _autoStart.IsEnabled();
        StartWithOsCheck.IsChecked = _autoStartWasEnabled;

        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void LoadSettings(AppSettings s)
    {
        SavePathBox.Text = s.DefaultSavePath;
        ConcurrentCombo.SelectedIndex = Math.Clamp(s.MaxConcurrentDownloads - 1, 0, 4);
        FileDownloadConnectionsCombo.SelectedIndex = GetFileConnectionsIndex(s.FileDownloadConnections);
        ThemeCombo.SelectedIndex = s.Theme == AppTheme.Light ? 1 : 0;
        MinimizeToTrayCheck.IsChecked = s.MinimizeToTray;
        ClipboardMonitorCheck.IsChecked = s.ClipboardMonitor;
        SortCategoriesCheck.IsChecked = s.SortIntoCategoryFolders;
        SchedulerStartCheck.IsChecked = s.SchedulerStartEnabled;
        SchedulerStartBox.Text = s.SchedulerStartTime;
        SchedulerStopCheck.IsChecked = s.SchedulerStopEnabled;
        SchedulerStopBox.Text = s.SchedulerStopTime;
        LanguageCombo.SelectedItem = _languages.FirstOrDefault(l => l.Language == s.Language);
    }

    private void ApplyLabels()
    {
        Title = _loc.Get("Settings");
        HeaderText.Text = _loc.Get("Settings");
        SavePathLabel.Text = _loc.Get("SavePath");
        MaxConcurrentLabel.Text = _loc.Get("MaxConcurrent");
        FileDownloadConnectionsLabel.Text = _loc.Get("FileDownloadConnections");
        ThemeLabel.Text = _loc.Get("Theme");
        MinimizeToTrayCheck.Content = _loc.Get("SettingsMinimizeToTray");
        StartWithOsCheck.Content = _loc.Get("SettingsStartWithOs");
        ClipboardMonitorCheck.Content = _loc.Get("SettingsClipboardMonitor");
        SortCategoriesText.Text = _loc.Get("SettingsSortCategories");
        SchedulerHeader.Text = _loc.Get("SettingsSchedulerHeader");
        SchedulerStartCheck.Content = _loc.Get("SettingsSchedulerStart");
        SchedulerStopCheck.Content = _loc.Get("SettingsSchedulerStop");
        SchedulerHintText.Text = _loc.Get("SettingsSchedulerHint");
        SchedulerStartBox.FlowDirection = FlowDirection.LeftToRight;
        SchedulerStopBox.FlowDirection = FlowDirection.LeftToRight;
        BrowserExtensionButton.Content = _loc.Get("SettingsBrowserExtension");
        LanguageLabel.Text = _loc.Get("Language");
        SaveButton.Content = _loc.Get("Save");
        CancelButton.Content = _loc.Get("Cancel");
        ResetButton.Content = _loc.Get("Reset");
        ThemeCombo.ItemsSource = new[] { _loc.Get("ThemeDark"), _loc.Get("ThemeLight") };
    }

    private async void BrowseSavePath_Click(object? sender, RoutedEventArgs e)
    {
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("SavePath"), SavePathBox.Text);
        if (folder is not null)
            SavePathBox.Text = folder;
    }

    private void BrowserExtensionButton_Click(object? sender, RoutedEventArgs e) =>
        (Avalonia.Application.Current as App)?.ShowExtensionInstaller();

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var startOn = SchedulerStartCheck.IsChecked == true;
        var stopOn = SchedulerStopCheck.IsChecked == true;
        var startText = SchedulerStartBox.Text?.Trim() ?? string.Empty;
        var stopText = SchedulerStopBox.Text?.Trim() ?? string.Empty;
        if ((startOn && !QueueSchedule.TryParseTime(startText, out _))
            || (stopOn && !QueueSchedule.TryParseTime(stopText, out _)))
        {
            await Dialogs.ShowAsync(this, _loc.Get("SettingsSchedulerInvalidTime"), _loc.Get("SettingsSchedulerHeader"), DialogKind.Warning);
            return;
        }

        var result = Clone(_original);
        result.DefaultSavePath = SavePathBox.Text?.Trim() ?? string.Empty;
        result.MaxConcurrentDownloads = ConcurrentCombo.SelectedIndex + 1;
        result.FileDownloadConnections = ParseConnections(FileDownloadConnectionsCombo);
        result.Theme = ThemeCombo.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark;
        result.Language = LanguageCombo.SelectedItem is LanguageOption lang ? lang.Language : AppLanguage.English;
        result.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        result.ClipboardMonitor = ClipboardMonitorCheck.IsChecked == true;
        result.SortIntoCategoryFolders = SortCategoriesCheck.IsChecked == true;
        result.SchedulerStartEnabled = startOn;
        result.SchedulerStopEnabled = stopOn;
        if (QueueSchedule.TryParseTime(startText, out var start))
            result.SchedulerStartTime = QueueSchedule.FormatTime(start);
        if (QueueSchedule.TryParseTime(stopText, out var stop))
            result.SchedulerStopTime = QueueSchedule.FormatTime(stop);

        var wantAutoStart = StartWithOsCheck.IsChecked == true;
        if (AutoStartService.IsSupported && wantAutoStart != _autoStartWasEnabled)
        {
            try
            {
                _autoStart.SetEnabled(wantAutoStart);
            }
            catch (Exception ex)
            {
                await Dialogs.ShowAsync(this, string.Format(_loc.Get("SettingsStartWithOsFailed"), ex.Message),
                    _loc.Get("Settings"), DialogKind.Warning);
            }
        }

        ResultSettings = result;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void ResetButton_Click(object? sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings
        {
            DefaultSavePath = SettingsService.DefaultSaveFolder,
            Theme = _original.Theme,
            Language = _original.Language
        };
        LoadSettings(defaults);
    }

    /// <summary>Deep copy, so every setting (including ones this window does not show) survives a save.</summary>
    private static AppSettings Clone(AppSettings source) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source)) ?? new AppSettings();

    private static int ParseConnections(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Content: string text } && int.TryParse(text, out var value)
            ? value
            : 8;

    private static int GetFileConnectionsIndex(int connections) => connections switch
    {
        <= 4 => 0,
        <= 8 => 1,
        <= 12 => 2,
        _ => 3
    };

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);
}
