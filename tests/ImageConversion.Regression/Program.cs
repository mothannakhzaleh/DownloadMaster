// Headless regressions for DownloadMaster's Image Convert service (ported from MediaConvertor and extended).
// Exit code 0 = all checks passed; any failure throws and exits non-zero (CI friendly).
using System.Buffers.Binary;
using System.IO.Compression;
using DownloadMaster.Models;
using DownloadMaster.Services;
using ImageMagick;

var scratch = Path.Combine(Path.GetTempPath(), "DownloadMaster-image-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var passed = 0;

try
{
    // ------------------------------------------------------------ PNG optimization (MediaConvertor suite)
    var fixtures = new List<(string Path, bool Lossless)>();
    foreach (var type in new byte[] { 0, 4, 2, 6 })
    {
        var channels = type switch { 0 => 1, 4 => 2, 2 => 3, _ => 4 };
        var data = new byte[32 * 32 * channels];
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var offset = (y * 32 + x) * channels;
                data[offset] = (byte)(40 + (x * 7 + y * 3) % 216);
                if (type is 2 or 6)
                {
                    data[offset + 1] = (byte)(x * 8);
                    data[offset + 2] = (byte)(y * 8);
                }
                if (type is 4 or 6)
                    data[offset + channels - 1] = (byte)(y * 255 / 31);
            }
        var path = Path.Combine(scratch, $"type-{type}.png");
        WritePng(path, 32, 32, 8, type, data);
        fixtures.Add((path, type != 2));
    }

    // A short palette and partially transparent entries exercise repeated reads.
    var palettePath = Path.Combine(scratch, "palette.png");
    WritePng(palettePath, 16, 16, 8, 3,
        Enumerable.Range(0, 256).Select(i => (byte)(i % 4)).ToArray(),
        [255, 80, 90, 10, 220, 30, 40, 50, 240, 255, 255, 255],
        [255, 128, 0, 200]);
    fixtures.Add((palettePath, true));

    var gray16Path = Path.Combine(scratch, "gray16.png");
    var gray16 = new byte[512];
    for (var i = 0; i < 256; i++) BinaryPrimitives.WriteUInt16BigEndian(gray16.AsSpan(i * 2), (ushort)(i * 257));
    WritePng(gray16Path, 16, 16, 16, 0, gray16);
    fixtures.Add((gray16Path, true));

    var clearPath = Path.Combine(scratch, "transparent-white.png");
    WritePng(clearPath, 4, 4, 8, 6, Enumerable.Range(0, 64).Select(i => (byte)(i % 4 == 3 ? 0 : 255)).ToArray());
    fixtures.Add((clearPath, true));

    fixtures.AddRange(args.Select(path => (Path.GetFullPath(path), true)));

    foreach (var fixture in fixtures)
    {
        await CheckPng(fixture.Path, ImageColorDepthMode.Default, 256, false, fixture.Lossless);
        await CheckPng(fixture.Path, ImageColorDepthMode.Default, 16, true, fixture.Lossless);
        await CheckPng(fixture.Path, ImageColorDepthMode.Keep32BitRgba, 16, true, true);
        if (!fixture.Path.EndsWith("transparent-white.png", StringComparison.Ordinal))
            await CheckPng(fixture.Path, ImageColorDepthMode.Indexed8Bit, 256, false, false);
    }

    // ------------------------------------------------------------ failed writes keep existing files
    var invalid = Path.Combine(scratch, "broken.png");
    byte[] broken = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0];
    File.WriteAllBytes(invalid, broken);
    var failure = await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = invalid, TargetFormat = ImageFormat.Png, OptimizeSize = true
    });
    Require(!failure.Single().Success && File.ReadAllBytes(invalid).SequenceEqual(broken), "failed in-place input changed");

    var existingDir = Path.Combine(scratch, "existing");
    Directory.CreateDirectory(existingDir);
    File.Copy(fixtures[0].Path, Path.Combine(existingDir, "broken.png"));
    var originalDestination = File.ReadAllBytes(Path.Combine(existingDir, "broken.png"));
    failure = await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = invalid, TargetFormat = ImageFormat.Png, OptimizeSize = true,
        OutputLocation = ImageOutputLocation.CustomFolder, OutputFolder = existingDir
    });
    Require(!failure.Single().Success &&
            File.ReadAllBytes(Path.Combine(existingDir, "broken.png")).SequenceEqual(originalDestination),
        "failed conversion changed existing destination");
    passed += 2;

    // ------------------------------------------------------------ game texture read repairs
    await CheckIenxRepair();
    await CheckBmp255Palette();

    // ------------------------------------------------------------ Satisfy / PKO pink key
    await CheckPinkKey(PinkKeyMatchMode.NearKey, expectTransparent: [true, true, false, false]);
    await CheckPinkKey(PinkKeyMatchMode.Exact, expectTransparent: [true, false, false, false]);
    await CheckPinkKeySkipsPngByDefault();
    CheckPinkKeyClassifier();

    // ------------------------------------------------------------ formats, resize, folders, filters
    await CheckAllOutputFormats();
    await CheckResize();
    await CheckFolderBatchCustomOutputAndFilters();
    await CheckDeleteSourceWhenExtensionChanges();

    Require(!Directory.EnumerateFiles(scratch, ".imgcvt_*", SearchOption.AllDirectories).Any(), "temporary image files leaked");
    Console.WriteLine(
        $"PASS {passed} image checks: grayscale, alpha, palettes, dithering, RGBA, indexed, in-place and failed writes, " +
        "IENX/BMP repairs, pink key, all formats, resize, folder batch, filters, delete-source.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}
finally
{
    // Only our unique generated directory is removed; supplied textures are read-only.
    try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
}

async Task CheckPng(string source, ImageColorDepthMode mode, int colors, bool dither, bool lossless)
{
    var destination = Path.Combine(scratch, $"case-{passed:D3}");
    Directory.CreateDirectory(destination);
    var path = Path.Combine(destination, Path.GetFileName(source));
    File.Copy(source, path);
    using var original = new MagickImage(source);
    var expected = Rgba(original);
    var service = new ImageConversionService();
    for (var pass = 0; pass < 2; pass++)
    {
        var result = (await service.ConvertAsync(new ImageConversionSettings
        {
            InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true,
            ColorDepthMode = mode, PngColorCount = colors, PngDither = dither
        })).Single();
        Require(result.Success, $"{Path.GetFileName(source)} {mode}/{colors}, pass {pass}: {result.ErrorMessage}");
        using var actual = new MagickImage(path);
        var rgba = Rgba(actual);
        Require(actual.Width == original.Width && actual.Height == original.Height, "dimensions changed");
        if (lossless)
            Require(expected.SequenceEqual(rgba), $"{Path.GetFileName(source)} {mode}/{colors}: RGBA changed");
        if (original.TotalColors > 1)
            Require(actual.TotalColors > 1, "image detail collapsed");
        if (Enumerable.Range(0, expected.Length / 4).Any(i => expected[i * 4 + 3] > 0))
            Require(Enumerable.Range(0, rgba.Length / 4).Any(i => rgba[i * 4 + 3] > 0), "visible image became transparent");
        if (mode == ImageColorDepthMode.Indexed8Bit)
            Require(File.ReadAllBytes(path)[25] == 3, "explicit indexed output has no palette");
        if (mode == ImageColorDepthMode.Keep32BitRgba)
            Require(File.ReadAllBytes(path)[25] == 6, "keep RGBA did not retain RGBA format");
    }
    passed++;
}

async Task CheckIenxRepair()
{
    var dir = NewCaseDir("ienx");
    var path = Path.Combine(dir, "ienx.png");
    WritePng(path, 8, 8, 8, 2, Enumerable.Range(0, 8 * 8 * 3).Select(i => (byte)(i * 5)).ToArray());
    var bytes = File.ReadAllBytes(path);
    // Corrupt the trailer chunk type: IEND → IENX (CRC no longer matches).
    bytes[^5] = (byte)'X'; // ...IEND + CRC: "D" is the 5th byte from the end
    File.WriteAllBytes(path, bytes);

    using (var loaded = ImageConversionService.LoadImage(path))
        Require(loaded.Width == 8 && loaded.Height == 8, "IENX PNG did not load");

    var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true
    })).Single();
    Require(result.Success, "IENX PNG conversion failed: " + result.ErrorMessage);
    using var fixedImage = new MagickImage(path);
    Require(fixedImage.Width == 8, "IENX PNG output is wrong");
    passed++;
}

