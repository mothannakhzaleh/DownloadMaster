// DownloadMaster browser extension — service worker.
// 1. Hands browser downloads to the desktop app (with cookies, referrer and user agent).
// 2. Sniffs HLS / DASH / MP4 streams per tab for the in-page "Download" button.
// The app listens on 127.0.0.1:45780. If it isn't running, the browser keeps the download.

const API = 'http://127.0.0.1:45780/api/v1';
const DEFAULTS = { interceptEnabled: true, videoButton: true, minSizeMB: 1 };
const MIN_DIRECT_MEDIA_BYTES = 2 * 1024 * 1024;
const MAX_STREAMS_PER_TAB = 25;
// Sites whose video requests are useless on their own; the "whole page" option (yt-dlp) handles them.
const IGNORED_MEDIA_HOSTS = [/\.googlevideo\.com$/i, /\.fbcdn\.net$/i, /\.cdninstagram\.com$/i];

let statusCache = { at: 0, value: null };

// ---- helpers ---------------------------------------------------------------------------------

async function getSettings() {
  return { ...DEFAULTS, ...(await chrome.storage.local.get(DEFAULTS)) };
}

async function getStatus(force = false) {
  if (!force && Date.now() - statusCache.at < 4000) return statusCache.value;

  let value = null;
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 1200);
    const response = await fetch(`${API}/status`, { signal: controller.signal, cache: 'no-store' });
    clearTimeout(timer);
    if (response.ok) {
      const json = await response.json();
      if (json && json.app === 'DownloadMaster') value = json;
    }
  } catch {
    // app not running
  }

  statusCache = { at: Date.now(), value };
  updateActionIcon(value);
  return value;
}

function updateActionIcon(status) {
  const suffix = status ? '' : '-off';
  chrome.action.setIcon({
    path: {
      16: `icons/icon16${suffix}.png`,
      32: `icons/icon32${suffix}.png`,
      48: `icons/icon48${suffix}.png`,
      128: `icons/icon128${suffix}.png`
    }
  }).catch(() => {});
  chrome.action.setTitle({
    title: status ? 'DownloadMaster — connected' : 'DownloadMaster — app not running'
  }).catch(() => {});
}

async function cookieHeader(url) {
  try {
    const cookies = await chrome.cookies.getAll({ url });
    return cookies.map((c) => `${c.name}=${c.value}`).join('; ');
  } catch {
    return '';
  }
}

async function post(path, body) {
  const response = await fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!response.ok) {
    let message = `DownloadMaster answered ${response.status}`;
    try {
      const json = await response.json();
      if (json && json.error) message = json.error;
    } catch {}
    throw new Error(message);
  }
  return response.json().catch(() => ({}));
}

function fileNameFromUrl(url) {
  try {
    const path = new URL(url).pathname;
    return decodeURIComponent(path.substring(path.lastIndexOf('/') + 1));
  } catch {
    return '';
  }
}

function extensionOf(name) {
  const match = /\.([a-z0-9]{1,9})$/i.exec(name || '');
  return match ? match[1].toLowerCase() : '';
}

function baseName(path) {
  return (path || '').split(/[\\/]/).pop();
}

// ---- 1. download interception ----------------------------------------------------------------

chrome.downloads.onCreated.addListener(async (item) => {
  const url = item.finalUrl || item.url;
  if (!/^https?:/i.test(url)) return;               // blob:, data:, file: stay in the browser
  if (item.byExtensionId === chrome.runtime.id) return; // our own fallback re-download
  if (item.state && item.state !== 'in_progress') return;

  const settings = await getSettings();
  if (!settings.interceptEnabled) return;

  const status = await getStatus();
  if (!status || !status.interceptEnabled) return;  // app closed or interception off → browser keeps it

  const filename = baseName(item.filename) || fileNameFromUrl(url);
  const ext = extensionOf(filename) || extensionOf(fileNameFromUrl(url));
  const size = item.fileSize > 0 ? item.fileSize : item.totalBytes;
  const typeMatch = ext && (status.fileTypes || []).includes(ext);
  const bigEnough = size > 0 && size >= settings.minSizeMB * 1024 * 1024;
  if (!typeMatch && !bigEnough) return;

  try {
    await chrome.downloads.cancel(item.id);
    await chrome.downloads.erase({ id: item.id });
  } catch {
    // already finished or removed
  }

  const payload = {
    url,
    filename,
    fileSize: size > 0 ? size : 0,
    mimeType: item.mime || '',
    referrer: item.referrer || '',
    userAgent: navigator.userAgent,
    cookies: await cookieHeader(url),
    headers: {}
  };

  try {
    await post('/download', payload);
  } catch {
    // The app went away between the status check and the hand-off: give the download back.
    chrome.downloads.download({ url, filename: filename || undefined }).catch(() => {});
  }
});

// ---- 2. media stream sniffer -----------------------------------------------------------------

function streamTypeFor(url, contentType) {
  const type = (contentType || '').toLowerCase();
  let path = '';
  try { path = new URL(url).pathname.toLowerCase(); } catch {}

  if (type.includes('mpegurl') || path.endsWith('.m3u8')) return 'hls';
  if (type.includes('dash+xml') || path.endsWith('.mpd')) return 'dash';
  if (/\.(ts|m4s|m4f|aac)$/.test(path)) return null; // individual segments
  if (/^video\/(mp4|webm|x-matroska|quicktime|ogg)/.test(type)) return 'video';
  if (/^audio\/(mp4|mpeg|webm|ogg|aac|x-m4a)/.test(type)) return 'audio';
  return null;
}

