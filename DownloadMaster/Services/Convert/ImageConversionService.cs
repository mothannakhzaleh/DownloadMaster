using DownloadMaster.Models;
using ImageMagick;
using ImageMagick.Formats;

namespace DownloadMaster.Services;

/// <summary>
/// Magick.NET image conversion / optimization (ported from MediaConvertor 1.0.12).
/// UI-free so the headless regression runner (tests/ImageConversion.Regression) can link it.
/// </summary>
public sealed class ImageConversionService
{
    private const string TempFilePrefix = ".imgcvt_";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif",
        ".tga", ".dds", ".ico", ".heic", ".heif", ".avif"
    };

    /// <summary>Extensions for file pickers (without dots).</summary>
    public static IReadOnlyList<string> PickerExtensions { get; } =
        SupportedExtensions.Select(e => e.TrimStart('.')).OrderBy(e => e, StringComparer.Ordinal).ToArray();

    /// <summary>Extensions offered by the folder extension filter.</summary>
    public static IReadOnlyList<string> FilterableExtensions { get; } =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".tga", ".dds"
    ];

    public static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    // ---------------------------------------------------------------- input files

    public IEnumerable<string> GetImageFiles(
        string folderPath,
        bool includeSubfolders,
        ImageExtensionFilterSettings? extensionFilter = null) =>
        EnumerateImageFiles(folderPath, includeSubfolders, extensionFilter)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateImageFiles(
        string folderPath,
        bool includeSubfolders,
        ImageExtensionFilterSettings? extensionFilter)
    {
        foreach (var filePath in SafeEnumerate(() => Directory.EnumerateFiles(folderPath)))
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(filePath)))
                continue;

            if (extensionFilter?.Matches(filePath) == false)
                continue;

            yield return filePath;
        }

        if (!includeSubfolders)
            yield break;

        foreach (var directoryPath in SafeEnumerate(() => Directory.EnumerateDirectories(folderPath)))
        {
            if (ShouldSkipDirectory(directoryPath))
                continue;

            foreach (var filePath in EnumerateImageFiles(directoryPath, includeSubfolders: true, extensionFilter))
                yield return filePath;
        }
    }

    private static IReadOnlyList<string> SafeEnumerate(Func<IEnumerable<string>> source)
    {
        try
        {
            return source().ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            // On macOS/Linux .NET reports dot-folders as Hidden.
            return (File.GetAttributes(directoryPath) & FileAttributes.Hidden) != 0;
        }
        catch
        {
            return true;
        }
    }

    public IReadOnlyList<ImageInputFileItem> GetInputFileItems(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders,
        ImageExtensionFilterSettings? extensionFilter = null) =>
        ResolveInputFiles(inputPath, mode, includeSubfolders, extensionFilter)
            .Select(CreateInputFileItemFast)
            .ToList();

    public IReadOnlyList<string> ResolveInputFilePaths(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders,
        ImageExtensionFilterSettings? extensionFilter = null) =>
        ResolveInputFiles(inputPath, mode, includeSubfolders, extensionFilter).ToList();

    public static ImageConversionMode ResolveInputMode(string inputPath, ImageConversionMode selectedMode)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return selectedMode;

        return Directory.Exists(inputPath) ? ImageConversionMode.Folder : selectedMode;
    }

    private IEnumerable<string> ResolveInputFiles(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders,
        ImageExtensionFilterSettings? extensionFilter)
    {
        var effectiveMode = ResolveInputMode(inputPath, mode);

        IEnumerable<string> files = effectiveMode switch
        {
            ImageConversionMode.SingleFile when File.Exists(inputPath) => [inputPath],
            ImageConversionMode.Folder when Directory.Exists(inputPath) =>
                GetImageFiles(inputPath, includeSubfolders, extensionFilter),
            _ => Array.Empty<string>()
        };

        if (effectiveMode == ImageConversionMode.SingleFile && extensionFilter?.IsActive == true)
            files = files.Where(extensionFilter.Matches);

        return files;
    }

    private static ImageInputFileItem CreateInputFileItemFast(string path)
    {
        long length = 0;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch
        {
            // File vanished between enumeration and listing.
        }

        return new ImageInputFileItem
        {
            FullPath = path,
            Details = FormatHelpers.FormatBytes(length)
        };
    }

    public static ImageInputFileItem CreateInputFileItemDetailed(string path)
    {
        try
        {
            using var image = LoadImage(path);
            var fileInfo = new FileInfo(path);

            return new ImageInputFileItem
            {
                FullPath = path,
                Details = $"{image.Width} × {image.Height}  •  {FormatHelpers.FormatBytes(fileInfo.Length)}  •  {image.Format.ToString().ToUpperInvariant()}"
            };
        }
        catch
        {
            return CreateInputFileItemFast(path);
        }
    }

    // ---------------------------------------------------------------- reading

    /// <summary>
    /// Loads an image with the game-texture repairs applied: 255-entry / short BMP palettes,
    /// PNG IENX → IEND trailer repair, and a png:ignore-crc fallback for other CRC damage.
    /// </summary>
    public static MagickImage LoadImage(string path)
    {
        var normalizedBytes = BmpPaletteNormalizer.TryNormalizeForRead(path)
            ?? PngReadNormalizer.TryNormalizeForRead(path);

        MagickImage image;
        if (normalizedBytes is not null)
        {
            image = new MagickImage(normalizedBytes);
        }
        else
        {
            try
            {
                image = new MagickImage(path);
            }
            catch (Exception ex) when (IsPngPath(path) && IsLikelyPngCrcFailure(ex))
            {
                // libpng is strict; some game PNGs only need CRC ignored after trailer repair fails.
                var readSettings = new MagickReadSettings();
                readSettings.SetDefines(new PngReadDefines { IgnoreCrc = true });
                image = new MagickImage(path, readSettings);
            }
        }

        ExpandIndexedToTrueColor(image);
        return image;
    }

    private static bool IsPngPath(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyPngCrcFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("CRC", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("IENX", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("png.c", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- conversion

    public async Task<IReadOnlyList<ImageConversionResult>> ConvertAsync(
        ImageConversionSettings settings,
        IProgress<(int current, int total, string message)>? progress = null,
        IProgress<ImageConversionResult>? resultProgress = null,
        CancellationToken cancellationToken = default)
    {
        var files = settings.Mode switch
        {
            ImageConversionMode.SingleFile => ResolveInputFilePaths(
                settings.InputPath, ImageConversionMode.SingleFile, false, settings.ExtensionFilter).ToArray(),
            ImageConversionMode.Folder => ResolveInputFilePaths(
                settings.InputPath, ImageConversionMode.Folder, settings.IncludeSubfolders, settings.ExtensionFilter).ToArray(),
            _ => Array.Empty<string>()
        };

        if (files.Length == 0)
            throw new InvalidOperationException("No images found to convert.");

        CleanupStaleTempFiles(files, settings);

        // Files that resolve to the same output (e.g. a.bmp + a.tga → a.png) run sequentially.
        var workGroups = files
            .Select((path, index) => new
            {
                Path = path,
                Index = index,
                OutputPath = Path.GetFullPath(ResolveOutputPath(path, settings))
            })
            .GroupBy(item => item.OutputPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Index).ToArray())
            .ToArray();

        var maxParallel = Math.Max(1, Math.Min(settings.MaxParallelConversions, workGroups.Length));
        var results = new ImageConversionResult[files.Length];
        var completed = 0;

        try
        {
            if (maxParallel == 1)
            {
                foreach (var group in workGroups)
                {
                    foreach (var item in group)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var result = await Task.Run(() => ConvertFile(item.Path, settings), cancellationToken);
                        results[item.Index] = result;
                        resultProgress?.Report(result);

                        completed++;
                        progress?.Report((completed, files.Length, Path.GetFileName(item.Path)));
                    }
                }

                return results;
            }

            await Parallel.ForEachAsync(
                workGroups,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxParallel,
                    CancellationToken = cancellationToken
                },
                async (group, token) =>
                {
                    foreach (var item in group)
                    {
                        token.ThrowIfCancellationRequested();

                        var result = await Task.Run(() => ConvertFile(item.Path, settings), token);
                        results[item.Index] = result;
                        resultProgress?.Report(result);

                        var done = Interlocked.Increment(ref completed);
                        progress?.Report((done, files.Length, Path.GetFileName(item.Path)));
                    }
                });

            return results;
        }
        finally
        {
            CleanupStaleTempFiles(files, settings);
        }
    }

    private static ImageConversionResult ConvertFile(string sourcePath, ImageConversionSettings settings)
    {
        long originalSize = 0;
        string? temporaryPath = null;

        try
        {
            originalSize = new FileInfo(sourcePath).Length;

            var outputPath = ResolveOutputPath(sourcePath, settings);
            var deleteSourceAfterSuccess = ShouldDeleteSourceAfterSuccess(sourcePath, outputPath, settings);
            var outputDir = Path.GetDirectoryName(outputPath)!;

            // Write beside the destination and validate before replacing any existing file.
            Directory.CreateDirectory(outputDir);
            temporaryPath = Path.Combine(outputDir, $"{TempFilePrefix}{Guid.NewGuid():N}.tmp");
            var writePath = temporaryPath;

            using var image = LoadImage(sourcePath);
            ApplyResize(image, settings);
            ApplyColorDepth(image, settings);
            ApplyBmpTgaToPngColorMode(image, sourcePath, settings);
            ApplyGameTransparency(image, sourcePath, settings);
            var originalColors = settings.TargetFormat == ImageFormat.Png ? image.TotalColors : 0;
            ApplyFormatSettings(image, settings);

            image.Write(writePath);

            if (settings.TargetFormat == ImageFormat.Png)
            {
                using var encoded = new MagickImage(writePath);
                if (encoded.Width != image.Width || encoded.Height != image.Height ||
                    (originalColors > 1 && encoded.TotalColors <= 1))
                {
                    throw new InvalidDataException("PNG verification failed: dimensions or image detail were lost. The existing file was kept.");
                }
            }

            File.Move(writePath, outputPath, overwrite: true);
            temporaryPath = null;

            var outputSize = new FileInfo(outputPath).Length;

            if (deleteSourceAfterSuccess)
                File.Delete(sourcePath);

            return new ImageConversionResult
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                OriginalSizeBytes = originalSize,
                OutputSizeBytes = outputSize,
                Success = true
            };
        }
        catch (Exception ex)
        {
            return new ImageConversionResult
            {
                SourcePath = sourcePath,
                OutputPath = string.Empty,
                OriginalSizeBytes = originalSize,
                OutputSizeBytes = 0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            DeleteTempFile(temporaryPath);
        }
    }

    private static void CleanupStaleTempFiles(IReadOnlyCollection<string> sourcePaths, ImageConversionSettings settings)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in sourcePaths)
        {
            var sourceDir = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(sourceDir))
                directories.Add(sourceDir);

            var outputDir = Path.GetDirectoryName(ResolveOutputPath(sourcePath, settings));
            if (!string.IsNullOrWhiteSpace(outputDir))
                directories.Add(outputDir);
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var tempPath in SafeEnumerate(() => Directory.EnumerateFiles(directory, TempFilePrefix + "*.tmp")))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(tempPath) <= cutoff)
                        File.Delete(tempPath);
                }
                catch
                {
                    // Another process may still be writing; leave it alone.
                }
            }
        }
    }

    private static void DeleteTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the next run sweeps stale converter temps.
        }
    }

    private static bool ShouldDeleteSourceAfterSuccess(string sourcePath, string outputPath, ImageConversionSettings settings)
    {
        if (!settings.DeleteSourceWhenExtensionChanges ||
            settings.OutputLocation != ImageOutputLocation.SameFolder)
        {
            return false;
        }

        if (IsSameFormat(Path.GetExtension(sourcePath), settings.TargetFormat))
            return false;

        return !Path.GetFullPath(sourcePath).Equals(
            Path.GetFullPath(outputPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyResize(MagickImage image, ImageConversionSettings settings)
    {
        if (settings.ResolutionMode == ImageResolutionMode.Original ||
            settings.TargetWidth <= 0 ||
            settings.TargetHeight <= 0)
        {
            return;
        }

        var geometry = new MagickGeometry((uint)settings.TargetWidth, (uint)settings.TargetHeight)
        {
            IgnoreAspectRatio = true
        };

        image.FilterType = ImageResizeFilterInfo.ToMagickFilter(settings.ResizeFilter);
        image.Interpolate = settings.ResizeFilter == ImageResizeFilter.NearestNeighbor
            ? PixelInterpolateMethod.Nearest
            : PixelInterpolateMethod.Bilinear;
        image.Resize(geometry);
    }

    private static void ApplyColorDepth(MagickImage image, ImageConversionSettings settings)
    {
        switch (settings.ColorDepthMode)
        {
            case ImageColorDepthMode.RemoveAlpha:
                image.BackgroundColor = MagickColors.Black;
                image.Alpha(AlphaOption.Remove);
                break;

            case ImageColorDepthMode.Indexed8Bit:
                image.Quantize(new QuantizeSettings
                {
                    Colors = 256,
                    ColorSpace = IsGrayscale(image) ? ColorSpace.Gray : ColorSpace.sRGB,
                    DitherMethod = DitherMethod.No
                });
                break;

            case ImageColorDepthMode.Keep32BitRgba:
                image.Depth = 8;
                if (!image.HasAlpha)
                    image.Alpha(AlphaOption.Opaque);
                break;
        }
    }

    private static void ApplyBmpTgaToPngColorMode(MagickImage image, string sourcePath, ImageConversionSettings settings)
    {
        if (settings.ColorDepthMode != ImageColorDepthMode.Default ||
            settings.TargetFormat != ImageFormat.Png ||
            !IsBmpOrTga(sourcePath))
        {
            return;
        }

        if (image.ColorType is ColorType.Palette or ColorType.PaletteAlpha)
            image.Depth = 8;

        if (SourceHasAlpha(image))
        {
            image.Depth = 8;
            if (!image.HasAlpha)
                image.Alpha(AlphaOption.Opaque);

            image.ColorType = ColorType.TrueColorAlpha;
            return;
        }

        image.BackgroundColor = MagickColors.Black;
        image.Alpha(AlphaOption.Remove);
        image.ColorType = ColorType.TrueColor;
        image.Depth = 8;
    }

    private static bool IsBmpOrTga(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        return extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SourceHasAlpha(MagickImage image) =>
        image.ColorType is ColorType.PaletteAlpha or ColorType.GrayscaleAlpha || image.HasAlpha;

    // ---------------------------------------------------------------- Satisfy / PKO pink key

    private static void ApplyGameTransparency(MagickImage image, string sourcePath, ImageConversionSettings settings)
    {
        if (settings.GameTransparencyMode != ImageGameTransparencyMode.SatisfyPinkKey ||
            settings.ColorDepthMode == ImageColorDepthMode.RemoveAlpha)
        {
            return;
        }

        if (!settings.ApplyGameTransparencyToPng && IsPngPath(sourcePath))
            return;

        // Expand palettes so key matching works on true RGBA samples.
        ExpandIndexedToTrueColor(image);

        if (!image.HasAlpha)
            image.Alpha(AlphaOption.Opaque);

        image.Depth = 8;
        image.ColorType = ColorType.TrueColorAlpha;

        if (settings.PinkKeyMatchMode == PinkKeyMatchMode.LegacySoftFuzz)
        {
            ApplyLegacySoftPinkKey(image);
            return;
        }

        // Pixel scan: only exact / near-key magenta, so artwork that merely contains pink is left alone.
        ApplyPrecisePinkKey(image, settings.PinkKeyMatchMode);
    }

    private static void ApplyLegacySoftPinkKey(MagickImage image)
    {
        var previousFuzz = image.ColorFuzz;
        try
        {
            image.ColorFuzz = new Percentage(1.5);
            image.Transparent(new MagickColor("#ff00ff"));
            image.Transparent(new MagickColor("#ff01ff"));
            image.Transparent(new MagickColor("#ff02ff"));
        }
        finally
        {
            image.ColorFuzz = previousFuzz;
        }
    }

    private static void ApplyPrecisePinkKey(MagickImage image, PinkKeyMatchMode matchMode)
    {
        using var pixels = image.GetPixels();
        if (pixels.Channels < 4)
            return;

        var max = Quantum.Max;
        var width = (int)image.Width;
        var height = (int)image.Height;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels.GetPixel(x, y);
                if (!IsPinkKeyColor(pixel.GetChannel(0), pixel.GetChannel(1), pixel.GetChannel(2), max, matchMode))
                    continue;

                // Fully transparent + zero RGB so later PNG quantize cannot smear magenta.
                pixel.SetChannel(0, 0);
                pixel.SetChannel(1, 0);
                pixel.SetChannel(2, 0);
                pixel.SetChannel(3, 0);
            }
        }
    }

    /// <summary>
    /// PKO / Satisfy chroma key is saturated magenta with green near zero.
    /// Deliberately does NOT use Magick color-distance fuzz (that matched real artwork).
    /// </summary>
    internal static bool IsPinkKeyColor(ushort r, ushort g, ushort b, ushort max, PinkKeyMatchMode matchMode)
    {
        // Work in 8-bit so thresholds match authored BMP/TGA palette values.
        var r8 = ScaleQuantumToByte(r, max);
        var g8 = ScaleQuantumToByte(g, max);
        var b8 = ScaleQuantumToByte(b, max);

        return matchMode switch
        {
            PinkKeyMatchMode.Exact => r8 == 255 && b8 == 255 && g8 is 0 or 1 or 2,
            // Near-key: classic hard chroma (and tiny encoder jitter), not soft pink art.
            PinkKeyMatchMode.NearKey => r8 >= 250 && b8 >= 250 && g8 <= 5,
            _ => false
        };
    }

    private static byte ScaleQuantumToByte(ushort value, ushort max) =>
        max == 0 ? (byte)0 : (byte)((value * 255u + max / 2u) / max);

    // ---------------------------------------------------------------- output

    public static string ResolveOutputPath(string sourcePath, ImageConversionSettings settings)
    {
        var sourceExt = Path.GetExtension(sourcePath);
        var extension = IsSameFormat(sourceExt, settings.TargetFormat)
            ? sourceExt
            : GetExtension(settings.TargetFormat);
        var fileName = Path.GetFileNameWithoutExtension(sourcePath) + extension;

        if (settings.OutputLocation == ImageOutputLocation.CustomFolder &&
            !string.IsNullOrWhiteSpace(settings.OutputFolder))
        {
            if (settings.Mode == ImageConversionMode.Folder)
            {
                // Preserve the subfolder structure under the custom output folder.
                var relativeDir = Path.GetDirectoryName(Path.GetRelativePath(settings.InputPath, sourcePath));
                var targetDir = string.IsNullOrEmpty(relativeDir)
                    ? settings.OutputFolder
                    : Path.Combine(settings.OutputFolder, relativeDir);

                return Path.Combine(targetDir, fileName);
            }

            return Path.Combine(settings.OutputFolder, fileName);
        }

        return Path.Combine(Path.GetDirectoryName(sourcePath)!, fileName);
    }

    private static void ApplyFormatSettings(MagickImage image, ImageConversionSettings settings)
    {
        if (settings.OptimizeSize)
        {
            image.Strip();
            image.Quality = settings.TargetFormat switch
            {
                ImageFormat.Jpeg => (uint)settings.JpegQuality,
                ImageFormat.WebP => (uint)settings.WebPQuality,
                _ => image.Quality
            };
        }

        if (settings.OptimizeSize &&
            settings.TargetFormat == ImageFormat.Png &&
            settings.ColorDepthMode != ImageColorDepthMode.Indexed8Bit &&
            settings.ColorDepthMode != ImageColorDepthMode.Keep32BitRgba)
        {
            // After chroma-key → alpha, palette quantize/dither often reintroduces magenta holes.
            if (settings.GameTransparencyMode == ImageGameTransparencyMode.SatisfyPinkKey)
            {
                image.Format = MagickFormat.Png;
                ApplyMaxPngCompression(image);
                return;
            }

            ApplyPngPaletteOptimization(image, settings);
            return;
        }

        switch (settings.TargetFormat)
        {
            case ImageFormat.Png:
                image.Format = settings.ColorDepthMode == ImageColorDepthMode.Keep32BitRgba
                    ? MagickFormat.Png32
                    : MagickFormat.Png;
                if (settings.ColorDepthMode == ImageColorDepthMode.Indexed8Bit)
                    image.Settings.SetDefine(MagickFormat.Png, "color-type", "3");
                if (settings.OptimizeSize)
                    ApplyMaxPngCompression(image);
                break;

            case ImageFormat.Jpeg:
                image.Format = MagickFormat.Jpeg;
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                if (settings.OptimizeSize)
                    image.Quality = (uint)settings.JpegQuality;
                break;

            case ImageFormat.WebP:
                image.Format = MagickFormat.WebP;
                if (settings.OptimizeSize)
                {
                    image.Quality = (uint)settings.WebPQuality;
                    image.Settings.SetDefine(MagickFormat.WebP, "method", "6");
                }
                break;

            case ImageFormat.Bmp:
                image.Format = MagickFormat.Bmp;
                break;

            case ImageFormat.Tiff:
                image.Format = MagickFormat.Tiff;
                if (settings.OptimizeSize)
                    image.Settings.SetDefine(MagickFormat.Tiff, "compress", "lzw");
                break;

            case ImageFormat.Gif:
                image.Format = MagickFormat.Gif;
                break;

            case ImageFormat.Tga:
                image.Format = MagickFormat.Tga;
                break;

            case ImageFormat.Dds:
                image.Format = MagickFormat.Dds;
                if (settings.OptimizeSize)
                    image.Settings.SetDefine(MagickFormat.Dds, "compression", "dxt5");
                break;
        }
    }

    private static void ApplyPngPaletteOptimization(MagickImage image, ImageConversionSettings settings)
    {
        ExpandIndexedToTrueColor(image);

        var colorCount = (uint)Math.Clamp(settings.PngColorCount, 16, 256);

        if (ShouldSkipPaletteQuantize(image, colorCount))
        {
            image.Format = MagickFormat.Png;
            ApplyMaxPngCompression(image);
            return;
        }

        image.Quantize(new QuantizeSettings
        {
            Colors = colorCount,
            ColorSpace = ColorSpace.sRGB,
            DitherMethod = settings.PngDither ? DitherMethod.FloydSteinberg : DitherMethod.No,
            TreeDepth = 8
        });

        // PNG8 applies another binary-alpha reduction. The regular encoder can
        // write our already-quantized palette without discarding alpha levels.
        image.Format = MagickFormat.Png;
        image.Settings.SetDefine(MagickFormat.Png, "color-type", "3");
        ApplyMaxPngCompression(image);
    }

    private static bool IsGrayscale(MagickImage image) =>
        image.ColorSpace is ColorSpace.Gray or ColorSpace.LinearGray ||
        image.ColorType is ColorType.Bilevel or ColorType.Grayscale or ColorType.GrayscaleAlpha;

    private static bool ShouldSkipPaletteQuantize(MagickImage image, uint targetColors) =>
        // Grayscale already stores compact luminance samples. Quantizing it in sRGB can
        // collapse Magick's grayscale-alpha channels. Keep alpha and hidden RGB lossless
        // for normal optimization; Indexed8Bit is the explicit palette request.
        IsGrayscale(image) || image.HasAlpha || image.TotalColors <= targetColors;

    private static void ApplyMaxPngCompression(MagickImage image)
    {
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
        image.Settings.SetDefine(MagickFormat.Png, "compression-strategy", "1");
    }

    private static void ExpandIndexedToTrueColor(MagickImage image)
    {
        // Grayscale can also be PseudoClass, and DirectClass can retain a stale colormap.
        // Neither is a reason to rebuild a valid palette. Expand only actual indexed color
        // images; BMP palette repair happens on read (BmpPaletteNormalizer).
        if (image.ColorType is not (ColorType.Palette or ColorType.PaletteAlpha))
            return;

        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        image.Depth = 8;
    }

    private static bool IsSameFormat(string sourceExtension, ImageFormat format) =>
        format switch
        {
            ImageFormat.Png => sourceExtension.Equals(".png", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Jpeg => sourceExtension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                sourceExtension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            ImageFormat.WebP => sourceExtension.Equals(".webp", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Bmp => sourceExtension.Equals(".bmp", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Tiff => sourceExtension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                                sourceExtension.Equals(".tiff", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Gif => sourceExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Tga => sourceExtension.Equals(".tga", StringComparison.OrdinalIgnoreCase),
            ImageFormat.Dds => sourceExtension.Equals(".dds", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static string GetExtension(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.WebP => ".webp",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Tiff => ".tiff",
        ImageFormat.Gif => ".gif",
        ImageFormat.Tga => ".tga",
        ImageFormat.Dds => ".dds",
        _ => ".png"
    };
}