async Task CheckBmp255Palette()
{
    var dir = NewCaseDir("bmp255");
    var path = Path.Combine(dir, "pal255.bmp");
    // 255 palette entries while pixels still reference index 255 (Satisfy / PKO exports).
    var palette = Enumerable.Range(0, 255).Select(i => ((byte)i, (byte)(255 - i), (byte)(i / 2))).ToArray();
    var pixels = Enumerable.Range(0, 16).Select(i => (byte)(i == 15 ? 255 : i * 16)).ToArray();
    WriteBmp8(path, 4, 4, palette, pixels);

    using (var loaded = ImageConversionService.LoadImage(path))
        Require(loaded.Width == 4 && loaded.Height == 4, "255-entry BMP did not load");

    var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true
    })).Single();
    Require(result.Success, "255-entry BMP → PNG failed: " + result.ErrorMessage);
    using var png = new MagickImage(result.OutputPath);
    Require(png.Width == 4 && png.TotalColors > 1, "255-entry BMP → PNG lost detail");
    passed++;
}

async Task CheckPinkKey(PinkKeyMatchMode mode, bool[] expectTransparent)
{
    var dir = NewCaseDir("pink-" + mode);
    var path = Path.Combine(dir, "key.bmp");
    // #FF00FF, near-key #FB04FB, artistic pink #FF80FF, white.
    WriteBmp8(path, 4, 1,
        [(255, 0, 255), (251, 4, 251), (255, 128, 255), (255, 255, 255)],
        [0, 1, 2, 3]);

    var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true,
        GameTransparencyMode = ImageGameTransparencyMode.SatisfyPinkKey, PinkKeyMatchMode = mode
    })).Single();
    Require(result.Success, $"pink key {mode} failed: {result.ErrorMessage}");

    using var png = new MagickImage(result.OutputPath);
    var rgba = Rgba(png);
    for (var i = 0; i < expectTransparent.Length; i++)
    {
        var alpha = rgba[i * 4 + 3];
        Require(expectTransparent[i] ? alpha == 0 : alpha == 255,
            $"pink key {mode}: pixel {i} alpha {alpha}, expected {(expectTransparent[i] ? "transparent" : "opaque")}");
    }
    passed++;
}

