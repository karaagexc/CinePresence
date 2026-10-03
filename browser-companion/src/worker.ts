import "./metadata.js";
const M = globalThis.CinePresenceMetadata;
const tabs = new Map<number, CinePresence.Tab>();
const epochs = new Map<number, number>(), sequences = new Map<string, number>();
let sequence = 0;
const browser = /Edg\//.test(navigator.userAgent) ? "msedge" : "chrome";
let port: chrome.runtime.Port | undefined; let clientId = ""; let busy = false, pending = false, lastReply = 0, status = "Connecting…";
const ready = chrome.storage.session.get("clientId").then(async saved => {
  clientId = typeof saved.clientId === "string" ? saved.clientId : crypto.randomUUID();
  await chrome.storage.session.set({ clientId });
});
// Ask existing content scripts for fresh facts; this does not inject scripts or
// change which pages the companion is allowed to observe.
const requested = new Map<number, number>();
function requestSample(id: number) {
  const now = Date.now();
  if (now - (requested.get(id) ?? -Infinity) < 1000) return;
  requested.set(id, now);
  void chrome.tabs.sendMessage(id, { type: "sample-now" }).catch(() => {});
}
async function discoverTabs() {
  try {
    const existing = await chrome.tabs.query({ url: ["http://*/*", "https://*/*"] });
    for (const tab of existing.slice(0, 64))
      if (tab.id !== undefined && !tab.incognito && M.host(tab.url ?? "") && !M.excluded(M.host(tab.url ?? ""))) requestSample(tab.id);
  } catch { /* Tab activation/loading and content events also rediscover players. */ }
}
function connect() {
  if (port) return;
  try {
    const connectedPort = chrome.runtime.connectNative("org.cinepresence.companion");
    port = connectedPort;
    connectedPort.onMessage.addListener((reply: CinePresence.Reply) => {
      if (port !== connectedPort) return;
      busy = false; lastReply = Date.now(); status = reply.message ?? (reply.connected ? "Connected" : "Open CinePresence");
      if (pending) { pending = false; flush(); }
    });
    connectedPort.onDisconnect.addListener(() => { void chrome.runtime.lastError; if (port !== connectedPort) return; port = undefined; busy = false; status = "Connection unavailable. Open CinePresence → Settings → Set up companion."; });
  } catch { status = "Set up the browser connection in CinePresence Settings."; }
}
async function hash(value: string): Promise<string> {
  return [...new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)))].map(x => x.toString(16).padStart(2, "0")).join("");
}
function packetItems() {
  const now = Date.now(), items: CinePresence.Item[] = [];
  for (const [id, tab] of tabs) {
    // Background-page timers can be throttled. Wake all frames before expiring
    // an old sample so a paused embed cannot keep winning source selection.
    if ([...tab.frames.values()].some(x => x.player && now - x.seen > 2000)) requestSample(id);
    for (const [frameId, frame] of tab.frames) if (now - frame.seen > 6000) tab.frames.delete(frameId);
    const frames = [...tab.frames.values()].filter((x): x is CinePresence.Frame & { player: CinePresence.Player } => x.player !== null);
    const playing = frames.filter(x => x.player.state === "playing").sort((a, b) => a.started - b.started);
    const active = playing.find(x => x.frameId === tab.selected) ?? playing[0] ?? frames[0];
    if (!active || M.excluded(tab.host)) continue;
    tab.selected = active.frameId;
    items.push({ id: String(id), pageKey: tab.key, host: tab.host, frameHost: active.host,
      titles: tab.manual ? [{ title: tab.manual.title, subtitle: tab.manual.subtitle, kind: "" }] : [...tab.titles, ...active.titles].slice(0, 10),
      ...active.player, manual: Boolean(tab.manual) });
  }
  return items.slice(0, 32);
}
async function flush() {
  await ready; connect(); if (!port) return;
  if (busy && Date.now() - lastReply < 5000) { pending = true; return; }
  if (busy) { port.disconnect(); port = undefined; busy = false; connect(); }
  if (!port) return;
  busy = true; lastReply = Date.now();
  const packet = { version: 1, clientId, browser, items: packetItems() };
  while (new TextEncoder().encode(JSON.stringify(packet)).length > 60000) packet.items.pop();
  port.postMessage(packet);
}
chrome.runtime.onMessage.addListener((message: CinePresence.Message, sender, reply) => {
  if (sender.id !== chrome.runtime.id) return;
  if (sender.tab) {
    if (sender.tab.id === undefined) return; const tabId = sender.tab.id; const frameId = sender.frameId ?? 0;
    if (!["sample", "gone"].includes(message.type) || sender.tab.incognito) return;
    reply({ received: true });
    if (message.type === "sample" && sender.documentLifecycle && sender.documentLifecycle !== "active") return;
    const topUrl = sender.tab.url ?? "", frameUrl = /^https?:/.test(sender.url ?? "") ? sender.url ?? "" : (sender.origin ?? "");
    const topHost = M.host(topUrl), frameHost = M.host(frameUrl);
    if (!topHost || M.excluded(topHost)) { forget(tabId); flush(); return; }
    if (!frameHost || M.excluded(frameHost)) {
      sequences.set(`${tabId}:${frameId}`, ++sequence);
      tabs.get(tabId)?.frames.delete(frameId); flush(); return;
    }
    if (message.type === "gone") {
      const previous = tabs.get(tabId)?.frames.get(frameId);
      if (previous?.documentId && sender.documentId && previous.documentId !== sender.documentId) return;
    }
    if (frameId === 0 && message.type === "sample" && message.page && message.page !== M.page(topUrl)) return;
    const frameKey = `${tabId}:${frameId}`, currentSequence = ++sequence;
    sequences.set(frameKey, currentSequence);
    if (message.type === "gone") {
      if (frameId === 0) forget(tabId); else tabs.get(tabId)?.frames.delete(frameId);
      flush(); return;
    }
    const epoch = epochs.get(tabId);
    // A content script only sends page/player facts. It cannot approve a manual title.
    (async () => {
      const id = tabId, page = M.page(topUrl), key = await hash(page);
      if (sequences.get(frameKey) !== currentSequence || epochs.get(id) !== epoch) return;
      let tab = tabs.get(id);
      if (!tab && tabs.size >= 64) return;
      if (!tab || tab.page !== page) { tab = { host: topHost, page, key, titles: [], frames: new Map() }; tabs.set(id, tab); }
      const titles = Array.isArray(message.titles) ? message.titles.slice(0, 8).map((x: CinePresence.Title) => ({ title: M.text(x.title), subtitle: M.text(x.subtitle), kind: ["Movie", "TVEpisode", "TVSeries"].includes(x.kind) ? x.kind : "", evidence: x.evidence ?? "", tmdbId: Number.isInteger(x.tmdbId) && x.tmdbId! > 0 ? x.tmdbId : undefined })) : [];
      if (frameId === 0) tab.titles = titles;
      const prior = tab.frames.get(frameId), now = Date.now();
      const p = message.player;
      const player = p && ["playing", "paused", "stopped"].includes(p.state) ? { state: p.state, position: Number.isFinite(p.position) ? p.position : null, duration: Number.isFinite(p.duration) ? p.duration : null, rate: Number.isFinite(p.rate) ? p.rate : 1, live: p.live === true, seekStart: Number.isFinite(p.seekStart) ? p.seekStart : 0 } : null;
      if (tab.frames.size < 20 || tab.frames.has(frameId)) tab.frames.set(frameId, {
        frameId: frameId, documentId: sender.documentId, host: frameHost, titles, player, seen: now,
        started: prior?.player?.state === "playing" && player?.state === "playing" ? prior.started : now
      });
      flush();
    })();
    return;
  }
  // Only the extension popup may set a temporary per-page manual title.
  if (sender.url !== chrome.runtime.getURL("popup.html")) return;
  (async () => {
    const [current] = await chrome.tabs.query({ active: true, currentWindow: true });
    const excluded = !current || current.incognito || !M.host(current.url ?? "") || M.excluded(M.host(current.url ?? ""));
    const tab = current?.id === undefined ? undefined : tabs.get(current.id);
    if (message.type === "manual" && !excluded && tab) {
      const title = M.text(message.title), season = String(message.season ?? ""), episode = String(message.episode ?? "");
      if (title && ((season === "" && episode === "") || (/^\d{1,2}$/.test(season) && /^\d{1,3}$/.test(episode) && Number(episode) > 0)))
        tab.manual = { title, subtitle: season !== "" ? `S${season}E${episode}` : "" };
    }
    if (message.type === "reset" && tab) delete tab.manual;
    await flush();
    reply({ status, excluded, detected: Boolean(tab && [...tab.frames.values()].some(x => x.player)), title: tab?.manual?.title ?? tab?.titles?.[0]?.title ?? "", manual: Boolean(tab?.manual) });
  })();
  return true;
});
function forget(id: number): void {
  tabs.delete(id); epochs.set(id, (epochs.get(id) ?? 0) + 1);
  requested.delete(id);
  for (const key of sequences.keys()) if (key.startsWith(`${id}:`)) sequences.delete(key);
}
chrome.tabs.onRemoved.addListener(id => { forget(id); flush(); });
chrome.tabs.onUpdated.addListener((id, change) => {
  if (change.url || change.status === "loading") { forget(id); flush(); }
  if (change.status === "complete") requestSample(id);
});
chrome.tabs.onActivated.addListener(({ tabId }) => { requestSample(tabId); });
setInterval(flush, 1000);
void discoverTabs();
flush();
