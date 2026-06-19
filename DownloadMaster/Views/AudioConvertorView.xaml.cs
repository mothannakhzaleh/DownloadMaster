using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DownloadMaster.Models;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster.Views;

public partial class AudioConvertorView : UserControl
{
    private readonly AudioConversionService _conversionService = new();
    private readonly AudioProbeService _probeService = new();
    private readonly ObservableCollection<ConversionResultItem> _audioResults = [];
    private CancellationTokenSource? _cancellationSource;
    private LocalizationService _loc = new();

    public AudioConvertorView()
    {
        InitializeComponent();
        WireUiEvents();
        AudioResultsList.ItemsSource = _audioResults;
        PopulateAudioFormats();
        PopulateAudioQuality();
        UpdateAudioOperationUi();
        UpdateFfmpegStatus();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        AudioSourceTitle.Text = loc.Get("AudioConvertSource");
        AudioSourceHint.Text = loc.Get("AudioConvertSourceHint");
        AudioFileLabel.Text = loc.Get("AudioConvertFile");
        AudioBrowseInputButton.Content = loc.Get("Browse");
        AudioOperationLabel.Text = loc.Get("AudioConvertOperation");
        AudioConvertRadio.Content = loc.Get("AudioConvertModeConvert");
        AudioOptimizeRadio.Content = loc.Get("AudioConvertModeOptimize");
        AudioFormatLabel.Text = loc.Get("AudioConvertTargetFormat");
        AudioOutputTitle.Text = loc.Get("AudioConvertOutput");
        AudioQualityLabel.Text = loc.Get("AudioConvertQuality");
        AudioSaveToLabel.Text = loc.Get("SavePath");
        AudioSameFolderRadio.Content = loc.Get("AudioConvertSameFolder");
        AudioCustomFolderRadio.Content = loc.Get("AudioConvertCustomFolder");
        AudioBrowseOutputButton.Content = loc.Get("Browse");
        AudioInfoTitle.Text = loc.Get("AudioConvertInfo");
        AudioInfoTitleText.Text = loc.Get("AudioConvertDropHint");
        AudioInfoDetails.Text = loc.Get("AudioConvertDropDetails");
        AudioSupportedLabel.Text = loc.Get("AudioConvertSupported");
        AudioSupportedText.Text = loc.Get("AudioConvertSupportedFormats");
        AudioResultsTitle.Text = loc.Get("AudioConvertResult");
        AudioClearResultsButton.Content = loc.Get("ClearList");
        AudioConvertButton.Content = loc.Get("AudioConvertButton");
        AudioResultStatusCol.Header = loc.Get("ResultColStatus");
        AudioResultFileCol.Header = loc.Get("ResultColFile");
        AudioResultSizeCol.Header = loc.Get("ResultColSizeChange");
        AudioResultDetailsCol.Header = loc.Get("ResultColDetails");
        UpdateAudioOperationUi();
        UpdateFfmpegStatus();
    }

    public void RefreshFfmpegStatus() => UpdateFfmpegStatus();

    private void WireUiEvents()
    {
        AudioConvertRadio.Checked += AudioOperation_Changed;
        AudioOptimizeRadio.Checked += AudioOperation_Changed;
        AudioSameFolderRadio.Checked += AudioOutput_Changed;
        AudioCustomFolderRadio.Checked += AudioOutput_Changed;
        AudioInputPathBox.TextChanged += AudioInputPathBox_TextChanged;
        AudioFormatCombo.SelectionChanged += AudioFormat_Changed;
    }

    private void PopulateAudioFormats()
    {
        AudioFormatCombo.ItemsSource = AudioFormatCatalog.OutputFormats;
        AudioFormatCombo.SelectedIndex = 0;
    }

    private void PopulateAudioQuality()
    {
        AudioQualityCombo.ItemsSource = AudioQualityPreset.Presets;
        AudioQualityCombo.SelectedItem = AudioQualityPreset.Standard;
    }

    private void UpdateFfmpegStatus()
    {
        if (FfmpegToolHelper.TryConfigure())
        {
            AudioFfmpegStatus.Text = $"FFmpeg: {FfmpegToolHelper.BinaryFolder}";
            return;
        }

        AudioFfmpegStatus.Text = _loc.Get("AudioConvertFfmpegMissing");
    }

    private void AudioOperation_Changed(object sender, RoutedEventArgs e) => UpdateAudioOperationUi();

