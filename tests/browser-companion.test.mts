import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { webcrypto, createHash } from 'node:crypto';
import path from 'node:path';
const root = path.join(import.meta.dirname, '..', 'browser-companion');
interface Listeners {
  message(message: unknown, sender: unknown, reply?: (value: unknown) => void): unknown;
  native(reply: unknown): void;
  disconnect(): void;
  removed(id: number): void;
  updated(id: number, change: { url?: string; status?: string }): void;
  activated(info: { tabId: number }): void;
}
interface Sample {
  host?: string; frameHost?: string; frame?: number; tab?: number; documentId?: string; lifecycle?: string;
  titles?: Partial<CinePresence.Title>[]; player?: Partial<CinePresence.Player> | null; incognito?: boolean;
}
function harness() {
  const listeners = {} as Listeners, sent: unknown[] = [], requested: number[] = [];
  let now = Date.now();
  class Clock extends Date { static override now() { return now; } }
  const context = vm.createContext({ URL, TextEncoder, crypto: webcrypto, navigator: { userAgent: 'Edg/140' }, setInterval() {}, Date: Clock,
    chrome: {
      storage: { session: { async get() { return {}; }, async set() {} } },
      runtime: { id: 'test', getURL: (x: string) => 'chrome-extension://test/' + x,
        onMessage: { addListener: (fn: Listeners['message']) => listeners.message = (message, sender, reply = () => {}) => fn(message, sender, reply) },
        connectNative: () => ({ postMessage(packet: unknown) { sent.push(JSON.parse(JSON.stringify(packet))); queueMicrotask(() => listeners.native({ connected: true, message: 'Connected' })); },
          onMessage: { addListener: (fn: Listeners['native']) => listeners.native = fn }, onDisconnect: { addListener: (fn: Listeners['disconnect']) => listeners.disconnect = fn }, disconnect() {} }) },
      tabs: { onRemoved: { addListener: (fn: Listeners['removed']) => listeners.removed = fn }, onUpdated: { addListener: (fn: Listeners['updated']) => listeners.updated = fn },
        onActivated: { addListener: (fn: Listeners['activated']) => listeners.activated = fn },
        async sendMessage(id: number) { requested.push(id); },
        async query() { return [{ id: 1, url: 'https://cinema.example/watch/17' }]; } }
    }
  }) as vm.Context & { CinePresenceMetadata: CinePresence.Metadata };
  vm.runInContext(fs.readFileSync(path.join(root, 'dist/metadata.js'), 'utf8'), context);
  vm.runInContext(fs.readFileSync(path.join(root, 'dist/worker.js'), 'utf8').replace('import "./metadata.js";', ''), context);
  const settle = () => new Promise(resolve => setTimeout(resolve, 25));
  async function sample({ host = 'cinema.example', frameHost = host, frame = 0, tab = 1, documentId, lifecycle = 'active', titles = [{ title: 'Regular Show', subtitle: 'S6E13', kind: 'TVEpisode' }], player = { state: 'playing', position: 70, duration: 673, rate: 1 }, incognito = false }: Sample = {}) {
    listeners.message({ type: 'sample', titles, player }, { id: 'test', tab: { id: tab, url: `https://${host}/watch/17`, incognito }, frameId: frame, documentId, documentLifecycle: lifecycle, url: `https://${frameHost}/embed` }); await settle();
  }
  return { context, listeners, sent, requested, sample, settle, advance: (ms: number) => now += ms,
    items: (): CinePresence.Item[] => JSON.parse(JSON.stringify(vm.runInContext('packetItems()', context))) };
}
test('stable extension ID matches the native host registration', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));
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
  const doc = { title: 'Watch', querySelector: (selector: string) => selector === 'h1' ? { textContent: 'Regular Show' } : null,
    querySelectorAll: (selector: string) => selector.startsWith('script') ? [] : [{ textContent: 'Regular Show' }, { textContent: 'Season 6 Episode 13' }] };
  const titles = h.context.CinePresenceMetadata.collect(doc as unknown as Document, {});
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
  for (const host of ['facebook.com', 'youtube.com', 'x.com', 'instagram.com', 'vk.com', 'vimeo.com', 'dailymotion.com', 'spotify.com', 'open.spotify.com']) {
    const h = harness(); await h.sample({ host }); assert.deepEqual(h.items(), []);
  }
});
test('excluded iframe cannot replace the real player', async () => {
  const h = harness(); await h.sample(); await h.sample({ frame: 3, frameHost: 'www.youtube.com' });
  assert.equal(h.items().length, 1); assert.equal(h.items()[0].frameHost, 'cinema.example');
});

