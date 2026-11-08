# DownloadMaster — IDM-Style Architecture & C++ Native Upgrade Specification

**Repository:** `https://github.com/mothannakhzaleh/DownloadMaster/`

**Target Architecture:** Native C++ Desktop Application (Event-Driven Dear ImGui `docking` GUI) + Chromium (Chrome/Edge) Manifest V3 Extension

**Core Goal:** Transform `DownloadMaster` into a standalone, high-performance Internet Download Manager (IDM) alternative featuring a modern, ultra-lightweight C++ GUI (0% idle GPU/CPU usage), automatic browser download interception, zero manual cookie copying, an in-page video stream sniffer, System Tray background execution, and zero external CLI dependencies (`yt-dlp.exe` or `ffmpeg.exe`).

---

## 1. Architectural Overview & Dependency Strategy

To eliminate external portable binaries (`ffmpeg.exe`, `yt-dlp.exe`), manual cookie inputs, and heavy GUI frameworks while maintaining 100% compatibility with protected downloads and video streams, the agent must implement the following architecture:

1. **Modern, Non-GPU-Hungry C++ GUI (Event-Driven Dear ImGui `docking`):**
   * Use Dear ImGui (`docking` branch) with native `Win32 + DirectX 11` (or `GLFW/SDL3 + OpenGL3`) configured as an **event-driven desktop utility**, *never* as an unthrottled 60 FPS game loop.
   * When minimized to the System Tray, completely suspend swap-chain rendering (`0%` GPU usage, `<0.1%` CPU usage). When visible and idle, block on OS window messages (`MsgWaitForMultipleObjects` / `WaitMessage`) so frames are only drawn on user input or download progress ticks.
   * Style ImGui with a modern Windows 11 / Fluent dark-and-light theme, anti-aliased rounded corners, embedded vector fonts (`Inter` / `Segoe UI` + `FontAwesome6` icons compiled into the binary), and Multi-Viewports so popups ("New Download Caught") float as independent OS windows even when the main window is hidden.

2. **Zero Manual Cookies via Browser Extension:**
   * A Chrome/Edge Manifest V3 extension intercepts downloads and media streams directly inside the browser context, automatically extracting session cookies (`chrome.cookies`), `Referer`, `User-Agent`, and authorization headers, and forwarding them to the running C++ application.

3. **Replacing `yt-dlp.exe` with Browser Network Sniffing + Native C++ Parser:**
   * **Primary Path (95% of sites):** The browser extension monitors `chrome.webRequest` for media MIME types (`.m3u8`, `.mpd`, `video/mp4`, `video/webm`, `audio/mp4`) and captures the resolved stream URLs and headers straight from the browser tab—just like IDM.
   * **Fallback Path (Direct URL paste for YouTube/sites):** A native C++ extractor using `libcurl` + `nlohmann/json` + embedded `QuickJS` (single-file C JS engine) to evaluate player signature ciphers in-process without Python or `yt-dlp`.

4. **Replacing `ffmpeg.exe` with In-Process C Libraries:**
   * Link directly against minimal FFmpeg C shared/static libraries (`libavformat`, `libavcodec`, `libavutil`) configured solely for demuxing and stream-copy remuxing (`--disable-programs --disable-doc --disable-encoders --disable-decoders`).
   * All `.ts` segment concatenation and separate video + audio track merging into `.mp4`/`.mkv` must happen in-memory/in-process via C API calls (`avformat_alloc_output_context2`, `av_interleaved_write_frame`), never via `CreateProcess` or `std::system`.

---

## 2. Target Repository Directory Structure

Refactor and expand the repository into the following modular structure:

