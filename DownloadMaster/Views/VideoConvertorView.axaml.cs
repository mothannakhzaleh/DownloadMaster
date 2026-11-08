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
/// Video Convert tab: Convert / Optimize / To GIF / To frame textures with resolution presets,
/// probe info, progress, cancel, result summary and drag-and-drop.
/// </summary>
public partial class VideoConvertorView : UserControl
{
    private readonly VideoConversionService _conversionService = new();
    private readonly VideoProbeService _probeService = new();
    private readonly ObservableCollection<ConversionResultItem> _videoResults = [];
    private CancellationTokenSource? _cancellationSource;
    private CancellationTokenSource? _probeCancellation;
    private int _probeVersion;
    private bool _busy;
    private LocalizationService _loc = new();

    public VideoConvertorView()
    {
        InitializeComponent();
        VideoResultsList.ItemsSource = _videoResults;
        PopulateVideoFormats();
        PopulateVideoResolutions();
        WireUiEvents();
        UpdateVideoOperationUi();
        UpdateFfmpegStatus();

        // Do not leave ffmpeg running when the app closes mid-conversion.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) => _cancellationSource?.Cancel();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        VideoSourceTitle.Text = loc.Get("VideoConvertSource");
        VideoSourceHint.Text = loc.Get("VideoConvertSourceHint");
        VideoFileLabel.Text = loc.Get("VideoConvertFile");
        VideoBrowseInputButton.Content = loc.Get("Browse");
        VideoOperationLabel.Text = loc.Get("VideoConvertOperation");
        VideoConvertRadioText.Text = loc.Get("VideoConvertModeConvert");
        VideoOptimizeRadioText.Text = loc.Get("VideoConvertModeOptimizeOverwrite");
        VideoToGifRadioText.Text = loc.Get("VideoConvertModeGif");
        VideoToFramesRadioText.Text = loc.Get("VideoConvertModeFrames");
        VideoFormatLabel.Text = loc.Get("VideoConvertTargetFormat");
        GifFpsLabel.Text = loc.Get("VideoConvertGifFps");
        FrameFormatLabel.Text = loc.Get("VideoConvertFrameFormat");
        FramePngItem.Content = loc.Get("VideoConvertFramePng");
        FrameExtractLabel.Text = loc.Get("VideoConvertExtractMode");
        EveryFrameRadioText.Text = loc.Get("VideoConvertEveryFrame");
        TargetFpsFrameRadioText.Text = loc.Get("VideoConvertTargetFps");
        FramesHintText.Text = loc.Get("VideoConvertFramesHint");
        VideoOutputTitle.Text = loc.Get("VideoConvertOutput");
        VideoSaveToLabel.Text = loc.Get("SaveTo");
        VideoSameFolderRadioText.Text = loc.Get("VideoConvertSameFolder");
        VideoCustomFolderRadioText.Text = loc.Get("VideoConvertCustomFolder");
        VideoBrowseOutputButton.Content = loc.Get("Browse");
        VideoResolutionLabel.Text = loc.Get("VideoConvertResolution");
        VideoInfoTitle.Text = loc.Get("VideoConvertInfo");
        VideoSupportedLabel.Text = loc.Get("VideoConvertSupported");
        VideoSupportedText.Text = loc.Get("VideoConvertSupportedFormats");
        VideoResultsTitle.Text = loc.Get("VideoConvertResult");
        VideoResultStatusCol.Text = loc.Get("ResultColStatus");
        VideoResultFileCol.Text = loc.Get("ResultColFile");
        VideoResultSizeCol.Text = loc.Get("ResultColSizeChange");
        VideoResultDetailsCol.Text = loc.Get("ResultColDetails");
        VideoClearResultsButton.Content = loc.Get("ClearList");
        VideoCancelButton.Content = loc.Get("Cancel");
        VideoConvertButton.Content = _busy ? loc.Get("VideoConvertProcessing") : loc.Get("VideoConvertButton");

        if (!_busy && _videoResults.Count == 0)
        {
            VideoStatusText.Text = loc.Get("VideoConvertReady");
            VideoResultsSummaryText.Text = loc.Get("VideoConvertNoResult");
        }

        if (!HasExistingInput())
            ShowDefaultInfo();

        UpdateVideoOperationUi();
        UpdateFfmpegStatus();
    }

    public void RefreshFfmpegStatus() => UpdateFfmpegStatus();