async Task CheckPinkKeySkipsPngByDefault()
{
    var dir = NewCaseDir("pink-png");
    var path = Path.Combine(dir, "art.png");
    WritePng(path, 2, 1, 8, 2, [255, 0, 255, 10, 20, 30]);

    var settings = new ImageConversionSettings
    {
        InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true,
        GameTransparencyMode = ImageGameTransparencyMode.SatisfyPinkKey
    };
    Require((await new ImageConversionService().ConvertAsync(settings)).Single().Success, "pink key PNG (skip) failed");
    using (var untouched = new MagickImage(path))
        Require(Rgba(untouched)[3] == 255, "pink key changed a PNG source without 'Also process source PNG files'");

    var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        InputPath = path, TargetFormat = ImageFormat.Png, OptimizeSize = true,
        GameTransparencyMode = ImageGameTransparencyMode.SatisfyPinkKey, ApplyGameTransparencyToPng = true
    })).Single();
    Require(result.Success, "pink key PNG (process) failed");
    using var keyed = new MagickImage(path);
    var rgba = Rgba(keyed);
    Require(rgba[3] == 0 && rgba[7] == 255, "pink key did not key the PNG source when enabled");
    passed++;
}

void CheckPinkKeyClassifier()
{
    var max = Quantum.Max;
    ushort Q(byte v) => (ushort)(v * max / 255);
    Require(ImageConversionService.IsPinkKeyColor(Q(255), Q(2), Q(255), max, PinkKeyMatchMode.Exact), "exact #FF02FF");
    Require(!ImageConversionService.IsPinkKeyColor(Q(255), Q(3), Q(255), max, PinkKeyMatchMode.Exact), "exact #FF03FF");
    Require(ImageConversionService.IsPinkKeyColor(Q(250), Q(5), Q(250), max, PinkKeyMatchMode.NearKey), "near #FA05FA");
    Require(!ImageConversionService.IsPinkKeyColor(Q(249), Q(0), Q(255), max, PinkKeyMatchMode.NearKey), "near #F900FF");
    Require(!ImageConversionService.IsPinkKeyColor(Q(255), Q(0), Q(255), max, PinkKeyMatchMode.LegacySoftFuzz), "legacy uses Magick fuzz, not the classifier");
    passed++;
}

