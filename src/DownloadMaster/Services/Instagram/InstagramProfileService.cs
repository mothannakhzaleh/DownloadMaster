using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DownloadMaster.Models;

namespace DownloadMaster.Services;

public sealed partial class InstagramProfileService
{
    private const string WebAppId = "936619743392459";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, CachedProfile> ProfileCache = new();

    private sealed record CachedProfile(DateTimeOffset ExpiresAt, List<InstagramBrowseSection> Sections);
    private sealed record ProfilePageLoadResult(JsonDocument? UserDocument, bool RateLimited, string? Html);

    public async Task<IReadOnlyList<InstagramBrowseSection>> FetchSectionsAsync(
        string profileUrl,
        string cookieFilePath,
        LocalizationService loc,
        CancellationToken ct = default)
    {
        if (!InstagramUrlHelper.TryGetUsername(profileUrl, out var username))
            throw new InvalidOperationException(loc.Get("InstagramInvalidProfile"));

        if (!InstagramCookieFileReader.TryRead(cookieFilePath, out var cookies, out var csrf, out var readError))
            throw new InvalidOperationException(readError ?? loc.Get("InstagramAuthError"));

        var cacheKey = BuildCacheKey(username, cookieFilePath);
        if (ProfileCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return CloneSections(cached.Sections);

        using var handler = new HttpClientHandler { CookieContainer = cookies, AutomaticDecompression = DecompressionMethods.All };
        using var pageClient = CreatePageClient(handler);
        using var apiClient = CreateApiClient(handler, csrf!);

        var pageResult = await TryLoadUserFromProfilePageAsync(pageClient, username, ct);
        if (pageResult.RateLimited)
            throw new InvalidOperationException(loc.Get("InstagramRateLimited"));

        var sections = new List<InstagramBrowseSection> { CreateStoriesSection(username, loc) };
        string? userId = null;

        if (pageResult.UserDocument is not null)
        {
            using (pageResult.UserDocument)
            {
                var user = pageResult.UserDocument.RootElement;
                userId = user.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                MergeUserData(sections, user, loc);
            }
        }

        if (!string.IsNullOrWhiteSpace(pageResult.Html))
            MergeFromHtml(sections, pageResult.Html, loc);

        if (!HasMediaSections(sections))
        {
            try
            {
                using var apiUserDoc = await TryLoadUserFromApiAsync(apiClient, username, loc, ct);
                var user = apiUserDoc.RootElement;
                userId ??= user.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                MergeUserData(sections, user, loc);
            }
            catch (InvalidOperationException ex) when (ex.Message == loc.Get("InstagramRateLimited"))
            {
                // Profile API is often rate-limited; keep HTML/page data already collected.
            }
        }

        userId ??= TryExtractUserIdFromHtml(pageResult.Html, username);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await MergePostsFromFeedApi(apiClient, userId, sections, loc, ct);

            var knownUrls = new HashSet<string>(
                sections.SelectMany(section => section.Items).Select(item => item.Url),
                StringComparer.OrdinalIgnoreCase);
            await MergeHighlightsFromTrayApi(apiClient, userId, sections, knownUrls, loc, ct);
            await ExpandHighlightItemsAsync(apiClient, sections, loc, ct);

            var stories = await TryFetchStoriesAsync(apiClient, username, userId, loc, ct);
            MergeStoriesSection(sections, stories);
        }

        sections = sections.Where(section => section.Items.Count > 0).ToList();
        if (sections.Count == 0)
            throw new InvalidOperationException(loc.Get("InstagramProfileEmpty"));

        ProfileCache[cacheKey] = new CachedProfile(DateTimeOffset.UtcNow.Add(CacheLifetime), CloneSections(sections));
        return sections;
    }

    private static bool HasMediaSections(IReadOnlyList<InstagramBrowseSection> sections) =>
        sections.Any(section =>
            section.Key == "posts" && section.Items.Count > 0
            || section.Key.StartsWith("highlight-", StringComparison.OrdinalIgnoreCase) && section.Items.Count > 0);

