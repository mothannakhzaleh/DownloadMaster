// DownloadMaster — runs inside instagram.com's own page (world: MAIN) from document_start.
// Instagram loads posts, stories and highlights through its own GraphQL requests; this watches those
// responses, but only while DownloadMaster asked for a profile/post (the collect flag in
// sessionStorage, set by instagram.js). Results go to instagram.js via window.postMessage.
(() => {
  if (window.__downloadMasterIgHook) return;
  window.__downloadMasterIgHook = true;

  const FLAG = 'downloadmaster-collect';
  const parser = window.__downloadMasterInstagram;
  if (!parser) return;

  const watched = (url) => /\/(api\/graphql|graphql\/query|api\/v1\/)/.test(url || '');
  const active = () => {
    try { return !!sessionStorage.getItem(FLAG); } catch { return false; }
  };

  function report(text) {
    if (!active()) return;
    try {
      const result = parser.extractText(text);
      if (result.items.length || result.highlights.length || result.timeline) {
        window.postMessage({ source: 'downloadmaster-ig', result }, location.origin);
      }
    } catch {
      // never break Instagram's page
    }
  }

  const XHR = XMLHttpRequest.prototype;
  const open = XHR.open;
  const send = XHR.send;
  XHR.open = function (method, url, ...rest) {
    try { this.__dmUrl = String(url); } catch {}
    return open.call(this, method, url, ...rest);
  };
  XHR.send = function (...args) {
    try {
      if (watched(this.__dmUrl)) {
        this.addEventListener('load', () => {
          try {
            if (!active()) return;
            if (this.responseType === '' || this.responseType === 'text') report(this.responseText);
            else if (this.responseType === 'json' && this.response) report(JSON.stringify(this.response));
          } catch {}
        });
      }
    } catch {}
    return send.apply(this, args);
  };

  const originalFetch = window.fetch;
  window.fetch = function (...args) {
    const promise = originalFetch.apply(this, args);
    try {
      const url = typeof args[0] === 'string' ? args[0] : (args[0] && args[0].url) || '';
      if (watched(url) && active()) {
        promise.then((response) => response.clone().text()).then(report).catch(() => {});
      }
    } catch {}
    return promise;
  };

  // Post pages can arrive with their data already in the HTML.
  document.addEventListener('DOMContentLoaded', () => {
    if (!active()) return;
    for (const script of document.querySelectorAll('script[type="application/json"]')) {
      const text = script.textContent || '';
      if (text.indexOf('image_versions2') >= 0 || text.indexOf('video_versions') >= 0) report(text);
    }
  });
})();
