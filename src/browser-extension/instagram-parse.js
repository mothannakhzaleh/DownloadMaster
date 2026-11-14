// DownloadMaster — reads the media Instagram's own web page receives (posts, reels, stories,
// highlights) out of its GraphQL/API responses. Pure function: loaded into the Instagram page by
// instagram-hook.js, and by tests under node.
(function (root) {
  'use strict';

  const MAX_DEPTH = 60;

  function bestImage(node) {
    const candidates = (node && node.image_versions2 && node.image_versions2.candidates) || [];
    let best = null;
    for (const c of candidates) {
      if (c && typeof c.url === 'string' && (!best || (c.width || 0) * (c.height || 0) > (best.width || 0) * (best.height || 0))) best = c;
    }
    return best;
  }

  function smallestImage(node, minWidth) {
    const candidates = (node && node.image_versions2 && node.image_versions2.candidates) || [];
    let pick = null;
    for (const c of candidates) {
      if (!c || typeof c.url !== 'string') continue;
      const w = c.width || 0;
      if (w >= minWidth && (!pick || w < (pick.width || 0))) pick = c;
    }
    return pick || bestImage(node);
  }

  function bestVideo(node) {
    const versions = (node && node.video_versions) || [];
    let best = null;
    for (const v of versions) {
      if (v && typeof v.url === 'string' && (!best || (v.width || 0) * (v.height || 0) > (best.width || 0) * (best.height || 0))) best = v;
    }
    return best;
  }

  function mediaOf(node) {
    const video = bestVideo(node);
    if (video) return { type: 'video', url: video.url, width: video.width || 0, height: video.height || 0 };
    const image = bestImage(node);
    return image ? { type: 'image', url: image.url, width: image.width || 0, height: image.height || 0 } : null;
  }

  function isMediaNode(o) {
    return o && typeof o === 'object' && !Array.isArray(o)
      && (o.image_versions2 || o.video_versions || Array.isArray(o.carousel_media))
      && (typeof o.code === 'string' || typeof o.pk === 'string' || typeof o.pk === 'number');
  }

  function usernameOf(node, fallback) {
    return (node.user && node.user.username) || (node.owner && node.owner.username) || fallback || '';
  }

  function toItem(node, section, sectionTitle, ownerName) {
    const media = [];
    if (Array.isArray(node.carousel_media) && node.carousel_media.length) {
      for (const child of node.carousel_media) {
        const m = mediaOf(child);
        if (m) media.push(m);
      }
    } else {
      const m = mediaOf(node);
      if (m) media.push(m);
    }
    if (!media.length) return null;

    const first = Array.isArray(node.carousel_media) && node.carousel_media.length ? node.carousel_media[0] : node;
    const thumb = smallestImage(first, 150);
    return {
      section,
      sectionTitle: sectionTitle || '',
      code: typeof node.code === 'string' ? node.code : '',
      pk: String(node.pk || (typeof node.id === 'string' ? node.id.split('_')[0] : '') || ''),
      takenAt: Number(node.taken_at) || 0,
      productType: node.product_type || '',
      username: usernameOf(node, ownerName),
      thumbnail: thumb ? thumb.url : '',
      media
    };
  }

  // A stories/highlight reel: { id: "highlight:123" | "<user id>", items: [...], title?, user? }.
  function reelSection(o) {
    if (!o || typeof o !== 'object' || Array.isArray(o) || !Array.isArray(o.items)) return null;
    const id = String(o.id || '');
    if (id.startsWith('highlight:')) return { section: id, title: typeof o.title === 'string' ? o.title : '' };
    if (o.reel_type === 'user_reel' || (/^\d+$/.test(id) && o.items.some((i) => i && i.expiring_at))) return { section: 'stories', title: '' };
    return null;
  }

  function extract(json) {
    const out = { items: [], highlights: [], timeline: null };
    const seen = new Set();

    function visit(o, section, sectionTitle, owner, depth) {
      if (!o || typeof o !== 'object' || depth > MAX_DEPTH) return;
      if (Array.isArray(o)) {
        for (const v of o) visit(v, section, sectionTitle, owner, depth + 1);
        return;
      }

      const reel = reelSection(o);
      if (reel) {
        const reelOwner = (o.user && o.user.username) || owner;
        if (reel.section !== 'stories') out.highlights.push({ id: reel.section.slice('highlight:'.length), title: reel.title });
        for (const item of o.items) visit(item, reel.section, reel.title, reelOwner, depth + 1);
        return;
      }

      if (isMediaNode(o)) {
        const item = toItem(o, section, sectionTitle, owner);
        const key = item && `${item.section}|${item.pk || item.code}`;
        if (item && !seen.has(key)) {
          seen.add(key);
          out.items.push(item);
        }
        return; // carousel children are part of this item
      }

      for (const k of Object.keys(o)) {
        const v = o[k];
        if (k.indexOf('timeline') >= 0 && v && typeof v === 'object' && v.page_info && Array.isArray(v.edges)) {
          out.timeline = { hasNext: !!v.page_info.has_next_page };
        }
        visit(v, section, sectionTitle, owner, depth + 1);
      }
    }

    visit(json, 'posts', '', '', 0);
    return out;
  }

  // Responses may hold several JSON documents, one per line (streamed GraphQL), and API responses
  // can start with the "for (;;);" guard.
  function extractText(text) {
    const merged = { items: [], highlights: [], timeline: null };
    if (typeof text !== 'string' || text.length < 2) return merged;
    const add = (json) => {
      const r = extract(json);
      merged.items.push(...r.items);
      merged.highlights.push(...r.highlights);
      if (r.timeline) merged.timeline = r.timeline;
    };
    let parsed = 0;
    for (let line of text.split('\n')) {
      line = line.trim().replace(/^for \(;;\);/, '');
      if (!line || (line[0] !== '{' && line[0] !== '[')) continue;
      try { add(JSON.parse(line)); parsed++; } catch { /* not one document per line */ }
    }
    if (!parsed) {
      // One document spread over several lines.
      try { add(JSON.parse(text.trim().replace(/^for \(;;\);/, ''))); } catch { /* not JSON */ }
    }
    return merged;
  }

  const api = { extract, extractText };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.__downloadMasterInstagram = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
