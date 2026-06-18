using System.Text.Json;

namespace DownloadMaster.Services;

public static class InstagramShortcodeHelper
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    public static string ToMediaId(string shortcode)
    {
        System.Numerics.BigInteger id = 0;
        foreach (var character in shortcode)
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0)
                throw new InvalidOperationException($"Invalid Instagram shortcode: {shortcode}");

            id = id * 64 + index;
        }

        return id.ToString();
    }

    public static string? ExtractMediaPk(JsonElement media)
    {
        var node = media.TryGetProperty("media", out var nested) ? nested : media;
        if (node.TryGetProperty("pk", out var pkProp))
        {
            return pkProp.ValueKind switch
            {
                JsonValueKind.Number => pkProp.GetRawText(),
                JsonValueKind.String => pkProp.GetString(),
                _ => null
            };
        }

        if (!node.TryGetProperty("id", out var idProp))
            return null;

        var raw = idProp.ValueKind == JsonValueKind.Number
            ? idProp.GetRawText()
            : idProp.GetString();

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var underscore = raw.IndexOf('_');
        return underscore > 0 ? raw[..underscore] : raw;
    }

    public static bool IsLikelyValidMediaPk(string? mediaPk) =>
        !string.IsNullOrWhiteSpace(mediaPk)
        && mediaPk.All(char.IsDigit)
        && mediaPk.Length is >= 8 and <= 22;
    public static bool TryGetShortcodeFromUrl(string url, out string shortcode)
    {
        shortcode = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && segments[0].Equals("p", StringComparison.OrdinalIgnoreCase)
            && segments[1].Length > 0)
        {
            shortcode = segments[1];
            return true;
        }

        if (segments.Length >= 2
            && segments[0].Equals("reel", StringComparison.OrdinalIgnoreCase)
            && segments[1].Length > 0)
        {
            shortcode = segments[1];
            return true;
        }

        return false;
    }
}
