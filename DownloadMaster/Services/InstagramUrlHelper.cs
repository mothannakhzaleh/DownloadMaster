using System.Text.RegularExpressions;



namespace DownloadMaster.Services;



public readonly record struct InstagramUrlNormalization(

    string Url,

    bool NoPlaylist,

    int? PlaylistItemIndex = null,

    bool IsCarouselPost = false);



public static partial class InstagramUrlHelper

{

    public static InstagramUrlNormalization NormalizeDetailed(string input)

    {

        var text = input.Trim();

        text = text.Replace(" ", string.Empty).Replace("\u00a0", string.Empty);



        if (text.StartsWith('@'))

            text = text[1..];



        if (!text.StartsWith("http", StringComparison.OrdinalIgnoreCase))

        {

            text = text.Trim('/');

            text = $"https://www.instagram.com/{text}/";

        }



        int? playlistItemIndex = null;

        var isCarouselPost = false;



        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))

        {

            playlistItemIndex = TryReadCarouselIndex(uri.Query);

            var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();

            isCarouselPost = path.StartsWith("p/", StringComparison.Ordinal);



            text = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

            if (!text.EndsWith('/'))

                text += '/';

        }



        var noPlaylist = StoryWithIdRegex().IsMatch(text);

        return new InstagramUrlNormalization(text, noPlaylist, playlistItemIndex, isCarouselPost);

    }



    public static (string Url, bool NoPlaylist) Normalize(string input)

    {

        var info = NormalizeDetailed(input);

        return (info.Url, info.NoPlaylist);

    }



    public static bool IsInstagramUrl(string input)

    {

        if (string.IsNullOrWhiteSpace(input)) return false;

        var text = input.Trim();

        if (text.StartsWith('@')) return true;

        return text.Contains("instagram.com", StringComparison.OrdinalIgnoreCase);

    }



    public static bool IsProfileHomeUrl(string url) => TryGetUsername(url, out _);



    public static bool TryGetUsername(string url, out string username)

    {

        username = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))

            return false;



        if (!uri.Host.Contains("instagram.com", StringComparison.OrdinalIgnoreCase))

            return false;



        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 1)

            return false;



        ReadOnlySpan<string> reserved =

        [

            "p", "reel", "reels", "stories", "tv", "explore", "accounts", "direct", "about", "legal", "privacy"

        ];



        foreach (var name in reserved)

        {

            if (segments[0].Equals(name, StringComparison.OrdinalIgnoreCase))

                return false;

        }



        username = segments[0];

        return username.Length > 0;

    }



    public static bool IsDirectMediaUrl(string url)

    {

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))

            return false;



        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();

        return path.StartsWith("p/", StringComparison.Ordinal)

            || path.StartsWith("reel/", StringComparison.Ordinal)

            || path.StartsWith("reels/", StringComparison.Ordinal)

            || StoryWithIdRegex().IsMatch(url)

            || path.StartsWith("stories/highlights/", StringComparison.Ordinal);

    }



    public static bool IsHighlightUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.AbsolutePath.Trim('/').StartsWith("stories/highlights/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStoriesBatchUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.Trim('/');
        if (!path.StartsWith("stories/", StringComparison.OrdinalIgnoreCase))
            return false;

        return !path.StartsWith("stories/highlights/", StringComparison.OrdinalIgnoreCase)
            && path.Split('/').Length == 2;
    }

    public static bool IsCarouselPostUrl(string url) => IsPostUrl(url);

    public static bool IsPostUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        return path.StartsWith("p/", StringComparison.Ordinal)
            || path.StartsWith("reel/", StringComparison.Ordinal);
    }

    private static int? TryReadCarouselIndex(string query)

    {

        if (string.IsNullOrWhiteSpace(query))

            return null;



        foreach (Match match in CarouselIndexRegex().Matches(query))

        {

            if (int.TryParse(match.Groups[1].Value, out var index) && index > 0)

                return index;

        }



        return null;

    }



    [GeneratedRegex(@"(?:^|[?&])(?:img_index|img-index)=([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]

    private static partial Regex CarouselIndexRegex();



    [GeneratedRegex(@"instagram\.com/stories/[^/?#]+/\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]

    private static partial Regex StoryWithIdRegex();

}