    private void WireUiEvents()
    {
        VideoConvertRadio.IsCheckedChanged += (_, _) => UpdateVideoOperationUi();
        VideoOptimizeRadio.IsCheckedChanged += (_, _) => UpdateVideoOperationUi();
        VideoToGifRadio.IsCheckedChanged += (_, _) => UpdateVideoOperationUi();
        VideoToFramesRadio.IsCheckedChanged += (_, _) => UpdateVideoOperationUi();
        EveryFrameRadio.IsCheckedChanged += (_, _) => UpdateFrameFpsEnabled();
        TargetFpsFrameRadio.IsCheckedChanged += (_, _) => UpdateFrameFpsEnabled();
        VideoCustomFolderRadio.IsCheckedChanged += (_, _) =>
            VideoOutputFolderPanel.IsEnabled = VideoCustomFolderRadio.IsChecked == true;

        VideoBrowseInputButton.Click += async (_, _) => await BrowseVideoInputAsync();
        VideoBrowseOutputButton.Click += async (_, _) => await BrowseVideoOutputAsync();
        VideoConvertButton.Click += async (_, _) => await ConvertVideoAsync();
        VideoCancelButton.Click += (_, _) => CancelConversion();
        VideoClearResultsButton.Click += (_, _) => ClearResults();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

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

    private void UpdateFfmpegStatus()
    {
        VideoFfmpegStatus.Text = FfmpegToolHelper.TryConfigure()
            ? string.Format(_loc.Get("MediaFfmpegFound"), FfmpegToolHelper.BinaryFolder)
            : FfmpegMissingMessage();
    }

    private string FfmpegMissingMessage() =>
        string.Format(_loc.Get("MediaFfmpegMissing"), ToolLocator.InstallHint);

    private void UpdateFrameFpsEnabled() =>
        FrameFpsSlider.IsEnabled = TargetFpsFrameRadio.IsChecked == true;

    private void UpdateVideoOperationUi()
    {
        var mode = GetSelectedVideoOperationMode();

        VideoFormatPanel.IsVisible = mode == VideoOperationMode.Convert;
        VideoGifOptionsPanel.IsVisible = mode == VideoOperationMode.ToGif;
        VideoFrameOptionsPanel.IsVisible = mode == VideoOperationMode.ToFrameTextures;
        UpdateFrameFpsEnabled();

        VideoOperationHint.Text = mode switch
        {
            VideoOperationMode.Optimize => _loc.Get("VideoConvertHintOptimize"),
            VideoOperationMode.ToGif => _loc.Get("VideoConvertHintGif"),
            VideoOperationMode.ToFrameTextures => _loc.Get("VideoConvertHintFrames"),
            _ => _loc.Get("VideoConvertHintConvert")
        };
    }

    private bool HasExistingInput()
    {
        var path = VideoInputPathBox.Text?.Trim();
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private void ShowDefaultInfo()
    {
        VideoInfoTitleText.Text = _loc.Get("VideoConvertDropHint");
        VideoInfoDetails.Text = _loc.Get("VideoConvertDropDetails");
    }

    private void SetInputPath(string path)
    {
        VideoInputPathBox.Text = path;
        _ = RefreshVideoInfoAsync();
    }

    private async Task RefreshVideoInfoAsync()
    {
        _probeCancellation?.Cancel();
        var cancellation = _probeCancellation = new CancellationTokenSource();
        var version = ++_probeVersion;

        var path = VideoInputPathBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ShowDefaultInfo();
            return;
        }

        VideoInfoTitleText.Text = Path.GetFileName(path);

        if (!VideoFormatCatalog.IsSupportedVideo(path))
        {
            VideoInfoDetails.Text = _loc.Get("VideoConvertUnsupported");
            return;
        }

        if (!FfmpegToolHelper.TryConfigure())
        {
            VideoInfoDetails.Text = FfmpegMissingMessage();
            return;
        }

        VideoInfoDetails.Text = _loc.Get("StatusFetching");

        try
        {
            var info = await _probeService.ProbeAsync(path, cancellation.Token);
            if (version != _probeVersion)
                return;

            VideoInfoDetails.Text = info is null ? _loc.Get("VideoConvertProbeFailed") : info.Summary;
        }
        catch (OperationCanceledException)
        {
            // a newer file was selected
        }
        catch (Exception ex)
        {
            if (version == _probeVersion)
                VideoInfoDetails.Text = ex.Message;
        }
    }

    private async Task BrowseVideoInputAsync()
    {
        var path = await Pickers.PickFileAsync(
            this,
            _loc.Get("VideoConvertSelectFileTitle"),
            [
                Pickers.Filter(_loc.Get("VideoConvertFilesFilter"), [.. VideoFormatCatalog.InputExtensions]),
                Pickers.AllFiles(_loc.Get("MediaAllFiles"))
            ],
            VideoInputPathBox.Text);

        if (!string.IsNullOrWhiteSpace(path))
            SetInputPath(path);
    }

    private async Task BrowseVideoOutputAsync()
    {
        var initial = string.IsNullOrWhiteSpace(VideoOutputFolderBox.Text)
            ? VideoInputPathBox.Text
            : VideoOutputFolderBox.Text;

        var folder = await Pickers.PickFolderAsync(this, _loc.Get("MediaSelectOutputFolder"), initial);
        if (!string.IsNullOrWhiteSpace(folder))
            VideoOutputFolderBox.Text = folder;
    }

    private static string? GetDroppedVideo(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is not { Length: 1 })
            return null;

        var path = files[0].TryGetLocalPath();
        return path is not null && File.Exists(path) && VideoFormatCatalog.IsSupportedVideo(path) ? path : null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var valid = !_busy && GetDroppedVideo(e) is not null;
        e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        VideoInfoDropZone.Classes.Set("dragover", valid);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        VideoInfoDropZone.Classes.Set("dragover", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        VideoInfoDropZone.Classes.Set("dragover", false);
        e.Handled = true;

        if (_busy)
            return;

        var path = GetDroppedVideo(e);
        if (path is null)
        {
            VideoStatusText.Text = _loc.Get("VideoConvertDropInvalid");
            return;
        }

        SetInputPath(path);
        VideoStatusText.Text = $"{_loc.Get("VideoConvertLoaded")}: {Path.GetFileName(path)}";
    }

    private async Task ConvertVideoAsync()
    {
        if (_busy)
            return;

        if (!TryBuildVideoSettings(out var settings, out var error))
        {
            await Dialogs.ShowAsync(this, error, _loc.Get("TabVideoConvert"), DialogKind.Warning);
            return;
        }

        _cancellationSource?.Dispose();
        _cancellationSource = new CancellationTokenSource();
        var token = _cancellationSource.Token;

        SetVideoResult(ConversionResultItem.Pending(settings.InputPath));
        SetVideoBusy(true);
        VideoResultsSummaryText.Text = GetProcessingSummary(settings);
        VideoStatusText.Text = _loc.Get("VideoConvertProcessing");

        var reportedConverting = false;
        var progress = new Progress<double>(percent =>
        {
            if (!_busy || token.IsCancellationRequested)
                return;

            VideoStatusText.Text = $"{_loc.Get("VideoConvertProcessing")} {percent:F0}%";
            VideoProgressBar.Value = percent;
            VideoResultsProgressBar.Value = percent;

            if (!reportedConverting)
            {
                reportedConverting = true;
                SetVideoResult(ConversionResultItem.Converting(settings.InputPath));
            }
        });

        VideoConversionResult? result = null;
        try
        {
            result = await _conversionService.ConvertAsync(settings, progress, token);

            SetVideoResult(ConversionResultItem.FromVideoResult(result));
            VideoStatusText.Text = result.Success ? _loc.Get("VideoConvertDone") : _loc.Get("VideoConvertFailed");
            VideoResultsSummaryText.Text = result.Success
                ? BuildFinishedSummary(result)
                : _loc.Get("VideoConvertFailed");
        }
        catch (OperationCanceledException)
        {
            VideoStatusText.Text = _loc.Get("StatusCancelled");
            VideoResultsSummaryText.Text = _loc.Get("VideoConvertCancelled");
            _videoResults.Clear();
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
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("VideoConvertFailedTitle"), DialogKind.Error);
        }
        finally
        {
            SetVideoBusy(false);
        }

        if (result is { Success: true })
        {
            if (await Dialogs.ConfirmAsync(this, _loc.Get("VideoConvertOpenFolderPrompt"), _loc.Get("VideoConvertDone")))
                OpenOutput(result.OutputPath);

            await RefreshVideoInfoAsync();
        }
    }

