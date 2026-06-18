using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DownloadMaster.Models;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster.Views;

public partial class VideoConvertorView : UserControl
{
    private readonly VideoConversionService _conversionService = new();
    private readonly VideoProbeService _probeService = new();
    private readonly ObservableCollection<ConversionResultItem> _videoResults = [];
    private CancellationTokenSource? _cancellationSource;
    private LocalizationService _loc = new();

    public VideoConvertorView()
    {
        InitializeComponent();
        WireUiEvents();
        VideoResultsList.ItemsSource = _videoResults;
        PopulateVideoFormats();
        PopulateVideoResolutions();
        UpdateVideoOperationUi();
        UpdateFfmpegStatus();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        VideoSourceTitle.Text = loc.Get("VideoConvertSource");
        VideoSourceHint.Text = loc.Get("VideoConvertSourceHint");
        VideoFileLabel.Text = loc.Get("VideoConvertFile");
        VideoBrowseInputButton.Content = loc.Get("Browse");
        VideoOperationLabel.Text = loc.Get("VideoConvertOperation");
        VideoConvertRadio.Content = loc.Get("VideoConvertModeConvert");
        VideoOptimizeRadio.Content = loc.Get("VideoConvertModeOptimize");
        VideoToGifRadio.Content = loc.Get("VideoConvertModeGif");
        VideoToFramesRadio.Content = loc.Get("VideoConvertModeFrames");
        VideoFormatLabel.Text = loc.Get("VideoConvertTargetFormat");
        GifFpsLabel.Text = loc.Get("VideoConvertGifFps");
        FrameFormatLabel.Text = loc.Get("VideoConvertFrameFormat");
        FrameExtractLabel.Text = loc.Get("VideoConvertExtractMode");
        EveryFrameRadio.Content = loc.Get("VideoConvertEveryFrame");
        TargetFpsFrameRadio.Content = loc.Get("VideoConvertTargetFps");
        VideoOutputTitle.Text = loc.Get("VideoConvertOutput");
        VideoSaveToLabel.Text = loc.Get("SavePath");
        VideoSameFolderRadio.Content = loc.Get("VideoConvertSameFolder");
        VideoCustomFolderRadio.Content = loc.Get("VideoConvertCustomFolder");
        VideoBrowseOutputButton.Content = loc.Get("Browse");
        VideoResolutionLabel.Text = loc.Get("VideoConvertResolution");
        VideoInfoTitle.Text = loc.Get("VideoConvertInfo");
        VideoSupportedLabel.Text = loc.Get("VideoConvertSupported");
        VideoSupportedText.Text = loc.Get("VideoConvertSupportedFormats");
        VideoResultsTitle.Text = loc.Get("VideoConvertResult");
        VideoClearResultsButton.Content = loc.Get("ClearList");
        VideoConvertButton.Content = loc.Get("VideoConvertButton");
        UpdateVideoOperationUi();
        UpdateFfmpegStatus();
    }

    public void RefreshFfmpegStatus() => UpdateFfmpegStatus();

    private void WireUiEvents()
    {
        VideoConvertRadio.Checked += VideoOperation_Changed;
        VideoOptimizeRadio.Checked += VideoOperation_Changed;
        VideoToGifRadio.Checked += VideoOperation_Changed;
        VideoToFramesRadio.Checked += VideoOperation_Changed;
        VideoSameFolderRadio.Checked += VideoOutput_Changed;
        VideoCustomFolderRadio.Checked += VideoOutput_Changed;
        VideoFormatCombo.SelectionChanged += VideoFormatCombo_SelectionChanged;
        EveryFrameRadio.Checked += FrameExtractMode_Changed;
        TargetFpsFrameRadio.Checked += FrameExtractMode_Changed;
        VideoInputPathBox.TextChanged += VideoInputPathBox_TextChanged;
    }

    private void FrameExtractMode_Changed(object sender, RoutedEventArgs e) =>
        FrameFpsSlider.IsEnabled = TargetFpsFrameRadio.IsChecked == true;

    private void PopulateVideoFormats()
    {
        VideoFormatCombo.ItemsSource = VideoFormatCatalog.OutputFormats;
        VideoFormatCombo.SelectedIndex = 0;
    }

