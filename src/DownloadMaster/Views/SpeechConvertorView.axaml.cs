using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster.Views;

/// <summary>
/// Speech tab: type text, pick a voice, preview aloud, export WAV/MP3/M4A.
/// Engine per OS comes from <see cref="SpeechSynthesisService"/> (System.Speech / say / espeak-ng / spd-say).
/// </summary>
public partial class SpeechConvertorView : UserControl
{
    private SpeechSynthesisService _speechService = new();
    private readonly ObservableCollection<ConversionResultItem> _speechResults = [];
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _voicesCancellation;
    private LocalizationService _loc = new();

    private IReadOnlyList<SpeechVoiceInfo> _voices = [];
    private bool _voicesLoading;
    private string? _voicesError;
    private bool _isPreviewing;
    private bool _isExporting;

    public SpeechConvertorView()
    {
        InitializeComponent();

        SpeechResultsGrid.ItemsSource = _speechResults;
        SpeechOutputFolderBox.Text = DefaultOutputFolder();

        SpeechWavRadio.IsCheckedChanged += SpeechFormat_Changed;
        SpeechMp3Radio.IsCheckedChanged += SpeechFormat_Changed;
        SpeechM4aRadio.IsCheckedChanged += SpeechFormat_Changed;
        SpeechTextBox.TextChanged += (_, _) => UpdateCharacterCount();
        SpeechPreviewButton.Click += (_, _) => _ = PreviewSpeechAsync();
        SpeechStopButton.Click += StopSpeech_Click;
        SpeechExportButton.Click += (_, _) => _ = ExportSpeechAsync();
        SpeechBrowseOutputButton.Click += (_, _) => _ = BrowseSpeechOutputAsync();
        SpeechClearResultsButton.Click += ClearSpeechResults_Click;
        SpeechRefreshVoicesButton.Click += (_, _) => _ = LoadVoicesAsync(redetectEngine: true);
        Unloaded += SpeechConvertorView_Unloaded;

        ApplyEngineTexts();
        UpdateCharacterCount();
        UpdateButtons();
        _ = LoadVoicesAsync(redetectEngine: false);
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        SpeechFormatLabel.Text = loc.Get("SpeechConvertExportFormat");
        SpeechTextTitle.Text = loc.Get("SpeechConvertTextTitle");
        SpeechTextHint.Text = loc.Get("SpeechConvertTextHint");
        SpeechVoiceTitle.Text = loc.Get("SpeechConvertVoiceTitle");
        SpeechVoiceLabel.Text = loc.Get("SpeechConvertVoice");
        SpeechRateLabel.Text = loc.Get("SpeechConvertSpeed");
        SpeechVolumeLabel.Text = loc.Get("SpeechConvertVolume");
        SpeechStopButton.Content = loc.Get("SpeechConvertStop");
        SpeechOutputTitle.Text = loc.Get("SpeechConvertOutput");
        SpeechFileNameLabel.Text = loc.Get("SpeechConvertFileName");
        SpeechSaveToLabel.Text = loc.Get("SpeechConvertSaveFolder");
        SpeechBrowseOutputButton.Content = loc.Get("Browse");
        SpeechResultsTitle.Text = loc.Get("SpeechConvertResult");
        SpeechClearResultsButton.Content = loc.Get("ClearList");
        ToolTip.SetTip(SpeechRefreshVoicesButton, loc.Get("SpeechRefreshVoices"));

        string[] headers = ["ResultColStatus", "ResultColLabel", "ResultColOutputSize", "ResultColDetails"];
        for (var i = 0; i < headers.Length && i < SpeechResultsGrid.Columns.Count; i++)
            SpeechResultsGrid.Columns[i].Header = loc.Get(headers[i]);

        if (_speechResults.Count == 0)
            SpeechResultsSummaryText.Text = loc.Get("SpeechConvertNoResult");

        if (!_isPreviewing && !_isExporting)
            SpeechStatusText.Text = loc.Get("SpeechConvertReady");

        ApplyEngineTexts();
        UpdateButtonTexts();
        UpdateCharacterCount();
    }

