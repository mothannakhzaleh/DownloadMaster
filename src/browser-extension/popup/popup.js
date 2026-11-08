// Extension popup: connection status, settings, and the current tab's detected streams.
const $ = (id) => document.getElementById(id);

function formatSize(bytes) {
  if (!bytes) return '';
  const units = ['B', 'KB', 'MB', 'GB'];
  let i = 0;
  let n = bytes;
  while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
  return `${n.toFixed(n >= 10 || i === 0 ? 0 : 1)} ${units[i]}`;
}

function streamLabel(stream) {
  const kind = { hls: 'HLS stream', dash: 'DASH stream', video: 'Video file', audio: 'Audio file' }[stream.type] || stream.type;
  return [kind, stream.quality, formatSize(stream.size)].filter(Boolean).join(' · ');
}

function showMessage(text, ok) {
  const el = $('message');
  el.textContent = text;
  el.className = `small ${ok ? 'ok' : 'bad'}`;
  el.hidden = false;
}

async function send(message) {
  const response = await chrome.runtime.sendMessage(message);
  if (response && response.ok === false) throw new Error(response.error || 'Failed');
  showMessage('Sent to DownloadMaster — check the app window.', true);
}

async function init() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const { status, settings } = await chrome.runtime.sendMessage({ type: 'status' });

  $('status').textContent = status ? `Connected · app ${status.version}` : 'App not running';
  $('status').className = status ? 'ok' : 'bad';
  $('offline').hidden = !!status;
  $('appOff').hidden = !status || status.interceptEnabled;
  $('sendPage').disabled = !status || !tab || !/^https?:/i.test(tab.url || '');

  $('interceptEnabled').checked = settings.interceptEnabled;
  $('videoButton').checked = settings.videoButton;
  $('minSizeMB').value = settings.minSizeMB;

  $('interceptEnabled').addEventListener('change', (e) => chrome.storage.local.set({ interceptEnabled: e.target.checked }));
  $('videoButton').addEventListener('change', (e) => chrome.storage.local.set({ videoButton: e.target.checked }));
  $('minSizeMB').addEventListener('change', (e) => {
    const value = Math.max(0, Number(e.target.value) || 0);
    chrome.storage.local.set({ minSizeMB: value });
  });

  $('sendPage').addEventListener('click', async () => {
    try {
      await send({ type: 'sendPage', tabId: tab.id, pageUrl: tab.url, pageTitle: tab.title });
    } catch (error) {
      showMessage(String(error.message || error), false);
    }
  });

  if (!tab) return;
  const { streams } = await chrome.runtime.sendMessage({ type: 'getStreams', tabId: tab.id });
  $('noStreams').hidden = streams.length > 0;
  const list = $('streams');
  for (const stream of streams) {
    const li = document.createElement('li');
    const text = document.createElement('div');
    const label = document.createElement('div');
    label.className = 'label';
    label.textContent = streamLabel(stream);
    const url = document.createElement('div');
    url.className = 'url';
    url.textContent = stream.url.length > 90 ? `${stream.url.slice(0, 87)}...` : stream.url;
    text.append(label, url);

    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = 'Send';
    button.disabled = !status;
    button.addEventListener('click', async () => {
      try {
        await send({ type: 'sendStream', tabId: tab.id, stream, pageUrl: tab.url, pageTitle: tab.title });
      } catch (error) {
        showMessage(String(error.message || error), false);
      }
    });

    li.append(text, button);
    list.appendChild(li);
  }
}

init().catch((error) => showMessage(String(error.message || error), false));