    private static void OpenOutput(string outputPath)
    {
        try
        {
            if (Directory.Exists(outputPath))
                PlatformShell.OpenFolder(outputPath);
            else
                PlatformShell.RevealInFolder(outputPath);
        }
        catch
        {
            // no file manager available (e.g. minimal Linux desktop)
        }
    }

    private void CancelConversion()
    {
        if (!_busy || _cancellationSource is null)
            return;

        VideoCancelButton.IsEnabled = false;
        VideoStatusText.Text = _loc.Get("MediaCancelling");
        _cancellationSource.Cancel();
    }

    private string BuildFinishedSummary(VideoConversionResult result)
    {
        if (result.ExtractedFrameCount is int frames)
            return string.Format(_loc.Get("VideoConvertFramesDone"), frames);

        var saved = result.OriginalSizeBytes - result.OutputSizeBytes;
        return saved > 0
            ? string.Format(_loc.Get("VideoConvertSaved"), FormatHelpers.FormatBytes(saved))
            : string.Format(
                _loc.Get("MediaFinishedSize"),
                FormatHelpers.FormatBytes(result.OriginalSizeBytes),
                FormatHelpers.FormatBytes(result.OutputSizeBytes));
    }

    private bool TryBuildVideoSettings(out VideoConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = VideoInputPathBox.Text?.Trim() ?? string.Empty;
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
            error = FfmpegMissingMessage();
            return false;
        }