```text
DownloadMaster/
├── CMakeLists.txt
├── vcpkg.json                          # Dependencies: imgui[docking-experimental,win32-binding,dx11-binding], curl, openssl, ffmpeg, nlohmann-json
├── src/
│   ├── main.cpp                        # Single-instance mutex check + event-driven render loop
│   ├── ui/
│   │   ├── GuiRenderer.hpp / .cpp      # Event-driven ImGui DX11/Win32 context, idle sleep manager, DPI scaling
│   │   ├── ThemeManager.hpp / .cpp     # Modern Fluent Dark/Light styling, embedded TTF fonts & FontAwesome icons
│   │   ├── MainWindow.hpp / .cpp       # Main dockspace: Sidebar categories, sortable table, chunk visualizer
│   │   ├── TrayManager.hpp / .cpp      # Native Win32 Shell_NotifyIconW, native context menu, toast notifications
│   │   ├── DownloadDialog.hpp / .cpp   # Standalone multi-viewport OS popup for intercepted downloads
│   │   └── FloatingDropZone.hpp / .cpp # Optional always-on-top mini dropbox / speed widget
│   ├── core/
│   │   ├── DownloadEngine.hpp / .cpp   # Multi-connection manager & queue scheduler
│   │   ├── SegmentWorker.hpp / .cpp    # Dynamic file segmentation (in-flight chunk splitting)
│   │   ├── ResumeState.hpp / .cpp      # Crash-safe .dmresume binary/JSON state serialization
│   │   ├── CategoryRouter.hpp / .cpp   # Auto-categorization (Video, Music, Programs, Compressed, Docs)
│   │   └── SpeedLimiter.hpp / .cpp     # Token-bucket bandwidth throttler
│   ├── media/
│   │   ├── HlsParser.hpp / .cpp        # Native .m3u8 master/media playlist parser & AES-128 decryptor
│   │   ├── DashParser.hpp / .cpp       # Native .mpd parser & segment template resolver
│   │   ├── NativeMuxer.hpp / .cpp      # libavformat in-process video+audio remuxer (Zero ffmpeg.exe)
│   │   └── NativeExtractor.hpp / .cpp  # Embedded QuickJS + Innertube extractor for pasted URLs
│   └── ipc/
│       ├── LocalBridgeServer.hpp / .cpp# Localhost HTTP/WebSocket server (127.0.0.1:45780)
│       └── ClipboardMonitor.hpp / .cpp # OS clipboard listener for copied URLs
└── browser-extension/
    ├── manifest.json                   # Chromium Manifest V3 (Chrome & Edge)
    ├── background.js                   # Service worker: download interception, cookie extraction, stream sniffer
    ├── content.js                      # Injects floating "Download Video" overlay on HTML5 <video> players
    ├── overlay.css                     # Styling for the in-page video download button
    └── popup/
        ├── popup.html                  # Extension status, toggle interception, quick settings
        └── popup.js
```

---

## 3. Step-by-Step Implementation Plan for the Agent

### Phase 1: Modern, Non-GPU-Hungry C++ GUI (`GuiRenderer`, `ThemeManager`, `MainWindow`)

1. **Event-Driven Power-Saving Render Loop (Zero GPU Bloat):**
   * **Strictly Forbidden:** Do not implement an unthrottled `while (!done)` loop that calls `Present()` continuously.
   * **State 1 — Hidden in System Tray (`!isMainWindowVisible && activePopups == 0`):**
     * Completely skip `ImGui::NewFrame()`, `ImGui::Render()`, and `SwapChain->Present()`.
     * Block the main thread using `WaitMessage()` (Win32) until an OS tray event, clipboard event, or custom `WM_USER_IPC_WAKEUP` message arrives from the background IPC server.
     * Release or trim DX11 staging buffers via `IDXGIDevice3::Trim()` when minimizing to tray so background RAM and GPU memory drop to near zero.
   * **State 2 — Window Visible & Idle (No Active Downloads):**
     * Use `MsgWaitForMultipleObjectsEx(..., 250, QS_ALLINPUT, MWMO_INPUTAVAILABLE)` so the main thread sleeps when the user is not moving the mouse or typing.
     * Only render a frame when an input event occurs or a dirty flag (`needsRedraw = true`) is set, plus 2–3 frames after the last input event so ImGui animations/tooltips settle cleanly.
   * **State 3 — Active Download Progress:**
     * When a download is actively progressing and a window is visible, wake the UI loop at a fixed **20–30 Hz (`33ms–50ms`)** interval via an atomic dirty flag or timer so progress bars and speed counters look smooth while using `< 1%` GPU.

2. **Independent OS Popup Windows (even when Main Window is in Tray):**
   * Enable `ImGuiConfigFlags_DockingEnable | ImGuiConfigFlags_ViewportsEnable`.
   * When the main window is hidden in the System Tray and the browser extension intercepts a new download, set `showInterceptDialog = true` and wake the UI loop.
   * Render *only* the `DownloadDialog` ImGui window with `ImGuiWindowFlags_NoDocking | ImGuiWindowFlags_AlwaysAutoResize`. ImGui's multi-viewport backend will automatically spawn a standalone native OS window on top of Chrome/Edge without needing to restore the main `DownloadMaster` window.

