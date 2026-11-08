# DownloadMaster browser extension (Chrome, Edge, Brave, Opera)

Sends browser downloads and videos to the DownloadMaster desktop app — cookies, referrer and
user agent included, so logged-in downloads work without copying cookies by hand.

## What it does

- **Catches downloads.** When you click a download link, the extension cancels the browser's
  download and hands it to DownloadMaster, which shows a "New download caught" window
  (even while the app sits in the tray). It catches the file types the app lists
  (zip, exe, iso, mp4, pdf, …) and anything larger than the size you set in the popup.
- **Video button.** Hover a video on any page and a **Download** button appears in its top-right
  corner. Pick "This video (best quality)" to let DownloadMaster (yt-dlp) read the page, or pick a
  detected HLS / DASH / MP4 stream.
- **Popup.** Shows whether the app is running, lets you turn catching and the video button on or
  off, and lists the streams found in the current tab.

If DownloadMaster isn't running (or interception is switched off in its tray menu), the browser
downloads normally.

## Install (unpacked)

1. Download `DownloadMaster-vX.Y.Z-browser-extension.zip` from the GitHub release and unzip it
   (or use this `browser-extension/` folder from the repo).
2. Open `chrome://extensions` (Edge: `edge://extensions`).
3. Turn on **Developer mode**.
4. Click **Load unpacked** and choose the unzipped folder.
5. Start DownloadMaster. The extension icon turns colored when it's connected.

## How it talks to the app

The app runs a small HTTP server on `127.0.0.1:45780` (your computer only):

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/status` | Is the app running, is interception on, which file types to catch |
| `POST /api/v1/download` | `{ url, filename, fileSize, mimeType, referrer, userAgent, cookies, headers }` |
| `POST /api/v1/media-stream` | `{ tabId, pageTitle, pageUrl, streamUrl, streamType, qualityLabel, cookies, userAgent, referrer }` |

The app only accepts browser requests from extension origins (`chrome-extension://`,
`moz-extension://`) and only on the loopback address, so web pages can't push downloads into it.
Every caught download is shown to you before anything is saved.

## Permissions

`downloads` (catch downloads), `cookies` (send your session cookies for that site only),
`webRequest` (spot video streams), `storage` (your settings), and access to all sites (to show the
video button and read cookies for the download's site).
