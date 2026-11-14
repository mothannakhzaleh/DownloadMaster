// DownloadMaster — Instagram collection, driven by the desktop app.
// The app opens https://www.instagram.com/<user or post>/#downloadmaster-collect=<nonce>. This script
// (isolated world, top frame) then lets Instagram's own page load the media: it scrolls a profile's
// post grid, opens the stories and the highlights, and forwards what instagram-hook.js reads to the
// app through background.js. Nothing happens on instagram.com unless the app asked for it, and the
// first batch is checked by the app (unknown nonce → stop).
(() => {
  if (window !== window.top || window.__downloadMasterIgCollect) return;
  window.__downloadMasterIgCollect = true;

  const FLAG = 'downloadmaster-collect';
  const HASH = '#downloadmaster-collect=';
  const MAX_POST_PAGES = 60;          // 12 posts per page
  const MAX_HIGHLIGHTS = 30;
  const STALL_MS = 15000;              // no new page of posts for this long → done with posts
  const REEL_WAIT_MS = 9000;
  const RESERVED = new Set(['p', 'reel', 'reels', 'tv', 'stories', 'explore', 'accounts', 'direct', 'about', 'developer', 'legal']);

  const load = () => { try { return JSON.parse(sessionStorage.getItem(FLAG) || 'null'); } catch { return null; } };
  const save = (s) => { try { sessionStorage.setItem(FLAG, JSON.stringify(s)); } catch {} };
  const clear = () => { try { sessionStorage.removeItem(FLAG); } catch {} };
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

  // ---- start: the app's link ------------------------------------------------------------------
  if (location.hash.startsWith(HASH)) {
    const nonce = location.hash.slice(HASH.length).replace(/[^a-zA-Z0-9]/g, '').slice(0, 64);
    const parts = location.pathname.split('/').filter(Boolean);
    const profile = parts.length === 1 && !RESERVED.has(parts[0].toLowerCase());
    history.replaceState(null, '', location.pathname + location.search);
    if (nonce) {
      save({
        nonce,
        target: profile ? 'profile' : 'single',
        username: profile ? parts[0] : '',
        phase: profile ? 'posts' : 'single',
        highlights: [],
        doneHighlights: [],
        tries: {},
        startedAt: Date.now()
      });
    }
  }

  let state = load();
  if (!state) return;
  if (Date.now() - state.startedAt > 20 * 60 * 1000) { clear(); return; } // abandoned collection

  // ---- data from the page hook ----------------------------------------------------------------
  let lastPageAt = Date.now();
  let timelineSeen = false;
  let hasNextPage = true;
  let pages = 0;
  let itemsThisPage = 0;
  const reelsSeen = new Set();
  let stopped = false;
  let queue = Promise.resolve();
  let lastSentAt = 0;

  function send(payload) {
    lastSentAt = Date.now();
    queue = queue.then(async () => {
      if (stopped) return;
      let response;
      try {
        response = await chrome.runtime.sendMessage({ type: 'igCollect', payload: { nonce: state.nonce, ...payload } });
      } catch {
        response = { ok: false };
      }
      if (!response || !response.ok) stop(response && response.error);
    });
    return queue;
  }

  function stop(reason) {
    if (stopped) return;
    stopped = true;
    clear();
    notice(reason ? `DownloadMaster stopped: ${reason}` : 'DownloadMaster stopped reading this page.');
  }

  window.addEventListener('message', (event) => {
    if (event.source !== window || event.origin !== location.origin) return;
    const data = event.data;
    if (!data || data.source !== 'downloadmaster-ig' || !data.result) return;
    const { items = [], highlights = [], timeline } = data.result;

    if (timeline) {
      timelineSeen = true;
      hasNextPage = timeline.hasNext;
      pages++;
      lastPageAt = Date.now();
    }
    for (const item of items) {
      if (item.section.startsWith('highlight:')) reelsSeen.add(item.section.slice('highlight:'.length));
      else if (item.section === 'stories') reelsSeen.add('stories');
    }
    for (const h of highlights) reelsSeen.add(h.id);

    const trimmed = items.slice(0, 200).map((i) => ({ ...i, media: i.media.slice(0, 20) }));
    itemsThisPage += trimmed.length;
    if (trimmed.length || highlights.length) send({ phase: state.phase, items: trimmed, highlights });
  });

  // ---- notice ----------------------------------------------------------------------------------
  let noticeEl = null;
  function notice(text) {
    if (!document.body) return;
    if (!noticeEl) {
      noticeEl = document.createElement('div');
      noticeEl.style.cssText = 'position:fixed;z-index:2147483647;bottom:20px;left:50%;transform:translateX(-50%);'
        + 'background:#1f2430;color:#fff;font:14px system-ui,sans-serif;padding:10px 16px;border-radius:8px;'
        + 'box-shadow:0 4px 16px rgba(0,0,0,.3);pointer-events:none';
      document.body.appendChild(noticeEl);
    }
    noticeEl.textContent = text;
  }

  async function waitForBody() {
    while (!document.body) await sleep(100);
  }

  // ---- phases ------------------------------------------------------------------------------------
  function go(path) {
    save(state);
    location.assign(path);
  }

  // The app gives up when it hears nothing for a while: say we are still here.
  function heartbeat() {
    if (Date.now() - lastSentAt > 10000) send({ phase: state.phase, hidden: document.hidden });
  }

  async function finish() {
    await send({ phase: 'done', done: true, partial: !!state.partial });
    await queue;
    clear();
    notice('DownloadMaster has the list. You can close this tab or keep browsing.');
    if (state.target === 'profile' && !location.pathname.startsWith(`/${state.username}`)) {
      location.assign(`/${state.username}/`);
    }
  }

  async function postsPhase() {
    // Highlights listed on the profile (ids and titles for the app, and the queue to open later).
    const found = [];
    for (const a of document.querySelectorAll('a[href*="/stories/highlights/"]')) {
      const id = (a.getAttribute('href').match(/highlights\/(\d+)/) || [])[1];
      if (id && !found.some((h) => h.id === id)) found.push({ id, title: (a.innerText || a.textContent || '').trim().slice(0, 80) });
    }

    let lastCount = -1;
    while (!stopped) {
      notice(`DownloadMaster is reading @${state.username}: ${pages} page(s) of posts so far...`
        + (document.hidden ? ' Keep this tab visible so Instagram loads more.' : ''));
      window.scrollTo(0, document.documentElement.scrollHeight);
      await sleep(1500);
      // A hidden or covered tab doesn't load more posts: don't count that time as a stall.
      if (document.hidden) lastPageAt = Math.max(lastPageAt, Date.now() - STALL_MS / 2);
      heartbeat();

      for (const a of document.querySelectorAll('a[href*="/stories/highlights/"]')) {
        const id = (a.getAttribute('href').match(/highlights\/(\d+)/) || [])[1];
        if (id && !found.some((h) => h.id === id)) found.push({ id, title: (a.innerText || a.textContent || '').trim().slice(0, 80) });
      }
      if (found.length !== lastCount) {
        lastCount = found.length;
        if (found.length) send({ phase: 'posts', highlights: found });
      }

      if (timelineSeen && !hasNextPage) break;
      if (pages >= MAX_POST_PAGES || Date.now() - lastPageAt > STALL_MS) {
        state.partial = true; // stopped with more posts left
        break;
      }
    }

    state.highlights = found.slice(0, MAX_HIGHLIGHTS);
    state.phase = 'stories';
    go(`/stories/${state.username}/`);
  }

  async function storiesPhase() {
    notice(`DownloadMaster is reading @${state.username}'s stories...`);
    if (location.pathname.startsWith('/stories/')) {
      await waitFor(() => reelsSeen.has('stories'), REEL_WAIT_MS);
      if (!reelsSeen.has('stories')) {
        // Someone else's stories open behind a "View story" prompt.
        const button = [...document.querySelectorAll('div[role="button"], button')]
          .find((b) => /view story|عرض/i.test(b.textContent || ''));
        if (button) {
          button.click();
          await waitFor(() => reelsSeen.has('stories'), REEL_WAIT_MS);
        }
      }
    }
    for (const id of reelsSeen) if (id !== 'stories') state.doneHighlights.push(id);
    state.phase = 'highlights';
    nextHighlight();
  }

  function nextHighlight() {
    const done = new Set(state.doneHighlights);
    const next = state.highlights.find((h) => !done.has(h.id) && (state.tries[h.id] || 0) < 2);
    if (!next) {
      state.phase = 'done';
      save(state);
      finish();
      return;
    }
    state.tries[next.id] = (state.tries[next.id] || 0) + 1;
    go(`/stories/highlights/${next.id}/`);
  }

  async function highlightsPhase() {
    const id = (location.pathname.match(/highlights\/(\d+)/) || [])[1];
    notice(`DownloadMaster is reading @${state.username}'s highlights (${state.doneHighlights.length + 1}/${state.highlights.length})...`);
    if (id) await waitFor(() => reelsSeen.has(id), REEL_WAIT_MS);
    // Opening one highlight also loads its neighbours: count them all as done.
    for (const seen of reelsSeen) if (seen !== 'stories' && !state.doneHighlights.includes(seen)) state.doneHighlights.push(seen);
    await queue;
    if (!stopped) nextHighlight();
  }

  async function singlePhase() {
    notice('DownloadMaster is reading this post...');
    await waitFor(() => itemsThisPage > 0, 12000);
    if (itemsThisPage > 0) await sleep(1500); // let a carousel's remaining data arrive
    await finish();
  }

  async function waitFor(condition, timeoutMs) {
    const end = Date.now() + timeoutMs;
    while (!condition() && Date.now() < end && !stopped) await sleep(250);
  }

  (async () => {
    await waitForBody();
    // Ask the app first: a link with an unknown nonce must not drive the tab.
    await send({ phase: 'hello', url: location.href });
    await queue;
    if (stopped) return;

    switch (state.phase) {
      case 'posts': return postsPhase();
      case 'stories': return storiesPhase();
      case 'highlights': return highlightsPhase();
      case 'single': return singlePhase();
      case 'done': return finish();
      default: clear();
    }
  })();
})();
