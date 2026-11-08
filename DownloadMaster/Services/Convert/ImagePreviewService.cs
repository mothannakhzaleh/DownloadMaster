using Avalonia.Media.Imaging;
using DownloadMaster.Models;
using ImageMagick;

namespace DownloadMaster.Services;

public enum ImagePreviewState
{
    Empty,
    Image,
    FileMissing,
    FolderMissing,
    Unsupported,
    FilteredOut,
    FolderEmpty,
    Failed
}

public sealed class ImagePreviewInfo
{
    public Bitmap? Image { get; init; }
    public ImagePreviewState State { get; init; }
    public string Title { get; init; } = string.Empty;

    /// <summary>For Image: "W × H • size • FORMAT"; for Failed: the error message.</summary>
    public string Details { get; init; } = string.Empty;

    public bool HasPreview => Image is not null;
}

/// <summary>
/// Cross-platform thumbnail + metadata loader for the Image Convert tab (replaces MediaConvertor's
/// WPF BitmapImage preview). Decodes with Magick.NET (so BMP/TGA/DDS/repaired PNGs work), downsizes,
/// and hands a PNG stream to Avalonia's Bitmap. Safe to call off the UI thread.
/// </summary>
public sealed class ImagePreviewService
{
    private const int ThumbnailSize = 512;

    public ImagePreviewInfo LoadPreview(
        string? path,
        bool isFolderMode,
        ImageExtensionFilterSettings? extensionFilter = null,
        string? selectedFile = null)
    {
        if (!string.IsNullOrWhiteSpace(selectedFile) && File.Exists(selectedFile))
            return LoadImagePreview(selectedFile);

        if (string.IsNullOrWhiteSpace(path))
            return new ImagePreviewInfo { State = ImagePreviewState.Empty };

        if (isFolderMode || Directory.Exists(path))
        {
            return Directory.Exists(path)
                ? new ImagePreviewInfo { State = ImagePreviewState.FolderEmpty, Title = Path.GetFileName(path) }
                : new ImagePreviewInfo { State = ImagePreviewState.FolderMissing, Title = path };
        }

        if (!File.Exists(path))
            return new ImagePreviewInfo { State = ImagePreviewState.FileMissing, Title = path };

        if (!ImageConversionService.IsSupportedImage(path))
            return new ImagePreviewInfo { State = ImagePreviewState.Unsupported, Title = Path.GetFileName(path) };

        if (extensionFilter?.IsActive == true && !extensionFilter.Matches(path))
            return new ImagePreviewInfo { State = ImagePreviewState.FilteredOut, Title = Path.GetFileName(path) };

        return LoadImagePreview(path);
    }

    public static ImagePreviewInfo LoadImagePreview(string path)
    {
        try
        {
            using var image = ImageConversionService.LoadImage(path);
            var length = new FileInfo(path).Length;
            var details = $"{image.Width} × {image.Height}  •  {FormatHelpers.FormatBytes(length)}  •  {image.Format.ToString().ToUpperInvariant()}";

            // First frame only (GIF/TIFF/ICO) and a small thumbnail keep big folders responsive.
            if (image.Width > ThumbnailSize || image.Height > ThumbnailSize)
            {
                image.FilterType = FilterType.Triangle;
                image.Thumbnail(new MagickGeometry(ThumbnailSize, ThumbnailSize));
            }

            using var stream = new MemoryStream();
            image.Format = MagickFormat.Png32;
            image.Write(stream);
            stream.Position = 0;

            return new ImagePreviewInfo
            {
                Image = new Bitmap(stream),
                State = ImagePreviewState.Image,
                Title = Path.GetFileName(path),
                Details = details
            };
        }
        catch (Exception ex)
        {
            return new ImagePreviewInfo
            {
                State = ImagePreviewState.Failed,
                Title = Path.GetFileName(path),
                Details = ex.Message
            };
        }
    }
}
