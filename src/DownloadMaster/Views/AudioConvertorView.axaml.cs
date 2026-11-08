using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster.Views;

/// <summary>
/// Audio Convert tab: format chip bar (mp3, wav, iPhone ringtone, m4a, flac, ogg, mp2, amr), Convert / Optimize,
/// quality presets, probe info, progress, cancel, result summary and drag-and-drop.
/// </summary>
public partial class AudioConvertorView : UserControl
{
    private readonly AudioConversionService _conversionService = new();
    private readonly AudioProbeService _probeService = new();
    private readonly ObservableCollection<ConversionResultItem> _audioResults = [];
    private readonly List<RadioButton> _formatRadios = [];
    private CancellationTokenSource? _cancellationSource;
    private CancellationTokenSource? _probeCancellation;
    private int _probeVersion;
    private bool _busy;
    private LocalizationService _loc = new();

    public AudioConvertorView()
    {
        InitializeComponent();
        AudioResultsList.ItemsSource = _audioResults;
        PopulateAudioFormats();
        PopulateAudioQuality();
        WireUiEvents();
        UpdateAudioOperationUi();
        UpdateFfmpegStatus();

        // Do not leave ffmpeg running when the app closes mid-conversion.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) => _cancellationSource?.Cancel();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        AudioFormatLabel.Text = loc.Get("AudioConvertTargetFormat");
        AudioSourceTitle.Text = loc.Get("AudioConvertSource");
        AudioSourceHint.Text = loc.Get("AudioConvertSourceHint");
        AudioFileLabel.Text = loc.Get("AudioConvertFile");
        AudioBrowseInputButton.Content = loc.Get("Browse");
        AudioOperationLabel.Text = loc.Get("AudioConvertOperation");
        AudioConvertRadioText.Text = loc.Get("AudioConvertModeConvert");
        AudioOptimizeRadioText.Text = loc.Get("AudioConvertModeOptimizeOverwrite");
        AudioOutputTitle.Text = loc.Get("AudioConvertOutput");
        AudioQualityLabel.Text = loc.Get("AudioConvertQuality");
        AudioSaveToLabel.Text = loc.Get("SaveTo");
        AudioSameFolderRadioText.Text = loc.Get("AudioConvertSameFolder");
        AudioCustomFolderRadioText.Text = loc.Get("AudioConvertCustomFolder");
        AudioBrowseOutputButton.Content = loc.Get("Browse");
        AudioInfoTitle.Text = loc.Get("AudioConvertInfo");
        AudioSupportedLabel.Text = loc.Get("AudioConvertSupported");
        AudioSupportedText.Text = loc.Get("AudioConvertSupportedFormats");
        AudioResultsTitle.Text = loc.Get("AudioConvertResult");
        AudioResultStatusCol.Text = loc.Get("ResultColStatus");
        AudioResultFileCol.Text = loc.Get("ResultColFile");
        AudioResultSizeCol.Text = loc.Get("ResultColSizeChange");
        AudioResultDetailsCol.Text = loc.Get("ResultColDetails");
        AudioClearResultsButton.Content = loc.Get("ClearList");
        AudioCancelButton.Content = loc.Get("Cancel");
        AudioConvertButton.Content = _busy ? loc.Get("AudioConvertProcessing") : loc.Get("AudioConvertButton");

        if (!_busy && _audioResults.Count == 0)
        {
            AudioStatusText.Text = loc.Get("AudioConvertReady");
            AudioResultsSummaryText.Text = loc.Get("AudioConvertNoResult");
        }

        if (!HasExistingInput())
            ShowDefaultInfo();

        UpdateAudioOperationUi();
        UpdateFfmpegStatus();
    }

    public void RefreshFfmpegStatus() => UpdateFfmpegStatus();

    private void WireUiEvents()
    {
        AudioConvertRadio.IsCheckedChanged += (_, _) => UpdateAudioOperationUi();
        AudioOptimizeRadio.IsCheckedChanged += (_, _) => UpdateAudioOperationUi();
        AudioCustomFolderRadio.IsCheckedChanged += (_, _) =>
            AudioOutputFolderPanel.IsEnabled = AudioCustomFolderRadio.IsChecked == true;

        AudioBrowseInputButton.Click += async (_, _) => await BrowseAudioInputAsync();
        AudioBrowseOutputButton.Click += async (_, _) => await BrowseAudioOutputAsync();
        AudioConvertButton.Click += async (_, _) => await ConvertAudioAsync();
        AudioCancelButton.Click += (_, _) => CancelConversion();
        AudioClearResultsButton.Click += (_, _) => ClearResults();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void PopulateAudioFormats()
    {
        foreach (var definition in AudioFormatCatalog.OutputFormats)
        {
            var radio = new RadioButton
            {
                Content = definition.ButtonLabel,
                GroupName = "AudioTargetFormat",
                Tag = definition.Format,
                IsChecked = definition.Format == AudioFormat.Mp3
            };
            radio.Classes.Add("chip");
            ToolTip.SetTip(radio, definition.DisplayName);
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    UpdateAudioOperationUi();
            };

            _formatRadios.Add(radio);
            AudioFormatPanel.Children.Add(radio);
        }
    }

