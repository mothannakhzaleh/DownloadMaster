# Chrome Web Store submission

Package: `ship/chrome-web-store/DownloadMaster-extension-<version>.zip` (manifest at the zip root,
README left out). Each new upload needs a higher `version` in `src/browser-extension/manifest.json`.

## Store listing

**Name:** DownloadMaster

**Summary (max 132 chars):**
Send browser downloads and online videos to the DownloadMaster desktop app — faster, resumable, with your logins kept.

**Description:**

> DownloadMaster connects Chrome to the free DownloadMaster desktop download manager (Windows,
> macOS, Linux).
>
> • Catches downloads and hands them to DownloadMaster: split into several connections, pause and
>   resume, queues and scheduling.
> • A "Download" button on video players sends the video (best quality, or a detected HLS/DASH/MP4
>   stream) to the app.
> • Your cookies, referrer and user agent go along, so downloads that need a login keep working.
> • Instagram: "Sync from browser" in the app takes your Instagram login from Chrome in one click,
>   and Fetch lists a profile's posts, reels, stories and highlights by reading the Instagram page
>   in your browser.
>
> Requires the DownloadMaster desktop app: https://github.com/mothannakhzaleh/DownloadMaster
> The extension talks only to the app on your own computer (127.0.0.1) and sends nothing to the
> internet. If the app is not running, Chrome keeps its downloads as usual.

**Category:** Tools (or Productivity) · **Language:** English

**Graphics:** icon 128×128 = `src/browser-extension/icons/icon128.png`; at least one screenshot
1280×800 or 640×400 (popup + the video button on a page); small promo tile 440×280 (optional).

## Privacy practices tab

**Single purpose:**
Hand downloads and videos from the browser to the DownloadMaster desktop download manager on the same computer.

**Permission justifications:**

| Permission | Justification |
|---|---|
| `downloads` | Detect a download the user starts and hand it to the DownloadMaster desktop app instead of the browser (cancelled in Chrome only after the app accepts it). |
| `cookies` | Send the cookies for the download's own site to the local desktop app so downloads that need the user's login work; and, when the user clicks "Sync from browser" in the app, send instagram.com cookies to it. Nothing leaves the computer. |
| Content scripts on instagram.com (including one in the page's own world) | Only when the desktop app opens an Instagram profile or post with its one-time code: read the media Instagram's page loads (file addresses, thumbnails, dates) and send that list to the local app, which downloads the files the user picks. Nothing is read otherwise, and nothing leaves the computer. |
| `webRequest` | Read response headers (content type, size) to detect video streams (HLS/DASH/MP4) on the current tab for the in-page Download button. No requests are blocked or modified. |
| `storage` | Save the user's extension settings and the streams found on each tab. |
| Host permission `<all_urls>` | Show the Download button on video players on any site, detect video streams there, and read that site's cookies for the download. Downloads and videos can come from any website, so the access cannot be limited to a fixed list. |

**Remote code:** No, I am not using remote code.

**Data usage** — tick:
- **Authentication information** (cookies, sent only to the local app)
- **Web history** (the page/download address the user chooses to send)
- **Website content** (detected video stream addresses; the Instagram media list the app asked for)

Then tick all three certifications (not sold, not used for unrelated purposes, not used for
creditworthiness).

**Privacy policy URL:**
https://github.com/mothannakhzaleh/DownloadMaster/blob/master/PRIVACY.md

## Notes for review

Reviewers can't test without the desktop app. In "Test instructions" (Distribution tab, if shown) say:
"Install DownloadMaster from https://github.com/mothannakhzaleh/DownloadMaster/releases/latest,
run it, then start any download or open a page with a video; the extension shows as connected in
its popup." Broad host permissions plus `cookies` usually means an in-depth review (days to weeks).
