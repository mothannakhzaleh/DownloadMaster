using System.IO;
using DownloadMaster.Models;
using ImageMagick;

namespace DownloadMaster.Services;

public sealed class ImageConversionService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif",
        ".tga", ".dds", ".ico", ".heic", ".heif", ".avif"
    };

    public static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    public IReadOnlyList<ImageInputFileItem> GetInputFileItems(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders)
    {
        return ResolveInputFiles(inputPath, mode, includeSubfolders)
            .Select(CreateInputFileItemFast)
            .ToList();
    }

    public IReadOnlyList<string> ResolveInputFilePaths(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders) =>
        ResolveInputFiles(inputPath, mode, includeSubfolders).ToList();

    public static ImageConversionMode ResolveInputMode(string inputPath, ImageConversionMode selectedMode)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return selectedMode;

        if (Directory.Exists(inputPath))
            return ImageConversionMode.Folder;

        return selectedMode;
    }

    private IEnumerable<string> ResolveInputFiles(
        string inputPath,
        ImageConversionMode mode,
        bool includeSubfolders)
    {
        var effectiveMode = ResolveInputMode(inputPath, mode);

        return effectiveMode switch
        {
            ImageConversionMode.SingleFile when File.Exists(inputPath) => [inputPath],
            ImageConversionMode.Folder when Directory.Exists(inputPath) =>
                GetImageFiles(inputPath, includeSubfolders),
            _ => Array.Empty<string>()
        };
    }

    public IEnumerable<string> GetImageFiles(string folderPath, bool includeSubfolders)
    {
        return EnumerateImageFiles(folderPath, includeSubfolders)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateImageFiles(string folderPath, bool includeSubfolders)
    {
        foreach (var filePath in Directory.EnumerateFiles(folderPath))
        {
            if (SupportedExtensions.Contains(Path.GetExtension(filePath)))
                yield return filePath;
        }

        if (!includeSubfolders)
            yield break;

        foreach (var directoryPath in Directory.EnumerateDirectories(folderPath))
        {
            if (ShouldSkipDirectory(directoryPath))
                continue;

            foreach (var filePath in EnumerateImageFiles(directoryPath, includeSubfolders: true))
                yield return filePath;
        }
    }

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase))
            return true;

        var attributes = File.GetAttributes(directoryPath);
        return (attributes & FileAttributes.Hidden) != 0;
    }

    private static ImageInputFileItem CreateInputFileItemFast(string path)
    {
        var fileInfo = new FileInfo(path);
        return new ImageInputFileItem
        {
            FullPath = path,
            Details = FormatBytes(fileInfo.Length)
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
                Details = $"{image.Width} × {image.Height}  •  {FormatBytes(fileInfo.Length)}  •  {image.Format.ToString().ToUpperInvariant()}"
            };
        }
        catch
        {
            return CreateInputFileItemFast(path);
        }
    }

    public static MagickImage LoadImage(string path)
    {
        var image = new MagickImage(path);
        ExpandIndexedToTrueColor(image);
        return image;
    }

    public async Task<IReadOnlyList<ImageConversionResult>> ConvertAsync(
        ImageConversionSettings settings,
        IProgress<(int current, int total, string message)>? progress = null,
        IProgress<ImageConversionResult>? resultProgress = null,
        CancellationToken cancellationToken = default)
    {
        var files = settings.Mode switch
        {
            ImageConversionMode.SingleFile => ResolveInputFilePaths(
                settings.InputPath, ImageConversionMode.SingleFile, false).ToArray(),
            ImageConversionMode.Folder => ResolveInputFilePaths(
                settings.InputPath, ImageConversionMode.Folder, settings.IncludeSubfolders).ToArray(),
            _ => Array.Empty<string>()
        };

        if (files.Length == 0)
            throw new InvalidOperationException("No images found to convert.");

        CleanupStaleTempFiles(files, settings);

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

            CleanupStaleTempFiles(files, settings);
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

        CleanupStaleTempFiles(files, settings);
        return results;
    }

    private ImageConversionResult ConvertFile(string sourcePath, ImageConversionSettings settings)
    {
        long originalSize = 0;
        string? temporaryPath = null;

        try
        {
            originalSize = new FileInfo(sourcePath).Length;

            var outputPath = ResolveOutputPath(sourcePath, settings);
            var inPlaceOverwrite = settings.OutputLocation == ImageOutputLocation.SameFolder &&
                IsSameFormat(Path.GetExtension(sourcePath), settings.TargetFormat);

            var writePath = inPlaceOverwrite
                ? Path.Combine(Path.GetDirectoryName(sourcePath)!, $".imgcvt_{Guid.NewGuid():N}.tmp")
                : outputPath;

            if (inPlaceOverwrite)
                temporaryPath = writePath;

            Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);

            using var image = LoadImage(sourcePath);
            ApplyResize(image, settings);
            ApplyFormatSettings(image, settings, sourcePath);

            image.Write(writePath);

            if (inPlaceOverwrite)
            {
                File.Move(writePath, sourcePath, overwrite: true);
                temporaryPath = null;
                outputPath = sourcePath;
            }
            else
            {
                outputPath = writePath;
            }

            var outputSize = new FileInfo(outputPath).Length;

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

        image.FilterType = FilterType.Lanczos;
        image.Resize(geometry);
    }

    private static void ApplyFormatSettings(MagickImage image, ImageConversionSettings settings, string sourcePath)
    {
        var optimizePng = settings.OptimizeSize &&
            (settings.TargetFormat == ImageFormat.Png ||
             (IsSameFormat(Path.GetExtension(sourcePath), ImageFormat.Png) &&
              settings.OutputLocation == ImageOutputLocation.SameFolder));

        if (settings.OptimizeSize)
            image.Strip();

        if (optimizePng)
        {
            ApplyPngPaletteOptimization(image, settings);
            return;
        }

        switch (settings.TargetFormat)
        {
            case ImageFormat.Png:
                image.Format = MagickFormat.Png;
                if (settings.OptimizeSize)
                {
                    image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                    image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
                    image.Settings.SetDefine(MagickFormat.Png, "compression-strategy", "1");
                }
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

        image.Quantize(new QuantizeSettings
        {
            Colors = colorCount,
            ColorSpace = ColorSpace.sRGB,
            DitherMethod = settings.PngDither ? DitherMethod.FloydSteinberg : DitherMethod.No,
            TreeDepth = 8
        });

        image.Format = MagickFormat.Png8;
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
        image.Settings.SetDefine(MagickFormat.Png, "compression-strategy", "1");
    }

    private static string ResolveOutputPath(string sourcePath, ImageConversionSettings settings)
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
                var relativePath = Path.GetRelativePath(settings.InputPath, sourcePath);
                var relativeDir = Path.GetDirectoryName(relativePath);

                var targetDir = string.IsNullOrEmpty(relativeDir)
                    ? settings.OutputFolder
                    : Path.Combine(settings.OutputFolder, relativeDir);

                return Path.Combine(targetDir, fileName);
            }

            return Path.Combine(settings.OutputFolder, fileName);
        }

        return Path.Combine(Path.GetDirectoryName(sourcePath)!, fileName);
    }

    private static void ExpandIndexedToTrueColor(MagickImage image)
    {
        var isIndexed = image.ClassType == ClassType.Pseudo ||
                        image.ColorType is ColorType.Palette or ColorType.PaletteAlpha;

        if (!isIndexed && image.ColormapSize <= 0)
            return;

        var originalSize = (int)Math.Clamp((int)image.ColormapSize, 0, 256);
        if (originalSize <= 0 && !isIndexed)
        {
            image.ColorType = ColorType.TrueColor;
            image.Depth = 8;
            return;
        }

        var fallback = originalSize > 0
            ? image.GetColormapColor(0) ?? MagickColors.Black
            : MagickColors.Black;

        var preserved = new IMagickColor<ushort>?[256];
        for (var i = 0; i < originalSize; i++)
            preserved[i] = image.GetColormapColor(i);

        image.ColormapSize = 256;

        for (var i = 0; i < 256; i++)
            image.SetColormapColor(i, preserved[i] ?? fallback);

        var preserveAlpha = image.ColorType == ColorType.PaletteAlpha || image.HasAlpha;
        image.ColorType = preserveAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
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

            foreach (var tempPath in Directory.EnumerateFiles(directory, ".imgcvt_*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(tempPath) <= cutoff)
                        File.Delete(tempPath);
                }
                catch
                {
                    // Best effort.
                }
            }
        }
    }

    private static void DeleteTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try { File.Delete(path); } catch { /* ignore */ }
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
