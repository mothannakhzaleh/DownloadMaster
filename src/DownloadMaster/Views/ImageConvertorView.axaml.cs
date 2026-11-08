using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DownloadMaster.Models;
using DownloadMaster.Services;
using DownloadMaster.Services.Ui;

namespace DownloadMaster.Views;

/// <summary>
/// Image Convert tab: single image or folder batch (Magick.NET), merged from MediaConvertor's Images tab.
/// MainWindow only calls <see cref="ApplyLocalization"/>.
/// </summary>
public partial class ImageConvertorView : UserControl
{
    private static LocalizationService s_statusLoc = new();

    /// <summary>Localizes ConversionResultItem.Status ("Pending"/"Converting"/"OK"/"Failed") for the results grid.</summary>
    public static IValueConverter StatusConverter { get; } = new FuncValueConverter<string?, string>(status => status switch
    {
        "Pending" => s_statusLoc.Get("ImgStatusPending"),
        "Converting" => s_statusLoc.Get("ImgStatusConverting"),
        "OK" => s_statusLoc.Get("ImgStatusOk"),
        "Failed" => s_statusLoc.Get("ImgStatusFailed"),
        _ => status ?? string.Empty
    });

    private readonly ImageConversionService _conversionService = new();
    private readonly ImagePreviewService _previewService = new();
    private readonly ObservableCollection<ConversionResultItem> _results = [];
    private readonly Dictionary<string, CheckBox> _extensionCheckboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _resultIndexByPath = new(StringComparer.OrdinalIgnoreCase);
    private ObservableCollection<ImageInputFileItem> _includedFiles = [];
    private CancellationTokenSource? _cancellationSource;
    private LocalizationService _loc = new();
    private Bitmap? _previewBitmap;
    private int _fileListGeneration;
    private int _previewGeneration;
    private bool _suppressFileSelectionChanged;
    private bool _suppressExtensionFilterSync;
    private bool _suppressModeChange;
    private bool _isBusy;
    private bool _fileListLoading;

    public ImageConvertorView()
    {
        InitializeComponent();

        IncludedFilesList.ItemsSource = _includedFiles;
        ResultsGrid.ItemsSource = _results;
        PresetResolutionCombo.ItemsSource = ImageResolutionPreset.Presets;
        PresetResolutionCombo.SelectedIndex = 1;

        PopulateExtensionCheckboxes();
        PopulateParallelismOptions();
        WireUiEvents();

        ApplyLocalization(_loc);
    }

    // ================================================================= localization

    public void ApplyLocalization(LocalizationService loc)
    {
        _loc = loc;
        s_statusLoc = loc;

        SourceTitle.Text = loc.Get("ImageConvertSource");
        ModeLabel.Text = loc.Get("ImageConvertMode");
        SingleFileRadio.Content = loc.Get("ImageConvertSingle");
        FolderRadio.Content = loc.Get("ImageConvertFolder");
        BrowseInputButton.Content = loc.Get("Browse");
        IncludeSubfoldersCheck.Content = loc.Get("ImageConvertSubfolders");
        EnableExtensionFilterCheck.Content = loc.Get("ImgFilterEnable");
        IncludeOnlyExtensionsRadio.Content = loc.Get("ImgFilterIncludeOnly");
        ExcludeExtensionsRadio.Content = loc.Get("ImgFilterExclude");
        ExtensionFilterHint.Text = loc.Get("ImgFilterSelectHint");
        SelectAllExtensionsCheck.Content = loc.Get("ImgFilterAll");

        OutputTitle.Text = loc.Get("ImageConvertOutput");
        FormatLabel.Text = loc.Get("ImageConvertTargetFormat");
        SaveToLabel.Text = loc.Get("VideoConvertOutput");
        SameFolderRadio.Content = loc.Get("ImageConvertSameFolder");
        CustomFolderRadio.Content = loc.Get("ImageConvertCustomFolder");
        BrowseOutputButton.Content = loc.Get("Browse");
        DeleteSourceCheck.Content = loc.Get("ImgDeleteSource");
        DeleteSourceHint.Text = loc.Get("ImgDeleteSourceHint");

        ResolutionTitle.Text = loc.Get("ImageConvertResolution");
        OriginalResolutionRadio.Content = loc.Get("ImgResolutionOriginal");
        PresetResolutionRadio.Content = loc.Get("ImgResolutionPreset");
        CustomResolutionRadio.Content = loc.Get("ImgResolutionCustom");
        ResamplingLabel.Text = loc.Get("ImgResampling");
        SetComboItemTexts(ResamplingCombo, new Dictionary<string, string>
        {
            ["Smooth"] = loc.Get("ImgResampleSmooth"),
            ["NearestNeighbor"] = loc.Get("ImgResampleNearest"),
            ["Bilinear"] = loc.Get("ImgResampleBilinear"),
            ["Bicubic"] = loc.Get("ImgResampleBicubic")
        });

        ColorDepthCheck.Content = loc.Get("ImgColorDepth");
        RemoveAlphaRadio.Content = loc.Get("ImgRemoveAlpha");
        Indexed8Radio.Content = loc.Get("ImgIndexed8");
        Keep32BitRadio.Content = loc.Get("ImgKeep32");

        SatisfyTransparencyCheck.Content = loc.Get("ImgSatisfy");
        SatisfyHint.Text = loc.Get("ImgSatisfyHint");
        PinkKeyMatchLabel.Text = loc.Get("ImgPinkKeyMatch");
        SetComboItemTexts(PinkKeyMatchCombo, new Dictionary<string, string>
        {
            ["NearKey"] = loc.Get("ImgPinkNear"),
            ["Exact"] = loc.Get("ImgPinkExact"),
            ["LegacySoftFuzz"] = loc.Get("ImgPinkLegacy")
        });
        PinkKeyHint.Text = loc.Get("ImgPinkHint");
        SatisfyProcessPngCheck.Content = loc.Get("ImgSatisfyPng");
        SatisfyPngHint.Text = loc.Get("ImgSatisfyPngHint");

        ParallelLabel.Text = loc.Get("ImgParallel");
        ParallelHint.Text = loc.Get("ImgParallelHint");
        PopulateParallelismOptions();

        OptimizeCheck.Content = loc.Get("ImageConvertOptimize");
        PngColorsLabel.Text = loc.Get("ImageConvertPngColors");
        PngDitherCheck.Content = loc.Get("ImageConvertPngDither");

        PreviewTitleHeader.Text = loc.Get("ImageConvertPreview");
        ResultsTitle.Text = loc.Get("ImgResultsTitle");
        ClearResultsButton.Content = loc.Get("ImgClearResults");
        CancelButton.Content = loc.Get("ImgCancel");
        ConvertButton.Content = _isBusy ? loc.Get("ImgConvertingButton") : loc.Get("ImageConvertButton");

        if (ResultsGrid.Columns.Count >= 4)
        {
            ResultsGrid.Columns[0].Header = loc.Get("ResultColStatus");
            ResultsGrid.Columns[1].Header = loc.Get("ResultColFile");
            ResultsGrid.Columns[2].Header = loc.Get("ResultColSizeChange");
            ResultsGrid.Columns[3].Header = loc.Get("ResultColDetails");
        }

        if (!_isBusy)
        {
            StatusText.Text = loc.Get("ImageConvertReady");
            UpdateResultsSummary();
        }

        UpdateModeUi();
        UpdateIncludedFilesLabel();
        UpdateResolutionUi();
        UpdateColorDepthUi();
        UpdateOptimizationUi();
        if (string.IsNullOrWhiteSpace(InputPath))
            ShowPlaceholder(loc.Get("ImageConvertDropHint"), loc.Get("ImageConvertDropDetails"));
    }