    private void PopulateAudioQuality()
    {
        AudioQualityCombo.ItemsSource = AudioQualityPreset.Presets;
        AudioQualityCombo.SelectedItem = AudioQualityPreset.Standard;
    }

    private void UpdateFfmpegStatus()
    {
        AudioFfmpegStatus.Text = FfmpegToolHelper.TryConfigure()
            ? string.Format(_loc.Get("MediaFfmpegFound"), FfmpegToolHelper.BinaryFolder)
            : FfmpegMissingMessage();
    }

    private string FfmpegMissingMessage() =>
        string.Format(_loc.Get("MediaFfmpegMissing"), ToolLocator.InstallHint);

    private void UpdateAudioOperationUi()
    {
        var isConvert = AudioConvertRadio.IsChecked == true;

        foreach (var radio in _formatRadios)
            radio.IsEnabled = isConvert;

        // Optimize keeps the source format, so describe that one.
        var definition = GetEffectiveFormatDefinition();

        AudioQualityCombo.IsEnabled = definition.UsesQuality;
        AudioQualityHint.Text = definition.UsesQuality
            ? _loc.Get("AudioConvertQualityHint")
            : string.Format(_loc.Get("AudioConvertQualityLossless"), definition.DisplayName);

        var note = definition.Format switch
        {
            AudioFormat.IPhoneRingtone => _loc.Get("AudioConvertRingtoneNote"),
            AudioFormat.Amr => _loc.Get("AudioConvertAmrNote"),
            _ => string.Empty
        };
        AudioFormatNote.Text = note;
        AudioFormatNote.IsVisible = note.Length > 0;

        AudioOperationHint.Text = isConvert
            ? _loc.Get("AudioConvertHintConvert")
            : _loc.Get("AudioConvertHintOptimize");
    }

    private AudioFormatDefinition GetEffectiveFormatDefinition()
    {
        if (AudioOptimizeRadio.IsChecked == true)
        {
            var source = AudioInputPathBox?.Text;
            if (!string.IsNullOrWhiteSpace(source) &&
                AudioFormatCatalog.ResolveFromExtension(Path.GetExtension(source)) is { } sourceDefinition)
                return sourceDefinition;
        }

        return AudioFormatCatalog.GetDefinition(GetSelectedAudioFormat());
    }

    private bool HasExistingInput()
    {
        var path = AudioInputPathBox.Text?.Trim();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private void ShowDefaultInfo()
    {
        AudioInfoTitleText.Text = _loc.Get("AudioConvertDropHint");
        AudioInfoDetails.Text = _loc.Get("AudioConvertDropDetails");
    }

    private void SetInputPath(string path)
    {
        AudioInputPathBox.Text = path;
        UpdateAudioOperationUi();
        _ = RefreshAudioInfoAsync();
    }

    private async Task RefreshAudioInfoAsync()
    {
        _probeCancellation?.Cancel();
        var cancellation = _probeCancellation = new CancellationTokenSource();
        var version = ++_probeVersion;

        var path = AudioInputPathBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowDefaultInfo();
            return;
        }

        AudioInfoTitleText.Text = Path.GetFileName(path);

        if (!AudioFormatCatalog.IsSupportedAudio(path))
        {
            AudioInfoDetails.Text = _loc.Get("AudioConvertUnsupported");
            return;
        }

        if (!FfmpegToolHelper.TryConfigure())
        {
            AudioInfoDetails.Text = FfmpegMissingMessage();
            return;
        }

        AudioInfoDetails.Text = _loc.Get("AudioConvertAnalyzing");

        try
        {
            var info = await _probeService.ProbeAsync(path, cancellation.Token);
            if (version != _probeVersion)
                return;

            AudioInfoDetails.Text = info is null ? _loc.Get("AudioConvertProbeFailed") : info.Summary;
        }
        catch (OperationCanceledException)
        {
            // a newer file was selected
        }
        catch (Exception ex)
        {
            if (version == _probeVersion)
                AudioInfoDetails.Text = ex.Message;
        }
    }

    private async Task BrowseAudioInputAsync()
    {
        var path = await Pickers.PickFileAsync(
            this,
            _loc.Get("AudioConvertSelectFileTitle"),
            [
                Pickers.Filter(_loc.Get("AudioConvertFilesFilter"), [.. AudioFormatCatalog.InputExtensions]),
                Pickers.AllFiles(_loc.Get("MediaAllFiles"))
            ],
            AudioInputPathBox.Text);

        if (!string.IsNullOrWhiteSpace(path))
            SetInputPath(path);
    }

