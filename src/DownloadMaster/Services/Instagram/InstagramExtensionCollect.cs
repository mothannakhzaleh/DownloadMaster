using System.Globalization;
using DownloadMaster.Models;
using DownloadMaster.Services.Bridge;

namespace DownloadMaster.Services;

/// <summary>
/// Collects the batches the browser extension reads from Instagram's page and turns them into the
/// Instagram tab's sections (Stories, each highlight, Posts). Nothing from the page is trusted as-is:
/// items of other accounts are dropped for a profile, and only https files on Instagram's own CDN
/// hosts are kept, so a page cannot make the app download from anywhere else.
/// </summary>
public sealed class InstagramExtensionCollect
{
    public const int MaxItems = 5000;

    private readonly string? _username;
    private readonly string? _code;
    private readonly List<string> _order = [];
    private readonly Dictionary<string, BridgeInstagramItem> _items = new(StringComparer.Ordinal);
    private readonly List<string> _highlightOrder = [];
    private readonly Dictionary<string, string?> _highlightTitles = new(StringComparer.Ordinal);

    /// <param name="username">The profile being read; null for a single post or reel.</param>
    /// <param name="code">For a single post or reel: its shortcode, so suggested posts are left out.</param>
    public InstagramExtensionCollect(string? username, string? code = null)
    {
        _username = string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');
        _code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
    }

    public int ItemCount => _order.Count;

    public bool Done { get; private set; }

    /// <summary>The extension stopped reading posts before the end of the profile.</summary>
    public bool Partial { get; private set; }

    /// <summary>The last batch said the Instagram tab is hidden.</summary>
    public bool TabHidden { get; private set; }

    /// <summary>Adds one batch; returns how many items were new.</summary>
    public int Add(BridgeInstagramCollectRequest batch)
    {
        if (batch.Done || string.Equals(batch.Phase, "done", StringComparison.OrdinalIgnoreCase))
            Done = true;
        if (batch.Partial)
            Partial = true;
        TabHidden = batch.Hidden;

        foreach (var highlight in batch.Highlights ?? [])
        {
            var id = DigitsOnly(highlight.Id);
            if (id is null)
                continue;
            if (!_highlightTitles.ContainsKey(id))
            {
                if (_highlightOrder.Count >= 200)
                    continue;
                _highlightOrder.Add(id);
                _highlightTitles[id] = null;
            }
            if (!string.IsNullOrWhiteSpace(highlight.Title))
                _highlightTitles[id] = Clean(highlight.Title, 80);
        }

        var added = 0;
        foreach (var raw in batch.Items ?? [])
        {
            var item = Sanitize(raw);
            if (item is null)
                continue;

            var key = $"{item.Section}|{item.Pk ?? item.Code}";
            if (_items.TryGetValue(key, out var existing))
            {
                // A later response can carry the complete carousel.
                if (item.Media.Count > existing.Media.Count)
                    _items[key] = item;
                continue;
            }

            if (_order.Count >= MaxItems)
                break;
            _order.Add(key);
            _items[key] = item;
            added++;

            if (item.Section.StartsWith("highlight:", StringComparison.Ordinal))
            {
                var id = item.Section["highlight:".Length..];
                if (!_highlightTitles.ContainsKey(id) && _highlightOrder.Count < 200)
                {
                    _highlightOrder.Add(id);
                    _highlightTitles[id] = null;
                }
                if (_highlightTitles.TryGetValue(id, out var known) && known is null && !string.IsNullOrWhiteSpace(item.SectionTitle))
                    _highlightTitles[id] = Clean(item.SectionTitle, 80);
            }
        }

        return added;
    }