    private static void SetComboItemTexts(ComboBox combo, IReadOnlyDictionary<string, string> textsByTag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && textsByTag.TryGetValue(tag, out var text))
                item.Content = text;
        }
    }

    // ================================================================= setup

    private void PopulateExtensionCheckboxes()
    {
        foreach (var extension in ImageConversionService.FilterableExtensions)
        {
            var checkbox = new CheckBox
            {
                Content = extension,
                MinWidth = 60,
                Margin = new Thickness(0, 0, 10, 0)
            };
            checkbox.IsCheckedChanged += ExtensionCheckbox_Changed;
            ExtensionCheckboxesPanel.Children.Add(checkbox);
            _extensionCheckboxes[extension] = checkbox;
        }
    }

    private void PopulateParallelismOptions()
    {
        var previousIndex = Math.Max(0, ParallelismCombo.SelectedIndex);
        var autoThreads = Math.Max(1, Environment.ProcessorCount - 1);
        var items = new List<ComboBoxItem>
        {
            new() { Content = string.Format(_loc.Get("ImgParallelAuto"), autoThreads), Tag = autoThreads }
        };

        foreach (var threads in new[] { 1, 2, 4, 8, 16 }.Where(v => v <= Math.Max(2, Environment.ProcessorCount * 2)))
        {
            items.Add(new ComboBoxItem
            {
                Content = threads == 1 ? _loc.Get("ImgParallelLight") : threads.ToString(),
                Tag = threads
            });
        }

        ParallelismCombo.ItemsSource = items;
        ParallelismCombo.SelectedIndex = Math.Min(previousIndex, items.Count - 1);
    }

    private void WireUiEvents()
    {
        SingleFileRadio.IsCheckedChanged += ModeRadio_Changed;
        FolderRadio.IsCheckedChanged += ModeRadio_Changed;
        BrowseInputButton.Click += BrowseInput_Click;
        UnloadButton.Click += (_, _) => ClearLoadedSource();
        InputPathBox.TextChanged += (_, _) =>
        {
            UpdateSourceActionsUi();
            _ = RefreshSourceAsync();
        };
        IncludeSubfoldersCheck.IsCheckedChanged += (_, _) => _ = RefreshSourceAsync();
        EnableExtensionFilterCheck.IsCheckedChanged += (_, _) => ExtensionFilterChanged();
        IncludeOnlyExtensionsRadio.IsCheckedChanged += (_, _) =>
        {
            if (IncludeOnlyExtensionsRadio.IsChecked == true || ExcludeExtensionsRadio.IsChecked == true)
                ExtensionFilterChanged();
        };
        SelectAllExtensionsCheck.IsCheckedChanged += SelectAllExtensionsCheck_Changed;

        FormatCombo.SelectionChanged += (_, _) =>
        {
            UpdateOptimizationUi();
            UpdateColorDepthUi();
        };
        SameFolderRadio.IsCheckedChanged += (_, _) => UpdateOutputUi();
        CustomFolderRadio.IsCheckedChanged += (_, _) => UpdateOutputUi();
        BrowseOutputButton.Click += BrowseOutput_Click;

        OriginalResolutionRadio.IsCheckedChanged += (_, _) => UpdateResolutionUi();
        PresetResolutionRadio.IsCheckedChanged += (_, _) => UpdateResolutionUi();
        CustomResolutionRadio.IsCheckedChanged += (_, _) => UpdateResolutionUi();
        ResamplingCombo.SelectionChanged += (_, _) => UpdateResamplingHint();

        ColorDepthCheck.IsCheckedChanged += (_, _) => UpdateColorDepthUi();
        RemoveAlphaRadio.IsCheckedChanged += (_, _) => UpdateColorDepthUi();
        Indexed8Radio.IsCheckedChanged += (_, _) => UpdateColorDepthUi();
        Keep32BitRadio.IsCheckedChanged += (_, _) => UpdateColorDepthUi();
        SatisfyTransparencyCheck.IsCheckedChanged += (_, _) =>
        {
            UpdateColorDepthUi();
            UpdateOptimizationUi();
        };
        OptimizeCheck.IsCheckedChanged += (_, _) => UpdateOptimizationUi();

        IncludedFilesList.SelectionChanged += IncludedFilesList_SelectionChanged;

        ConvertButton.Click += async (_, _) => await ConvertAsync();
        CancelButton.Click += (_, _) => _cancellationSource?.Cancel();
        ClearResultsButton.Click += (_, _) => ClearResults();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ================================================================= source / mode

    private string InputPath => InputPathBox.Text?.Trim() ?? string.Empty;

    private bool IsFolderMode => FolderRadio.IsChecked == true;

    private void ModeRadio_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressModeChange || sender is not RadioButton { IsChecked: true })
            return;

        ClearSourceIfIncompatibleWithMode();
        UpdateModeUi();
        _ = RefreshSourceAsync();
    }

    private void SetModeRadio(bool folder)
    {
        _suppressModeChange = true;
        try
        {
            if (folder)
                FolderRadio.IsChecked = true;
            else
                SingleFileRadio.IsChecked = true;
        }
        finally
        {
            _suppressModeChange = false;
        }

        UpdateModeUi();
    }

    /// <summary>Browse / drop auto-detect: a folder path switches to Folder mode, a file to Single.</summary>
    private void SyncInputModeFromPath()
    {
        var path = InputPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Directory.Exists(path) && !IsFolderMode)
            SetModeRadio(folder: true);
        else if (File.Exists(path) && IsFolderMode)
            SetModeRadio(folder: false);
    }

    /// <summary>Switching Single ↔ Folder clears a path that does not match the new mode.</summary>
    private void ClearSourceIfIncompatibleWithMode()
    {
        var path = InputPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (IsFolderMode ? File.Exists(path) && !Directory.Exists(path) : Directory.Exists(path))
            ClearLoadedSource(showStatus: false);
    }

    private void UpdateModeUi()
    {
        var isFolder = IsFolderMode;
        InputLabel.Text = isFolder ? _loc.Get("ImageConvertFolderPath") : _loc.Get("ImageConvertFile");
        IncludeSubfoldersCheck.IsVisible = isFolder;
        ExtensionFilterPanel.IsVisible = isFolder;
        UpdateSourceActionsUi();
    }

    private void UpdateSourceActionsUi()
    {
        var path = InputPath;
        var isFolder = IsFolderMode;
        var loaded = !string.IsNullOrWhiteSpace(path) &&
                     (isFolder ? Directory.Exists(path) : File.Exists(path));

        UnloadButton.IsVisible = loaded;
        UnloadButton.Content = isFolder ? _loc.Get("ImgUnloadFolder") : _loc.Get("ImgClearFile");
    }

    private void ClearLoadedSource(bool showStatus = true)
    {
        Interlocked.Increment(ref _fileListGeneration);
        Interlocked.Increment(ref _previewGeneration);

        InputPathBox.Text = string.Empty;
        SetIncludedFiles([]);
        _fileListLoading = false;
        UpdateIncludedFilesLabel();

        PreviewTitle.Text = string.Empty;
        PreviewDetails.Text = string.Empty;
        ShowPlaceholder(_loc.Get("ImageConvertDropHint"), _loc.Get("ImageConvertDropDetails"));

        if (showStatus && !_isBusy)
            StatusText.Text = IsFolderMode ? _loc.Get("ImgFolderUnloaded") : _loc.Get("ImgFileCleared");

        UpdateSourceActionsUi();
    }

    private async void BrowseInput_Click(object? sender, RoutedEventArgs e)
    {
        string? picked;
        if (IsFolderMode)
        {
            picked = await Pickers.PickFolderAsync(this, _loc.Get("ImgSelectFolderTitle"), InputPath);
        }
        else
        {
            picked = await Pickers.PickFileAsync(
                this,
                _loc.Get("ImgSelectImageTitle"),
                [
                    Pickers.Filter(_loc.Get("ImgPickerImages"), ImageConversionService.PickerExtensions.ToArray()),
                    Pickers.AllFiles(_loc.Get("ImgPickerAllFiles"))
                ],
                InputPath);
        }

        if (!string.IsNullOrWhiteSpace(picked))
            InputPathBox.Text = picked;
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        var start = string.IsNullOrWhiteSpace(OutputFolderBox.Text) ? InputPath : OutputFolderBox.Text;
        var folder = await Pickers.PickFolderAsync(this, _loc.Get("ImgSelectOutputTitle"), start);
        if (!string.IsNullOrWhiteSpace(folder))
            OutputFolderBox.Text = folder;
    }

    // ================================================================= extension filter

    private void ExtensionCheckbox_Changed(object? sender, RoutedEventArgs e)
    {
        if (!_suppressExtensionFilterSync)
            SyncSelectAllExtensionsState();

        ExtensionFilterChanged();
    }

    private void ExtensionFilterChanged()
    {
        ExtensionFilterOptionsPanel.IsEnabled = EnableExtensionFilterCheck.IsChecked == true;
        if (!_suppressExtensionFilterSync)
            _ = RefreshSourceAsync();
    }

    private void SelectAllExtensionsCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressExtensionFilterSync || SelectAllExtensionsCheck.IsChecked is not bool selectAll)
            return;

        _suppressExtensionFilterSync = true;
        try
        {
            foreach (var checkbox in _extensionCheckboxes.Values)
                checkbox.IsChecked = selectAll;
        }
        finally
        {
            _suppressExtensionFilterSync = false;
        }

        ExtensionFilterChanged();
    }

    private void SyncSelectAllExtensionsState()
    {
        if (_extensionCheckboxes.Count == 0)
            return;

        var checkedCount = _extensionCheckboxes.Values.Count(cb => cb.IsChecked == true);

        _suppressExtensionFilterSync = true;
        try
        {
            SelectAllExtensionsCheck.IsChecked = checkedCount switch
            {
                0 => false,
                _ when checkedCount == _extensionCheckboxes.Count => true,
                _ => null
            };
        }
        finally
        {
            _suppressExtensionFilterSync = false;
        }
    }

    private ImageExtensionFilterSettings GetExtensionFilterSettings()
    {
        if (!IsFolderMode || EnableExtensionFilterCheck.IsChecked != true)
            return ImageExtensionFilterSettings.None;

        return new ImageExtensionFilterSettings
        {
            Mode = ExcludeExtensionsRadio.IsChecked == true
                ? ImageExtensionFilterMode.Exclude
                : ImageExtensionFilterMode.IncludeOnly,
            Extensions = _extensionCheckboxes
                .Where(pair => pair.Value.IsChecked == true)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
        };
    }

    // ================================================================= output / resolution / depth

    private void UpdateOutputUi()
    {
        var useCustom = CustomFolderRadio.IsChecked == true;
        OutputFolderPanel.IsEnabled = useCustom;
        DeleteSourceCheck.IsEnabled = !useCustom;
        if (useCustom)
            DeleteSourceCheck.IsChecked = false;
    }

    private void UpdateResolutionUi()
    {
        var usePreset = PresetResolutionRadio.IsChecked == true;
        var useCustom = CustomResolutionRadio.IsChecked == true;
        PresetResolutionCombo.IsEnabled = usePreset;
        CustomWidthBox.IsEnabled = useCustom;
        CustomHeightBox.IsEnabled = useCustom;
        ResamplingPanel.IsVisible = usePreset || useCustom;
        UpdateResamplingHint();
    }

    private void UpdateResamplingHint()
    {
        ResamplingHint.Text = GetSelectedResizeFilter() switch
        {
            ImageResizeFilter.NearestNeighbor => _loc.Get("ImgResampleHintNearest"),
            ImageResizeFilter.Bilinear => _loc.Get("ImgResampleHintBilinear"),
            ImageResizeFilter.Bicubic => _loc.Get("ImgResampleHintBicubic"),
            _ => _loc.Get("ImgResampleHintSmooth")
        };
    }

    private void UpdateColorDepthUi()
    {
        if (SatisfyTransparencyCheck.IsChecked == true)
        {
            SelectFormat(ImageFormat.Png);
            FormatCombo.IsEnabled = false;
            ColorDepthCheck.IsChecked = false;
            ColorDepthCheck.IsEnabled = false;
            ColorDepthPanel.IsEnabled = false;
            SatisfyProcessPngCheck.IsEnabled = true;
            SatisfyMatchModePanel.IsEnabled = true;
            ColorDepthHint.Text = _loc.Get("ImgColorDepthHintLocked");
            return;
        }

        FormatCombo.IsEnabled = true;
        ColorDepthCheck.IsEnabled = true;
        SatisfyProcessPngCheck.IsChecked = false;
        SatisfyProcessPngCheck.IsEnabled = false;
        SatisfyMatchModePanel.IsEnabled = false;

        var enabled = ColorDepthCheck.IsChecked == true;
        ColorDepthPanel.IsEnabled = enabled;

        if (!enabled)
        {
            ColorDepthHint.Text = _loc.Get("ImgColorDepthHintOff");
            return;
        }

        var format = GetSelectedFormat();
        ColorDepthHint.Text = GetColorDepthMode() switch
        {
            ImageColorDepthMode.RemoveAlpha => format == ImageFormat.Jpeg
                ? _loc.Get("ImgColorDepthHintRemoveAlphaJpeg")
                : _loc.Get("ImgColorDepthHintRemoveAlpha"),
            ImageColorDepthMode.Indexed8Bit => format is ImageFormat.Png or ImageFormat.Gif or ImageFormat.Bmp
                ? _loc.Get("ImgColorDepthHintIndexed")
                : _loc.Get("ImgColorDepthHintIndexedOther"),
            ImageColorDepthMode.Keep32BitRgba => format == ImageFormat.Jpeg
                ? _loc.Get("ImgColorDepthHintKeep32Jpeg")
                : _loc.Get("ImgColorDepthHintKeep32"),
            _ => _loc.Get("ImgColorDepthHintOff")
        };
    }

    private void UpdateOptimizationUi()
    {
        var optimize = OptimizeCheck.IsChecked == true;
        OptimizationPanel.IsEnabled = optimize;

        var format = SatisfyTransparencyCheck.IsChecked == true ? ImageFormat.Png : GetSelectedFormat();

        var isPng = format == ImageFormat.Png;
        PngColorsLabel.IsVisible = isPng;
        PngColorsPanel.IsVisible = isPng;
        PngDitherCheck.IsVisible = isPng && optimize;

        var showQuality = format is ImageFormat.Jpeg or ImageFormat.WebP;
        QualityLabel.IsVisible = showQuality;
        QualityPanel.IsVisible = showQuality;
        QualityLabel.Text = format == ImageFormat.WebP ? _loc.Get("ImgQualityWebp") : _loc.Get("ImgQualityJpeg");

        OptimizationHint.Text = format switch
        {
            ImageFormat.Png when optimize => _loc.Get("ImgHintPngOptimize"),
            ImageFormat.Png => _loc.Get("ImgHintPngLossless"),
            ImageFormat.Tga => _loc.Get("ImgHintTga"),
            ImageFormat.Bmp => _loc.Get("ImgHintBmp"),
            ImageFormat.Tiff => _loc.Get("ImgHintTiff"),
            ImageFormat.Gif => _loc.Get("ImgHintGif"),
            ImageFormat.Jpeg => _loc.Get("ImgHintJpeg"),
            ImageFormat.WebP => _loc.Get("ImgHintWebp"),
            ImageFormat.Dds => _loc.Get("ImgHintDds"),
            _ => _loc.Get("ImageConvertOptimizeHintGeneric")
        };
    }

    private ImageFormat GetSelectedFormat() =>
        FormatCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<ImageFormat>(tag, out var format)
            ? format
            : ImageFormat.Png;

    private void SelectFormat(ImageFormat format)
    {
        var item = FormatCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Tag is string tag && Enum.TryParse<ImageFormat>(tag, out var f) && f == format);
        if (item is not null && !ReferenceEquals(FormatCombo.SelectedItem, item))
            FormatCombo.SelectedItem = item;
    }

    private ImageResizeFilter GetSelectedResizeFilter() =>
        ResamplingCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<ImageResizeFilter>(tag, out var filter)
            ? filter
            : ImageResizeFilter.Smooth;

    private ImageColorDepthMode GetColorDepthMode()
    {
        if (SatisfyTransparencyCheck.IsChecked == true || ColorDepthCheck.IsChecked != true)
            return ImageColorDepthMode.Default;

        if (RemoveAlphaRadio.IsChecked == true)
            return ImageColorDepthMode.RemoveAlpha;
        if (Indexed8Radio.IsChecked == true)
            return ImageColorDepthMode.Indexed8Bit;
        if (Keep32BitRadio.IsChecked == true)
            return ImageColorDepthMode.Keep32BitRgba;

        return ImageColorDepthMode.Default;
    }

    private PinkKeyMatchMode GetPinkKeyMatchMode() =>
        PinkKeyMatchCombo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<PinkKeyMatchMode>(tag, out var mode)
            ? mode
            : PinkKeyMatchMode.NearKey;

    private int GetMaxParallelConversions() =>
        ParallelismCombo.SelectedItem is ComboBoxItem { Tag: int threads }
            ? Math.Max(1, threads)
            : Math.Max(1, Environment.ProcessorCount - 1);

    // ================================================================= file list + preview

    private void SetIncludedFiles(IReadOnlyList<ImageInputFileItem> items)
    {
        // A fresh collection is far cheaper than thousands of Add notifications for big folders.
        _suppressFileSelectionChanged = true;
        try
        {
            _includedFiles = new ObservableCollection<ImageInputFileItem>(items);
            IncludedFilesList.ItemsSource = _includedFiles;
        }
        finally
        {
            _suppressFileSelectionChanged = false;
        }
    }

    private void UpdateIncludedFilesLabel() =>
        IncludedFilesLabel.Text = _fileListLoading
            ? _loc.Get("ImgIncludedFilesLoading")
            : string.Format(_loc.Get("ImgIncludedFiles"), _includedFiles.Count);

    private async Task RefreshSourceAsync()
    {
        SyncInputModeFromPath();

        var generation = Interlocked.Increment(ref _fileListGeneration);
        var path = InputPath;
        var mode = IsFolderMode ? ImageConversionMode.Folder : ImageConversionMode.SingleFile;
        var includeSubfolders = IncludeSubfoldersCheck.IsChecked == true;
        var extensionFilter = GetExtensionFilterSettings();

        if (string.IsNullOrWhiteSpace(path))
        {
            Interlocked.Increment(ref _previewGeneration);
            _fileListLoading = false;
            SetIncludedFiles([]);
            UpdateIncludedFilesLabel();
            PreviewTitle.Text = string.Empty;
            PreviewDetails.Text = string.Empty;
            ShowPlaceholder(_loc.Get("ImageConvertDropHint"), _loc.Get("ImageConvertDropDetails"));
            return;
        }

        _fileListLoading = true;
        UpdateIncludedFilesLabel();

        IReadOnlyList<ImageInputFileItem> items;
        try
        {
            items = await Task.Run(() => _conversionService.GetInputFileItems(path, mode, includeSubfolders, extensionFilter));
        }
        catch (Exception)
        {
            if (generation != _fileListGeneration)
                return;

            items = [];
            if (!_isBusy)
                ResultsSummaryText.Text = _loc.Get("ImgFileListFailed");
        }

        if (generation != _fileListGeneration)
            return;

        _fileListLoading = false;
        SetIncludedFiles(items);
        UpdateIncludedFilesLabel();

        var first = items.FirstOrDefault();
        if (first is not null)
        {
            _suppressFileSelectionChanged = true;
            IncludedFilesList.SelectedIndex = 0;
            _suppressFileSelectionChanged = false;
            IncludedFilesList.ScrollIntoView(0);
        }

        var isFolder = mode == ImageConversionMode.Folder || Directory.Exists(path);
        string? folderSummary = isFolder && first is not null
            ? string.Format(
                _loc.Get("ImgFolderSummary"),
                items.Count,
                includeSubfolders ? _loc.Get("ImgScopeSubfolders") : _loc.Get("ImgScopeTop"),
                DescribeFilter(extensionFilter))
            : null;

        await LoadPreviewAsync(
            () => _previewService.LoadPreview(path, isFolder, extensionFilter, first?.FullPath),
            isFolder ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : null,
            folderSummary,
            extensionFilter.IsActive);
    }

    private string DescribeFilter(ImageExtensionFilterSettings filter)
    {
        if (!filter.IsActive)
            return string.Empty;

        var extensions = string.Join(", ", filter.Extensions.OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
        return string.Format(
            filter.Mode == ImageExtensionFilterMode.IncludeOnly ? _loc.Get("ImgFilterScopeOnly") : _loc.Get("ImgFilterScopeSkip"),
            extensions);
    }

    private void IncludedFilesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressFileSelectionChanged || IncludedFilesList.SelectedItem is not ImageInputFileItem item)
            return;

        _ = LoadPreviewAsync(() => ImagePreviewService.LoadImagePreview(item.FullPath), null, null, false);
    }

    private async Task LoadPreviewAsync(
        Func<ImagePreviewInfo> load,
        string? titleOverride,
        string? detailsOverride,
        bool filterActive)
    {
        var generation = Interlocked.Increment(ref _previewGeneration);

        ImagePreviewInfo preview;
        try
        {
            preview = await Task.Run(load);
        }
        catch (Exception ex)
        {
            preview = new ImagePreviewInfo { State = ImagePreviewState.Failed, Details = ex.Message };
        }

        if (generation != _previewGeneration)
        {
            preview.Image?.Dispose();
            return;
        }

        var (title, details) = DescribePreview(preview, filterActive);
        if (preview.HasPreview)
        {
            title = titleOverride ?? title;
            details = detailsOverride ?? details;
        }

        PreviewTitle.Text = title;
        PreviewDetails.Text = details;

        if (preview.Image is { } bitmap)
        {
            // Tiny sprites/icons look right with hard pixels when scaled up.
            RenderOptions.SetBitmapInterpolationMode(
                PreviewImage,
                bitmap.PixelSize.Width <= 128 && bitmap.PixelSize.Height <= 128
                    ? BitmapInterpolationMode.None
                    : BitmapInterpolationMode.HighQuality);
            SetPreviewBitmap(bitmap);
            PreviewImage.IsVisible = true;
            PreviewPlaceholder.IsVisible = false;
        }
        else
        {
            ShowPlaceholder(title, details);
        }
    }

    private (string Title, string Details) DescribePreview(ImagePreviewInfo preview, bool filterActive) => preview.State switch
    {
        ImagePreviewState.Image => (preview.Title, preview.Details),
        ImagePreviewState.Empty => (_loc.Get("ImageConvertDropHint"), _loc.Get("ImageConvertDropDetails")),
        ImagePreviewState.FileMissing => (_loc.Get("ImgPreviewFileMissing"), preview.Title),
        ImagePreviewState.FolderMissing => (_loc.Get("ImgPreviewFolderMissing"), preview.Title),
        ImagePreviewState.Unsupported => (_loc.Get("ImgPreviewUnsupported"), preview.Title),
        ImagePreviewState.FilteredOut => (_loc.Get("ImgPreviewFilteredOut"),
            string.Format(_loc.Get("ImgPreviewFilteredOutDetails"), preview.Title)),
        ImagePreviewState.FolderEmpty => (_loc.Get("ImgPreviewFolderEmpty"),
            string.Format(_loc.Get("ImgPreviewFolderEmptyDetails"), preview.Title,
                filterActive ? _loc.Get("ImgAfterFilter") : string.Empty)),
        _ => (_loc.Get("ImgPreviewUnavailable"), preview.Details)
    };

    private void ShowPlaceholder(string title, string details)
    {
        SetPreviewBitmap(null);
        PreviewImage.IsVisible = false;
        PreviewPlaceholder.IsVisible = true;
        PreviewPlaceholderTitle.Text = title;
        PreviewPlaceholderDetails.Text = details;
    }

    private void SetPreviewBitmap(Bitmap? bitmap)
    {
        var old = _previewBitmap;
        _previewBitmap = bitmap;
        PreviewImage.Source = bitmap;
        if (!ReferenceEquals(old, bitmap))
            old?.Dispose();
    }

    // ================================================================= drag & drop

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accept = TryResolveDrop(GetDroppedPaths(e), out _, out _, out _);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        PreviewDropZone.Classes.Set("dropTarget", accept);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) =>
        PreviewDropZone.Classes.Set("dropTarget", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        PreviewDropZone.Classes.Set("dropTarget", false);
        e.Handled = true;

        if (!TryResolveDrop(GetDroppedPaths(e), out var resolvedPath, out var useFolderMode, out var message))
        {
            if (!_isBusy)
                StatusText.Text = message;
            return;
        }

        SetModeRadio(useFolderMode);
        InputPathBox.Text = resolvedPath;
        if (!_isBusy)
            StatusText.Text = $"{_loc.Get("ImageConvertLoaded")}: {resolvedPath}";
    }

    private static List<string> GetDroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList() ?? [];

    /// <summary>
    /// One folder → folder mode; one image → single mode (or its folder when already in folder mode);
    /// several images → their folder in folder mode.
    /// </summary>
    private bool TryResolveDrop(IReadOnlyList<string> paths, out string resolvedPath, out bool useFolderMode, out string message)
    {
        resolvedPath = string.Empty;
        useFolderMode = IsFolderMode;
        message = string.Empty;

        if (paths.Count == 0)
        {
            message = _loc.Get("ImgDropNoFiles");
            return false;
        }

        if (paths.Count == 1 && Directory.Exists(paths[0]))
        {
            resolvedPath = paths[0];
            useFolderMode = true;
            return true;
        }

        var imageFiles = paths.Where(p => File.Exists(p) && ImageConversionService.IsSupportedImage(p)).ToList();
        if (imageFiles.Count == 0)
        {
            message = _loc.Get("ImgDropNoImages");
            return false;
        }

        if (useFolderMode || imageFiles.Count > 1)
        {
            var folder = Path.GetDirectoryName(imageFiles[0]);
            if (string.IsNullOrWhiteSpace(folder))
            {
                message = _loc.Get("ImgDropNoFolder");
                return false;
            }

            resolvedPath = folder;
            useFolderMode = true;
            return true;
        }

        resolvedPath = imageFiles[0];
        useFolderMode = false;
        return true;
    }

    // ================================================================= conversion

    private async Task ConvertAsync()
    {
        if (_isBusy)
            return;

        if (!TryBuildSettings(out var settings, out var validationError))
        {
            await Dialogs.ShowAsync(this, validationError, _loc.Get("TabImageConvert"), DialogKind.Warning);
            return;
        }

        IReadOnlyList<string> files;
        try
        {
            files = await Task.Run(() => _conversionService.ResolveInputFilePaths(
                settings.InputPath, settings.Mode, settings.IncludeSubfolders, settings.ExtensionFilter));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("ImageConvertFailed"), DialogKind.Error);
            return;
        }

        if (files.Count == 0)
        {
            await Dialogs.ShowAsync(this, _loc.Get("ImgNoFilterMatch"), _loc.Get("TabImageConvert"), DialogKind.Warning);
            return;
        }

        _cancellationSource?.Dispose();
        _cancellationSource = new CancellationTokenSource();

        PrepareConversionResults(files);
        SetBusy(true);

        var resolutionNote = settings.ResolutionMode == ImageResolutionMode.Original
            ? _loc.Get("ImgOriginalSize")
            : $"{settings.TargetWidth}×{settings.TargetHeight} ({settings.ResizeFilter})";
        ResultsSummaryText.Text = string.Format(
            _loc.Get("ImgConvertingSummary"), files.Count, settings.TargetFormat.ToString().ToUpperInvariant(), resolutionNote);

        try
        {
            var progress = new Progress<(int current, int total, string message)>(report =>
            {
                var completed = Math.Clamp(report.current, 0, report.total);
                StatusText.Text = completed < report.total
                    ? string.Format(_loc.Get("ImgProgressStatus"), completed, report.total, report.message)
                    : string.Format(_loc.Get("ImgFinishingStatus"), report.total, report.total);

                var percent = report.total > 0 ? (double)completed / report.total * 100 : 0;
                ProgressBar.Value = percent;
                ResultsProgressBar.Value = percent;
            });

            var resultProgress = new Progress<ImageConversionResult>(result =>
            {
                if (_resultIndexByPath.TryGetValue(result.SourcePath, out var index) &&
                    index >= 0 && index < _results.Count)
                {
                    var item = ConversionResultItem.FromImageResult(result);
                    _results[index] = item;
                    ResultsGrid.ScrollIntoView(item, null);
                }

                UpdateResultsSummary();
            });

            var results = await _conversionService.ConvertAsync(
                settings, progress, resultProgress, _cancellationSource.Token);

            var successCount = results.Count(r => r.Success);
            var failCount = results.Count - successCount;
            var savedBytes = results.Where(r => r.Success).Sum(r => r.OriginalSizeBytes - r.OutputSizeBytes);

            StatusText.Text = string.Format(_loc.Get("ImgDoneStatus"), successCount, failCount);
            ResultsSummaryText.Text = string.Format(
                _loc.Get("ImgFinishedSummary"), successCount, failCount, FormatHelpers.FormatBytes(Math.Max(0, savedBytes)));

            if (successCount > 0)
            {
                // Sources may be deleted / outputs added beside them: reload the queue.
                _ = RefreshSourceAsync();

                var openFolder = settings.OutputLocation == ImageOutputLocation.CustomFolder && settings.OutputFolder is not null
                    ? settings.OutputFolder
                    : Path.GetDirectoryName(results.First(r => r.Success).OutputPath);

                if (!string.IsNullOrWhiteSpace(openFolder) &&
                    await Dialogs.ConfirmAsync(this, _loc.Get("ImageConvertOpenFolderPrompt"), _loc.Get("ImageConvertDone")))
                {
                    PlatformShell.OpenFolder(openFolder);
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = _loc.Get("StatusCancelled");
            ResultsSummaryText.Text = _loc.Get("ImageConvertCancelled");
        }
        catch (Exception ex)
        {
            StatusText.Text = _loc.Get("ImageConvertFailed");
            ResultsSummaryText.Text = _loc.Get("ImageConvertFailed");
            await Dialogs.ShowAsync(this, ex.Message, _loc.Get("ImageConvertFailed"), DialogKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PrepareConversionResults(IReadOnlyList<string> files)
    {
        _results.Clear();
        _resultIndexByPath.Clear();

        var waiting = _loc.Get("ImgDetailWaiting");
        for (var i = 0; i < files.Count; i++)
        {
            _results.Add(new ConversionResultItem
            {
                SourcePath = files[i],
                FileName = Path.GetFileName(files[i]),
                Status = "Pending",
                SizeChange = "—",
                Detail = waiting,
                IsSuccess = false
            });
            _resultIndexByPath[files[i]] = i;
        }
    }

    private void UpdateResultsSummary()
    {
        var total = _results.Count;
        if (total == 0)
        {
            ResultsSummaryText.Text = _loc.Get("ImageConvertNoResult");
            return;
        }

        var ok = _results.Count(r => r.IsSuccess);
        var failed = _results.Count(r => r.IsFailed);
        ResultsSummaryText.Text = string.Format(_loc.Get("ImgProgressSummary"), ok + failed, total, ok, failed);
    }

    private bool TryBuildSettings(out ImageConversionSettings settings, out string error)
    {
        settings = null!;
        error = string.Empty;

        var inputPath = InputPath;
        var isFolder = IsFolderMode;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            error = isFolder ? _loc.Get("ImgSelectFolderError") : _loc.Get("ImgSelectFileError");
            return false;
        }

        if (isFolder)
        {
            if (!Directory.Exists(inputPath))
            {
                error = _loc.Get("ImageConvertFolderMissing");
                return false;
            }
        }
        else if (!File.Exists(inputPath))
        {
            error = _loc.Get("ImageConvertFileMissing");
            return false;
        }
        else if (!ImageConversionService.IsSupportedImage(inputPath))
        {
            error = _loc.Get("ImageConvertUnsupported");
            return false;
        }

        var useCustomOutput = CustomFolderRadio.IsChecked == true;
        var outputFolder = OutputFolderBox.Text?.Trim() ?? string.Empty;

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

        if (!TryGetResolutionSettings(out var resolutionMode, out var targetWidth, out var targetHeight, out error))
            return false;

        var extensionFilter = GetExtensionFilterSettings();
        if (isFolder && EnableExtensionFilterCheck.IsChecked == true && extensionFilter.Extensions.Count == 0)
        {
            error = _loc.Get("ImgFilterNoneSelected");
            return false;
        }

        var satisfy = SatisfyTransparencyCheck.IsChecked == true;
        var quality = (int)Math.Round(QualitySlider.Value);

        settings = new ImageConversionSettings
        {
            Mode = isFolder ? ImageConversionMode.Folder : ImageConversionMode.SingleFile,
            InputPath = inputPath,
            TargetFormat = satisfy ? ImageFormat.Png : GetSelectedFormat(),
            OutputLocation = useCustomOutput ? ImageOutputLocation.CustomFolder : ImageOutputLocation.SameFolder,
            OutputFolder = useCustomOutput ? outputFolder : null,
            DeleteSourceWhenExtensionChanges = !useCustomOutput && DeleteSourceCheck.IsChecked == true,
            OptimizeSize = OptimizeCheck.IsChecked == true,
            PngColorCount = (int)Math.Round(PngColorsSlider.Value),
            PngDither = PngDitherCheck.IsChecked == true,
            JpegQuality = quality,
            WebPQuality = quality,
            IncludeSubfolders = isFolder && IncludeSubfoldersCheck.IsChecked == true,
            ResolutionMode = resolutionMode,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            ResizeFilter = resolutionMode == ImageResolutionMode.Original
                ? ImageResizeFilter.Smooth
                : GetSelectedResizeFilter(),
            ColorDepthMode = GetColorDepthMode(),
            GameTransparencyMode = satisfy ? ImageGameTransparencyMode.SatisfyPinkKey : ImageGameTransparencyMode.Off,
            ApplyGameTransparencyToPng = satisfy && SatisfyProcessPngCheck.IsChecked == true,
            PinkKeyMatchMode = GetPinkKeyMatchMode(),
            MaxParallelConversions = GetMaxParallelConversions(),
            ExtensionFilter = extensionFilter
        };

        return true;
    }

    private bool TryGetResolutionSettings(out ImageResolutionMode mode, out int width, out int height, out string error)
    {
        mode = ImageResolutionMode.Original;
        width = 0;
        height = 0;
        error = string.Empty;

        if (PresetResolutionRadio.IsChecked == true)
        {
            if (PresetResolutionCombo.SelectedItem is not ImageResolutionPreset preset)
            {
                error = _loc.Get("ImgSelectPreset");
                return false;
            }

            mode = ImageResolutionMode.Preset;
            width = preset.Width;
            height = preset.Height;
            return true;
        }

        if (CustomResolutionRadio.IsChecked == true)
        {
            if (!int.TryParse(CustomWidthBox.Text?.Trim(), out width) ||
                !int.TryParse(CustomHeightBox.Text?.Trim(), out height) ||
                width <= 0 || height <= 0 || width > 65535 || height > 65535)
            {
                error = _loc.Get("ImgCustomSizeInvalid");
                return false;
            }

            mode = ImageResolutionMode.Custom;
        }

        return true;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        ConvertButton.IsEnabled = !busy;
        ConvertButton.Content = busy ? _loc.Get("ImgConvertingButton") : _loc.Get("ImageConvertButton");
        CancelButton.IsVisible = busy;
        ClearResultsButton.IsEnabled = !busy;
        ProgressBar.IsVisible = busy;
        ResultsProgressBar.IsVisible = busy;

        if (!busy)
        {
            ProgressBar.Value = 0;
            ResultsProgressBar.Value = 0;
        }
    }

    private void ClearResults()
    {
        _results.Clear();
        _resultIndexByPath.Clear();
        StatusText.Text = _loc.Get("ImageConvertReady");
        UpdateResultsSummary();
    }
}