3. **Modern C++ Desktop Visual Polish (`ThemeManager`):**
   * Query Per-Monitor DPI awareness (`SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)`) and scale fonts and `ImGuiStyle` paddings dynamically.
   * Embed compressed TTF byte arrays for a clean UI font (`Inter-Regular`, `Inter-SemiBold`) and `FontAwesome6` icons so zero external `.ttf` files are needed.
   * Configure a sleek Fluent aesthetic:
     * `style.WindowRounding = 8.0f; style.FrameRounding = 6.0f; style.PopupRounding = 6.0f;`
     * `style.CellPadding = ImVec2(10.0f, 8.0f); style.ItemSpacing = ImVec2(8.0f, 8.0f);`
     * Custom `ImDrawList` segmented progress bar widget: draw a rounded track showing each worker thread's byte-range block filling up in real time (just like IDM's dynamic segment visualizer).

---

### Phase 2: Single-Instance Daemon & System Tray (`TrayManager`)

1. **Single-Instance Enforcement:**
   * On startup in `main.cpp`, create a named OS mutex (`Global\DownloadMaster_SingleInstance_Mutex` on Windows) or local socket lock. If an instance is already running, forward any CLI arguments to the running instance via IPC and exit immediately.

2. **System Tray Lifecycle:**
   * Implement `TrayManager` using native Windows API (`Shell_NotifyIconW`).
   * Intercept the main window's close event (`WM_CLOSE`): hide the HWND (`ShowWindow(hwnd, SW_HIDE)`) instead of quitting, call `dxgiDevice3->Trim()`, and display a one-time balloon notification confirming `DownloadMaster is running in the background`.
   * Support starting minimized to tray via `--minimized` flag and optional Windows Registry startup key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

3. **Tray Context Menu & Notifications:**
   * Right-clicking the tray icon must open a native Win32 popup menu (`CreatePopupMenu` + `TrackPopupMenu`) so it works instantly with 0% GPU overhead:
     * *Open DownloadMaster*
     * *Add URL from Clipboard*
     * *Browser Interception: Enabled (Toggle)*
     * *Speed Limiter (Unlimited / 2 MB/s / 500 KB/s)*
     * *Pause All / Resume All*
     * *Exit*
   * Double-clicking the tray icon restores (`SW_SHOW` + `SetForegroundWindow`) and wakes the main window.

4. **Clipboard Monitor:**
   * Listen to OS clipboard changes (`AddClipboardFormatListener` on Windows). When a valid HTTP/HTTPS URL ending in target extensions (`.zip`, `.7z`, `.rar`, `.exe`, `.msi`, `.iso`, `.mp4`, `.mkv`, `.mp3`, `.pdf`) is copied, post `WM_USER_IPC_WAKEUP` to pop up the standalone "New Download" dialog.

---

### Phase 3: Local IPC Bridge Server (`LocalBridgeServer`)

1. **Localhost HTTP + WebSocket Server:**
   * Run on a dedicated background `std::jthread`, bound strictly to `127.0.0.1:45780`.
   * Implement CORS headers allowing requests from `chrome-extension://*`.

2. **Required REST Endpoints:**
   * `GET /api/v1/status`
     * Returns `{ "running": true, "version": "2.0.0", "interceptEnabled": true, "fileTypes": ["zip","exe","mp4","mkv","iso","rar","7z","pdf","mp3"] }`.
   * `POST /api/v1/download`
     * Payload:
       ```json
       {
         "url": "https://example.com/file.zip",
         "filename": "file.zip",
         "fileSize": 104857600,
         "mimeType": "application/zip",
         "referrer": "https://example.com/download-page",
         "userAgent": "Mozilla/5.0 ...",
         "cookies": "session_id=xyz; auth_token=abc",
         "headers": { "Authorization": "Bearer ..." }
       }
       ```
     * Action: Enqueues the pending download struct, posts `PostMessage(mainHwnd, WM_USER_IPC_WAKEUP, 0, 0)` to wake the sleeping UI thread, and opens the standalone `DownloadDialog` with all headers and cookies pre-populated.
   * `POST /api/v1/media-stream`
     * Payload:
       ```json
       {
         "tabId": 102,
         "pageTitle": "Sample Video Page",
         "pageUrl": "https://example.com/watch/123",
         "streamUrl": "https://cdn.example.com/master.m3u8",
         "streamType": "hls",
         "qualityLabel": "1080p",
         "cookies": "hdntl=exp=...",
         "userAgent": "Mozilla/5.0 ...",
         "referrer": "https://example.com/"
       }
       ```
     * Action: Wakes the UI thread, parses the manifest (`HlsParser` or `DashParser`), lets the user select resolution/audio track in the popup dialog, and begins multi-threaded segment downloading.

