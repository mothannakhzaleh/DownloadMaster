using System.IO;

namespace DownloadMaster.Services;

/// <summary>
/// Satisfy / PKO BMPs often store 255 palette entries (biClrUsed=255) while pixels still use index 255.
/// ImageMagick rejects that during read, before any in-memory palette expansion can run.
/// </summary>
internal static class BmpPaletteNormalizer
{
    private const int FileHeaderSize = 14;
    private const int Bmp8PaletteEntries = 256;

    public static byte[]? TryNormalizeForRead(string path)
    {
        if (!path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            return null;

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 54 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            return null;

        var headerSize = BitConverter.ToInt32(bytes, 14);
        if (headerSize < 40)
            return null;

        var bpp = BitConverter.ToInt16(bytes, 28);
        if (bpp != 8)
            return null;

        var pixelOffset = BitConverter.ToInt32(bytes, 10);
        var paletteStart = FileHeaderSize + headerSize;
        if (pixelOffset <= paletteStart || pixelOffset > bytes.Length)
            return null;

        var paletteEntries = (pixelOffset - paletteStart) / 4;
        if (paletteEntries <= 0 || paletteEntries > Bmp8PaletteEntries)
            return null;

        var width = BitConverter.ToInt32(bytes, 18);
        var height = Math.Abs(BitConverter.ToInt32(bytes, 22));
        if (width <= 0 || height <= 0)
            return null;

        var rowSize = ((width * bpp + 31) / 32) * 4;
        var pixelBytes = (long)rowSize * height;
        if (pixelOffset + pixelBytes > bytes.Length)
            return null;

        var maxIndex = ScanMaxPixelIndex(bytes, pixelOffset, width, height, rowSize);
        if (paletteEntries >= Bmp8PaletteEntries && maxIndex < paletteEntries)
            return null;

        var missingEntries = Bmp8PaletteEntries - paletteEntries;
        if (missingEntries <= 0)
            return null;

        var insertSize = missingEntries * 4;
        var result = new byte[bytes.Length + insertSize];

        Array.Copy(bytes, 0, result, 0, pixelOffset);

        var fillColor = new byte[4];
        Array.Copy(bytes, pixelOffset - 4, fillColor, 0, 4);
        for (var i = 0; i < missingEntries; i++)
            Array.Copy(fillColor, 0, result, pixelOffset + i * 4, 4);

        Array.Copy(bytes, pixelOffset, result, pixelOffset + insertSize, bytes.Length - pixelOffset);

        var newOffset = pixelOffset + insertSize;
        Array.Copy(BitConverter.GetBytes(newOffset), 0, result, 10, 4);
        Array.Copy(BitConverter.GetBytes(result.Length), 0, result, 2, 4);
        Array.Copy(BitConverter.GetBytes(0), 0, result, 46, 4);

        return result;
    }

    private static int ScanMaxPixelIndex(byte[] bytes, int offset, int width, int height, int rowSize)
    {
        var max = 0;
        for (var y = 0; y < height; y++)
        {
            var rowStart = offset + y * rowSize;
            for (var x = 0; x < width; x++)
            {
                var index = bytes[rowStart + x];
                if (index > max)
                    max = index;
            }
        }

        return max;
    }
}