    private void AudioFormat_Changed(object sender, SelectionChangedEventArgs e) => UpdateAudioOperationUi();

    private void AudioOutput_Changed(object sender, RoutedEventArgs e) =>
        AudioOutputFolderPanel.IsEnabled = AudioCustomFolderRadio.IsChecked == true;

    private void UpdateAudioOperationUi()
    {
        var isConvert = AudioConvertRadio.IsChecked == true;
        var isOptimize = AudioOptimizeRadio.IsChecked == true;
        var definition = GetSelectedFormatDefinition();

        AudioFormatPanel.IsEnabled = isConvert;
        AudioQualityCombo.IsEnabled = definition.UsesQuality;
        AudioQualityHint.Text = definition.UsesQuality
            ? _loc.Get("AudioConvertQualityHint")
            : string.Format(_loc.Get("AudioConvertQualityLossless"), definition.DisplayName);

        AudioOperationHint.Text = isConvert
            ? _loc.Get("AudioConvertHintConvert")
            : isOptimize
                ? _loc.Get("AudioConvertHintOptimize")
                : string.Empty;
    }

    private void AudioInputPathBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _ = RefreshAudioInfoAsync();

    private async Task RefreshAudioInfoAsync()
    {
        var path = AudioInputPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AudioInfoTitleText.Text = _loc.Get("AudioConvertDropHint");
            AudioInfoDetails.Text = _loc.Get("AudioConvertDropDetails");
            return;
        }

        if (!AudioFormatCatalog.IsSupportedAudio(path))
        {
            AudioInfoTitleText.Text = Path.GetFileName(path);
            AudioInfoDetails.Text = _loc.Get("AudioConvertUnsupported");
            return;
        }

        AudioInfoTitleText.Text = Path.GetFileName(path);
        AudioInfoDetails.Text = _loc.Get("AudioConvertAnalyzing");

