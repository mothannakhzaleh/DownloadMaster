// DownloadMaster — floating "Download" button on HTML5 video players.
// Runs in every frame; the button lives in a shadow root so page CSS can't touch it.
(() => {
  if (window.__downloadMasterOverlay) return;
  window.__downloadMasterOverlay = true;

  const MIN_WIDTH = 250;
  const MIN_HEIGHT = 140;
  const HIDE_DELAY_MS = 1800;
  const badges = new Map(); // video element -> badge
  let enabled = true;

  chrome.storage.local.get({ videoButton: true }).then((s) => {
    enabled = s.videoButton;
    scan();
  });

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area === 'local' && changes.videoButton) {
      enabled = changes.videoButton.newValue;
      if (!enabled) removeAll();
      scan();
    }
  });

  function removeAll() {
    for (const badge of badges.values()) badge.host.remove();
    badges.clear();
  }

  function isBigEnough(video) {
    const rect = video.getBoundingClientRect();
    return rect.width >= MIN_WIDTH && rect.height >= MIN_HEIGHT;
  }

  function createBadge(video) {
    const host = document.createElement('div');
    host.style.cssText = 'position:fixed;top:0;left:0;z-index:2147483646;display:none;';
    const shadow = host.attachShadow({ mode: 'closed' });

    const style = document.createElement('link');
    style.rel = 'stylesheet';
    style.href = chrome.runtime.getURL('overlay.css');
    shadow.appendChild(style);

    const wrap = document.createElement('div');
    wrap.className = 'dm-wrap';
    wrap.innerHTML = `
      <button class="dm-button" type="button" title="Download with DownloadMaster">
        <span class="dm-icon">&#x2B07;</span><span class="dm-label">Download</span>
      </button>
      <div class="dm-menu" hidden></div>
      <div class="dm-toast" hidden></div>`;
    shadow.appendChild(wrap);

    const badge = {
      host,
      button: wrap.querySelector('.dm-button'),
      menu: wrap.querySelector('.dm-menu'),
      toast: wrap.querySelector('.dm-toast'),
      hideTimer: 0,
      hovered: false
    };

    badge.button.addEventListener('click', (event) => {
      event.preventDefault();
      event.stopPropagation();
      if (badge.menu.hidden) openMenu(badge);
      else badge.menu.hidden = true;
    });

    host.addEventListener('mouseenter', () => { badge.hovered = true; show(badge); });
    host.addEventListener('mouseleave', () => { badge.hovered = false; scheduleHide(badge); });

    (document.body || document.documentElement).appendChild(host);
    badges.set(video, badge);
    return badge;
  }

  function show(badge) {
    clearTimeout(badge.hideTimer);
    badge.host.style.display = 'block';
  }

  function scheduleHide(badge) {
    clearTimeout(badge.hideTimer);
    badge.hideTimer = setTimeout(() => {
      if (!badge.hovered && badge.menu.hidden) badge.host.style.display = 'none';
    }, HIDE_DELAY_MS);
  }

  function position(video, badge) {
    const rect = video.getBoundingClientRect();
    const visible = rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth;
    if (!visible || !isBigEnough(video)) {
      badge.host.style.display = 'none';
      return;
    }
    const top = Math.max(4, rect.top + 10);
    const right = Math.min(innerWidth - 4, rect.right - 10);
    badge.host.style.top = `${top}px`;
    badge.host.style.left = `${right}px`;
    badge.host.style.transform = 'translateX(-100%)';
  }

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

  async function openMenu(badge) {
    badge.menu.innerHTML = '';
    badge.menu.hidden = false;
    show(badge);

    const add = (label, hint, onClick) => {
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'dm-item';
      item.innerHTML = '<span class="dm-item-label"></span><span class="dm-item-hint"></span>';
      item.querySelector('.dm-item-label').textContent = label;
      item.querySelector('.dm-item-hint').textContent = hint || '';
      item.addEventListener('click', async (event) => {
        event.preventDefault();
        event.stopPropagation();
        badge.menu.hidden = true;
        await onClick();
      });
      badge.menu.appendChild(item);
    };

    add('This video (best quality)', 'Let DownloadMaster read the page', () =>
      send(badge, { type: 'sendPage', pageUrl: location.href, pageTitle: document.title }));

    let streams = [];
    try {
      const response = await chrome.runtime.sendMessage({ type: 'getStreams' });
      streams = response?.streams || [];
    } catch {
      // extension reloaded; page needs a refresh
    }

    for (const stream of streams) {
      add(streamLabel(stream), shorten(stream.url), () =>
        send(badge, { type: 'sendStream', stream, pageUrl: location.href, pageTitle: document.title }));
    }
  }

  function shorten(url) {
    try {
      const u = new URL(url);
      const name = u.pathname.split('/').pop() || u.hostname;
      return name.length > 42 ? `${name.slice(0, 39)}...` : name;
    } catch {
      return '';
    }
  }

  async function send(badge, message) {
    try {
      const response = await chrome.runtime.sendMessage(message);
      if (response && response.ok === false) throw new Error(response.error || 'Failed');
      toast(badge, 'Sent to DownloadMaster');
    } catch (error) {
      toast(badge, String(error?.message || error).includes('not running')
        ? 'Start DownloadMaster first'
        : 'Could not reach DownloadMaster', true);
    }
  }

  function toast(badge, text, isError = false) {
    badge.toast.textContent = text;
    badge.toast.classList.toggle('dm-error', isError);
    badge.toast.hidden = false;
    show(badge);
    setTimeout(() => { badge.toast.hidden = true; scheduleHide(badge); }, 2600);
  }

  function scan() {
    if (!enabled) return;
    for (const video of document.querySelectorAll('video')) {
      if (!badges.has(video) && isBigEnough(video)) createBadge(video);
    }
    for (const [video, badge] of badges) {
      if (!video.isConnected) {
        badge.host.remove();
        badges.delete(video);
      }
    }
  }

  function refreshPositions() {
    for (const [video, badge] of badges) {
      if (badge.host.style.display !== 'none') position(video, badge);
    }
  }

  // Show the button while the pointer is over a video (like IDM), hide shortly after.
  document.addEventListener('mousemove', (event) => {
    if (!enabled) return;
    for (const [video, badge] of badges) {
      const rect = video.getBoundingClientRect();
      const inside = event.clientX >= rect.left && event.clientX <= rect.right
        && event.clientY >= rect.top && event.clientY <= rect.bottom;
      if (inside && isBigEnough(video)) {
        position(video, badge);
        show(badge);
        scheduleHide(badge);
      }
    }
  }, { passive: true, capture: true });

  document.addEventListener('click', () => {
    for (const badge of badges.values()) badge.menu.hidden = true;
  }, true);

  addEventListener('scroll', refreshPositions, { passive: true, capture: true });
  addEventListener('resize', refreshPositions, { passive: true });

  new MutationObserver(() => scan()).observe(document.documentElement, { childList: true, subtree: true });
  setInterval(scan, 3000);

  chrome.runtime.onMessage.addListener((message) => {
    if (message?.type === 'streamsChanged') scan();
  });
})();