    private static void AddHighlightFromHtml(
        List<InstagramBrowseSection> sections,
        HashSet<string> knownUrls,
        string highlightId,
        string? title,
        LocalizationService loc,
        string? thumbnailUrl = null)
    {
        if (string.IsNullOrWhiteSpace(highlightId))
            return;

        var batchUrl = $"https://www.instagram.com/stories/highlights/{highlightId}/";
        if (!knownUrls.Add(batchUrl))
            return;

        var section = new InstagramBrowseSection
        {
            Key = $"highlight-{highlightId}",
            Title = string.IsNullOrWhiteSpace(title)
                ? loc.Get("InstagramSectionHighlight")
                : string.Format(loc.Get("InstagramHighlightSection"), title),
            BatchUrl = batchUrl
        };

        section.Items.Add(new InstagramBrowseItem
        {
            SectionKey = section.Key,
            SectionTitle = section.Title,
            Kind = "highlight",
            Title = loc.Get("InstagramHighlightBatch"),
            Url = batchUrl,
            ThumbnailUrl = thumbnailUrl
        });

        sections.Add(section);
    }

    private static void MergeUserData(List<InstagramBrowseSection> sections, JsonElement user, LocalizationService loc)
    {
        foreach (var highlight in ParseHighlights(user, loc))
            AddOrMergeSection(sections, highlight, loc);

        foreach (var posts in ParsePosts(user, loc))
            AddOrMergeSection(sections, posts, loc);
    }