        try
        {
            var info = await _probeService.ProbeAsync(path);
            if (info is null)
            {
                AudioInfoDetails.Text = _loc.Get("AudioConvertProbeFailed");
                return;
            }

            AudioInfoDetails.Text = info.Summary;
        }
        catch (Exception ex)
        {
            AudioInfoDetails.Text = ex.Message;
        }
    }

    private void BrowseAudioInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = _loc.Get("AudioConvertSelectFileTitle"),
            Filter = AudioFormatCatalog.BuildInputFilter()
        };

        if (dialog.ShowDialog() == true)
        {
            AudioInputPathBox.Text = dialog.FileName;
            _ = RefreshAudioInfoAsync();
        }
    }

    private void BrowseAudioOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = _loc.Get("SavePath") };
        if (dialog.ShowDialog() == true)
            AudioOutputFolderBox.Text = dialog.FolderName;
    }

    private void Audio_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        e.Effects = paths.Length == 1 && File.Exists(paths[0]) && AudioFormatCatalog.IsSupportedAudio(paths[0])
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        AudioInfoDropZone.BorderBrush = e.Effects == DragDropEffects.Copy
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("BorderBrush");

        e.Handled = true;
    }

    private void Audio_DragLeave(object sender, DragEventArgs e)
    {
        AudioInfoDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
    }

    private void Audio_Drop(object sender, DragEventArgs e)
    {
        AudioInfoDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        if (paths.Length != 1 || !File.Exists(paths[0]) || !AudioFormatCatalog.IsSupportedAudio(paths[0]))
        {
            AudioStatusText.Text = _loc.Get("AudioConvertDropInvalid");
            return;
        }

        AudioInputPathBox.Text = paths[0];
        AudioStatusText.Text = $"{_loc.Get("AudioConvertLoaded")}: {paths[0]}";
        _ = RefreshAudioInfoAsync();
    }

    private void ConvertAudio_Click(object sender, RoutedEventArgs e) => _ = ConvertAudioAsync();

    private async Task ConvertAudioAsync()
    {
        if (!TryBuildAudioSettings(out var settings, out var error))
        {
            MessageBox.Show(error, _loc.Get("TabAudioConvert"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cancellationSource?.Cancel();
        _cancellationSource = new CancellationTokenSource();

        _audioResults.Clear();
        _audioResults.Add(ConversionResultItem.Pending(settings.InputPath));

        SetAudioBusy(true);
        AudioResultsSummaryText.Text = GetProcessingSummary(settings);

        try
        {
            var progress = new Progress<double>(percent =>
            {
                AudioStatusText.Text = $"{_loc.Get("AudioConvertProcessing")} {percent:F0}%";
                AudioProgressBar.Value = percent;
                AudioResultsProgressBar.Value = percent;
                SetAudioResult(ConversionResultItem.Converting(settings.InputPath));
            });

            var result = await _conversionService.ConvertAsync(
                settings,
                progress,
                _cancellationSource.Token);

            SetAudioResult(ConversionResultItem.FromAudioResult(result));
            AudioStatusText.Text = result.Success ? _loc.Get("AudioConvertDone") : _loc.Get("AudioConvertFailed");
            AudioResultsSummaryText.Text = result.Success
                ? string.Format(_loc.Get("AudioConvertSaved"), FormatBytes(Math.Max(0, result.OriginalSizeBytes - result.OutputSizeBytes)))
                : _loc.Get("AudioConvertFailed");

            if (result.Success)
            {
                var prompt = MessageBox.Show(
                    _loc.Get("AudioConvertOpenFolderPrompt"),
                    _loc.Get("AudioConvertDone"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (prompt == MessageBoxResult.Yes)
                {
                    var folder = Path.GetDirectoryName(result.OutputPath)!;
                    Process.Start("explorer.exe", folder);
                }

                await RefreshAudioInfoAsync();
            }
        }
        catch (OperationCanceledException)
        {
            AudioStatusText.Text = _loc.Get("AudioConvertCancelled");
            AudioResultsSummaryText.Text = _loc.Get("AudioConvertCancelled");
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
            MessageBox.Show(ex.Message, _loc.Get("AudioConvertFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetAudioBusy(false);
        }
    }

    private bool TryBuildAudioSettings(out AudioConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = AudioInputPathBox.Text.Trim();
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
            error = _loc.Get("AudioConvertFfmpegMissing");
            return false;
        }

        var useCustomOutput = AudioCustomFolderRadio.IsChecked == true;
        var outputFolder = AudioOutputFolderBox.Text.Trim();

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
            Quality = GetSelectedAudioQuality()
        };

        return true;
    }

    private AudioFormat GetSelectedAudioFormat()
    {
        if (AudioFormatCombo.SelectedItem is AudioFormatDefinition definition)
            return definition.Format;

        return AudioFormat.Mp3;
    }

    private AudioFormatDefinition GetSelectedFormatDefinition() =>
        AudioFormatCatalog.GetDefinition(GetSelectedAudioFormat());

    private AudioQualityPreset GetSelectedAudioQuality()
    {
        if (AudioQualityCombo.SelectedItem is AudioQualityPreset preset)
            return preset;

        return AudioQualityPreset.Standard;
    }

    private string GetProcessingSummary(AudioConversionSettings settings)
    {
        var definition = settings.OperationMode == AudioOperationMode.Optimize
            ? AudioFormatCatalog.ResolveFromExtension(Path.GetExtension(settings.InputPath)) ??
              AudioFormatCatalog.GetDefinition(settings.TargetFormat)
            : AudioFormatCatalog.GetDefinition(settings.TargetFormat);

        var qualityNote = definition.UsesQuality
            ? $" at {settings.Quality.Label} ({settings.Quality.BitrateText})"
            : string.Empty;

        return settings.OperationMode == AudioOperationMode.Optimize
            ? string.Format(_loc.Get("AudioConvertProcessingOptimize"), definition.DisplayName) + qualityNote + "..."
            : string.Format(_loc.Get("AudioConvertProcessingConvert"), definition.DisplayName) + qualityNote + "...";
    }

    private void SetAudioResult(ConversionResultItem item)
    {
        _audioResults.Clear();
        _audioResults.Add(item);
    }

    private void SetAudioBusy(bool busy)
    {
        AudioConvertButton.IsEnabled = !busy;
        AudioConvertButton.Content = busy ? _loc.Get("AudioConvertProcessing") : _loc.Get("AudioConvertButton");
        AudioProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AudioResultsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (!busy)
        {
            AudioProgressBar.Value = 0;
            AudioResultsProgressBar.Value = 0;
        }
    }

    private void ClearAudioResults_Click(object sender, RoutedEventArgs e)
    {
        _audioResults.Clear();
        AudioStatusText.Text = _loc.Get("AudioConvertReady");
        AudioResultsSummaryText.Text = _loc.Get("AudioConvertNoResult");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
