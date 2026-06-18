using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed partial class InstagramDirectDownloadService
{
    public static bool CanHandle(string url) =>
        InstagramUrlHelper.IsHighlightUrl(url) || InstagramUrlHelper.IsPostUrl(url);

    public async Task DownloadAsync(
        DownloadItem item,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct = default)
    {
        if (InstagramUrlHelper.IsHighlightUrl(item.Url))
            await DownloadHighlightAsync(item, progress, ct);
        else
            await DownloadPostAsync(item, progress, ct);
    }

    private async Task DownloadHighlightAsync(
        DownloadItem item,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct)
    {
        if (!InstagramCookieFileReader.TryRead(item.InstagramCookieFile!, out var cookies, out var csrf, out var readError))
            throw new InvalidOperationException(readError ?? "Instagram cookies are missing.");

        if (!TryGetHighlightId(item.Url, out var highlightId))
            throw new InvalidOperationException("Could not read the highlight id from that link.");

        using var handler = new HttpClientHandler { CookieContainer = cookies, AutomaticDecompression = DecompressionMethods.All };
        using var client = CreateApiClient(handler, csrf!);

        using var doc = await GetJsonAsync(
            client,
            $"https://www.instagram.com/api/v1/feed/reels_media/?reel_ids=highlight:{Uri.EscapeDataString(highlightId)}",
            ct);

        if (!doc.RootElement.TryGetProperty("reels", out var reels)
            || !reels.TryGetProperty($"highlight:{highlightId}", out var reel))
        {
            throw new InvalidOperationException("Instagram did not return highlight media for that link.");
        }

        if (!reel.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("That highlight reel is empty or unavailable.");

        Directory.CreateDirectory(item.SaveFolder);
        var highlightTitle = reel.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
        var baseName = FormatHelpers.SanitizeFileName(string.IsNullOrWhiteSpace(highlightTitle) ? "highlight" : highlightTitle);
        if (!string.IsNullOrWhiteSpace(highlightTitle))
            item.Title = highlightTitle;

        var index = 0;
        var total = items.GetArrayLength();
        var downloaded = 0;
        if (item.PlaylistItemIndex is int highlightIndex)
        {
            if (highlightIndex < 1 || highlightIndex > total)
                throw new InvalidOperationException($"That highlight only has {total} item(s). Selection {highlightIndex} is out of range.");

            var story = items[highlightIndex - 1];
            downloaded += await DownloadHighlightStoryAsync(client, story, item.SaveFolder, baseName, highlightIndex, total, item, ct);
            progress?.Report(new DownloadProgressReport
            {
                Percent = 100,
                TotalText = $"1/1",
                SpeedText = string.Empty,
                EtaText = string.Empty
            });
        }
        else
        {
            foreach (var story in items.EnumerateArray())
            {
                index++;
                downloaded += await DownloadHighlightStoryAsync(client, story, item.SaveFolder, baseName, index, total, item, ct);

                progress?.Report(new DownloadProgressReport
                {
                    Percent = index * 100.0 / Math.Max(total, 1),
                    TotalText = $"{index}/{total}",
                    SpeedText = string.Empty,
                    EtaText = string.Empty
                });
            }
        }

        if (downloaded == 0)
            throw new InvalidOperationException("No media could be downloaded from that highlight.");

        item.OutputPath = FindLatestOutput(item.SaveFolder, baseName);
    }

    private static async Task<int> DownloadHighlightStoryAsync(
        HttpClient client,
        JsonElement story,
        string saveFolder,
        string baseName,
        int index,
        int total,
        DownloadItem item,
        CancellationToken ct)
    {
        var suffix = total > 1 ? $"_{index:D2}" : string.Empty;
        if (InstagramMediaJsonHelper.TryGetVideoUrl(story, out var videoUrl))
        {
            var outputPath = Path.Combine(saveFolder, $"{baseName}{suffix}.mp4");
            await DownloadBinaryAsync(client, videoUrl!, outputPath, ct);
            return 1;
        }

        if (InstagramMediaJsonHelper.TryGetBestPhotoUrl(story, out var photoUrl))
        {
            var outputPath = Path.Combine(saveFolder, $"{baseName}{suffix}.png");
            await DownloadPhotoAsync(client, photoUrl!, outputPath, item, ct);
            return 1;
        }

        return 0;
    }

    private async Task DownloadPostAsync(
        DownloadItem item,
        IProgress<DownloadProgressReport>? progress,
        CancellationToken ct)
    {
        if (!InstagramShortcodeHelper.TryGetShortcodeFromUrl(item.Url, out var shortcode))
            throw new InvalidOperationException("Could not read the post id from that link.");

        if (!InstagramCookieFileReader.TryRead(item.InstagramCookieFile!, out var cookies, out var csrf, out var readError))
            throw new InvalidOperationException(readError ?? "Instagram cookies are missing.");

        using var handler = new HttpClientHandler { CookieContainer = cookies, AutomaticDecompression = DecompressionMethods.All };
        using var client = CreateApiClient(handler, csrf!);

        var mediaPk = NormalizeMediaPk(item.InstagramMediaPk);
        item.AppendDiagnostic($"Post URL: {item.Url}");
        item.AppendDiagnostic($"Media PK: {mediaPk ?? "(missing — re-fetch profile)"}");
        item.AppendDiagnostic($"Slide index: {(item.PlaylistItemIndex?.ToString() ?? "all")}");

        var media = await LoadPostMediaAsync(client, item.Url, shortcode, mediaPk, item, ct);
        var slides = ExtractSlides(media).ToList();
        item.AppendDiagnostic($"Slides found: {slides.Count}");
        if (slides.Count == 0)
            throw new InvalidOperationException("No media was found in that post.");

        var carouselCount = media.TryGetProperty("carousel_media", out var carouselProp)
            && carouselProp.ValueKind == JsonValueKind.Array
            ? carouselProp.GetArrayLength()
            : slides.Count;

        if (item.PlaylistItemIndex is int playlistIndex)
        {
            if (playlistIndex < 1 || playlistIndex > slides.Count)
                throw new InvalidOperationException($"That post only has {slides.Count} image(s). img_index={playlistIndex} is out of range.");

            slides = [slides[playlistIndex - 1]];
        }

        if (string.IsNullOrWhiteSpace(item.SaveFolder))
            throw new InvalidOperationException("Save folder is not set. Choose a save folder and try again.");

        Directory.CreateDirectory(item.SaveFolder);
        item.AppendDiagnostic($"Save folder: {item.SaveFolder}");
        var baseName = FormatHelpers.SanitizeFileName(
            media.TryGetProperty("code", out var codeProp) && !string.IsNullOrWhiteSpace(codeProp.GetString())
                ? codeProp.GetString()!
                : shortcode);
        if (media.TryGetProperty("caption", out var captionProp)
            && captionProp.TryGetProperty("text", out var textProp)
            && !string.IsNullOrWhiteSpace(textProp.GetString()))
        {
            item.Title = FormatHelpers.SanitizeFileName(textProp.GetString()!);
            baseName = item.Title;
        }
        else if (string.IsNullOrWhiteSpace(item.Title))
        {
            item.Title = baseName;
        }

        if (item.PlaylistItemIndex is int selectedIndex && carouselCount > 1)
            item.Title = $"{item.Title} ({selectedIndex}/{carouselCount})";

        var index = 0;
        var total = slides.Count;
        var downloaded = 0;
        string? lastSavedPath = null;
        foreach (var slide in slides)
        {
            index++;
            var slideNumber = item.PlaylistItemIndex ?? index;
            var suffix = carouselCount > 1 ? $"_{slideNumber:D2}" : (total > 1 ? $"_{index:D2}" : string.Empty);
            if (InstagramMediaJsonHelper.TryGetVideoUrl(slide, out var videoUrl))
            {
                var outputPath = Path.Combine(item.SaveFolder, $"{baseName}{suffix}.mp4");
                await DownloadBinaryAsync(client, videoUrl!, outputPath, ct);
                lastSavedPath = outputPath;
                downloaded++;
            }
            else if (InstagramMediaJsonHelper.TryGetBestPhotoUrl(slide, out var photoUrl))
            {
                item.AppendDiagnostic($"Photo URL: {photoUrl}");
                var outputPath = Path.Combine(item.SaveFolder, $"{baseName}{suffix}.png");
                lastSavedPath = await DownloadPhotoAsync(client, photoUrl!, outputPath, item, ct);
                item.AppendDiagnostic($"Saved: {lastSavedPath}");
                downloaded++;
            }
            else
            {
                item.AppendDiagnostic($"Slide {slideNumber}: no photo or video URL found in API data");
            }

            progress?.Report(new DownloadProgressReport
            {
                Percent = index * 100.0 / Math.Max(total, 1),
                TotalText = $"{index}/{total}",
                SpeedText = string.Empty,
                EtaText = string.Empty
            });
        }

        if (downloaded == 0)
            throw new InvalidOperationException("No photo or video could be downloaded from that post.");

        item.OutputPath = lastSavedPath ?? FindLatestOutput(item.SaveFolder, baseName);
    }

    private static async Task<JsonElement> LoadPostMediaAsync(
        HttpClient client,
        string postUrl,
        string shortcode,
        string? mediaPk,
        DownloadItem item,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(mediaPk))
        {
            try
            {
                item.AppendDiagnostic($"API: /api/v1/media/{mediaPk}/info/");
                return await FetchMediaInfoByPkAsync(client, mediaPk, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.AppendDiagnostic($"Media PK lookup failed: {ex.Message}");
            }
        }

        try
        {
            var mediaId = InstagramShortcodeHelper.ToMediaId(shortcode);
            if (InstagramShortcodeHelper.IsLikelyValidMediaPk(mediaId))
                return await FetchMediaInfoByPkAsync(client, mediaId, ct);
        }
        catch
        {
            // legacy shortcodes only
        }

        using var response = await client.GetAsync(postUrl, ct);
        var html = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Instagram blocked loading that post page.");

        foreach (Match match in ApplicationJsonScriptRegex().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                if (TryFindMediaByShortcode(doc.RootElement, shortcode, out var media))
                    return media.Clone();
            }
            catch
            {
                // keep searching other script blocks
            }
        }

        throw new InvalidOperationException(
            "Could not read media data for that post. Re-fetch the profile so DownloadMaster can attach the media id, then try again.");
    }

    private static async Task<JsonElement> FetchMediaInfoByPkAsync(HttpClient client, string mediaPk, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(client, $"https://www.instagram.com/api/v1/media/{mediaPk}/info/", ct);
        if (doc.RootElement.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array
            && items.GetArrayLength() > 0)
        {
            return items[0].Clone();
        }

        throw new InvalidOperationException("Instagram returned no media info for that post.");
    }

    private static IEnumerable<JsonElement> ExtractSlides(JsonElement media)
    {
        if (media.TryGetProperty("carousel_media", out var carousel)
            && carousel.ValueKind == JsonValueKind.Array
            && carousel.GetArrayLength() > 0)
        {
            foreach (var slide in carousel.EnumerateArray())
                yield return slide;

            yield break;
        }

        yield return media;
    }

    private static bool TryFindMediaByShortcode(JsonElement element, string shortcode, out JsonElement media)
    {
        media = default;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if ((element.TryGetProperty("shortcode", out var codeProp)
                    && shortcode.Equals(codeProp.GetString(), StringComparison.OrdinalIgnoreCase))
                || (element.TryGetProperty("code", out var altCodeProp)
                    && shortcode.Equals(altCodeProp.GetString(), StringComparison.OrdinalIgnoreCase)))
            {
                if (element.TryGetProperty("image_versions2", out _)
                    || element.TryGetProperty("video_versions", out _)
                    || element.TryGetProperty("carousel_media", out _)
                    || element.TryGetProperty("display_url", out _)
                    || element.TryGetProperty("thumbnail_src", out _))
                {
                    media = element;
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindMediaByShortcode(property.Value, shortcode, out media))
                    return true;
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindMediaByShortcode(item, shortcode, out media))
                    return true;
            }
        }

        return false;
    }

    private static string? NormalizeMediaPk(string? mediaPk)
    {
        mediaPk = mediaPk?.Trim();
        return InstagramShortcodeHelper.IsLikelyValidMediaPk(mediaPk) ? mediaPk : null;
    }

    private static async Task<string> DownloadPhotoAsync(
        HttpClient client,
        string mediaUrl,
        string preferredPngPath,
        DownloadItem item,
        CancellationToken ct)
    {
        var bytes = await DownloadBytesAsync(client, mediaUrl, ct);
        item.AppendDiagnostic($"Downloaded {bytes.Length} bytes from CDN");
        return await InstagramImageSaveHelper.SaveImageBytesAsync(bytes, preferredPngPath, item, ct);
    }

    private static async Task DownloadBinaryAsync(HttpClient client, string mediaUrl, string outputPath, CancellationToken ct)
    {
        var bytes = await DownloadBytesAsync(client, mediaUrl, ct);
        await File.WriteAllBytesAsync(outputPath, bytes, ct);
    }

    private static async Task<byte[]> DownloadBytesAsync(HttpClient client, string mediaUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        request.Headers.TryAddWithoutValidation("Referer", "https://www.instagram.com/");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string? FindLatestOutput(string folder, string baseName) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder)
                .Where(path => Path.GetFileName(path).StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

    private static bool TryGetHighlightId(string url, out string highlightId)
    {
        highlightId = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3
            && segments[0].Equals("stories", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("highlights", StringComparison.OrdinalIgnoreCase)
            && segments[2].Length > 0)
        {
            highlightId = segments[2];
            return true;
        }

        return false;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);

        return JsonDocument.Parse(body);
    }

    private static HttpClient CreateApiClient(HttpClientHandler handler, string csrfToken)
    {
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("X-IG-App-ID", "936619743392459");
        client.DefaultRequestHeaders.Add("X-CSRFToken", csrfToken);
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        client.DefaultRequestHeaders.Add("Referer", "https://www.instagram.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    [GeneratedRegex(@"<script type=""application/json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ApplicationJsonScriptRegex();
}