    private async Task BrowseAudioOutputAsync()
    {
        var initial = string.IsNullOrWhiteSpace(AudioOutputFolderBox.Text)
            ? AudioInputPathBox.Text
            : AudioOutputFolderBox.Text;

        var folder = await Pickers.PickFolderAsync(this, _loc.Get("MediaSelectOutputFolder"), initial);
        if (!string.IsNullOrWhiteSpace(folder))
            AudioOutputFolderBox.Text = folder;
    }

    private static string? GetDroppedAudio(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is not { Length: 1 })
            return null;

        var path = files[0].TryGetLocalPath();
        return path is not null && File.Exists(path) && AudioFormatCatalog.IsSupportedAudio(path) ? path : null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var valid = !_busy && GetDroppedAudio(e) is not null;
        e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        AudioInfoDropZone.Classes.Set("dragover", valid);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        AudioInfoDropZone.Classes.Set("dragover", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        AudioInfoDropZone.Classes.Set("dragover", false);
        e.Handled = true;

        if (_busy)
            return;

        var path = GetDroppedAudio(e);
        if (path is null)
        {
            AudioStatusText.Text = _loc.Get("AudioConvertDropInvalid");
            return;
        }

        SetInputPath(path);
        AudioStatusText.Text = $"{_loc.Get("AudioConvertLoaded")}: {Path.GetFileName(path)}";
    }

    private async Task ConvertAudioAsync()
    {
        if (_busy)
            return;

        if (!TryBuildAudioSettings(out var settings, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("TabAudioConvert"), DialogKind.Warning);
            return;
        }

        _cancellationSource?.Dispose();
        _cancellationSource = new CancellationTokenSource();
        var token = _cancellationSource.Token;

        SetAudioResult(ConversionResultItem.Pending(settings.InputPath));
        SetAudioBusy(true);
        AudioResultsSummaryText.Text = GetProcessingSummary(settings);
        AudioStatusText.Text = _loc.Get("AudioConvertProcessing");

        var reportedConverting = false;
        var progress = new Progress<double>(percent =>
        {
            if (!_busy || token.IsCancellationRequested)
                return;

            AudioStatusText.Text = $"{_loc.Get("AudioConvertProcessing")} {percent:F0}%";
            AudioProgressBar.Value = percent;
            AudioResultsProgressBar.Value = percent;

            if (!reportedConverting)
            {
                reportedConverting = true;
                SetAudioResult(ConversionResultItem.Converting(settings.InputPath));
            }
        });

        AudioConversionResult? result = null;
        try
        {
            result = await _conversionService.ConvertAsync(settings, progress, token);

            SetAudioResult(ConversionResultItem.FromAudioResult(result));
            AudioStatusText.Text = result.Success ? _loc.Get("AudioConvertDone") : _loc.Get("AudioConvertFailed");
            AudioResultsSummaryText.Text = result.Success
                ? BuildFinishedSummary(result)
                : _loc.Get("AudioConvertFailed");
        }
        catch (OperationCanceledException)
        {
            AudioStatusText.Text = _loc.Get("StatusCancelled");
            AudioResultsSummaryText.Text = _loc.Get("AudioConvertCancelled");
            _audioResults.Clear();
        }
        catch (Exception ex)
        {
            AudioStatusText.Text = _loc.Get("AudioConvertFailed");
            AudioResultsSummaryText.Text = _loc.Get("AudioConvertFailed");
            SetAudioResult(new ConversionResultItem
            {
                SourcePath = settings.InputPath,
                FileName = Path.GetFileName(settings.InputPath),
                Status = "Failed",
                SizeChange = "—",
                Detail = ex.Message,
                IsSuccess = false
            });
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("AudioConvertFailedTitle"), DialogKind.Error);
        }
        finally
        {
            SetAudioBusy(false);
        }