        var useCustomOutput = VideoCustomFolderRadio.IsChecked == true;
        var outputFolder = VideoOutputFolderBox.Text?.Trim() ?? string.Empty;

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

        settings = new VideoConversionSettings
        {
            InputPath = inputPath,
            OperationMode = operationMode,
            TargetFormat = (VideoFormatCombo.SelectedItem as VideoFormatDefinition)?.Format ?? VideoFormat.Mp4,
            OutputLocation = useCustomOutput ? VideoOutputLocation.CustomFolder : VideoOutputLocation.SameFolder,
            OutputFolder = useCustomOutput ? outputFolder : null,
            GifFps = (int)Math.Round(GifFpsSlider.Value),
            FrameTextureFormat = GetSelectedFrameTextureFormat(),
            FrameExtractMode = TargetFpsFrameRadio.IsChecked == true
                ? FrameExtractMode.TargetFps
                : FrameExtractMode.EveryFrame,
            FrameExtractFps = (int)Math.Round(FrameFpsSlider.Value),
            Resolution = VideoResolutionCombo.SelectedItem as VideoResolutionPreset ?? VideoResolutionPreset.Source
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

    private FrameTextureFormat GetSelectedFrameTextureFormat() =>
        FrameFormatCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
        Enum.TryParse<FrameTextureFormat>(tag, out var format)
            ? format
            : FrameTextureFormat.Png;

    private string GetProcessingSummary(VideoConversionSettings settings)
    {
        var resolutionNote = settings.Resolution.IsSource
            ? string.Empty
            : string.Format(_loc.Get("MediaSizeNote"), settings.Resolution.Label, settings.Resolution.DimensionsText);

        var frameFormat = settings.FrameTextureFormat.ToString().ToUpperInvariant();

        return settings.OperationMode switch
        {
            VideoOperationMode.Optimize =>
                string.Format(_loc.Get("VideoConvertSummaryOptimize"), resolutionNote),
            VideoOperationMode.ToGif =>
                string.Format(_loc.Get("VideoConvertSummaryGif"), settings.GifFps, resolutionNote),
            VideoOperationMode.ToFrameTextures => settings.FrameExtractMode == FrameExtractMode.EveryFrame
                ? string.Format(_loc.Get("VideoConvertSummaryFramesEvery"), frameFormat, resolutionNote)
                : string.Format(_loc.Get("VideoConvertSummaryFramesFps"), settings.FrameExtractFps, frameFormat, resolutionNote),
            _ => string.Format(
                _loc.Get("VideoConvertSummaryConvert"),
                VideoFormatCatalog.GetDefinition(settings.TargetFormat).DisplayName,
                resolutionNote)
        };
    }

    private void SetVideoResult(ConversionResultItem item)
    {
        _videoResults.Clear();
        _videoResults.Add(item);
    }

    private void SetVideoBusy(bool busy)
    {
        _busy = busy;
        VideoConvertButton.IsEnabled = !busy;
        VideoConvertButton.Content = busy ? _loc.Get("VideoConvertProcessing") : _loc.Get("VideoConvertButton");
        VideoCancelButton.IsVisible = busy;
        VideoCancelButton.IsEnabled = busy;
        VideoClearResultsButton.IsEnabled = !busy;
        VideoBrowseInputButton.IsEnabled = !busy;
        VideoProgressBar.IsVisible = busy;
        VideoResultsProgressBar.IsVisible = busy;
        VideoProgressBar.Value = 0;
        VideoResultsProgressBar.Value = 0;
    }

    private void ClearResults()
    {
        _videoResults.Clear();
        VideoStatusText.Text = _loc.Get("VideoConvertReady");
        VideoResultsSummaryText.Text = _loc.Get("VideoConvertNoResult");
    }
}