    /// <summary>Builds the sections; <paramref name="text"/> looks up a localized string by key.</summary>
    public IReadOnlyList<InstagramBrowseSection> BuildSections(Func<string, string> text)
    {
        var all = _order.Select(key => _items[key]).ToList();
        var sections = new List<InstagramBrowseSection>();
        var owner = _username ?? all.Select(i => i.Username).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? "instagram";

        // Stories, oldest first like Instagram plays them.
        var stories = new InstagramBrowseSection { Key = "stories", Title = text("InstagramSectionStories") };
        foreach (var story in all.Where(i => i.Section == "stories").OrderBy(i => i.TakenAt))
        {
            var isVideo = IsVideo(story.Media[0]);
            stories.Items.Add(new InstagramBrowseItem
            {
                SectionKey = stories.Key,
                SectionTitle = stories.Title,
                Kind = "story",
                Title = story.TakenAt > 0
                    ? string.Format(text("InstagramStoryItem"), LocalTime(story.TakenAt).ToString("g", CultureInfo.CurrentCulture))
                    : text("InstagramStoryItemGeneric"),
                Url = $"https://www.instagram.com/stories/{owner}/{story.Pk}/",
                MediaPk = story.Pk,
                ThumbnailUrl = story.Thumbnail,
                DirectMedia = [Media(story.Media[0], $"{owner}_story_{story.Pk ?? story.Code}")]
            });
        }
        sections.Add(stories);

        // Highlights, in the order the profile lists them.
        foreach (var id in _highlightOrder)
        {
            var reel = all.Where(i => i.Section == $"highlight:{id}").OrderBy(i => i.TakenAt).ToList();
            if (reel.Count == 0)
                continue;

            var title = _highlightTitles[id];
            var section = new InstagramBrowseSection
            {
                Key = $"highlight-{id}",
                Title = string.IsNullOrWhiteSpace(title)
                    ? text("InstagramSectionHighlight")
                    : string.Format(text("InstagramHighlightSection"), title)
            };
            var url = $"https://www.instagram.com/stories/highlights/{id}/";
            var namePrefix = $"{owner}_{(string.IsNullOrWhiteSpace(title) ? "highlight_" + id : title)}";
            var files = reel.Select(story => Media(story.Media[0], $"{namePrefix}_{story.Pk ?? story.Code}")).ToList();

            section.Items.Add(new InstagramBrowseItem
            {
                SectionKey = section.Key,
                SectionTitle = section.Title,
                Kind = "highlight",
                Title = text("InstagramHighlightBatch"),
                Url = url,
                ThumbnailUrl = reel[0].Thumbnail,
                DirectMedia = files
            });
            for (var index = 0; index < reel.Count; index++)
            {
                section.Items.Add(new InstagramBrowseItem
                {
                    SectionKey = section.Key,
                    SectionTitle = section.Title,
                    Kind = "highlight-story",
                    Title = string.Format(
                        text("InstagramHighlightStoryItem"),
                        index + 1,
                        reel.Count,
                        files[index].IsVideo ? text("InstagramKindVideo") : text("InstagramKindPhoto")),
                    Url = url,
                    HighlightStoryIndex = index + 1,
                    MediaPk = reel[index].Pk,
                    ThumbnailUrl = reel[index].Thumbnail,
                    DirectMedia = [files[index]]
                });
            }
            sections.Add(section);
        }

        // Posts and reels, newest first like the profile grid. A single post or reel lands here too.
        var posts = new InstagramBrowseSection { Key = "posts", Title = text("InstagramSectionPosts") };
        var postItems = all.Where(i => i.Section == "posts" || (_username is null && i.Section != "stories" && !i.Section.StartsWith("highlight:", StringComparison.Ordinal)));
        foreach (var post in postItems.OrderByDescending(i => i.TakenAt))
            AddPost(posts, post, owner, text);
        sections.Add(posts);

        return sections;
    }