---

### Phase 4: Chrome & Edge Browser Extension (Manifest V3)

Create the extension in `/browser-extension` compatible with both Google Chrome and Microsoft Edge.

#### 1. `manifest.json` Requirements:
* **Permissions:** `downloads`, `cookies`, `webRequest`, `storage`, `tabs`, `scripting`.
* **Host Permissions:** `<all_urls>`, `http://127.0.0.1:45780/*`.

#### 2. Automatic Download Interception (`background.js`):
* Periodically ping `http://127.0.0.1:45780/api/v1/status` (or check on download creation) to verify `DownloadMaster` is running in the system tray.
* Register `chrome.downloads.onCreated.addListener(async (downloadItem) => { ... })`:
  1. Ignore `blob:` or `data:` URLs or downloads below a minimum size threshold (e.g., `< 1 MB` unless matched by extension).
  2. Check if the C++ daemon is reachable. If not reachable, let the browser handle the download normally.
  3. If reachable, immediately call `chrome.downloads.cancel(downloadItem.id)` and `chrome.downloads.erase({ id: downloadItem.id })`.
  4. Query `chrome.cookies.getAll({ url: downloadItem.finalUrl || downloadItem.url })` and format them into a standard `Cookie: key=value; key2=value2` string.
  5. Dispatch the full request metadata (`url`, `referrer`, `mime`, `filename`, `cookies`, `navigator.userAgent`) via `POST http://127.0.0.1:45780/api/v1/download`.

#### 3. Media Stream Sniffer & In-Page Video Button (`background.js` + `content.js`):
* In `background.js`, attach `chrome.webRequest.onHeadersReceived` across `<all_urls>`:
  * Inspect `Content-Type` headers for:
    * `application/vnd.apple.mpegurl` or `application/x-mpegurl` (HLS `.m3u8`)
    * `application/dash+xml` (DASH `.mpd`)
    * `video/mp4`, `video/webm`, `audio/mp4` (Direct media streams > 2 MB)
  * When detected, fetch cookies for the stream URL, cache the detected stream entry per `tabId`, and send a message (`chrome.tabs.sendMessage`) to `content.js` in that tab.
* In `content.js`:
  * Observe the DOM (`MutationObserver`) for active `<video>` elements with width $\ge 250\text{px}$.
  * Anchor a sleek floating IDM-style badge (`"Download with DownloadMaster"`) at the top-right corner of the video player.
  * Clicking the badge opens a dropdown of detected resolutions/streams for that tab and sends the selected stream directly to `POST http://127.0.0.1:45780/api/v1/media-stream`.

---

### Phase 5: High-Speed C++ Engine with Dynamic Segmentation (`DownloadEngine`)

1. **Multi-Connection Architecture (`libcurl` multi interface):**
   * Perform an initial `HEAD` or `GET` (`Range: bytes=0-0`) request with the browser's exact `Cookie`, `Referer`, and `User-Agent` headers.
   * Extract `Content-Length`, `Accept-Ranges`, and `Content-Disposition` filename.

2. **Dynamic In-Flight Chunk Splitting (IDM Algorithm):**
   * Pre-allocate the target `.part` file on disk (`SetFileValidData` / `ftruncate`).
   * Divide the file into $N$ initial segments (default $N = 8$, configurable up to $32$).
   * **Dynamic Work Stealing:** Whenever a segment worker finishes its assigned byte range `[start, end]`, scan all currently active workers to find the worker with the largest remaining byte count $(R = \text{end} - \text{current\_offset})$.
   * If $R > 1\text{ MB}$, dynamically halve that worker's range at $\text{midpoint} = \text{current\_offset} + \lfloor R / 2 \rfloor$:
     * Update the existing worker's end byte to $\text{midpoint} - 1$.
     * Assign the newly freed worker a new HTTP range request for `Range: bytes=midpoint-end`.
   * This ensures maximum bandwidth utilization until the very last megabyte finishes.

3. **Crash-Safe Resume Metadata (`.dmresume`) & Link Refresh:**
   * Flush progress offsets for every segment to `<filename>.dmresume` every 1 second.
   * If a server link expires (HTTP `403 Forbidden` or `410 Gone` on resume), implement a **"Refresh Download Address"** state: when the user clicks the download link again in their browser, match the filename/size and seamlessly attach the new URL + new cookies to the existing `.part` file without losing downloaded bytes.

---

