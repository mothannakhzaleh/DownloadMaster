# DownloadMaster browser extension — Privacy Policy

_Last updated: November 14, 2026_

The DownloadMaster extension connects your browser to the DownloadMaster desktop app running on
the same computer. It has no servers of its own and sends nothing to the developer or anyone else.

## What the extension handles

| Data | When | Where it goes |
|---|---|---|
| Download address, file name, size, type, referring page, browser user agent | You start a download that DownloadMaster takes over, or click its video button | The DownloadMaster app on your computer (`http://127.0.0.1:45780`) |
| Cookies for the site of that download or video | Same as above, so downloads that need your login keep working | The DownloadMaster app on your computer |
| instagram.com cookies | Only when you click **Sync from browser** in the DownloadMaster app and it opens the Instagram sync tab | The DownloadMaster app on your computer |
| Instagram posts, reels, stories and highlights of one account or post (addresses of the photo/video files, thumbnails, dates, the account name) | Only when you click **Fetch** or **Download** in the DownloadMaster app's Instagram tab and it opens that profile or post with its own one-time code; the extension reads what Instagram's page loads in that tab and nothing else | The DownloadMaster app on your computer |
| Addresses of video streams on the page you are viewing | While a page plays video, to list them under the video button | Kept in the browser's session storage for that tab; sent to the app only when you choose one |
| Your extension settings | When you change them in the popup | The browser's local extension storage |

## What the extension does not do

- It does not send any data to the developer, to analytics services, to advertisers or to any
  server on the internet. Its only network connection is to `127.0.0.1` (your own computer).
- It does not sell or share data, and does not use it for anything other than downloading the
  files and videos you ask DownloadMaster to download.
- It does not load or run code from the internet.
- It reads nothing on instagram.com unless the DownloadMaster app asked for that profile or post,
  and it stops when the app does not recognise the request.

## Good to know

While DownloadMaster reads a profile, the Instagram tab scrolls the posts and opens that account's
stories and highlights in your logged-in browser, the same as if you opened them yourself. Instagram
may therefore show you as a viewer of those stories.

## Your control

- Turn off download catching or the video button in the extension popup.
- Close the DownloadMaster app: the extension then sends nothing, and the browser keeps its downloads.
- Remove the extension at any time from your browser's extensions page.

## Contact

Questions: open an issue at https://github.com/mothannakhzaleh/DownloadMaster/issues
