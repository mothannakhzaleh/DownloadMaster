using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DownloadMaster.Models;
using DownloadMaster.Services;

namespace DownloadMaster;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly LocalizationService _loc;
    public AppSettings? ResultSettings { get; private set; }

    public SettingsWindow(AppSettings current, LocalizationService loc)
    {
        InitializeComponent();
        _original = current;
        _loc = loc;

        LanguageCombo.DisplayMemberPath = nameof(LanguageOption.DisplayName);
        LanguageCombo.SelectedValuePath = nameof(LanguageOption.Language);
        LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages
            .Select(lang => new LanguageOption(lang, LocalizationService.GetNativeLanguageName(lang)))
            .ToList();

        LoadSettings(current);
        ApplyLabels();
        FlowDirection = _loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void LoadSettings(AppSettings s)
    {
        SavePathBox.Text = s.DefaultSavePath;
        ConcurrentCombo.SelectedIndex = Math.Clamp(s.MaxConcurrentDownloads - 1, 0, 4);
        ThemeCombo.SelectedIndex = s.Theme == AppTheme.Light ? 1 : 0;
        LanguageCombo.SelectedValue = s.Language;
    }

    private void ApplyLabels()
    {
        HeaderText.Text = _loc.Get("Settings");
        SavePathLabel.Text = _loc.Get("SavePath");
        MaxConcurrentLabel.Text = _loc.Get("MaxConcurrent");
        ThemeLabel.Text = _loc.Get("Theme");
        LanguageLabel.Text = _loc.Get("Language");
        SaveButton.Content = _loc.Get("Save");
        CancelButton.Content = _loc.Get("Cancel");
        ResetButton.Content = _loc.Get("Reset");

        var selectedTheme = ThemeCombo.SelectedIndex;
        ThemeCombo.Items.Clear();
        ThemeCombo.Items.Add(_loc.Get("ThemeDark"));
        ThemeCombo.Items.Add(_loc.Get("ThemeLight"));
        ThemeCombo.SelectedIndex = selectedTheme >= 0 ? selectedTheme : 0;
    }

    private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = _loc.Get("SavePath") };
        if (dlg.ShowDialog() == true)
            SavePathBox.Text = dlg.FolderName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultSettings = new AppSettings
        {
            DefaultSavePath = SavePathBox.Text.Trim(),
            MaxConcurrentDownloads = ConcurrentCombo.SelectedIndex + 1,
            Theme = ThemeCombo.SelectedIndex == 1 ? AppTheme.Light : AppTheme.Dark,
            Language = LanguageCombo.SelectedValue is AppLanguage lang ? lang : AppLanguage.English,
            SpeedLimitKbps = _original.SpeedLimitKbps,
            AutoRetryAttempts = _original.AutoRetryAttempts,
            DefaultQuality = _original.DefaultQuality,
            PreferredFormat = _original.PreferredFormat,
            NamingTemplate = _original.NamingTemplate,
            DownloadSubtitles = _original.DownloadSubtitles,
            ClipboardMonitor = _original.ClipboardMonitor
        };
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(new AppSettings
        {
            DefaultSavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DownloadMaster"),
            MaxConcurrentDownloads = _original.MaxConcurrentDownloads,
            Theme = _original.Theme,
            Language = _original.Language,
            SpeedLimitKbps = _original.SpeedLimitKbps,
            AutoRetryAttempts = _original.AutoRetryAttempts,
            DefaultQuality = _original.DefaultQuality,
            PreferredFormat = _original.PreferredFormat,
            NamingTemplate = _original.NamingTemplate,
            DownloadSubtitles = _original.DownloadSubtitles,
            ClipboardMonitor = _original.ClipboardMonitor
        });
    }

    private sealed record LanguageOption(AppLanguage Language, string DisplayName);
}
