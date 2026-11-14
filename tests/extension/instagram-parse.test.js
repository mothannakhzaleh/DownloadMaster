// Tests for src/browser-extension/instagram-parse.js with made-up data in the shapes Instagram's web
// app receives (no real account data). Run:  node --test tests/extension
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { extract, extractText } = require('../../src/browser-extension/instagram-parse.js');

const cdn = (name) => `https://scontent-xx1-1.cdninstagram.com/v/t51/${name}`;
const image = (name, w = 1080, h = 1350) => ({
  image_versions2: {
    candidates: [
      { url: cdn(`${name}-1080.webp`), width: w, height: h },
      { url: cdn(`${name}-320.webp`), width: 320, height: 400 },
      { url: cdn(`${name}-150.webp`), width: 150, height: 188 }
    ]
  }
});
const user = { username: 'sample.account', pk: '42' };

const timeline = {
  data: {
    xdt_api__v1__feed__user_timeline_graphql_connection: {
      page_info: { has_next_page: true, end_cursor: 'x' },
      edges: [
        { node: { code: 'PhotoCode1', pk: '1001', id: '1001_42', taken_at: 1700000000, media_type: 1, product_type: 'feed', user, ...image('photo') } },
        {
          node: {
            code: 'ReelCode2', pk: '1002', taken_at: 1700000100, media_type: 2, product_type: 'clips', user, ...image('reel'),
            video_versions: [
              { url: cdn('reel-480.mp4'), width: 480, height: 854, type: 101 },
              { url: cdn('reel-720.mp4'), width: 720, height: 1280, type: 102 }
            ]
          }
        },
        {
          node: {
            code: 'CarouselCode3', pk: '1003', taken_at: 1700000200, media_type: 8, product_type: 'carousel_container', user,
            carousel_media: [
              { pk: '2001', media_type: 1, ...image('slide1') },
              { pk: '2002', media_type: 2, ...image('slide2'), video_versions: [{ url: cdn('slide2.mp4'), width: 720, height: 720 }] }
            ]
          }
        }
      ]
    }
  }
};

test('timeline: posts, reels and carousels with the best file and a small thumbnail', () => {
  const r = extract(timeline);
  assert.deepEqual(r.timeline, { hasNext: true });
  assert.equal(r.items.length, 3);

  const [photo, reel, carousel] = r.items;
  assert.equal(photo.section, 'posts');
  assert.equal(photo.code, 'PhotoCode1');
  assert.equal(photo.pk, '1001');
  assert.equal(photo.takenAt, 1700000000);
  assert.equal(photo.username, 'sample.account');
  assert.deepEqual(photo.media, [{ type: 'image', url: cdn('photo-1080.webp'), width: 1080, height: 1350 }]);
  assert.equal(photo.thumbnail, cdn('photo-150.webp'));

  assert.equal(reel.media.length, 1);
  assert.equal(reel.media[0].type, 'video');
  assert.equal(reel.media[0].url, cdn('reel-720.mp4'));

  assert.equal(carousel.media.length, 2, 'carousel children become files of one item, not items');
  assert.equal(carousel.media[0].type, 'image');
  assert.equal(carousel.media[1].type, 'video');
  assert.equal(carousel.thumbnail, cdn('slide1-150.webp'));
});

test('stories and highlights get their own sections and owner', () => {
  const reels = {
    data: {
      xdt_api__v1__feed__reels_media__connection: {
        edges: [
          { node: { id: '42', reel_type: 'user_reel', user, items: [{ pk: '3001', taken_at: 1700001000, expiring_at: 1700087400, ...image('story') }] } },
          { node: { id: 'highlight:555', title: 'Trips', user, items: [{ pk: '4001', ...image('h1') }, { pk: '4002', ...image('h2') }] } }
        ]
      }
    }
  };
  const r = extract(reels);
  assert.deepEqual(r.highlights, [{ id: '555', title: 'Trips' }]);
  assert.deepEqual(r.items.map((i) => [i.section, i.pk, i.username]), [
    ['stories', '3001', 'sample.account'],
    ['highlight:555', '4001', 'sample.account'],
    ['highlight:555', '4002', 'sample.account']
  ]);
  assert.equal(r.items[1].sectionTitle, 'Trips');
});

test('text: one document per line, the for(;;) guard, multi-line JSON, and junk', () => {
  const line = JSON.stringify(timeline);
  assert.equal(extractText(`${line}\n${JSON.stringify({ data: {} })}`).items.length, 3);
  assert.equal(extractText(`for (;;);${line}`).items.length, 3);
  assert.equal(extractText(JSON.stringify(timeline, null, 2)).items.length, 3);
  assert.deepEqual(extractText('<html>not json</html>'), { items: [], highlights: [], timeline: null });
  assert.deepEqual(extractText(null), { items: [], highlights: [], timeline: null });
});

test('nodes without files or ids are ignored, deep trees do not throw', () => {
  assert.equal(extract({ a: { code: 'X', image_versions2: { candidates: [] } } }).items.length, 0);
  assert.equal(extract({ a: { image_versions2: image('x').image_versions2 } }).items.length, 0);
  let deep = {};
  const root = deep;
  for (let i = 0; i < 500; i++) deep = deep.next = {};
  assert.doesNotThrow(() => extract(root));
});