### Phase 6: Native Media Engine (`HlsParser`, `DashParser`, `NativeMuxer`)

1. **Native HLS & DASH Downloading (Zero `yt-dlp`):**
   * `HlsParser`: Parse `#EXT-X-STREAM-INF` master playlists to list available resolutions and bitrates, as well as separate `#EXT-X-MEDIA:TYPE=AUDIO` tracks.
   * Parse media playlists (`#EXTINF`, `#EXT-X-KEY`, `#EXT-X-MAP`), download segments in parallel using `DownloadEngine` worker pool, and decrypt `AES-128` encrypted segments in-memory using OpenSSL `EVP_DecryptInit_ex`.
   * `DashParser`: Parse `.mpd` XML manifests (`AdaptationSet`, `Representation`, `SegmentTemplate`) and download video and audio initialization + media segments concurrently.

2. **In-Process FFmpeg C API Remuxer (`NativeMuxer` — Zero `ffmpeg.exe`):**
   * Implement `NativeMuxer::remux(videoInputPath, audioInputPath, outputPath)` using `libavformat`:
     1. Open input contexts with `avformat_open_input` and `avformat_find_stream_info`.
     2. Allocate output context via `avformat_alloc_output_context2(&outCtx, nullptr, "mp4", outputPath.c_str())`.
     3. Copy codec parameters (`avcodec_parameters_copy`) from input video/audio streams to output streams without re-encoding (`AV_CODEC_ID` stream copy).
     4. Write header (`avformat_write_header`), loop through packets (`av_read_frame`), rescale timestamps (`av_packet_rescale_ts`), and interleave packets (`av_interleaved_write_frame`).
     5. Finalize container with `av_write_trailer`.
   * Result: Near-instant muxing (1–2 seconds for a full movie) with zero CPU re-encoding and zero external `.exe` files.

---

### Phase 7: Essential IDM Parity Features to Include

Ensure the UI and core engine include these essential IDM capabilities:

| Feature | Implementation Requirement |
| ----- | ----- |
| **Auto File Categorization** | Automatically route files into `Downloads/Video`, `Downloads/Music`, `Downloads/Programs`, `Downloads/Compressed`, and `Downloads/Documents` based on MIME/extension. |
| **Smart Queue & Scheduler** | Support multiple queues (Main Queue, Media Queue), max concurrent active downloads (e.g., 3 at once), scheduled start/stop times, and "Shutdown PC when done". |
| **Global Speed Limiter** | Token-bucket rate limiter across all active `libcurl` sockets (`CURL_MAX_RECV_SPEED_LARGE`) so users can browse or game without lag. |
| **Batch / Wildcard Downloader** | Dialog to expand numeric or alphabetic patterns like `https://example.com/part_[01-20].rar` into queued batch downloads. |
| **Real-Time Per-Segment Progress Bar** | Custom `ImDrawList` multi-segment bar inside the download dialog showing each thread's byte progress filling up in real time. |
| **Post-Download Integrity & Actions** | Optional SHA-256 checksum verification, Windows Defender scan hook (`MpScanStart` / command-line scanner), and "Open File / Open Folder" completion dialog. |

---

## 4. Definition of Done & Verification Checklist

The agent must verify the following milestones before marking the upgrade complete:

* [ ] **Standalone Binary Build:** The C++ project compiles cleanly via CMake without requiring `yt-dlp.exe` or `ffmpeg.exe` in the runtime directory.
* [ ] **0% Idle GPU / CPU Usage:** When sitting idle or minimized to the System Tray, Task Manager reports `0%` GPU and `0.0%` CPU usage for `DownloadMaster.exe`.
* [ ] **System Tray & Independent Popups:** Closing the main window minimizes `DownloadMaster` to the System Tray; when a new download is caught by the browser, the "New Download" ImGui viewport window pops up independently without restoring the main window.
* [ ] **Zero-Cookie Browser Interception:** Clicking a protected download link in Chrome/Edge (requiring login cookies) cancels the browser's built-in download and opens `DownloadMaster` with cookies and headers automatically populated.
* [ ] **In-Page Video Sniffing:** Playing an HTML5 video (direct MP4 or HLS `.m3u8`) in Chrome/Edge displays the floating "Download with DownloadMaster" overlay button, and clicking a resolution downloads and muxes the stream into a playable `.mp4`.
* [ ] **Dynamic Segmentation & Resume:** A large download splits across multiple workers, dynamically steals remaining chunks when early threads finish, and resumes cleanly after killing and restarting the process.