async Task CheckAllOutputFormats()
{
    var dir = NewCaseDir("formats");
    var source = Path.Combine(dir, "src.png");
    var data = new byte[16 * 16 * 4];
    for (var i = 0; i < 256; i++)
    {
        data[i * 4] = (byte)(i * 7);
        data[i * 4 + 1] = (byte)(i * 3);
        data[i * 4 + 2] = (byte)(255 - i);
        data[i * 4 + 3] = 255;
    }
    WritePng(source, 16, 16, 8, 6, data);

    foreach (var format in Enum.GetValues<ImageFormat>())
    {
        foreach (var optimize in new[] { false, true })
        {
            var outDir = Path.Combine(dir, $"{format}-{optimize}");
            Directory.CreateDirectory(outDir);
            var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
            {
                InputPath = source, TargetFormat = format, OptimizeSize = optimize,
                OutputLocation = ImageOutputLocation.CustomFolder, OutputFolder = outDir
            })).Single();
            Require(result.Success, $"{format} (optimize={optimize}) failed: {result.ErrorMessage}");
            Require(File.Exists(result.OutputPath) && result.OutputSizeBytes > 0, $"{format} wrote nothing");
            using var decoded = ImageConversionService.LoadImage(result.OutputPath);
            Require(decoded.Width == 16 && decoded.Height == 16, $"{format} dimensions changed");
        }
    }
    passed++;
}

async Task CheckResize()
{
    var dir = NewCaseDir("resize");
    var source = Path.Combine(dir, "sprite.png");
    WritePng(source, 4, 4, 8, 2, Enumerable.Range(0, 48).Select(i => (byte)(i % 3 == 0 ? 255 : 0)).ToArray());

    foreach (var filter in Enum.GetValues<ImageResizeFilter>())
    {
        var outDir = Path.Combine(dir, filter.ToString());
        Directory.CreateDirectory(outDir);
        var result = (await new ImageConversionService().ConvertAsync(new ImageConversionSettings
        {
            InputPath = source, TargetFormat = ImageFormat.Png, OptimizeSize = false,
            OutputLocation = ImageOutputLocation.CustomFolder, OutputFolder = outDir,
            ResolutionMode = ImageResolutionMode.Custom, TargetWidth = 32, TargetHeight = 16, ResizeFilter = filter
        })).Single();
        Require(result.Success, $"resize {filter} failed: {result.ErrorMessage}");
        using var resized = new MagickImage(result.OutputPath);
        Require(resized.Width == 32 && resized.Height == 16, $"resize {filter} produced {resized.Width}×{resized.Height}");
        if (filter == ImageResizeFilter.NearestNeighbor)
            Require(resized.TotalColors <= 2, "nearest neighbor introduced blended colors");
    }
    passed++;
}

async Task CheckFolderBatchCustomOutputAndFilters()
{
    var root = NewCaseDir("batch-src");
    var nested = Path.Combine(root, "sub", "deeper");
    Directory.CreateDirectory(nested);
    var hidden = Path.Combine(root, ".git");
    Directory.CreateDirectory(hidden);
    byte[] rgb = Enumerable.Range(0, 12).Select(i => (byte)(i * 20)).ToArray();
    WritePng(Path.Combine(root, "a.png"), 2, 2, 8, 2, rgb);
    WriteBmp8(Path.Combine(root, "b.bmp"), 2, 2, [(1, 2, 3), (200, 100, 50)], [0, 1, 1, 0]);
    WriteBmp8(Path.Combine(nested, "c.bmp"), 2, 2, [(9, 9, 9), (90, 90, 90)], [1, 0, 0, 1]);
    WritePng(Path.Combine(hidden, "ignored.png"), 2, 2, 8, 2, rgb);
    File.WriteAllText(Path.Combine(root, "notes.txt"), "not an image");

    var service = new ImageConversionService();
    Require(service.ResolveInputFilePaths(root, ImageConversionMode.Folder, false).Count == 2, "top folder count");
    Require(service.ResolveInputFilePaths(root, ImageConversionMode.Folder, true).Count == 3, "subfolder count (.git skipped)");

    var onlyBmp = new ImageExtensionFilterSettings
    {
        Mode = ImageExtensionFilterMode.IncludeOnly,
        Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bmp" }
    };
    var skipBmp = new ImageExtensionFilterSettings { Mode = ImageExtensionFilterMode.Exclude, Extensions = onlyBmp.Extensions };
    Require(service.ResolveInputFilePaths(root, ImageConversionMode.Folder, true, onlyBmp).Count == 2, "convert-only filter");
    Require(service.ResolveInputFilePaths(root, ImageConversionMode.Folder, true, skipBmp).Count == 1, "skip filter");
    Require(service.ResolveInputFilePaths(root, ImageConversionMode.SingleFile, false).Count == 2,
        "a folder path must auto-switch to folder mode");

    var output = NewCaseDir("batch-out");
    var results = await service.ConvertAsync(new ImageConversionSettings
    {
        Mode = ImageConversionMode.Folder, InputPath = root, IncludeSubfolders = true,
        TargetFormat = ImageFormat.Png, OptimizeSize = true,
        OutputLocation = ImageOutputLocation.CustomFolder, OutputFolder = output,
        ExtensionFilter = onlyBmp, MaxParallelConversions = 4
    });
    Require(results.Count == 2 && results.All(r => r.Success), "folder batch failed: " +
        string.Join("; ", results.Where(r => !r.Success).Select(r => r.ErrorMessage)));
    Require(File.Exists(Path.Combine(output, "b.png")), "batch top-level output missing");
    Require(File.Exists(Path.Combine(output, "sub", "deeper", "c.png")), "batch did not preserve subfolder structure");
    Require(!File.Exists(Path.Combine(output, "a.png")), "convert-only filter leaked a.png");
    Require(File.Exists(Path.Combine(root, "b.bmp")), "custom output must never delete sources");
    passed++;
}