    private static void MergeFromHtml(List<InstagramBrowseSection> sections, string html, LocalizationService loc)
    {
        var knownUrls = new HashSet<string>(
            sections.SelectMany(section => section.Items).Select(item => item.Url),
            StringComparer.OrdinalIgnoreCase);

        foreach (Match match in HighlightIdRegex().Matches(html))
        {
            AddHighlightFromHtml(sections, knownUrls, match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : null, loc);
        }

        foreach (Match match in HighlightUrlRegex().Matches(html))
            AddHighlightFromHtml(sections, knownUrls, match.Groups[1].Value, null, loc);

        foreach (Match match in HighlightReelIdRegex().Matches(html))
            AddHighlightFromHtml(sections, knownUrls, match.Groups[1].Value,
                match.Groups[2].Success ? match.Groups[2].Value : null, loc);

        var posts = GetOrCreatePostsSection(sections, loc);
        foreach (Match match in MediaUrlRegex().Matches(html))
        {
            var path = match.Groups[1].Value.ToLowerInvariant();
            var shortcode = match.Groups[2].Value;
            if (!IsLikelyMediaShortcode(shortcode))
                continue;

            var url = $"https://www.instagram.com/{path}/{shortcode}/";
            if (!knownUrls.Add(url))
                continue;

            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = path == "reel" ? "reel" : "post",
                Title = shortcode,
                Url = url
            });
        }

        foreach (Match match in ShortcodeRegex().Matches(html))
        {
            var shortcode = match.Groups[1].Value;
            if (!IsLikelyMediaShortcode(shortcode))
                continue;

            var url = $"https://www.instagram.com/p/{shortcode}/";
            if (!knownUrls.Add(url))
                continue;

            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = "post",
                Title = shortcode,
                Url = url
            });
        }

        foreach (Match match in PathShortcodeRegex().Matches(html))
        {
            var path = match.Groups[1].Value.ToLowerInvariant();
            var shortcode = match.Groups[2].Value;
            if (!IsLikelyMediaShortcode(shortcode))
                continue;

            var url = $"https://www.instagram.com/{path}/{shortcode}/";
            if (!knownUrls.Add(url))
                continue;

            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = path == "reel" ? "reel" : "post",
                Title = shortcode,
                Url = url
            });
        }
    }

    private static InstagramBrowseSection GetOrCreatePostsSection(List<InstagramBrowseSection> sections, LocalizationService loc)
    {
        var posts = sections.FirstOrDefault(section => section.Key == "posts");
        if (posts is not null)
            return posts;

        posts = new InstagramBrowseSection { Key = "posts", Title = loc.Get("InstagramSectionPosts") };
        sections.Add(posts);
        return posts;
    }

    private static void AddOrMergeSection(List<InstagramBrowseSection> sections, InstagramBrowseSection incoming, LocalizationService loc)
    {
        if (incoming.Items.Count == 0 && incoming.Key != "posts")
            return;

        if (incoming.Key == "posts")
        {
            var posts = GetOrCreatePostsSection(sections, loc);
            foreach (var item in incoming.Items)
            {
                var existingIndex = posts.Items.FindIndex(existing =>
                    existing.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)
                    && existing.CarouselSlideIndex == item.CarouselSlideIndex);

                if (existingIndex < 0)
                {
                    if (item.CarouselSlideIndex is null
                        && posts.Items.Any(existing =>
                            existing.Url.Equals(item.Url, StringComparison.OrdinalIgnoreCase)
                            && (existing.Kind is "carousel" or "carousel-slide")))
                    {
                        continue;
                    }

                    posts.Items.Add(item);
                    continue;
                }

                var existingItem = posts.Items[existingIndex];
                if (!string.IsNullOrWhiteSpace(item.MediaPk))
                    existingItem.MediaPk = item.MediaPk;
                if (!string.IsNullOrWhiteSpace(item.ThumbnailUrl))
                    existingItem.ThumbnailUrl = item.ThumbnailUrl;
            }

            return;
        }

        var existing = sections.FirstOrDefault(section => section.Key == incoming.Key);
        if (existing is null)
        {
            sections.Add(incoming);
            return;
        }

        var urls = new HashSet<string>(existing.Items.Select(item => item.Url), StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming.Items)
        {
            if (urls.Add(item.Url))
                existing.Items.Add(item);
        }
    }

    private static void MergeStoriesSection(List<InstagramBrowseSection> sections, InstagramBrowseSection? apiStories)
    {
        if (apiStories is null || apiStories.Items.Count == 0)
            return;

        var stories = sections.FirstOrDefault(section => section.Key == "stories");
        if (stories is null)
        {
            sections.Insert(0, apiStories);
            return;
        }

        var knownUrls = new HashSet<string>(stories.Items.Select(item => item.Url), StringComparer.OrdinalIgnoreCase);
        foreach (var item in apiStories.Items)
        {
            if (knownUrls.Add(item.Url))
                stories.Items.Add(item);
        }
    }

    private static async Task MergePostsFromFeedApi(
        HttpClient client,
        string userId,
        List<InstagramBrowseSection> sections,
        LocalizationService loc,
        CancellationToken ct)
    {
        var posts = GetOrCreatePostsSection(sections, loc);
        var knownUrls = new HashSet<string>(posts.Items.Select(item => item.Url), StringComparer.OrdinalIgnoreCase);
        string? maxId = null;

        for (var page = 0; page < 30; page++)
        {
            try
            {
                var requestUrl = string.IsNullOrWhiteSpace(maxId)
                    ? $"https://www.instagram.com/api/v1/feed/user/{Uri.EscapeDataString(userId)}/?count=50"
                    : $"https://www.instagram.com/api/v1/feed/user/{Uri.EscapeDataString(userId)}/?count=50&max_id={Uri.EscapeDataString(maxId)}";

                using var doc = await GetJsonAsync(client, requestUrl, loc, ct);
                if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    break;

                foreach (var media in items.EnumerateArray())
                    AddFeedMediaItem(posts, knownUrls, media, loc);

                if (!doc.RootElement.TryGetProperty("more_available", out var moreAvailable)
                    || !moreAvailable.GetBoolean())
                    break;

                if (!doc.RootElement.TryGetProperty("next_max_id", out var nextMaxIdProp))
                    break;

                maxId = nextMaxIdProp.ValueKind switch
                {
                    JsonValueKind.String => nextMaxIdProp.GetString(),
                    JsonValueKind.Number => nextMaxIdProp.GetRawText(),
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(maxId))
                    break;
            }
            catch (InvalidOperationException ex) when (ex.Message == loc.Get("InstagramRateLimited"))
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task MergeHighlightsFromTrayApi(
        HttpClient client,
        string userId,
        List<InstagramBrowseSection> sections,
        HashSet<string> knownUrls,
        LocalizationService loc,
        CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync(
                client,
                $"https://www.instagram.com/api/v1/highlights/{Uri.EscapeDataString(userId)}/highlights_tray/",
                loc,
                ct);

            if (!doc.RootElement.TryGetProperty("tray", out var tray) || tray.ValueKind != JsonValueKind.Array)
                return;

            foreach (var highlight in tray.EnumerateArray())
            {
                var rawId = highlight.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var highlightId = ExtractHighlightId(rawId);
                if (string.IsNullOrWhiteSpace(highlightId))
                    continue;

                var title = highlight.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                var thumbnailUrl = highlight.TryGetProperty("cover_media", out var cover)
                    ? InstagramMediaJsonHelper.ExtractThumbnailUrl(cover)
                    : null;
                AddHighlightFromHtml(sections, knownUrls, highlightId, title, loc, thumbnailUrl);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message == loc.Get("InstagramRateLimited"))
        {
            // keep HTML-derived highlights
        }
        catch
        {
            // keep HTML-derived highlights
        }
    }

    private static void AddFeedMediaItem(
        InstagramBrowseSection posts,
        HashSet<string> knownUrls,
        JsonElement media,
        LocalizationService loc)
    {
        var code = media.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(code) && media.TryGetProperty("media", out var nested))
            code = nested.TryGetProperty("code", out var nestedCode) ? nestedCode.GetString() : null;

        if (string.IsNullOrWhiteSpace(code))
            return;

        var isVideo = InstagramMediaJsonHelper.IsVideo(media);
        var takenAt = media.TryGetProperty("taken_at", out var ts) && ts.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            : (DateTime?)null;

        var path = isVideo ? "reel" : "p";
        var url = $"https://www.instagram.com/{path}/{code}/";
        var mediaPk = InstagramMediaJsonHelper.ExtractMediaPk(media);

        if (!isVideo
            && media.TryGetProperty("carousel_media", out var carousel)
            && carousel.ValueKind == JsonValueKind.Array
            && carousel.GetArrayLength() > 1)
        {
            AddCarouselPostItems(posts, knownUrls, url, mediaPk, carousel, takenAt, code, loc);
            return;
        }

        var itemKey = BuildPostItemKey(url, null);
        if (!knownUrls.Add(itemKey))
        {
            UpdateExistingPostItem(posts, url, null, mediaPk, media, loc);
            return;
        }

        posts.Items.Add(CreatePostBrowseItem(posts, url, isVideo, takenAt, code, mediaPk, media, loc, slideIndex: null));
    }

    private static void AddCarouselPostItems(
        InstagramBrowseSection posts,
        HashSet<string> knownUrls,
        string url,
        string? mediaPk,
        JsonElement carousel,
        DateTime? takenAt,
        string code,
        LocalizationService loc)
    {
        posts.Items.RemoveAll(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase));

        var total = carousel.GetArrayLength();
        var dateLabel = takenAt.HasValue ? takenAt.Value.ToString("d") : code;
        var batchKey = BuildPostItemKey(url, null, batch: true);
        if (knownUrls.Add(batchKey))
        {
            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = "carousel",
                Title = string.Format(loc.Get("InstagramCarouselBatch"), dateLabel, total),
                Url = url,
                MediaPk = mediaPk,
                ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(carousel[0])
            });
        }

        for (var slideIndex = 1; slideIndex <= total; slideIndex++)
        {
            var slideKey = BuildPostItemKey(url, slideIndex);
            var slide = carousel[slideIndex - 1];
            var isSlideVideo = InstagramMediaJsonHelper.IsVideo(slide);
            if (!knownUrls.Add(slideKey))
            {
                UpdateExistingPostItem(posts, url, slideIndex, mediaPk, slide, loc);
                continue;
            }

            posts.Items.Add(new InstagramBrowseItem
            {
                SectionKey = posts.Key,
                SectionTitle = posts.Title,
                Kind = "carousel-slide",
                Title = string.Format(
                    loc.Get("InstagramCarouselSlideItem"),
                    dateLabel,
                    slideIndex,
                    total,
                    isSlideVideo ? loc.Get("InstagramKindVideo") : loc.Get("InstagramKindPhoto")),
                Url = url,
                MediaPk = mediaPk,
                CarouselSlideIndex = slideIndex,
                ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(slide)
            });
        }

        knownUrls.Add(BuildPostItemKey(url, null));
    }

    private static InstagramBrowseItem CreatePostBrowseItem(
        InstagramBrowseSection posts,
        string url,
        bool isVideo,
        DateTime? takenAt,
        string code,
        string? mediaPk,
        JsonElement media,
        LocalizationService loc,
        int? slideIndex)
    {
        return new InstagramBrowseItem
        {
            SectionKey = posts.Key,
            SectionTitle = posts.Title,
            Kind = isVideo ? "reel" : "post",
            Title = takenAt.HasValue
                ? string.Format(loc.Get("InstagramPostItem"), takenAt.Value.ToString("d"), isVideo ? loc.Get("InstagramKindVideo") : loc.Get("InstagramKindPhoto"))
                : code,
            Url = url,
            MediaPk = mediaPk,
            CarouselSlideIndex = slideIndex,
            ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(media)
        };
    }

    private static void UpdateExistingPostItem(
        InstagramBrowseSection posts,
        string url,
        int? slideIndex,
        string? mediaPk,
        JsonElement media,
        LocalizationService loc)
    {
        var existing = posts.Items.FirstOrDefault(item =>
            item.Url.Equals(url, StringComparison.OrdinalIgnoreCase)
            && item.CarouselSlideIndex == slideIndex);

        if (existing is null)
            return;

        if (InstagramShortcodeHelper.IsLikelyValidMediaPk(mediaPk))
            existing.MediaPk = mediaPk;

        var thumb = InstagramMediaJsonHelper.ExtractThumbnailUrl(media);
        if (!string.IsNullOrWhiteSpace(thumb))
            existing.ThumbnailUrl = thumb;
    }

    private static string BuildPostItemKey(string url, int? slideIndex, bool batch = false)
    {
        if (batch)
            return url + "|all";

        return slideIndex is int index ? $"{url}|slide:{index}" : url;
    }

    private static async Task ExpandHighlightItemsAsync(
        HttpClient client,
        List<InstagramBrowseSection> sections,
        LocalizationService loc,
        CancellationToken ct)
    {
        foreach (var section in sections.Where(section => section.Key.StartsWith("highlight-", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(section.BatchUrl))
                continue;

            var highlightId = ExtractHighlightIdFromBatchUrl(section.BatchUrl);
            if (string.IsNullOrWhiteSpace(highlightId))
                continue;

            try
            {
                using var doc = await GetJsonAsync(
                    client,
                    $"https://www.instagram.com/api/v1/feed/reels_media/?reel_ids=highlight:{Uri.EscapeDataString(highlightId)}",
                    loc,
                    ct);

                if (!doc.RootElement.TryGetProperty("reels", out var reels)
                    || !reels.TryGetProperty($"highlight:{highlightId}", out var reel)
                    || !reel.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array
                    || items.GetArrayLength() == 0)
                {
                    continue;
                }

                var total = items.GetArrayLength();
                var batchItem = section.Items.FirstOrDefault(item => item.HighlightStoryIndex is null)
                    ?? section.Items.FirstOrDefault();
                var newItems = new List<InstagramBrowseItem>();
                if (batchItem is not null)
                {
                    newItems.Add(new InstagramBrowseItem
                    {
                        SectionKey = section.Key,
                        SectionTitle = section.Title,
                        Kind = "highlight",
                        Title = loc.Get("InstagramHighlightBatch"),
                        Url = section.BatchUrl,
                        ThumbnailUrl = batchItem.ThumbnailUrl ?? InstagramMediaJsonHelper.ExtractThumbnailUrl(items[0]),
                        Selected = batchItem.Selected
                    });
                }

                var index = 0;
                foreach (var story in items.EnumerateArray())
                {
                    index++;
                    var isVideo = InstagramMediaJsonHelper.IsVideo(story);
                    newItems.Add(new InstagramBrowseItem
                    {
                        SectionKey = section.Key,
                        SectionTitle = section.Title,
                        Kind = "highlight-story",
                        Title = string.Format(
                            loc.Get("InstagramHighlightStoryItem"),
                            index,
                            total,
                            isVideo ? loc.Get("InstagramKindVideo") : loc.Get("InstagramKindPhoto")),
                        Url = section.BatchUrl,
                        HighlightStoryIndex = index,
                        MediaPk = InstagramMediaJsonHelper.ExtractMediaPk(story),
                        ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(story)
                    });
                }

                section.Items.Clear();
                section.Items.AddRange(newItems);
            }
            catch
            {
                // keep the batch-only item from HTML/tray
            }
        }
    }

    private static string? ExtractHighlightIdFromBatchUrl(string batchUrl)
    {
        if (!Uri.TryCreate(batchUrl, UriKind.Absolute, out var uri))
            return null;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3
            && segments[0].Equals("stories", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("highlights", StringComparison.OrdinalIgnoreCase))
        {
            return segments[2].TrimEnd('/');
        }

        return null;
    }

    private static async Task<InstagramBrowseSection?> TryFetchStoriesAsync(
        HttpClient client,
        string username,
        string userId,
        LocalizationService loc,
        CancellationToken ct)
    {
        try
        {
            using var doc = await GetJsonAsync(
                client,
                $"https://www.instagram.com/api/v1/feed/user/{Uri.EscapeDataString(userId)}/story/",
                loc,
                ct);

            var section = new InstagramBrowseSection
            {
                Key = "stories",
                Title = loc.Get("InstagramSectionStories"),
                BatchUrl = $"https://www.instagram.com/stories/{username}/"
            };

            if (!doc.RootElement.TryGetProperty("reel", out var reel)
                || !reel.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in items.EnumerateArray())
            {
                var code = item.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var takenAt = item.TryGetProperty("taken_at", out var ts) && ts.TryGetInt64(out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                    : (DateTime?)null;

                section.Items.Add(new InstagramBrowseItem
                {
                    SectionKey = section.Key,
                    SectionTitle = section.Title,
                    Kind = "story",
                    Title = takenAt.HasValue
                        ? string.Format(loc.Get("InstagramStoryItem"), takenAt.Value.ToString("g"))
                        : loc.Get("InstagramStoryItemGeneric"),
                    Url = $"https://www.instagram.com/stories/{username}/{code}/",
                    MediaPk = InstagramMediaJsonHelper.ExtractMediaPk(item),
                    ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(item)
                });
            }

            return section.Items.Count > 0 ? section : null;
        }
        catch (InvalidOperationException ex) when (ex.Message == loc.Get("InstagramRateLimited"))
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static InstagramBrowseSection CreateStoriesSection(string username, LocalizationService loc)
    {
        var batchUrl = $"https://www.instagram.com/stories/{username}/";
        var section = new InstagramBrowseSection
        {
            Key = "stories",
            Title = loc.Get("InstagramSectionStories"),
            BatchUrl = batchUrl
        };

        section.Items.Add(new InstagramBrowseItem
        {
            SectionKey = section.Key,
            SectionTitle = section.Title,
            Kind = "story",
            Title = loc.Get("InstagramStoriesBatch"),
            Url = batchUrl
        });

        return section;
    }

    private static async Task<ProfilePageLoadResult> TryLoadUserFromProfilePageAsync(
        HttpClient client,
        string username,
        CancellationToken ct)
    {
        using var response = await client.GetAsync(
            $"https://www.instagram.com/{Uri.EscapeDataString(username)}/",
            ct);
        var html = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return new ProfilePageLoadResult(null, true, null);

        if (!response.IsSuccessStatusCode)
            return new ProfilePageLoadResult(null, false, null);

        var userJson = FindUserJsonInHtml(html, username);
        if (!string.IsNullOrWhiteSpace(userJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(userJson);
                if (doc.RootElement.TryGetProperty("edge_owner_to_timeline_media", out _)
                    || doc.RootElement.TryGetProperty("edge_felix_profile_timeline_media", out _)
                    || doc.RootElement.TryGetProperty("edge_highlight_reels", out _))
                {
                    return new ProfilePageLoadResult(JsonDocument.Parse(userJson), false, html);
                }
            }
            catch
            {
                // try script-tag parsing below
            }
        }

        var fromScripts = TryExtractUserFromScriptTags(html, username);
        return new ProfilePageLoadResult(fromScripts, false, html);
    }

    private static bool IsLikelyMediaShortcode(string shortcode) =>
        shortcode.Length >= 8
        && !shortcode.Contains("media", StringComparison.OrdinalIgnoreCase)
        && !shortcode.Contains("embed", StringComparison.OrdinalIgnoreCase);

    private static JsonDocument? TryExtractUserFromScriptTags(string html, string username)
    {
        foreach (Match match in ApplicationJsonScriptRegex().Matches(html))
        {
            var json = match.Groups[1].Value;
            if (!json.Contains($"\"username\":\"{username}\"", StringComparison.OrdinalIgnoreCase)
                && !json.Contains($"\"username\": \"{username}\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (TryFindUserElement(doc.RootElement, username, out var user))
                    return JsonDocument.Parse(user.GetRawText());
            }
            catch
            {
                // keep searching other script blocks
            }
        }

        return null;
    }

    private static async Task<JsonDocument> TryLoadUserFromApiAsync(
        HttpClient client,
        string username,
        LocalizationService loc,
        CancellationToken ct)
    {
        using var profileJson = await GetJsonAsync(
            client,
            $"https://www.instagram.com/api/v1/users/web_profile_info/?username={Uri.EscapeDataString(username)}",
            loc,
            ct);

        var user = profileJson.RootElement.GetProperty("data").GetProperty("user");
        return JsonDocument.Parse(user.GetRawText());
    }

    private static string? FindUserJsonInHtml(string html, string username)
    {
        var usernameNeedle = $"\"username\":\"{username}\"";
        var searchFrom = 0;

        while ((searchFrom = html.IndexOf(usernameNeedle, searchFrom, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var userMarker = html.LastIndexOf("\"user\":{", searchFrom, StringComparison.Ordinal);
            if (userMarker >= 0)
            {
                var json = ExtractBalancedJson(html, userMarker + "\"user\":".Length);
                if (!string.IsNullOrWhiteSpace(json)
                    && (json.Contains("edge_owner_to_timeline_media", StringComparison.Ordinal)
                        || json.Contains("edge_felix_profile_timeline_media", StringComparison.Ordinal)
                        || json.Contains("edge_highlight_reels", StringComparison.Ordinal)))
                {
                    return json;
                }
            }

            searchFrom += usernameNeedle.Length;
        }

        return null;
    }

    private static bool TryFindUserElement(JsonElement element, string username, out JsonElement user)
    {
        user = default;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("username", out var nameProp)
                && nameProp.ValueKind == JsonValueKind.String
                && username.Equals(nameProp.GetString(), StringComparison.OrdinalIgnoreCase)
                && element.TryGetProperty("id", out _)
                && (element.TryGetProperty("edge_owner_to_timeline_media", out _)
                    || element.TryGetProperty("edge_felix_profile_timeline_media", out _)
                    || element.TryGetProperty("edge_highlight_reels", out _)
                    || element.TryGetProperty("edge_followed_by", out _)
                    || element.TryGetProperty("full_name", out _)
                    || element.TryGetProperty("biography", out _)))
            {
                user = element;
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindUserElement(property.Value, username, out user))
                    return true;
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindUserElement(item, username, out user))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<InstagramBrowseSection> ParseHighlights(JsonElement user, LocalizationService loc)
    {
        if (!user.TryGetProperty("edge_highlight_reels", out var reels)
            || !reels.TryGetProperty("edges", out var edges)
            || edges.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var edge in edges.EnumerateArray())
        {
            if (!edge.TryGetProperty("node", out var node))
                continue;

            var rawId = node.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var highlightId = ExtractHighlightId(rawId);
            if (string.IsNullOrWhiteSpace(highlightId))
                continue;

            var title = node.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            var section = new InstagramBrowseSection
            {
                Key = $"highlight-{highlightId}",
                Title = string.IsNullOrWhiteSpace(title)
                    ? loc.Get("InstagramSectionHighlight")
                    : string.Format(loc.Get("InstagramHighlightSection"), title),
                BatchUrl = $"https://www.instagram.com/stories/highlights/{highlightId}/"
            };

            section.Items.Add(new InstagramBrowseItem
            {
                SectionKey = section.Key,
                SectionTitle = section.Title,
                Kind = "highlight",
                Title = loc.Get("InstagramHighlightBatch"),
                Url = section.BatchUrl,
                ThumbnailUrl = node.TryGetProperty("cover_media", out var cover)
                    ? InstagramMediaJsonHelper.ExtractThumbnailUrl(cover)
                    : null
            });

            yield return section;
        }
    }

    private static IEnumerable<InstagramBrowseSection> ParsePosts(JsonElement user, LocalizationService loc)
    {
        var section = new InstagramBrowseSection
        {
            Key = "posts",
            Title = loc.Get("InstagramSectionPosts")
        };

        if (!user.TryGetProperty("edge_owner_to_timeline_media", out var media)
            && !user.TryGetProperty("edge_felix_profile_timeline_media", out media))
        {
            yield return section;
            yield break;
        }

        if (!media.TryGetProperty("edges", out var edges)
            || edges.ValueKind != JsonValueKind.Array)
        {
            yield return section;
            yield break;
        }

        foreach (var edge in edges.EnumerateArray())
        {
            if (!edge.TryGetProperty("node", out var node))
                continue;

            var shortcode = node.TryGetProperty("shortcode", out var codeProp) ? codeProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(shortcode))
                continue;

            var isVideo = node.TryGetProperty("is_video", out var videoProp) && videoProp.GetBoolean();
            var takenAt = node.TryGetProperty("taken_at_timestamp", out var ts) && ts.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
                : (DateTime?)null;

            var path = isVideo ? "reel" : "p";
            section.Items.Add(new InstagramBrowseItem
            {
                SectionKey = section.Key,
                SectionTitle = section.Title,
                Kind = isVideo ? "reel" : "post",
                Title = takenAt.HasValue
                    ? string.Format(loc.Get("InstagramPostItem"), takenAt.Value.ToString("d"), isVideo ? loc.Get("InstagramKindVideo") : loc.Get("InstagramKindPhoto"))
                    : shortcode,
                Url = $"https://www.instagram.com/{path}/{shortcode}/",
                MediaPk = InstagramMediaJsonHelper.ExtractMediaPk(node),
                ThumbnailUrl = InstagramMediaJsonHelper.ExtractThumbnailUrl(node)
            });
        }

        yield return section;
    }

    private static HttpClient CreatePageClient(HttpClientHandler handler)
    {
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }

    private static HttpClient CreateApiClient(HttpClientHandler handler, string csrfToken)
    {
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("X-IG-App-ID", WebAppId);
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

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient client,
        string url,
        LocalizationService loc,
        CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException(loc.Get("InstagramRateLimited"));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ParseApiError(body, response.ReasonPhrase, loc));

        return JsonDocument.Parse(body);
    }

    private static bool LooksLikeRateLimit(string body) =>
        body.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
        || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
        || body.Contains("rate-limit", StringComparison.OrdinalIgnoreCase)
        || body.Contains("rate_limit", StringComparison.OrdinalIgnoreCase);

    private static string ParseApiError(string body, string? reason, LocalizationService loc)
    {
        if (LooksLikeRateLimit(body))
            return loc.Get("InstagramRateLimited");

        if (string.IsNullOrWhiteSpace(body))
            return reason ?? loc.Get("InstagramExtractError");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (LooksLikeRateLimit(text))
                        return loc.Get("InstagramRateLimited");
                    return text;
                }
            }
        }
        catch
        {
            // fall back to raw body below
        }

        return body.Length > 240 ? body[..240] : body;
    }

    private static string? ExtractBalancedJson(string text, int startIndex)
    {
        while (startIndex < text.Length && text[startIndex] != '{')
            startIndex++;

        if (startIndex >= text.Length)
            return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[startIndex..(i + 1)];
            }
        }

        return null;
    }

    private static string BuildCacheKey(string username, string cookieFilePath)
    {
        var stamp = File.Exists(cookieFilePath) ? File.GetLastWriteTimeUtc(cookieFilePath).Ticks : 0;
        return $"{username.ToLowerInvariant()}:{stamp}:v4";
    }

    private static List<InstagramBrowseSection> CloneSections(IReadOnlyList<InstagramBrowseSection> sections) =>
        sections.Select(section => new InstagramBrowseSection
        {
            Key = section.Key,
            Title = section.Title,
            BatchUrl = section.BatchUrl,
            Items = section.Items.Select(item => new InstagramBrowseItem
            {
                SectionKey = item.SectionKey,
                SectionTitle = item.SectionTitle,
                Title = item.Title,
                Url = item.Url,
                Kind = item.Kind,
                MediaPk = item.MediaPk,
                ThumbnailUrl = item.ThumbnailUrl,
                HighlightStoryIndex = item.HighlightStoryIndex,
                CarouselSlideIndex = item.CarouselSlideIndex,
                Selected = item.Selected
            }).ToList()
        }).ToList();

    private static string? ExtractHighlightId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return null;

        const string prefix = "highlight:";
        return rawId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? rawId[prefix.Length..]
            : rawId;
    }

    private static string? TryExtractUserIdFromHtml(string? html, string username)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        foreach (Match match in ProfilePageIdRegex().Matches(html))
        {
            var id = match.Groups[1].Value;
            if (id.Length >= 6)
                return id;
        }

        var needle = $"\"username\":\"{username}\"";
        var searchFrom = 0;
        while ((searchFrom = html.IndexOf(needle, searchFrom, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = Math.Max(0, searchFrom - 600);
            var end = Math.Min(html.Length, searchFrom + 600);
            var window = html[start..end];

            foreach (Match match in UserPkRegex().Matches(window))
            {
                var id = match.Groups[1].Value;
                if (id.Length >= 6)
                    return id;
            }

            searchFrom += needle.Length;
        }

        return null;
    }

    [GeneratedRegex(@"<script type=""application/json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ApplicationJsonScriptRegex();

    [GeneratedRegex(@"""shortcode""\s*:\s*""([A-Za-z0-9_-]{8,})""", RegexOptions.IgnoreCase)]
    private static partial Regex ShortcodeRegex();

    [GeneratedRegex(@"instagram\.com/(p|reel)/([A-Za-z0-9_-]{8,})/", RegexOptions.IgnoreCase)]
    private static partial Regex MediaUrlRegex();

    [GeneratedRegex(@"""id""\s*:\s*""highlight:([0-9]+)""(?:[^}]*""title""\s*:\s*""([^""]*)"")?", RegexOptions.IgnoreCase)]
    private static partial Regex HighlightIdRegex();

    [GeneratedRegex(@"stories/highlights/([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HighlightUrlRegex();

    [GeneratedRegex(@"""highlight_reel_id""\s*:\s*""(\d+)""(?:[^}]{0,120}?""title""\s*:\s*""([^""]*)"")?", RegexOptions.IgnoreCase)]
    private static partial Regex HighlightReelIdRegex();

    [GeneratedRegex(@"/(p|reel)/([A-Za-z0-9_-]{8,})/", RegexOptions.IgnoreCase)]
    private static partial Regex PathShortcodeRegex();

    [GeneratedRegex(@"""profilePage_(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilePageIdRegex();

    [GeneratedRegex(@"""(?:pk|id|user_id)""\s*:\s*""(\d{6,})""", RegexOptions.IgnoreCase)]
    private static partial Regex UserPkRegex();
}