    private void SpeechConvertorView_Unloaded(object? sender, RoutedEventArgs e)
    {
        // Leaving the tab stops live speech; a running export keeps going.
        _previewCancellation?.Cancel();
        _speechService.StopPreview();
    }

    // ---------- Voices / engine ----------

    private async Task LoadVoicesAsync(bool redetectEngine)
    {
        if (_isPreviewing || _isExporting)
            return;

        if (redetectEngine)
        {
            // Picks up an engine installed while the app was running (e.g. apt install espeak-ng).
            var old = _speechService;
            _speechService = new SpeechSynthesisService();
            old.Dispose();
        }

        _voicesCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _voicesCancellation = cancellation;

        var previousVoice = (SpeechVoiceCombo.SelectedItem as SpeechVoiceInfo)?.Name;
        _voicesLoading = true;
        _voicesError = null;
        ApplyEngineTexts();
        UpdateButtons();

        IReadOnlyList<SpeechVoiceInfo> voices = [];
        try
        {
            if (_speechService.IsAvailable)
                voices = await _speechService.GetInstalledVoicesAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _voicesError = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_voicesCancellation, cancellation))
            {
                _voicesCancellation = null;
                _voicesLoading = false;
            }

            cancellation.Dispose();
        }

        _voices = voices;
        SpeechVoiceCombo.ItemsSource = voices;
        SpeechVoiceCombo.SelectedItem = PickDefaultVoice(voices, previousVoice);

        ApplyEngineTexts();
        UpdateButtons();
    }

    /// <summary>Keep the previous choice, else a voice in the UI language, else English, else the first one.</summary>
    private SpeechVoiceInfo? PickDefaultVoice(IReadOnlyList<SpeechVoiceInfo> voices, string? previousName)
    {
        if (voices.Count == 0)
            return null;

        if (previousName is not null && voices.FirstOrDefault(v => v.Name == previousName) is { } previous)
            return previous;

        var uiLanguage = _loc.IsRtl ? "ar" : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var uiCulture = CultureInfo.CurrentUICulture.Name;

        return voices.FirstOrDefault(v => SameLocale(v.Locale, uiCulture))
            ?? voices.FirstOrDefault(v => LanguageOf(v.Locale) == uiLanguage)
            ?? voices.FirstOrDefault(v => SameLocale(v.Locale, "en-US"))
            ?? voices.FirstOrDefault(v => LanguageOf(v.Locale) == "en")
            ?? voices[0];

        static string Normalize(string locale) => locale.Replace('_', '-').ToLowerInvariant();
        static bool SameLocale(string a, string b) => a.Length > 0 && Normalize(a) == Normalize(b);
        static string LanguageOf(string locale)
        {
            var normalized = Normalize(locale);
            var dash = normalized.IndexOf('-');
            return dash > 0 ? normalized[..dash] : normalized;
        }
    }

    /// <summary>Voice status line, engine name and per-OS install hint.</summary>
    private void ApplyEngineTexts()
    {
        SpeechEngineText.Text = _speechService.IsAvailable
            ? string.Format(_loc.Get("SpeechEngineLabel"), _speechService.EngineName)
            : string.Empty;
        SpeechEngineText.IsVisible = _speechService.IsAvailable;

        SpeechInstallHint.Text = _speechService.EngineKind switch
        {
            SpeechEngineKind.Windows => _loc.Get("SpeechHintWindows"),
            SpeechEngineKind.MacSay => _loc.Get("SpeechHintMac"),
            SpeechEngineKind.ESpeak => _loc.Get("SpeechHintESpeak"),
            SpeechEngineKind.SpeechDispatcher => _loc.Get("SpeechHintSpd"),
            _ => NoEngineMessage()
        };

        SpeechVoiceStatus.Text = BuildVoiceStatus();
    }

    private string BuildVoiceStatus()
    {
        if (_voicesLoading)
            return _loc.Get("SpeechLoadingVoices");

        if (!_speechService.IsAvailable)
            return _loc.Get("SpeechConvertNoVoicesShort");

        if (_voicesError is not null)
            return string.Format(_loc.Get("SpeechVoicesLoadFailed"), _voicesError);

        if (_voices.Count == 0)
        {
            return _speechService.EngineKind switch
            {
                SpeechEngineKind.Windows => _loc.Get("SpeechNoVoicesWindows"),
                SpeechEngineKind.MacSay => _loc.Get("SpeechNoVoicesMac"),
                _ => _loc.Get("SpeechNoVoicesLinux")
            };
        }

        var line = string.Format(_loc.Get("SpeechConvertVoicesAvailable"), _voices.Count);
        if (!_speechService.CanExport)
            return line + " " + string.Format(_loc.Get("SpeechExportUnsupported"), _speechService.EngineName);

        return line + " " + (GetSelectedOutputFormat() == SpeechOutputFormat.Wav
            ? _loc.Get("SpeechConvertWavHint")
            : _loc.Get("SpeechConvertFfmpegHint"));
    }

    private string NoEngineMessage() =>
        OperatingSystem.IsWindows() ? _loc.Get("SpeechNoEngineWindows")
        : OperatingSystem.IsMacOS() ? _loc.Get("SpeechNoEngineMac")
        : _loc.Get("SpeechNoEngineLinux");

    private void SpeechFormat_Changed(object? sender, RoutedEventArgs e) =>
        SpeechVoiceStatus.Text = BuildVoiceStatus();

    private void UpdateCharacterCount()
    {
        var length = SpeechTextBox.Text?.Length ?? 0;
        SpeechCharacterCount.Text = length == 1
            ? _loc.Get("SpeechConvertOneCharacter")
            : string.Format(_loc.Get("SpeechConvertCharacterCount"), length);
    }

    // ---------- Output folder ----------

    private static string DefaultOutputFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return !string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async Task BrowseSpeechOutputAsync()
    {
        try
        {
            var folder = await Pickers.PickFolderAsync(this, _loc.Get("SpeechChooseFolder"), SpeechOutputFolderBox.Text);
            if (!string.IsNullOrWhiteSpace(folder))
                SpeechOutputFolderBox.Text = folder;
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("TabSpeechConvert"), DialogKind.Error);
        }
    }

    // ---------- Preview ----------

    private async Task PreviewSpeechAsync()
    {
        if (!TryBuildPreviewSettings(out var text, out var voiceName, out var rate, out var volume, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("TabSpeechConvert"), DialogKind.Warning);
            return;
        }

        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;

        SetPreviewBusy(true);
        SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewSpeaking");

        string? failure = null;
        try
        {
            await _speechService.PreviewAsync(text, voiceName, rate, volume, cancellation.Token);
            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewDone");
        }
        catch (OperationCanceledException)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewStopped");
        }
        catch (Exception ex)
        {
            SpeechStatusText.Text = _loc.Get("SpeechConvertPreviewFailed");
            failure = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                _previewCancellation = null;
                SetPreviewBusy(false);
            }

            cancellation.Dispose();
        }

        if (failure is not null)
            await Dialogs.ShowAsync(this, failure, _loc.Get("SpeechConvertPreviewFailed"), DialogKind.Error);
    }

    private void StopSpeech_Click(object? sender, RoutedEventArgs e)
    {
        // Stop cancels live preview and a running export alike.
        _previewCancellation?.Cancel();
        _exportCancellation?.Cancel();
        _speechService.StopPreview();
    }

    // ---------- Export ----------

    private async Task ExportSpeechAsync()
    {
        if (!TryBuildExportSettings(out var settings, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("TabSpeechConvert"), DialogKind.Warning);
            return;
        }

        _exportCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _exportCancellation = cancellation;

        var label = BuildResultLabel(settings.Text);
        SetSpeechResult(new ConversionResultItem
        {
            SourcePath = label,
            FileName = label,
            Status = "Exporting",
            SizeChange = "—",
            Detail = _loc.Get("SpeechConvertGenerating"),
            IsSuccess = false
        });

        SetExportBusy(true);
        SpeechResultsSummaryText.Text = string.Format(_loc.Get("SpeechConvertExporting"), GetFormatLabel(settings.OutputFormat));
        SpeechStatusText.Text = _loc.Get("SpeechConvertExportingStatus");

        SpeechConversionResult? result = null;
        string? failure = null;
        try
        {
            var progress = new Progress<double>(percent =>
            {
                SpeechProgressBar.Value = percent;
                SpeechResultsProgressBar.Value = percent;
                SpeechStatusText.Text = $"{_loc.Get("SpeechConvertExportingStatus")} {percent:F0}%";
            });

            result = await _speechService.ExportAsync(settings, progress, cancellation.Token);

            SetSpeechResult(ConversionResultItem.FromSpeechResult(result, label));
            SpeechStatusText.Text = _loc.Get("SpeechConvertDone");
            SpeechResultsSummaryText.Text = string.Format(_loc.Get("SpeechConvertExported"), Path.GetFileName(result.OutputPath));
        }
        catch (OperationCanceledException)
        {
            _speechResults.Clear();
            SpeechStatusText.Text = _loc.Get("SpeechConvertCancelled");
            SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertCancelled");
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            SpeechStatusText.Text = _loc.Get("SpeechConvertFailed");
            SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertFailed");
            SetSpeechResult(new ConversionResultItem
            {
                SourcePath = label,
                FileName = label,
                Status = "Failed",
                SizeChange = "—",
                Detail = ex.Message,
                IsSuccess = false
            });
        }
        finally
        {
            if (ReferenceEquals(_exportCancellation, cancellation))
                _exportCancellation = null;

            cancellation.Dispose();
            SetExportBusy(false);
        }

        if (failure is not null)
        {
            await Dialogs.ShowAsync(this, failure, _loc.Get("SpeechConvertFailed"), DialogKind.Error);
            return;
        }

        if (result is not null
            && await Dialogs.ConfirmAsync(this, _loc.Get("SpeechConvertOpenFolderPrompt"), _loc.Get("SpeechConvertDone")))
        {
            try
            {
                PlatformShell.RevealInFolder(result.OutputPath);
            }
            catch (Exception ex)
            {
                await Dialogs.ShowAsync(this, ex.Message, _loc.Get("TabSpeechConvert"), DialogKind.Error);
            }
        }
    }

    private bool TryBuildPreviewSettings(
        out string text,
        out string voiceName,
        out int rate,
        out int volume,
        out string error)
    {
        text = (SpeechTextBox.Text ?? string.Empty).Trim();
        voiceName = (SpeechVoiceCombo.SelectedItem as SpeechVoiceInfo)?.Name ?? string.Empty;
        rate = (int)Math.Round(SpeechRateSlider.Value);
        volume = (int)Math.Round(SpeechVolumeSlider.Value);
        error = string.Empty;

        if (!_speechService.IsAvailable)
        {
            error = NoEngineMessage();
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = _loc.Get("SpeechConvertEnterText");
            return false;
        }

        // Some engines expose a "Default" voice with an empty id, so check the selection, not the name.
        if (SpeechVoiceCombo.SelectedItem is not SpeechVoiceInfo)
        {
            error = _loc.Get("SpeechConvertSelectVoice");
            return false;
        }

        return true;
    }

    private bool TryBuildExportSettings(out SpeechConversionSettings settings, out string error)
    {
        settings = null!;

        if (!TryBuildPreviewSettings(out var text, out var voiceName, out var rate, out var volume, out error))
            return false;

        if (!_speechService.CanExport)
        {
            error = string.Format(_loc.Get("SpeechExportUnsupported"), _speechService.EngineName);
            return false;
        }

        var outputFolder = (SpeechOutputFolderBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            error = _loc.Get("SpeechConvertSelectOutput");
            return false;
        }

        if (!Directory.Exists(outputFolder))
        {
            error = _loc.Get("SpeechConvertOutputMissing");
            return false;
        }

        var format = GetSelectedOutputFormat();
        if (format != SpeechOutputFormat.Wav && !FfmpegToolHelper.TryConfigure())
        {
            error = _loc.Get("SpeechConvertFfmpegMissing") + Environment.NewLine + ToolLocator.InstallHint;
            return false;
        }

        settings = new SpeechConversionSettings
        {
            Text = text,
            VoiceName = voiceName,
            Rate = rate,
            Volume = volume,
            OutputFormat = format,
            OutputFolder = outputFolder,
            OutputBaseName = (SpeechOutputNameBox.Text ?? string.Empty).Trim()
        };

        return true;
    }

    private SpeechOutputFormat GetSelectedOutputFormat()
    {
        if (SpeechWavRadio.IsChecked == true)
            return SpeechOutputFormat.Wav;
        if (SpeechM4aRadio.IsChecked == true)
            return SpeechOutputFormat.M4a;

        return SpeechOutputFormat.Mp3;
    }

    private static string GetFormatLabel(SpeechOutputFormat format) => format switch
    {
        SpeechOutputFormat.Wav => "WAV",
        SpeechOutputFormat.M4a => "M4A",
        _ => "MP3"
    };

    private static string BuildResultLabel(string text)
    {
        var preview = text.Length <= 48 ? text : text[..45] + "...";
        return preview.Replace('\r', ' ').Replace('\n', ' ');
    }

    private void SetSpeechResult(ConversionResultItem item)
    {
        _speechResults.Clear();
        _speechResults.Add(item);
    }

    // ---------- Busy state ----------

    private void SetPreviewBusy(bool busy)
    {
        _isPreviewing = busy;
        UpdateButtonTexts();
        UpdateButtons();
    }

    private void SetExportBusy(bool busy)
    {
        _isExporting = busy;
        SpeechProgressBar.IsVisible = busy;
        SpeechResultsProgressBar.IsVisible = busy;

        if (!busy)
        {
            SpeechProgressBar.Value = 0;
            SpeechResultsProgressBar.Value = 0;
        }

        UpdateButtonTexts();
        UpdateButtons();
    }

    private void UpdateButtonTexts()
    {
        SpeechPreviewButton.Content = _isPreviewing
            ? _loc.Get("SpeechConvertPreviewSpeaking")
            : _loc.Get("SpeechConvertPreview");
        SpeechExportButton.Content = _isExporting
            ? _loc.Get("SpeechConvertExportingStatus")
            : _loc.Get("SpeechConvertButton");
    }

    private void UpdateButtons()
    {
        var busy = _isPreviewing || _isExporting;
        var ready = _speechService.IsAvailable && !_voicesLoading && _voices.Count > 0;

        SpeechPreviewButton.IsEnabled = ready && !busy;
        SpeechExportButton.IsEnabled = ready && !busy && _speechService.CanExport;
        SpeechStopButton.IsEnabled = busy;
        SpeechRefreshVoicesButton.IsEnabled = !busy && !_voicesLoading;
        SpeechVoiceCombo.IsEnabled = !busy;
        SpeechWavRadio.IsEnabled = SpeechMp3Radio.IsEnabled = SpeechM4aRadio.IsEnabled = !_isExporting;
    }

    private void ClearSpeechResults_Click(object? sender, RoutedEventArgs e)
    {
        _speechResults.Clear();
        if (!_isPreviewing && !_isExporting)
            SpeechStatusText.Text = _loc.Get("SpeechConvertReady");
        SpeechResultsSummaryText.Text = _loc.Get("SpeechConvertNoResult");
    }
}
