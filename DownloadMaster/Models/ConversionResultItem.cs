using System.IO;

namespace DownloadMaster.Models;

public sealed class ConversionResultItem
{
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required string SizeChange { get; init; }
    public required string Detail { get; init; }
    public bool IsSuccess { get; init; }
    public bool IsFailed => !IsSuccess && Status is not "Pending" and not "Converting";

    public static ConversionResultItem Pending(string sourcePath) => new()
    {
        SourcePath = sourcePath,
        FileName = Path.GetFileName(sourcePath),
        Status = "Pending",
        SizeChange = "—",
        Detail = "Waiting",
        IsSuccess = false
    };

    public static ConversionResultItem Converting(string sourcePath) => new()
    {
        SourcePath = sourcePath,
        FileName = Path.GetFileName(sourcePath),
        Status = "Converting",
        SizeChange = "—",
        Detail = "Processing...",
        IsSuccess = false
    };

    public static ConversionResultItem FromImageResult(ImageConversionResult result)
    {
        if (result.Success)
        {
            return new ConversionResultItem
            {
                SourcePath = result.SourcePath,
                FileName = Path.GetFileName(result.SourcePath),
                Status = "OK",
                SizeChange = $"{FormatBytes(result.OriginalSizeBytes)} → {FormatBytes(result.OutputSizeBytes)} ({result.SizeReductionPercent:F1}%)",
                Detail = Path.GetFileName(result.OutputPath),
                IsSuccess = true
            };
        }

        return new ConversionResultItem
        {
            SourcePath = result.SourcePath,
            FileName = Path.GetFileName(result.SourcePath),
            Status = "Failed",
            SizeChange = FormatBytes(result.OriginalSizeBytes),
            Detail = result.ErrorMessage ?? "Unknown error",
            IsSuccess = false
        };
    }

    public static ConversionResultItem FromVideoResult(VideoConversionResult result)
    {
        if (result.Success)
        {
            var detail = Directory.Exists(result.OutputPath)
                ? $"{Path.GetFileName(result.OutputPath)} ({result.ExtractedFrameCount ?? 0} frames)"
                : Path.GetFileName(result.OutputPath);

            return new ConversionResultItem
            {
                SourcePath = result.SourcePath,
                FileName = Path.GetFileName(result.SourcePath),
                Status = "OK",
                SizeChange = $"{FormatBytes(result.OriginalSizeBytes)} → {FormatBytes(result.OutputSizeBytes)} ({result.SizeReductionPercent:F1}%)",
                Detail = detail,
                IsSuccess = true
            };
        }

        return new ConversionResultItem
        {
            SourcePath = result.SourcePath,
            FileName = Path.GetFileName(result.SourcePath),
            Status = "Failed",
            SizeChange = FormatBytes(result.OriginalSizeBytes),
            Detail = result.ErrorMessage ?? "Unknown error",
            IsSuccess = false
        };
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
