namespace DownloadMaster.Services.Remote;

/// <summary>
/// The remote control page (phone or another computer). Self-contained: no external files.
/// The pairing link carries the token in the #fragment, which browsers never send to a server;
/// the page keeps it in localStorage and sends it as a Bearer header. All data is rendered with
/// textContent (never as HTML).
/// </summary>
public static class RemotePage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="referrer" content="no-referrer">
<title>DownloadMaster Remote</title>
<style>
:root { --bg:#0f1115; --card:#181b22; --line:#262b35; --text:#e8eaf0; --muted:#9aa3b2; --accent:#4f8cff; --ok:#3fb950; --bad:#f85149; --warn:#d29922; }
@media (prefers-color-scheme: light) { :root { --bg:#f5f6f8; --card:#fff; --line:#e1e4ea; --text:#1c2230; --muted:#5d6779; } }
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--text); font:15px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; }
header { display:flex; align-items:center; gap:10px; padding:14px 16px; border-bottom:1px solid var(--line); position:sticky; top:0; background:var(--bg); z-index:1; }
header h1 { font-size:17px; margin:0; flex:1; }
#conn { font-size:12px; color:var(--muted); }
main { max-width:760px; margin:0 auto; padding:16px; }
.card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:14px; margin-bottom:12px; }
input[type=text], input[type=url], input[type=password] { width:100%; padding:10px 12px; border-radius:8px; border:1px solid var(--line); background:var(--bg); color:var(--text); font-size:15px; }
.row { display:flex; gap:8px; margin-top:8px; flex-wrap:wrap; }
.row > input { flex:1 1 160px; }
button { border:0; border-radius:8px; padding:9px 14px; font-size:14px; cursor:pointer; background:var(--line); color:var(--text); }
button.primary { background:var(--accent); color:#fff; }
button:disabled { opacity:.45; cursor:default; }
label.chk { display:flex; align-items:center; gap:6px; color:var(--muted); font-size:13px; }
.item .top { display:flex; gap:8px; align-items:baseline; }
.item .name { flex:1; font-weight:600; word-break:break-word; }
.item .meta { color:var(--muted); font-size:13px; margin-top:4px; }
.bar { height:6px; border-radius:3px; background:var(--line); margin-top:8px; overflow:hidden; }
.bar > div { height:100%; background:var(--accent); width:0; transition:width .4s; }
.Completed .bar > div { background:var(--ok); } .Failed .bar > div { background:var(--bad); } .Paused .bar > div { background:var(--warn); }
.actions { display:flex; gap:6px; margin-top:10px; flex-wrap:wrap; }
.actions button { padding:6px 10px; font-size:13px; }
#msg { min-height:20px; font-size:13px; color:var(--muted); margin-top:8px; }
#empty { color:var(--muted); text-align:center; padding:24px 0; }
.hidden { display:none !important; }
</style>
</head>
<body>
<header><h1>DownloadMaster</h1><span id="conn">connecting…</span></header>
<main>
  <section id="pair" class="card hidden">
    <strong>Pair this device</strong>
    <p style="color:var(--muted);font-size:13px">Open the pairing link shown by DownloadMaster (Settings → Remote access, or the command-line server), or paste the token here.</p>
    <div class="row"><input id="tokenInput" type="password" autocomplete="off" placeholder="Pairing token"><button class="primary" id="saveToken">Pair</button></div>
  </section>

  <section id="add" class="card hidden">
    <input id="url" type="url" placeholder="Paste a link (file or video page)" autocomplete="off">
    <div class="row">
      <input id="folder" type="text" placeholder="Sub-folder (optional)" autocomplete="off">
      <input id="name" type="text" placeholder="File name (optional)" autocomplete="off">
    </div>
    <div class="row" style="align-items:center">
      <label class="chk"><input id="video" type="checkbox"> Video / page (yt-dlp)</label>
      <span style="flex:1"></span>
      <button class="primary" id="addBtn">Download</button>
    </div>
    <div id="msg"></div>
  </section>

  <section id="list"></section>
  <div id="empty" class="hidden">No downloads yet.</div>
  <p style="text-align:center"><button id="forget" class="hidden">Forget this device</button></p>
</main>
<script>
(() => {
  const KEY = 'dm-remote-token';
  const $ = (id) => document.getElementById(id);
  let token = '';
  try { token = localStorage.getItem(KEY) || ''; } catch {}

  const fromHash = new URLSearchParams(location.hash.slice(1)).get('token');
  if (fromHash) {
    token = fromHash;
    try { localStorage.setItem(KEY, token); } catch {}
    history.replaceState(null, '', location.pathname);
  }

  async function api(method, path, body) {
    const res = await fetch('/api/v1' + path, {
      method,
      headers: Object.assign({ 'Authorization': 'Bearer ' + token }, body ? { 'Content-Type': 'application/json' } : {}),
      body: body ? JSON.stringify(body) : undefined,
      cache: 'no-store'
    });
    let json = null;
    try { json = await res.json(); } catch {}
    if (!res.ok) {
      const err = new Error((json && json.error) || ('HTTP ' + res.status));
      err.status = res.status;
      throw err;
    }
    return json;
  }

  function showPaired(paired) {
    $('pair').classList.toggle('hidden', paired);
    $('add').classList.toggle('hidden', !paired);
    $('forget').classList.toggle('hidden', !paired);
  }

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
  }

  function render(items) {
    const list = $('list');
    list.replaceChildren();
    $('empty').classList.toggle('hidden', items.length > 0);
    for (const d of items) {
      const card = el('div', 'card item ' + d.status);
      const top = el('div', 'top');
      top.append(el('div', 'name', d.fileName || d.title), el('span', 'meta', Math.round(d.progress) + '%'));
      const meta = el('div', 'meta', [d.statusText || d.status, d.size, d.speed, d.eta].filter(Boolean).join(' · '));
      const bar = el('div', 'bar'); const fill = el('div'); fill.style.width = Math.max(0, Math.min(100, d.progress)) + '%'; bar.append(fill);
      const actions = el('div', 'actions');
      const add = (label, action, enabled) => {
        const b = el('button', '', label); b.disabled = !enabled;
        b.onclick = async () => { b.disabled = true; try { await api(action === 'remove' ? 'DELETE' : 'POST', '/downloads/' + encodeURIComponent(d.id) + (action === 'remove' ? '' : '/' + action)); } catch (e) { $('msg').textContent = e.message; } refresh(); };
        actions.append(b);
      };
      add('Pause', 'pause', d.canPause);
      add(d.status === 'Failed' || d.status === 'Cancelled' ? 'Retry' : 'Resume', 'resume', d.canResume);
      add('Cancel', 'cancel', d.canCancel);
      add('Remove', 'remove', !d.canCancel);
      card.append(top, meta, bar, actions);
      list.append(card);
    }
  }

  let timer = null;
  async function refresh() {
    clearTimeout(timer);
    if (!token) { showPaired(false); $('conn').textContent = 'not paired'; return; }
    try {
      render(await api('GET', '/downloads'));
      showPaired(true);
      $('conn').textContent = 'connected';
    } catch (e) {
      if (e.status === 401 || e.status === 429) { showPaired(false); $('conn').textContent = e.message; return; }
      $('conn').textContent = 'offline — retrying';
    }
    timer = setTimeout(refresh, document.hidden ? 5000 : 1500);
  }

  $('saveToken').onclick = () => {
    token = $('tokenInput').value.trim();
    try { localStorage.setItem(KEY, token); } catch {}
    refresh();
  };
  $('forget').onclick = () => { token = ''; try { localStorage.removeItem(KEY); } catch {} render([]); refresh(); };
  $('url').addEventListener('input', () => {
    const v = $('url').value.trim().toLowerCase();
    $('video').checked = /youtube\.com|youtu\.be|vimeo\.com|twitch\.tv|tiktok\.com|instagram\.com|x\.com|twitter\.com/.test(v);
  });
  $('addBtn').onclick = async () => {
    const url = $('url').value.trim();
    if (!url) return;
    $('addBtn').disabled = true; $('msg').textContent = 'Adding…';
    try {
      await api('POST', '/downloads', { url, folder: $('folder').value.trim() || null, name: $('name').value.trim() || null, video: $('video').checked ? true : null });
      $('url').value = ''; $('name').value = ''; $('msg').textContent = 'Added.';
    } catch (e) { $('msg').textContent = e.message; }
    $('addBtn').disabled = false;
    refresh();
  };
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  refresh();
})();
</script>
</body>
</html>
""";
}