async Task CheckDeleteSourceWhenExtensionChanges()
{
    var dir = NewCaseDir("delete-source");
    var bmp = Path.Combine(dir, "tex.bmp");
    var png = Path.Combine(dir, "keep.png");
    WriteBmp8(bmp, 2, 2, [(10, 20, 30), (40, 50, 60)], [0, 1, 0, 1]);
    WritePng(png, 2, 2, 8, 2, Enumerable.Range(0, 12).Select(i => (byte)(i * 9)).ToArray());

    var results = await new ImageConversionService().ConvertAsync(new ImageConversionSettings
    {
        Mode = ImageConversionMode.Folder, InputPath = dir, TargetFormat = ImageFormat.Png,
        OptimizeSize = true, DeleteSourceWhenExtensionChanges = true
    });
    Require(results.All(r => r.Success), "delete-source batch failed");
    Require(!File.Exists(bmp) && File.Exists(Path.Combine(dir, "tex.png")), "source .bmp was not replaced by .png");
    Require(File.Exists(png), "same-extension source must be kept");
    passed++;
}

string NewCaseDir(string name)
{
    var dir = Path.Combine(scratch, name);
    Directory.CreateDirectory(dir);
    return dir;
}

static byte[] Rgba(MagickImage image)
{
    using var pixels = image.GetPixels();
    return pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidDataException("No pixels");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void WritePng(string path, int width, int height, byte depth, byte type, byte[] pixels, byte[]? palette = null, byte[]? alpha = null)
{
    using var output = File.Create(path);
    output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    var header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header, width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
    header[8] = depth;
    header[9] = type;
    Chunk(output, "IHDR", header);
    if (palette is not null) Chunk(output, "PLTE", palette);
    if (alpha is not null) Chunk(output, "tRNS", alpha);
    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        for (var y = 0; y < height; y++)
        {
            zlib.WriteByte(0);
            zlib.Write(pixels, y * pixels.Length / height, pixels.Length / height);
        }
    Chunk(output, "IDAT", compressed.ToArray());
    Chunk(output, "IEND", []);
}

static void Chunk(Stream stream, string type, byte[] data)
{
    Span<byte> number = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
    stream.Write(number);
    var bytes = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
    stream.Write(bytes);
    var crc = uint.MaxValue;
    foreach (var value in bytes)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
    }
    BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ uint.MaxValue);
    stream.Write(number);
}

/// <summary>8-bit indexed BMP (BITMAPINFOHEADER) with exactly palette.Length entries; pixels top-down.</summary>
static void WriteBmp8(string path, int width, int height, (byte R, byte G, byte B)[] palette, byte[] pixels)
{
    var rowSize = (width + 3) / 4 * 4;
    var pixelOffset = 14 + 40 + palette.Length * 4;
    var fileSize = pixelOffset + rowSize * height;
    var bytes = new byte[fileSize];
    bytes[0] = (byte)'B';
    bytes[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), fileSize);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), pixelOffset);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 8);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), rowSize * height);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(38), 2835);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(42), 2835);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(46), palette.Length);
    for (var i = 0; i < palette.Length; i++)
    {
        var at = 54 + i * 4;
        bytes[at] = palette[i].B;
        bytes[at + 1] = palette[i].G;
        bytes[at + 2] = palette[i].R;
    }
    for (var y = 0; y < height; y++)
    {
        var row = pixelOffset + (height - 1 - y) * rowSize; // bottom-up
        for (var x = 0; x < width; x++)
            bytes[row + x] = pixels[y * width + x];
    }
    File.WriteAllBytes(path, bytes);
}