    private void PopulateVideoResolutions()
    {
        VideoResolutionCombo.ItemsSource = VideoResolutionPreset.Presets;
        VideoResolutionCombo.SelectedIndex = 0;
    }

    private VideoResolutionPreset GetSelectedVideoResolution() =>
        VideoResolutionCombo.SelectedItem as VideoResolutionPreset ?? VideoResolutionPreset.Source;

    private void UpdateFfmpegStatus()
    {
        if (FfmpegToolHelper.TryConfigure())
        {
            VideoFfmpegStatus.Text = $"FFmpeg: {FfmpegToolHelper.BinaryFolder}";
            return;
        }

        VideoFfmpegStatus.Text = _loc.Get("VideoConvertFfmpegMissing");
    }

    private void VideoOperation_Changed(object sender, RoutedEventArgs e) => UpdateVideoOperationUi();

    private void VideoOutput_Changed(object sender, RoutedEventArgs e) =>
        VideoOutputFolderPanel.IsEnabled = VideoCustomFolderRadio.IsChecked == true;

    private void VideoFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoConvertRadio.IsChecked == true)
            UpdateVideoOperationUi();
    }

    private void UpdateVideoOperationUi()
    {
        var isConvert = VideoConvertRadio.IsChecked == true;
        var isOptimize = VideoOptimizeRadio.IsChecked == true;
        var isGif = VideoToGifRadio.IsChecked == true;
        var isFrames = VideoToFramesRadio.IsChecked == true;

        VideoFormatPanel.Visibility = isConvert ? Visibility.Visible : Visibility.Collapsed;
        VideoGifOptionsPanel.Visibility = isGif ? Visibility.Visible : Visibility.Collapsed;
        VideoFrameOptionsPanel.Visibility = isFrames ? Visibility.Visible : Visibility.Collapsed;
        FrameFpsSlider.IsEnabled = TargetFpsFrameRadio.IsChecked == true;

        VideoOperationHint.Text = isConvert
            ? _loc.Get("VideoConvertHintConvert")
            : isOptimize
                ? _loc.Get("VideoConvertHintOptimize")
                : isGif
                    ? _loc.Get("VideoConvertHintGif")
                    : _loc.Get("VideoConvertHintFrames");
    }

    private void VideoInputPathBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _ = RefreshVideoInfoAsync();

    private async Task RefreshVideoInfoAsync()
    {
        var path = VideoInputPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            VideoInfoTitleText.Text = _loc.Get("VideoConvertDropHint");
            VideoInfoDetails.Text = _loc.Get("VideoConvertDropDetails");
            return;
        }

        if (!VideoFormatCatalog.IsSupportedVideo(path))
        {
            VideoInfoTitleText.Text = Path.GetFileName(path);
            VideoInfoDetails.Text = _loc.Get("VideoConvertUnsupported");
            return;
        }

        VideoInfoTitleText.Text = Path.GetFileName(path);
        VideoInfoDetails.Text = _loc.Get("StatusFetching");

        try
        {
            var info = await _probeService.ProbeAsync(path);
            VideoInfoDetails.Text = info is null
                ? _loc.Get("VideoConvertProbeFailed")
                : info.Summary;
        }
        catch (Exception ex)
        {
            VideoInfoDetails.Text = ex.Message;
        }
    }

    private void BrowseVideoInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = _loc.Get("VideoConvertFile"),
            Filter = $"Videos|{VideoFormatCatalog.BuildInputFilter()}"
        };

        if (dialog.ShowDialog() == true)
        {
            VideoInputPathBox.Text = dialog.FileName;
            _ = RefreshVideoInfoAsync();
        }
    }

    private void BrowseVideoOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = _loc.Get("SavePath") };
        if (dialog.ShowDialog() == true)
            VideoOutputFolderBox.Text = dialog.FolderName;
    }

    private void Video_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        e.Effects = paths.Length == 1 && File.Exists(paths[0]) && VideoFormatCatalog.IsSupportedVideo(paths[0])
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        VideoInfoDropZone.BorderBrush = e.Effects == DragDropEffects.Copy
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("BorderBrush");

        e.Handled = true;
    }

    private void Video_DragLeave(object sender, DragEventArgs e) =>
        VideoInfoDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");

    private void Video_Drop(object sender, DragEventArgs e)
    {
        VideoInfoDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        if (paths.Length != 1 || !File.Exists(paths[0]) || !VideoFormatCatalog.IsSupportedVideo(paths[0]))
        {
            VideoStatusText.Text = _loc.Get("VideoConvertDropInvalid");
            return;
        }

        VideoInputPathBox.Text = paths[0];
        VideoStatusText.Text = $"{_loc.Get("VideoConvertLoaded")}: {Path.GetFileName(paths[0])}";
        _ = RefreshVideoInfoAsync();
    }

    private void ConvertVideo_Click(object sender, RoutedEventArgs e) => _ = ConvertVideoAsync();

    private async Task ConvertVideoAsync()
    {
        if (!TryBuildVideoSettings(out var settings, out var error))
        {
            MessageBox.Show(error, _loc.Get("TabVideoConvert"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cancellationSource?.Cancel();
        _cancellationSource = new CancellationTokenSource();

        _videoResults.Clear();
        _videoResults.Add(ConversionResultItem.Pending(settings.InputPath));

        SetVideoBusy(true);
        VideoResultsSummaryText.Text = GetProcessingSummary(settings);

        try
        {
            var progress = new Progress<double>(percent =>
            {
                VideoStatusText.Text = $"{_loc.Get("VideoConvertProcessing")} {percent:F0}%";
                VideoProgressBar.Value = percent;
                VideoResultsProgressBar.Value = percent;
                SetVideoResult(ConversionResultItem.Converting(settings.InputPath));
            });

            var result = await _conversionService.ConvertAsync(settings, progress, _cancellationSource.Token);

            SetVideoResult(ConversionResultItem.FromVideoResult(result));
            VideoStatusText.Text = result.Success ? _loc.Get("VideoConvertDone") : _loc.Get("VideoConvertFailed");
            VideoResultsSummaryText.Text = result.Success
                ? result.ExtractedFrameCount is int frames
                    ? string.Format(_loc.Get("VideoConvertFramesDone"), frames)
                    : string.Format(_loc.Get("VideoConvertSaved"), FormatBytes(Math.Max(0, result.OriginalSizeBytes - result.OutputSizeBytes)))
                : _loc.Get("VideoConvertFailed");

            if (result.Success)
            {
                var prompt = MessageBox.Show(
                    _loc.Get("VideoConvertOpenFolderPrompt"),
                    _loc.Get("VideoConvertDone"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (prompt == MessageBoxResult.Yes)
                {
                    var folder = Directory.Exists(result.OutputPath)
                        ? result.OutputPath
                        : Path.GetDirectoryName(result.OutputPath)!;
                    Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
                }

                await RefreshVideoInfoAsync();
            }
        }
        catch (OperationCanceledException)
        {
            VideoStatusText.Text = _loc.Get("StatusCancelled");
            VideoResultsSummaryText.Text = _loc.Get("VideoConvertCancelled");
        }
        catch (Exception ex)
        {
            VideoStatusText.Text = _loc.Get("VideoConvertFailed");
            VideoResultsSummaryText.Text = _loc.Get("VideoConvertFailed");
            SetVideoResult(new ConversionResultItem
            {
                SourcePath = settings.InputPath,
                FileName = Path.GetFileName(settings.InputPath),
                Status = "Failed",
                SizeChange = "—",
                Detail = ex.Message,
                IsSuccess = false
            });
            MessageBox.Show(ex.Message, _loc.Get("VideoConvertFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetVideoBusy(false);
        }
    }

    private bool TryBuildVideoSettings(out VideoConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = VideoInputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error = _loc.Get("VideoConvertSelectFile");
            return false;
        }

        if (!File.Exists(inputPath))
        {
            error = _loc.Get("VideoConvertFileMissing");
            return false;
        }

        if (!VideoFormatCatalog.IsSupportedVideo(inputPath))
        {
            error = _loc.Get("VideoConvertUnsupported");
            return false;
        }

        if (!FfmpegToolHelper.TryConfigure())
        {
            error = _loc.Get("VideoConvertFfmpegMissing");
            return false;
        }

        var useCustomOutput = VideoCustomFolderRadio.IsChecked == true;
        var outputFolder = VideoOutputFolderBox.Text.Trim();

        if (useCustomOutput && string.IsNullOrWhiteSpace(outputFolder))
        {
            error = _loc.Get("VideoConvertSelectOutput");
            return false;
        }

        if (useCustomOutput && !Directory.Exists(outputFolder))
        {
            error = _loc.Get("VideoConvertOutputMissing");
            return false;
        }

        var operationMode = GetSelectedVideoOperationMode();

        if (operationMode == VideoOperationMode.Optimize &&
            VideoFormatCatalog.ResolveFromExtension(Path.GetExtension(inputPath)) is null)
        {
            error = _loc.Get("VideoConvertOptimizeError");
            return false;
        }

        var targetFormat = VideoFormatCombo.SelectedValue is VideoFormat format
            ? format
            : VideoFormat.Mp4;

        settings = new VideoConversionSettings
        {
            InputPath = inputPath,
            OperationMode = operationMode,
            TargetFormat = targetFormat,
            OutputLocation = useCustomOutput ? VideoOutputLocation.CustomFolder : VideoOutputLocation.SameFolder,
            OutputFolder = useCustomOutput ? outputFolder : null,
            GifFps = (int)GifFpsSlider.Value,
            FrameTextureFormat = GetSelectedFrameTextureFormat(),
            FrameExtractMode = TargetFpsFrameRadio.IsChecked == true
                ? FrameExtractMode.TargetFps
                : FrameExtractMode.EveryFrame,
            FrameExtractFps = (int)FrameFpsSlider.Value,
            Resolution = GetSelectedVideoResolution()
        };

        return true;
    }

    private VideoOperationMode GetSelectedVideoOperationMode()
    {
        if (VideoOptimizeRadio.IsChecked == true) return VideoOperationMode.Optimize;
        if (VideoToGifRadio.IsChecked == true) return VideoOperationMode.ToGif;
        if (VideoToFramesRadio.IsChecked == true) return VideoOperationMode.ToFrameTextures;
        return VideoOperationMode.Convert;
    }

    private FrameTextureFormat GetSelectedFrameTextureFormat()
    {
        if (FrameFormatCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse<FrameTextureFormat>(tag, out var format))
            return format;

        return FrameTextureFormat.Png;
    }

    private string GetProcessingSummary(VideoConversionSettings settings)
    {
        var resolutionNote = settings.Resolution.IsSource
            ? string.Empty
            : $" ({settings.Resolution.Label})";

        return settings.OperationMode switch
        {
            VideoOperationMode.Optimize => _loc.Get("VideoConvertProcessingOptimize") + resolutionNote,
            VideoOperationMode.ToGif => string.Format(_loc.Get("VideoConvertProcessingGif"), settings.GifFps) + resolutionNote,
            VideoOperationMode.ToFrameTextures => _loc.Get("VideoConvertProcessingFrames") + resolutionNote,
            _ => string.Format(_loc.Get("VideoConvertProcessingConvert"),
                VideoFormatCatalog.GetDefinition(settings.TargetFormat).DisplayName) + resolutionNote
        };
    }

    private void SetVideoResult(ConversionResultItem item)
    {
        _videoResults.Clear();
        _videoResults.Add(item);
    }

    private void SetVideoBusy(bool busy)
    {
        VideoConvertButton.IsEnabled = !busy;
        VideoConvertButton.Content = busy ? _loc.Get("VideoConvertProcessing") : _loc.Get("VideoConvertButton");
        VideoProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        VideoResultsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (!busy)
        {
            VideoProgressBar.Value = 0;
            VideoResultsProgressBar.Value = 0;
        }
    }

    private void ClearVideoResults_Click(object sender, RoutedEventArgs e)
    {
        _videoResults.Clear();
        VideoStatusText.Text = _loc.Get("VideoConvertReady");
        VideoResultsSummaryText.Text = _loc.Get("VideoConvertNoResult");
    }

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);
}