test('Spotify embeds cannot send movie-like titles and unrelated hostname suffixes stay eligible', async () => {
  const h = harness(); await h.sample({ frame: 3, frameHost: 'open.spotify.com' });
  assert.deepEqual(h.items(), []);
  await h.sample({ host: 'notspotify.com' }); assert.equal(h.items().length, 1);
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


function documentFixture(title: string, heading: string, nearby = '', meta: Record<string, string> = {}) {
  const h1 = heading ? { textContent: heading, parentElement: { textContent: nearby || heading, parentElement: null } } : null;
  return { title, querySelector: (selector: string) => selector === 'h1' ? h1 : Object.entries(meta).map(([key, content]) => selector.includes(`"${key}"`) ? { content } : null).find(Boolean) ?? null,
    querySelectorAll: (selector: string) => selector.startsWith('script') ? [] : h1 ? [h1] : [] } as unknown as Document;
}
test('series route plus local player heading yields type, ID and episode without a site rule', () => {
  const m = harness().context.CinePresenceMetadata;
  for (const [name, id, season, episode] of [['Lanterns', 95350, 1, 7], ['Raw', 4656, 34, 14]] as const) {
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

test('separate tabs keep their own title and pause state through an embedded-player handoff', async () => {
  const h = harness();
  await h.sample({ tab: 1, player: null });
  await h.sample({ tab: 1, frame: 3, frameHost: 'embed.example', titles: [] });
  await h.sample({ tab: 2, host: 'another.example', titles: [{ title: 'Arrival (2016)', kind: 'Movie' }] });
  assert.deepEqual(h.items().map(x => [x.id, x.state]), [['1', 'playing'], ['2', 'playing']]);
  await h.sample({ tab: 1, frame: 3, frameHost: 'embed.example', titles: [], player: { state: 'paused', position: 75, duration: 673 } });
  const items = h.items();
  assert.deepEqual(items.map(x => [x.id, x.state]), [['1', 'paused'], ['2', 'playing']]);
  assert.equal(items[1].titles[0].title, 'Arrival (2016)');
  assert.deepEqual((h.sent.at(-1) as { items: CinePresence.Item[] }).items.map(x => [x.id, x.state]), [['1', 'paused'], ['2', 'playing']]);
  h.listeners.removed(1); assert.equal(h.items()[0].id, '2');
});

test('worker rediscovers existing, activated and newly loaded tabs without changing selection', async () => {
  const h = harness(); await h.settle(); assert.ok(h.requested.includes(1));
  h.listeners.activated({ tabId: 2 }); assert.ok(h.requested.includes(2));
  h.listeners.updated(3, { status: 'complete' }); assert.ok(h.requested.includes(3));
  await h.sample(); h.requested.length = 0; h.advance(3000);
  assert.equal(h.items()[0].state, 'playing'); assert.deepEqual(h.requested, [1]);
  await h.sample({ player: { state: 'paused' } }); assert.equal(h.items()[0].state, 'paused');
});

test('an old document cannot remove a new player and cached pages cannot revive playback', async () => {
  const h = harness(); await h.sample({ documentId: 'new-document' });
  h.listeners.message({ type: 'gone' }, { id: 'test', tab: { id: 1, url: 'https://cinema.example/watch/17' }, frameId: 0, documentId: 'old-document', url: 'https://cinema.example/watch/17' });
  assert.equal(h.items().length, 1);
  await h.sample({ documentId: 'old-document', lifecycle: 'cached', player: { state: 'paused' } });
  assert.equal(h.items()[0].state, 'playing');
  h.listeners.message({ type: 'gone' }, { id: 'test', tab: { id: 1, url: 'https://cinema.example/watch/17' }, frameId: 0, documentId: 'new-document', documentLifecycle: 'cached', url: 'https://cinema.example/watch/17' });
  assert.deepEqual(h.items(), []);
});

function contentHarness() {
  const sent: CinePresence.Message[] = [], timers = new Map<number, () => void>();
  const page = new EventTarget(); let listener: Listeners['message'] = () => {}, failures = 0;
  const createVideo = () => Object.assign(new EventTarget(), { paused: true, ended: false, readyState: 1, currentTime: 70, duration: 673, playbackRate: 1,
    seekable: { length: 0, start: () => 0 }, getBoundingClientRect: () => ({ width: 800, height: 450 }) });
  let video = createVideo();
  let videos = [video]; const doc = Object.assign(new EventTarget(), { querySelectorAll: () => videos });
  const runtime = { id: 'test',
    onMessage: { addListener: (fn: Listeners['message']) => listener = fn },
    async sendMessage(message: CinePresence.Message) {
      if (failures-- > 0) throw new Error('Could not establish connection. Receiving end does not exist.');
      sent.push(message); return { received: true };
    } };
  const context = vm.createContext({ document: doc, navigator: {}, location: { href: 'https://cinema.example/tv/17/6/13' }, Date,
    chrome: { runtime }, addEventListener: page.addEventListener.bind(page),
    setInterval(fn: () => void) { timers.set(1, fn); return 1; }, clearInterval(id: number) { timers.delete(id); },
    CinePresenceMetadata: { host: () => 'cinema.example', excluded: () => false, page: (url: string) => url,
      collect: () => [{ title: 'Regular Show', subtitle: 'S6E13', kind: 'TVEpisode' }] } });
  vm.runInContext(fs.readFileSync(path.join(root, 'dist/content.js'), 'utf8'), context);
  return { sent, get video() { return video; }, runtime, doc, page, timers, failNext: () => failures = 1,
    media: (event: string) => { doc.dispatchEvent(new Event(event)); video.dispatchEvent(new Event(event)); },
    removeVideo: () => videos = [], restoreVideo: () => { video = createVideo(); videos = [video]; },
    tick: () => { for (const fn of timers.values()) fn(); },
    wake: () => listener({ type: 'sample-now' }, { id: 'test' }, () => {}),
    settle: () => new Promise(resolve => setTimeout(resolve, 0)) };
}

test('a transient content-message failure does not permanently freeze a tab in Playing', async () => {
  const h = contentHarness(); h.video.paused = false; h.media('play'); await h.settle();
  assert.equal(h.sent.at(-1)!.player!.state, 'playing');
  h.failNext(); h.video.paused = true; h.media('pause'); await h.settle();
  h.tick(); await h.settle(); assert.equal(h.sent.at(-1)!.player!.state, 'paused');
});

test('a newly mounted player reports play and pause before the next timer tick', async () => {
  const h = contentHarness(); h.removeVideo(); h.tick();
  h.restoreVideo(); h.video.paused = false; h.media('play'); await h.settle();
  assert.equal(h.sent.at(-1)!.player!.state, 'playing');
  h.video.paused = true; h.media('pause'); await h.settle();
  assert.equal(h.sent.at(-1)!.player!.state, 'paused');
});

test('background content responds to a sample request even when timers have not run', async () => {
  const h = contentHarness(); h.video.paused = false; h.wake(); await h.settle();
  assert.equal(h.sent.at(-1)!.player!.state, 'playing');
  h.video.paused = true; h.wake(); await h.settle(); assert.equal(h.sent.at(-1)!.player!.state, 'paused');
});

test('history navigation removes playback until pageshow restores a fresh sample', async () => {
  const h = contentHarness(); h.page.dispatchEvent(new Event('pagehide')); await h.settle();
  assert.equal(h.sent.at(-1)!.type, 'gone'); const count = h.sent.length;
  h.tick(); h.wake(); await h.settle(); assert.equal(h.sent.length, count);
  h.video.paused = false; h.page.dispatchEvent(new Event('pageshow')); await h.settle();
  assert.equal(h.sent.at(-1)!.player!.state, 'playing');
});
