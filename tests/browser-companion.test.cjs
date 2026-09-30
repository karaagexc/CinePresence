const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const { webcrypto, createHash } = require('node:crypto');
const path = require('node:path');
const root = path.join(__dirname, '..', 'browser-companion');
function harness() {
  const listeners = {}, sent = [];
  const context = vm.createContext({ URL, TextEncoder, crypto: webcrypto, navigator: { userAgent: 'Edg/140' }, setInterval() {}, Date,
    chrome: {
      storage: { session: { async get() { return {}; }, async set() {} } },
      runtime: { id: 'test', getURL: x => 'chrome-extension://test/' + x,
        onMessage: { addListener: fn => listeners.message = fn },
        connectNative: () => ({ postMessage(packet) { sent.push(packet); queueMicrotask(() => listeners.native({ connected: true, message: 'Connected' })); },
          onMessage: { addListener: fn => listeners.native = fn }, onDisconnect: { addListener: fn => listeners.disconnect = fn }, disconnect() {} }) },
      tabs: { onRemoved: { addListener: fn => listeners.removed = fn }, onUpdated: { addListener: fn => listeners.updated = fn },
        async query() { return [{ id: 1, url: 'https://cinema.example/watch/17' }]; } }
    }
  });
  vm.runInContext(fs.readFileSync(path.join(root, 'dist/metadata.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'dist/worker.js'), 'utf8').replace('import "./metadata.js";', ''), context);
  const settle = () => new Promise(resolve => setTimeout(resolve, 25));
  async function sample({ host = 'cinema.example', frameHost = host, frame = 0, titles = [{ title: 'Regular Show', subtitle: 'S6E13', kind: 'TVEpisode' }], player = { state: 'playing', position: 70, duration: 673, rate: 1 }, incognito = false } = {}) {
    listeners.message({ type: 'sample', titles, player }, { id: 'test', tab: { id: 1, url: `https://${host}/watch/17`, incognito }, frameId: frame, url: `https://${frameHost}/embed` }); await settle();
  }
  return { context, listeners, sent, sample, settle, items: () => JSON.parse(JSON.stringify(vm.runInContext('packetItems()', context))) };
}
test('stable extension ID matches the native host registration', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json')));
  const id = [...createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest().subarray(0, 16)].map(n => String.fromCharCode(97 + (n >> 4), 97 + (n & 15))).join('');
  assert.equal(id, 'ndikeejjjaangmgeohkglafbldikbnag');
});
test('structured episode extraction uses the series name, not the episode title', () => {
  const h = harness();
  const result = h.context.CinePresenceMetadata.structured({ '@type': 'TVEpisode', name: 'Mordecai and Rigby Down Under', partOfSeries: { '@type': 'TVSeries', name: 'Regular Show' }, partOfSeason: { seasonNumber: 6 }, episodeNumber: 13 });
  assert.equal(result[0].title, 'Regular Show'); assert.equal(result[0].subtitle, 'S6E13');
});
test('generic tab title is supplemented by headings and explicit episode details', () => {
  const h = harness();
  const doc = { title: 'Watch', querySelector: selector => selector === 'h1' ? { textContent: 'Regular Show' } : null,
    querySelectorAll: selector => selector.startsWith('script') ? [] : [{ textContent: 'Regular Show' }, { textContent: 'Season 6 Episode 13' }] };
  const titles = h.context.CinePresenceMetadata.collect(doc, {});
  assert.equal(titles[0].title, 'Regular Show'); assert.equal(titles[0].subtitle, 'Season 6 Episode 13');
});
test('structured movie release year distinguishes remakes without inventing episodes', () => {
  const h = harness();
  const result = h.context.CinePresenceMetadata.structured({ '@type': 'Movie', name: 'Dune', datePublished: '2021-09-15' });
  assert.equal(result[0].title, 'Dune (2021)'); assert.equal(result[0].subtitle, '');
});
test('parent page metadata and embedded video timing are combined', async () => {
  const h = harness(); await h.sample({ player: null }); await h.sample({ frame: 3, frameHost: 'player.example', titles: [{ title: 'Video' }] });
  const item = h.items()[0]; assert.equal(item.titles[0].title, 'Regular Show'); assert.equal(item.position, 70); assert.equal(item.frameHost, 'player.example');
  assert.equal(item.pageKey.length, 64); assert.equal(JSON.stringify(item).includes('/watch/'), false);
});
test('top-level excluded platforms never report episodes', async () => {
  for (const host of ['facebook.com', 'youtube.com', 'x.com', 'instagram.com', 'vk.com', 'vimeo.com', 'dailymotion.com']) {
    const h = harness(); await h.sample({ host }); assert.deepEqual(h.items(), []);
  }
});
test('excluded iframe cannot replace the real player', async () => {
  const h = harness(); await h.sample(); await h.sample({ frame: 3, frameHost: 'www.youtube.com' });
  assert.equal(h.items().length, 1); assert.equal(h.items()[0].frameHost, 'cinema.example');
});
test('pause, seek, speed and closure reach the native message', async () => {
  const h = harness(); await h.sample(); await h.sample({ player: { state: 'paused', position: 222, duration: 673, rate: 2 } });
  assert.equal(h.items()[0].state, 'paused'); assert.equal(h.items()[0].position, 222); assert.equal(h.items()[0].rate, 2);
  h.listeners.removed(1); assert.deepEqual(h.items(), []);
});
test('manual title is available only to popup and clears on navigation', async () => {
  const h = harness(); await h.sample({ titles: [], player: { state: 'playing' } });
  await new Promise(resolve => h.listeners.message({ type: 'manual', title: 'Arrival', season: '', episode: '' }, { id: 'test', url: 'chrome-extension://test/popup.html' }, resolve));
  assert.equal(h.items()[0].manual, true); assert.equal(h.items()[0].titles[0].title, 'Arrival');
  h.listeners.updated(1, { url: 'https://cinema.example/another' }); assert.deepEqual(h.items(), []);
});
test('private windows are not collected', async () => {
  const h = harness(); await h.sample({ incognito: true }); assert.deepEqual(h.items(), []);
});
test('late samples cannot restore a closed tab', async () => {
  const h = harness(); const pending = h.sample(); h.listeners.removed(1); await pending; assert.deepEqual(h.items(), []);
});
test('an excluded page invalidates in-flight samples from the previous page', async () => {
  const h = harness(); const pending = h.sample(); await h.sample({ host: 'facebook.com' }); await pending; assert.deepEqual(h.items(), []);
});


function documentFixture(title, heading, nearby = '', meta = {}) {
  const h1 = heading ? { textContent: heading, parentElement: { textContent: nearby || heading, parentElement: null } } : null;
  return { title, querySelector: selector => selector === 'h1' ? h1 : Object.entries(meta).map(([key, content]) => selector.includes(`"${key}"`) ? { content } : null).find(Boolean) ?? null,
    querySelectorAll: selector => selector.startsWith('script') ? [] : h1 ? [h1] : [] };
}
test('series route plus local player heading yields type, ID and episode without a site rule', () => {
  const m = harness().context.CinePresenceMetadata;
  for (const [name, id, season, episode] of [['Lanterns', 95350, 1, 7], ['Raw', 4656, 34, 14]]) {
    const titles = m.collect(documentFixture(name + ' | Cinema', name, `${name} S${season} E${episode} Episode title`), {}, `https://cinema.example/tv/${id}/${season}/${episode}?play=true`);
    assert.equal(titles[0].title, name); assert.equal(titles[0].kind, 'TVSeries'); assert.equal(titles[0].tmdbId, id);
    assert.equal(titles[0].subtitle, `S${season} E${episode}`);
  }
});
test('movie page route and nearby year allow automatic identification of remakes', () => {
  const titles = harness().context.CinePresenceMetadata.collect(documentFixture('Watch The Odyssey', 'The Odyssey', 'The Odyssey 2026 8.0'), {}, 'https://cinema.example/watch/movie/1368337');
  assert.equal(titles[0].title, 'The Odyssey (2026)'); assert.equal(titles[0].tmdbId, 1368337); assert.equal(titles[0].kind, 'Movie'); assert.equal(titles[0].subtitle, '');
});
test('on-screen episode overrides outdated route episode and broad recommendation text is ignored', () => {
  const m = harness().context.CinePresenceMetadata;
  let titles = m.collect(documentFixture('Dark', 'Dark', 'Dark S2 E5'), {}, 'https://cinema.example/tv/70523/2/4');
  assert.equal(titles[0].subtitle, 'S2 E5');
  titles = m.collect(documentFixture('Dark', 'Dark', 'Dark Recommended Another Series S8E9'), {}, 'https://cinema.example/watch');
  assert.equal(titles[0].subtitle, '');
});
test('corroborating heading, media metadata and route hints survive iframe forwarding', async () => {
  const h = harness();
  const titles = h.context.CinePresenceMetadata.collect(documentFixture('Raw | Cinema', 'Raw', 'Raw S34 E14'), {}, 'https://cinema.example/tv/4656/34/14');
  await h.sample({ titles, player: null }); await h.sample({ frame: 2, frameHost: 'embed.example', titles: [] });
  assert.equal(h.items()[0].titles[0].tmdbId, 4656); assert.equal(h.items()[0].titles[0].evidence, 'heading');
});
test('movie year is not borrowed from a recommendations region', () => {
  const titles = harness().context.CinePresenceMetadata.collect(documentFixture('Dune', 'Dune', 'Dune Related movies Arrival 2016'), {}, 'https://cinema.example/movie/438631');
  assert.equal(titles[0].title, 'Dune');
});