        if (result is { Success: true })
        {
            if (await Dialogs.ConfirmAsync(this, _loc.Get("AudioConvertOpenFolderPrompt"), _loc.Get("AudioConvertDone")))
            {
                try
                {
                    PlatformShell.RevealInFolder(result.OutputPath);
                }
                catch
                {
                    // no file manager available (e.g. minimal Linux desktop)
                }
            }

            await RefreshAudioInfoAsync();
        }
    }

    private void CancelConversion()
    {
        if (!_busy || _cancellationSource is null)
            return;

        AudioCancelButton.IsEnabled = false;
        AudioStatusText.Text = _loc.Get("MediaCancelling");
        _cancellationSource.Cancel();
    }

    private string BuildFinishedSummary(AudioConversionResult result)
    {
        var saved = result.OriginalSizeBytes - result.OutputSizeBytes;
        return saved > 0
            ? string.Format(_loc.Get("AudioConvertSaved"), FormatHelpers.FormatBytes(saved))
            : string.Format(
                _loc.Get("MediaFinishedSize"),
                FormatHelpers.FormatBytes(result.OriginalSizeBytes),
                FormatHelpers.FormatBytes(result.OutputSizeBytes));
    }

    private bool TryBuildAudioSettings(out AudioConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = AudioInputPathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error = _loc.Get("AudioConvertSelectFile");
            return false;
        }

        if (!File.Exists(inputPath))
        {
            error = _loc.Get("AudioConvertFileMissing");
            return false;
        }

        if (!AudioFormatCatalog.IsSupportedAudio(inputPath))
        {
            error = _loc.Get("AudioConvertUnsupported");
            return false;
        }

        if (!FfmpegToolHelper.TryConfigure())
        {
            error = FfmpegMissingMessage();
            return false;
        }

        var useCustomOutput = AudioCustomFolderRadio.IsChecked == true;
        var outputFolder = AudioOutputFolderBox.Text?.Trim() ?? string.Empty;

        if (useCustomOutput && string.IsNullOrWhiteSpace(outputFolder))
        {
            error = _loc.Get("AudioConvertSelectOutput");
            return false;
        }

        if (useCustomOutput && !Directory.Exists(outputFolder))
        {
            error = _loc.Get("AudioConvertOutputMissing");
            return false;
        }

        var operationMode = AudioOptimizeRadio.IsChecked == true
            ? AudioOperationMode.Optimize
            : AudioOperationMode.Convert;

        if (operationMode == AudioOperationMode.Optimize &&
            AudioFormatCatalog.ResolveFromExtension(Path.GetExtension(inputPath)) is null)
        {
            error = _loc.Get("AudioConvertOptimizeError");
            return false;
        }

        settings = new AudioConversionSettings
        {
            InputPath = inputPath,
            OperationMode = operationMode,
            TargetFormat = GetSelectedAudioFormat(),
            OutputLocation = useCustomOutput ? AudioOutputLocation.CustomFolder : AudioOutputLocation.SameFolder,
            OutputFolder = useCustomOutput ? outputFolder : null,
            Quality = AudioQualityCombo.SelectedItem as AudioQualityPreset ?? AudioQualityPreset.Standard
        };

        return true;
    }

    private AudioFormat GetSelectedAudioFormat()
    {
        foreach (var radio in _formatRadios)
        {
            if (radio.IsChecked == true && radio.Tag is AudioFormat format)
                return format;
        }

        return AudioFormat.Mp3;
    }

    private string GetProcessingSummary(AudioConversionSettings settings)
    {
        var definition = settings.OperationMode == AudioOperationMode.Optimize
            ? AudioFormatCatalog.ResolveFromExtension(Path.GetExtension(settings.InputPath)) ??
              AudioFormatCatalog.GetDefinition(settings.TargetFormat)
            : AudioFormatCatalog.GetDefinition(settings.TargetFormat);

        var qualityNote = definition.UsesQuality
            ? string.Format(_loc.Get("MediaSizeNote"), settings.Quality.Label, settings.Quality.BitrateText)
            : string.Empty;

        return settings.OperationMode == AudioOperationMode.Optimize
            ? string.Format(_loc.Get("AudioConvertSummaryOptimize"), definition.DisplayName, qualityNote)
            : string.Format(_loc.Get("AudioConvertSummaryConvert"), definition.DisplayName, qualityNote);
    }

    private void SetAudioResult(ConversionResultItem item)
    {
        _audioResults.Clear();
        _audioResults.Add(item);
    }

    private void SetAudioBusy(bool busy)
    {
        _busy = busy;
        AudioConvertButton.IsEnabled = !busy;
        AudioConvertButton.Content = busy ? _loc.Get("AudioConvertProcessing") : _loc.Get("AudioConvertButton");
        AudioCancelButton.IsVisible = busy;
        AudioCancelButton.IsEnabled = busy;
        AudioClearResultsButton.IsEnabled = !busy;
        AudioBrowseInputButton.IsEnabled = !busy;
        AudioProgressBar.IsVisible = busy;
        AudioResultsProgressBar.IsVisible = busy;
        AudioProgressBar.Value = 0;
        AudioResultsProgressBar.Value = 0;
    }

    private void ClearResults()
    {
        _audioResults.Clear();
        AudioStatusText.Text = _loc.Get("AudioConvertReady");
        AudioResultsSummaryText.Text = _loc.Get("AudioConvertNoResult");
    }
}
