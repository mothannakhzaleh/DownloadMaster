using System.Text.Json;

namespace DownloadMaster.Services;

internal static class InstagramMediaJsonHelper
{
    public static JsonElement ResolveMediaNode(JsonElement media) =>
        media.TryGetProperty("media", out var nested) ? nested : media;

    public static string? ExtractMediaPk(JsonElement media) =>
        InstagramShortcodeHelper.ExtractMediaPk(ResolveMediaNode(media));

    public static string? ExtractThumbnailUrl(JsonElement media)
    {
        var node = ResolveMediaNode(media);

        if (TryGetBestPhotoUrl(node, out var photoUrl))
            return photoUrl;

        if (node.TryGetProperty("display_url", out var displayUrl)
            && displayUrl.ValueKind == JsonValueKind.String)
        {
            return displayUrl.GetString();
        }

        if (node.TryGetProperty("thumbnail_src", out var thumbSrc)
            && thumbSrc.ValueKind == JsonValueKind.String)
        {
            return thumbSrc.GetString();
        }

        if (node.TryGetProperty("cover_media", out var cover))
            return ExtractThumbnailUrl(cover);

        if (node.TryGetProperty("cropped_image_version", out var cropped)
            && cropped.TryGetProperty("url", out var croppedUrl)
            && croppedUrl.ValueKind == JsonValueKind.String)
        {
            return croppedUrl.GetString();
        }

        return null;
    }

    public static bool IsVideo(JsonElement media)
    {
        var node = ResolveMediaNode(media);
        if (node.TryGetProperty("media_type", out var mediaType) && mediaType.GetInt32() == 2)
            return true;

        return node.TryGetProperty("is_video", out var videoProp) && videoProp.GetBoolean();
    }

    public static bool TryGetVideoUrl(JsonElement media, out string? url)
    {
        url = null;
        var node = ResolveMediaNode(media);
        if (!node.TryGetProperty("video_versions", out var videos)
            || videos.ValueKind != JsonValueKind.Array
            || videos.GetArrayLength() == 0)
        {
            return false;
        }

        url = videos[0].TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
        return !string.IsNullOrWhiteSpace(url);
    }

    public static bool TryGetBestPhotoUrl(JsonElement media, out string? url)
    {
        url = null;
        var node = ResolveMediaNode(media);

        if (node.TryGetProperty("image_versions2", out var images)
            && images.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && TryPickBestCandidate(candidates, out url))
        {
            return true;
        }

        if (node.TryGetProperty("display_url", out var displayUrl)
            && displayUrl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(displayUrl.GetString()))
        {
            url = displayUrl.GetString();
            return true;
        }

        if (node.TryGetProperty("display_uri", out var displayUri)
            && displayUri.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(displayUri.GetString()))
        {
            url = displayUri.GetString();
            return true;
        }

        if (node.TryGetProperty("thumbnail_src", out var thumbSrc)
            && thumbSrc.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(thumbSrc.GetString()))
        {
            url = thumbSrc.GetString();
            return true;
        }

        if (node.TryGetProperty("thumbnail_resources", out var resources)
            && resources.ValueKind == JsonValueKind.Array
            && TryPickBestThumbnailResource(resources, out url))
        {
            return true;
        }

        return false;
    }

    private static bool TryPickBestCandidate(JsonElement candidates, out string? url)
    {
        url = null;
        var bestScore = -1L;
        foreach (var candidate in candidates.EnumerateArray())
        {
            var candidateUrl = candidate.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(candidateUrl))
                continue;

            var width = candidate.TryGetProperty("width", out var widthProp) && widthProp.TryGetInt32(out var w) ? w : 0;
            var height = candidate.TryGetProperty("height", out var heightProp) && heightProp.TryGetInt32(out var h) ? h : 0;
            var score = (long)width * height;
            if (candidateUrl.Contains(".png", StringComparison.OrdinalIgnoreCase))
                score += 1_000_000_000;
            if (candidateUrl.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                || candidateUrl.Contains(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                score += 500_000_000;
            }

            if (score <= bestScore)
                continue;

            bestScore = score;
            url = candidateUrl;
        }

        return !string.IsNullOrWhiteSpace(url);
    }

    private static bool TryPickBestThumbnailResource(JsonElement resources, out string? url)
    {
        url = null;
        var bestWidth = -1;
        foreach (var resource in resources.EnumerateArray())
        {
            var candidateUrl = resource.TryGetProperty("src", out var srcProp) ? srcProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(candidateUrl))
                continue;

            var width = resource.TryGetProperty("config_width", out var widthProp) && widthProp.TryGetInt32(out var w)
                ? w
                : 0;
            if (width < bestWidth)
                continue;

            bestWidth = width;
            url = candidateUrl;
        }

        return !string.IsNullOrWhiteSpace(url);
    }
}
