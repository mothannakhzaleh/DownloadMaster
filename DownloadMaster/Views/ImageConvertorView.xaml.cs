using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DownloadMaster.Models;
using DownloadMaster.Services;
using Microsoft.Win32;

namespace DownloadMaster.Views;

public partial class ImageConvertorView : UserControl
{
    private readonly ImageConversionService _conversionService = new();
    private readonly ObservableCollection<ImageInputFileItem> _includedFiles = [];
    private readonly ObservableCollection<ConversionResultItem> _imageResults = [];
    private CancellationTokenSource? _cancellationSource;
    private LocalizationService _loc = new();

    public ImageConvertorView()
    {
        InitializeComponent();
        WireUiEvents();
        ImageIncludedFilesList.ItemsSource = _includedFiles;
        ImageResultsList.ItemsSource = _imageResults;
        ImageResolutionCombo.ItemsSource = ImageResolutionPreset.Presets;
        ImageResolutionCombo.SelectedIndex = 0;
        UpdateModeUi();
        UpdateOptimizationUi();
    }

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        ImageSourceTitle.Text = loc.Get("ImageConvertSource");
        ImageModeLabel.Text = loc.Get("ImageConvertMode");
        ImageSingleFileRadio.Content = loc.Get("ImageConvertSingle");
        ImageFolderRadio.Content = loc.Get("ImageConvertFolder");
        ImageInputLabel.Text = loc.Get("ImageConvertFile");
        ImageBrowseInputButton.Content = loc.Get("Browse");
        ImageIncludeSubfoldersCheck.Content = loc.Get("ImageConvertSubfolders");
        ImageOutputTitle.Text = loc.Get("ImageConvertOutput");
        ImageFormatLabel.Text = loc.Get("ImageConvertTargetFormat");
        ImageSaveToLabel.Text = loc.Get("SavePath");
        ImageSameFolderRadio.Content = loc.Get("ImageConvertSameFolder");
        ImageCustomFolderRadio.Content = loc.Get("ImageConvertCustomFolder");
        ImageBrowseOutputButton.Content = loc.Get("Browse");
        ImageResolutionLabel.Text = loc.Get("ImageConvertResolution");
        ImageOptimizeCheck.Content = loc.Get("ImageConvertOptimize");
        ImageOptimizeHint.Text = loc.Get("ImageConvertOptimizeHint");
        ImagePngColorsLabel.Text = loc.Get("ImageConvertPngColors");
        ImagePngDitherCheck.Content = loc.Get("ImageConvertPngDither");
        ImageQualityLabel.Text = loc.Get("ImageConvertQuality");
        ImagePreviewTitle.Text = loc.Get("ImageConvertPreview");
        ImagePreviewTitleText.Text = loc.Get("ImageConvertDropHint");
        ImagePreviewDetails.Text = loc.Get("ImageConvertDropDetails");
        ImageResultsTitle.Text = loc.Get("ImageConvertResult");
        ImageClearResultsButton.Content = loc.Get("ClearList");
        ImageConvertButton.Content = loc.Get("ImageConvertButton");
        ImageStatusText.Text = loc.Get("ImageConvertReady");
        ImageResultsSummaryText.Text = loc.Get("ImageConvertNoResult");
        UpdateModeUi();
        UpdateOptimizationUi();
    }

    private void WireUiEvents()
    {
        ImageSingleFileRadio.Checked += ImageMode_Changed;
        ImageFolderRadio.Checked += ImageMode_Changed;
        ImageSameFolderRadio.Checked += ImageOutput_Changed;
        ImageCustomFolderRadio.Checked += ImageOutput_Changed;
        ImageFormatCombo.SelectionChanged += ImageFormatCombo_SelectionChanged;
        ImageOptimizeCheck.Checked += (_, _) => UpdateOptimizationUi();
        ImageOptimizeCheck.Unchecked += (_, _) => UpdateOptimizationUi();
    }

    private void ImageMode_Changed(object sender, RoutedEventArgs e) => UpdateModeUi();

    private void ImageOutput_Changed(object sender, RoutedEventArgs e) =>
        ImageOutputFolderPanel.IsEnabled = ImageCustomFolderRadio.IsChecked == true;

    private void ImageFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateOptimizationUi();

    private void UpdateModeUi()
    {
        var isFolder = ImageFolderRadio.IsChecked == true;
        ImageInputLabel.Text = isFolder ? _loc.Get("ImageConvertFolderPath") : _loc.Get("ImageConvertFile");
        ImageIncludeSubfoldersCheck.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
        _ = RefreshPreviewAsync();
    }

    private void UpdateOptimizationUi()
    {
        var optimize = ImageOptimizeCheck.IsChecked == true;
        ImageOptimizePanel.IsEnabled = optimize;

        var format = GetSelectedImageFormat();
        var showPng = optimize && format == ImageFormat.Png;
        ImagePngColorsLabel.Visibility = showPng ? Visibility.Visible : Visibility.Collapsed;
        ImagePngColorsPanel.Visibility = showPng ? Visibility.Visible : Visibility.Collapsed;
        ImagePngDitherCheck.Visibility = showPng ? Visibility.Visible : Visibility.Collapsed;

        var showQuality = optimize && format is ImageFormat.Jpeg or ImageFormat.WebP;
        ImageQualityLabel.Visibility = showQuality ? Visibility.Visible : Visibility.Collapsed;
        ImageQualityPanel.Visibility = showQuality ? Visibility.Visible : Visibility.Collapsed;

        ImageOptimizeHint.Text = format switch
        {
            ImageFormat.Png => _loc.Get("ImageConvertOptimizeHint"),
            ImageFormat.Jpeg => _loc.Get("ImageConvertOptimizeHintJpeg"),
            ImageFormat.WebP => _loc.Get("ImageConvertOptimizeHintWebp"),
            _ => _loc.Get("ImageConvertOptimizeHintGeneric")
        };
    }

    private void ImageInputPathBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _ = RefreshPreviewAsync();

    private async Task RefreshPreviewAsync()
    {
        var path = ImageInputPathBox.Text.Trim();
        var isFolder = ImageFolderRadio.IsChecked == true || Directory.Exists(path);

        if (string.IsNullOrWhiteSpace(path))
        {
            ClearPreview(_loc.Get("ImageConvertDropHint"), _loc.Get("ImageConvertDropDetails"));
            _includedFiles.Clear();
            return;
        }

        if (isFolder && Directory.Exists(path))
        {
            var files = _conversionService
                .ResolveInputFilePaths(path, ImageConversionMode.Folder, ImageIncludeSubfoldersCheck.IsChecked == true)
                .Select(ImageConversionService.CreateInputFileItemDetailed)
                .ToList();

            _includedFiles.Clear();
            foreach (var file in files)
                _includedFiles.Add(file);

            if (files.Count == 0)
            {
                ClearPreview(Path.GetFileName(path), _loc.Get("ImageConvertFolderEmpty"));
                return;
            }

            ImagePreviewTitleText.Text = Path.GetFileName(path);
            ImagePreviewDetails.Text = string.Format(_loc.Get("ImageConvertFolderLoaded"), files.Count);
            await LoadPreviewImageAsync(files[0].FullPath);
            return;
        }

        if (!File.Exists(path))
        {
            ClearPreview(_loc.Get("ImageConvertFileMissing"), path);
            _includedFiles.Clear();
            return;
        }

        if (!ImageConversionService.IsSupportedImage(path))
        {
            ClearPreview(Path.GetFileName(path), _loc.Get("ImageConvertUnsupported"));
            _includedFiles.Clear();
            return;
        }

        var item = ImageConversionService.CreateInputFileItemDetailed(path);
        _includedFiles.Clear();
        _includedFiles.Add(item);
        ImagePreviewTitleText.Text = item.FileName;
        ImagePreviewDetails.Text = item.Details;
        await LoadPreviewImageAsync(path);
    }

    private void ClearPreview(string title, string details)
    {
        ImagePreviewImage.Source = null;
        ImagePreviewImage.Visibility = Visibility.Collapsed;
        ImagePreviewPlaceholder.Visibility = Visibility.Visible;
        ImagePreviewTitleText.Text = title;
        ImagePreviewDetails.Text = details;
    }

    private async Task LoadPreviewImageAsync(string path)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.DecodePixelWidth = 240;
                image.EndInit();
                image.Freeze();
                return image;
            });

            ImagePreviewImage.Source = bitmap;
            ImagePreviewImage.Visibility = Visibility.Visible;
            ImagePreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            ClearPreview(Path.GetFileName(path), _loc.Get("ImageConvertPreviewFailed"));
        }
    }

    private void ImageIncludedFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageIncludedFilesList.SelectedItem is ImageInputFileItem item)
            _ = LoadPreviewImageAsync(item.FullPath);
    }

    private void BrowseImageInput_Click(object sender, RoutedEventArgs e)
    {
        if (ImageFolderRadio.IsChecked == true)
        {
            var folderDialog = new OpenFolderDialog { Title = _loc.Get("ImageConvertFolderPath") };
            if (folderDialog.ShowDialog() == true)
                ImageInputPathBox.Text = folderDialog.FolderName;
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = _loc.Get("ImageConvertFile"),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff;*.gif;*.tga;*.dds;*.ico;*.heic;*.avif"
        };

        if (dialog.ShowDialog() == true)
            ImageInputPathBox.Text = dialog.FileName;
    }

    private void BrowseImageOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = _loc.Get("SavePath") };
        if (dialog.ShowDialog() == true)
            ImageOutputFolderBox.Text = dialog.FolderName;
    }

    private void Image_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        e.Effects = paths.Length >= 1 && paths.Any(IsValidDropPath)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        ImagePreviewDropZone.BorderBrush = e.Effects == DragDropEffects.Copy
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("BorderBrush");

        e.Handled = true;
    }

    private void Image_DragLeave(object sender, DragEventArgs e) =>
        ImagePreviewDropZone.BorderBrush = (Brush)FindResource("BorderBrush");

    private void Image_Drop(object sender, DragEventArgs e)
    {
        ImagePreviewDropZone.BorderBrush = (Brush)FindResource("BorderBrush");

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var valid = paths.FirstOrDefault(IsValidDropPath);
        if (valid is null)
        {
            ImageStatusText.Text = _loc.Get("ImageConvertDropInvalid");
            return;
        }

        if (Directory.Exists(valid))
        {
            ImageFolderRadio.IsChecked = true;
            ImageInputPathBox.Text = valid;
        }
        else
        {
            ImageSingleFileRadio.IsChecked = true;
            ImageInputPathBox.Text = valid;
        }

        ImageStatusText.Text = $"{_loc.Get("ImageConvertLoaded")}: {Path.GetFileName(valid)}";
    }

    private static bool IsValidDropPath(string path) =>
        Directory.Exists(path) ||
        (File.Exists(path) && ImageConversionService.IsSupportedImage(path));

    private void ConvertImages_Click(object sender, RoutedEventArgs e) => _ = ConvertImagesAsync();

    private async Task ConvertImagesAsync()
    {
        if (!TryBuildSettings(out var settings, out var error))
        {
            MessageBox.Show(error, _loc.Get("TabImageConvert"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cancellationSource?.Cancel();
        _cancellationSource = new CancellationTokenSource();

        _imageResults.Clear();
        SetImageBusy(true);
        ImageResultsSummaryText.Text = _loc.Get("ImageConvertProcessing");

        try
        {
            var fileCount = _conversionService.ResolveInputFilePaths(
                settings.InputPath,
                settings.Mode,
                settings.IncludeSubfolders).Count;

            var progress = new Progress<(int current, int total, string message)>(update =>
            {
                var percent = update.total > 0 ? (double)update.current / update.total * 100.0 : 0;
                ImageStatusText.Text = $"{_loc.Get("ImageConvertProcessing")} {update.current}/{update.total} — {update.message}";
                ImageProgressBar.Value = percent;
                ImageResultsProgressBar.Value = percent;
            });

            var resultProgress = new Progress<ImageConversionResult>(result =>
            {
                _imageResults.Add(ConversionResultItem.FromImageResult(result));
            });

            var results = await _conversionService.ConvertAsync(
                settings,
                progress,
                resultProgress,
                _cancellationSource.Token);

            var successCount = results.Count(r => r.Success);
            var savedBytes = results.Where(r => r.Success).Sum(r => Math.Max(0, r.OriginalSizeBytes - r.OutputSizeBytes));

            ImageStatusText.Text = successCount == results.Count
                ? _loc.Get("ImageConvertDone")
                : _loc.Get("ImageConvertPartial");

            ImageResultsSummaryText.Text = successCount == results.Count
                ? string.Format(_loc.Get("ImageConvertSavedSummary"), successCount, FormatHelpers.FormatBytes(savedBytes))
                : string.Format(_loc.Get("ImageConvertPartialSummary"), successCount, results.Count);

            if (successCount > 0)
            {
                var prompt = MessageBox.Show(
                    _loc.Get("ImageConvertOpenFolderPrompt"),
                    _loc.Get("ImageConvertDone"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (prompt == MessageBoxResult.Yes)
                {
                    var firstOutput = results.First(r => r.Success).OutputPath;
                    var folder = Directory.Exists(firstOutput)
                        ? firstOutput
                        : Path.GetDirectoryName(firstOutput)!;
                    Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
                }

                await RefreshPreviewAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ImageStatusText.Text = _loc.Get("StatusCancelled");
            ImageResultsSummaryText.Text = _loc.Get("ImageConvertCancelled");
        }
        catch (Exception ex)
        {
            ImageStatusText.Text = _loc.Get("ImageConvertFailed");
            ImageResultsSummaryText.Text = _loc.Get("ImageConvertFailed");
            MessageBox.Show(ex.Message, _loc.Get("ImageConvertFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetImageBusy(false);
        }
    }

    private bool TryBuildSettings(out ImageConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = ImageInputPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error = _loc.Get("ImageConvertSelectSource");
            return false;
        }

        var mode = ImageConversionMode.SingleFile;
        if (ImageFolderRadio.IsChecked == true || Directory.Exists(inputPath))
            mode = ImageConversionMode.Folder;

        if (mode == ImageConversionMode.SingleFile)
        {
            if (!File.Exists(inputPath))
            {
                error = _loc.Get("ImageConvertFileMissing");
                return false;
            }

            if (!ImageConversionService.IsSupportedImage(inputPath))
            {
                error = _loc.Get("ImageConvertUnsupported");
                return false;
            }
        }
        else if (!Directory.Exists(inputPath))
        {
            error = _loc.Get("ImageConvertFolderMissing");
            return false;
        }

        var useCustomOutput = ImageCustomFolderRadio.IsChecked == true;
        var outputFolder = ImageOutputFolderBox.Text.Trim();

        if (useCustomOutput && string.IsNullOrWhiteSpace(outputFolder))
        {
            error = _loc.Get("ImageConvertSelectOutput");
            return false;
        }

        if (useCustomOutput && !Directory.Exists(outputFolder))
        {
            error = _loc.Get("ImageConvertOutputMissing");
            return false;
        }

        var resolution = ImageResolutionCombo.SelectedItem as ImageResolutionPreset ?? ImageResolutionPreset.Original;

        settings = new ImageConversionSettings
        {
            InputPath = inputPath,
            Mode = mode,
            TargetFormat = GetSelectedImageFormat(),
            OutputLocation = useCustomOutput ? ImageOutputLocation.CustomFolder : ImageOutputLocation.SameFolder,
            OutputFolder = useCustomOutput ? outputFolder : null,
            OptimizeSize = ImageOptimizeCheck.IsChecked == true,
            PngColorCount = (int)ImagePngColorsSlider.Value,
            PngDither = ImagePngDitherCheck.IsChecked == true,
            JpegQuality = (int)ImageQualitySlider.Value,
            WebPQuality = (int)ImageQualitySlider.Value,
            IncludeSubfolders = ImageIncludeSubfoldersCheck.IsChecked == true,
            ResolutionMode = resolution.Width > 0 ? ImageResolutionMode.Preset : ImageResolutionMode.Original,
            TargetWidth = resolution.Width,
            TargetHeight = resolution.Height
        };

        var fileCount = _conversionService.ResolveInputFilePaths(
            settings.InputPath,
            settings.Mode,
            settings.IncludeSubfolders).Count;

        if (fileCount == 0)
        {
            error = _loc.Get("ImageConvertNoFiles");
            return false;
        }

        return true;
    }

    private ImageFormat GetSelectedImageFormat()
    {
        if (ImageFormatCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse<ImageFormat>(tag, out var format))
            return format;

        return ImageFormat.Png;
    }

    private void SetImageBusy(bool busy)
    {
        ImageConvertButton.IsEnabled = !busy;
        ImageConvertButton.Content = busy ? _loc.Get("ImageConvertProcessing") : _loc.Get("ImageConvertButton");
        ImageProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ImageResultsProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (!busy)
        {
            ImageProgressBar.Value = 0;
            ImageResultsProgressBar.Value = 0;
        }
    }

    private void ClearImageResults_Click(object sender, RoutedEventArgs e)
    {
        _imageResults.Clear();
        ImageStatusText.Text = _loc.Get("ImageConvertReady");
        ImageResultsSummaryText.Text = _loc.Get("ImageConvertNoResult");
    }

    private static string FormatBytes(long bytes) => FormatHelpers.FormatBytes(bytes);
}