    private static void AddPost(InstagramBrowseSection posts, BridgeInstagramItem post, string owner, Func<string, string> text)
    {
        var code = post.Code ?? post.Pk ?? "post";
        var url = post.Code is null ? $"https://www.instagram.com/{owner}/" : $"https://www.instagram.com/p/{post.Code}/";
        var dateLabel = post.TakenAt > 0 ? LocalTime(post.TakenAt).ToString("d", CultureInfo.CurrentCulture) : code;
        var user = string.IsNullOrWhiteSpace(post.Username) ? owner : post.Username;

        if (post.Media.Count == 1)
        {
            var file = Media(post.Media[0], $"{user}_{code}");
            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = file.IsVideo ? "reel" : "post",
                Title = post.TakenAt > 0
                    ? string.Format(text("InstagramPostItem"), dateLabel, file.IsVideo ? text("InstagramKindVideo") : text("InstagramKindPhoto"))
                    : code,
                Url = url,
                MediaPk = post.Pk,
                ThumbnailUrl = post.Thumbnail,
                DirectMedia = [file]
            });
            return;
        }

        var files = post.Media.Select((media, index) => Media(media, $"{user}_{code}_{index + 1}")).ToList();
        posts.Items.Add(new InstagramBrowseItem
        {
            SectionKey = posts.Key,
            SectionTitle = posts.Title,
            Kind = "carousel",
            Title = string.Format(text("InstagramCarouselBatch"), dateLabel, files.Count),
            Url = url,
            MediaPk = post.Pk,
            ThumbnailUrl = post.Thumbnail,
            DirectMedia = files
        });
        for (var index = 0; index < files.Count; index++)
        {
            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = "carousel-slide",
                Title = string.Format(
                    text("InstagramCarouselSlideItem"),
                    dateLabel,
                    index + 1,
                    files.Count,
                    files[index].IsVideo ? text("InstagramKindVideo") : text("InstagramKindPhoto")),
                Url = url,
                MediaPk = post.Pk,
                CarouselSlideIndex = index + 1,
                ThumbnailUrl = files[index].IsVideo ? post.Thumbnail : files[index].Url,
                DirectMedia = [files[index]]
            });
        }
    }

    private BridgeInstagramItem? Sanitize(BridgeInstagramItem? raw)
    {
        if (raw is null)
            return null;

        var section = raw.Section switch
        {
            "posts" or "stories" => raw.Section,
            { } s when s.StartsWith("highlight:", StringComparison.Ordinal) && DigitsOnly(s["highlight:".Length..]) is { } id => $"highlight:{id}",
            _ => null
        };
        if (section is null)
            return null;

        var username = Username(raw.Username);
        if (_username is not null && username is not null
            && !string.Equals(username, _username, StringComparison.OrdinalIgnoreCase))
            return null;

        var pk = DigitsOnly(raw.Pk);
        var code = raw.Code is { Length: > 0 and <= 64 } c && c.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') ? c : null;
        if (pk is null && code is null)
            return null;
        if (_code is not null && !string.Equals(code, _code, StringComparison.Ordinal))
            return null;

        var media = (raw.Media ?? [])
            .Where(m => m is not null && IsAllowedMediaUrl(m.Url))
            .Take(20)
            .Select(m => new BridgeInstagramMedia { Type = IsVideo(m) ? "video" : "image", Url = m.Url, Width = m.Width, Height = m.Height })
            .ToList();
        if (media.Count == 0)
            return null;

        return new BridgeInstagramItem
        {
            Section = section,
            SectionTitle = string.IsNullOrWhiteSpace(raw.SectionTitle) ? null : Clean(raw.SectionTitle, 80),
            Code = code,
            Pk = pk,
            TakenAt = raw.TakenAt is > 0 and < 32503680000 ? raw.TakenAt : 0,
            ProductType = raw.ProductType,
            Username = username,
            Thumbnail = IsAllowedMediaUrl(raw.Thumbnail) ? raw.Thumbnail : null,
            Media = media
        };
    }

    /// <summary>https on Instagram's / Facebook's CDN only.</summary>
    public static bool IsAllowedMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        return host.EndsWith(".cdninstagram.com", StringComparison.Ordinal)
            || host.EndsWith(".fbcdn.net", StringComparison.Ordinal);
    }

    /// <summary>A name safe on Windows, macOS and Linux (no extension), at most 120 characters.</summary>
    public static string SafeFileName(string name)
    {
        var chars = name.Select(ch => ch < 32 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch).ToArray();
        var cleaned = new string(chars).Trim().Trim('.', ' ');
        if (cleaned.Length > 120)
            cleaned = cleaned[..120].TrimEnd('.', ' ');
        return cleaned.Length == 0 ? "instagram" : cleaned;
    }

    private static InstagramDirectMedia Media(BridgeInstagramMedia media, string baseName)
    {
        var isVideo = IsVideo(media);
        return new InstagramDirectMedia(media.Url, isVideo, SafeFileName(baseName) + (isVideo ? ".mp4" : ".webp"));
    }

    private static bool IsVideo(BridgeInstagramMedia media) =>
        string.Equals(media.Type, "video", StringComparison.OrdinalIgnoreCase);

    private static DateTime LocalTime(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;

    private static string? DigitsOnly(string? value) =>
        value is { Length: > 0 and <= 40 } && value.All(char.IsAsciiDigit) ? value : null;

    private static string? Username(string? value)
    {
        var name = value?.Trim().TrimStart('@');
        return name is { Length: > 0 and <= 64 } && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.') ? name : null;
    }

    private static string Clean(string value, int max)
    {
        var text = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }
}