function totalSize(headers) {
  const range = headers['content-range'];
  if (range) {
    const match = /\/(\d+)\s*$/.exec(range);
    if (match) return Number(match[1]);
  }
  return Number(headers['content-length'] || 0);
}

function qualityLabel(url) {
  const match = /(?:^|[^0-9])(2160|1440|1080|720|540|480|360|240|144)p?(?:[^0-9]|$)/i.exec(url);
  return match ? `${match[1]}p` : '';
}

async function loadStreams(tabId) {
  const key = `streams:${tabId}`;
  const stored = await chrome.storage.session.get(key);
  return stored[key] || [];
}

async function saveStreams(tabId, streams) {
  await chrome.storage.session.set({ [`streams:${tabId}`]: streams });
  chrome.action.setBadgeBackgroundColor({ tabId, color: '#6366F1' }).catch(() => {});
  chrome.action.setBadgeText({ tabId, text: streams.length ? String(streams.length) : '' }).catch(() => {});
}

async function addStream(tabId, stream) {
  const streams = await loadStreams(tabId);
  const key = stream.url.split('#')[0];
  if (streams.some((s) => s.url.split('#')[0] === key)) return;
  streams.unshift(stream);
  streams.length = Math.min(streams.length, MAX_STREAMS_PER_TAB);
  await saveStreams(tabId, streams);
  chrome.tabs.sendMessage(tabId, { type: 'streamsChanged', count: streams.length }).catch(() => {});
}

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    if (details.tabId < 0 || !/^https?:/i.test(details.url)) return;

    const headers = {};
    for (const h of details.responseHeaders || []) headers[h.name.toLowerCase()] = h.value || '';

    const contentType = (headers['content-type'] || '').split(';')[0].trim();
    const type = streamTypeFor(details.url, contentType);
    if (!type) return;

    let host = '';
    try { host = new URL(details.url).hostname; } catch {}
    if (IGNORED_MEDIA_HOSTS.some((re) => re.test(host))) return;

    const size = totalSize(headers);
    if ((type === 'video' || type === 'audio') && size < MIN_DIRECT_MEDIA_BYTES) return;

    addStream(details.tabId, {
      url: details.url,
      type,
      mime: contentType,
      size,
      quality: qualityLabel(details.url),
      seenAt: Date.now()
    });
  },
  { urls: ['<all_urls>'], types: ['xmlhttprequest', 'media', 'other'] },
  ['responseHeaders']
);

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  // New page in the tab → forget the old page's streams.
  if (changeInfo.url) saveStreams(tabId, []).catch(() => {});
});

chrome.tabs.onRemoved.addListener((tabId) => {
  chrome.storage.session.remove(`streams:${tabId}`).catch(() => {});
});

// ---- 3. messages from content script and popup ----------------------------------------------

async function sendStream(tab, stream, pageUrl, pageTitle) {
  const referrer = pageUrl || tab?.url || '';
  return post('/media-stream', {
    tabId: tab?.id ?? -1,
    pageTitle: pageTitle || tab?.title || '',
    pageUrl: referrer,
    streamUrl: stream.url,
    streamType: stream.type,
    qualityLabel: stream.quality || '',
    fileSize: stream.size || 0,
    cookies: await cookieHeader(stream.url),
    userAgent: navigator.userAgent,
    referrer
  });
}

async function sendPage(tab, pageUrl, pageTitle) {
  const url = pageUrl || tab?.url;
  return post('/media-stream', {
    tabId: tab?.id ?? -1,
    pageTitle: pageTitle || tab?.title || '',
    pageUrl: url,
    streamUrl: url,
    streamType: 'page',
    cookies: await cookieHeader(url),
    userAgent: navigator.userAgent,
    referrer: url
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  (async () => {
    const tabId = message.tabId ?? sender.tab?.id;
    const tab = sender.tab ?? (tabId != null ? await chrome.tabs.get(tabId).catch(() => null) : null);

    switch (message.type) {
      case 'status': {
        const [status, settings] = await Promise.all([getStatus(true), getSettings()]);
        return { status, settings };
      }
      case 'getStreams':
        return { streams: tabId != null ? await loadStreams(tabId) : [] };
      case 'sendStream':
        if (!(await getStatus(true))) throw new Error('DownloadMaster is not running.');
        await sendStream(tab, message.stream, message.pageUrl, message.pageTitle);
        return { ok: true };
      case 'sendPage':
        if (!(await getStatus(true))) throw new Error('DownloadMaster is not running.');
        await sendPage(tab, message.pageUrl, message.pageTitle);
        return { ok: true };
      default:
        return { ok: false, error: 'Unknown message' };
    }
  })()
    .then(sendResponse)
    .catch((error) => sendResponse({ ok: false, error: String(error?.message || error) }));
  return true; // async response
});

chrome.runtime.onStartup.addListener(() => getStatus(true));
chrome.runtime.onInstalled.addListener(() => getStatus(true));
