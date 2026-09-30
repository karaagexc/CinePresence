import "./metadata.js";
const M = globalThis.CinePresenceMetadata;
const tabs = new Map();
const epochs = new Map(), sequences = new Map();
let sequence = 0;
const browser = /Edg\//.test(navigator.userAgent) ? "msedge" : "chrome";
let port, clientId, busy = false, pending = false, lastReply = 0, status = "Connecting…";
const ready = chrome.storage.session.get("clientId").then(async saved => {
  clientId = saved.clientId ?? crypto.randomUUID();
  await chrome.storage.session.set({ clientId });
});
function connect() {
  if (port) return;
  try {
    const connectedPort = chrome.runtime.connectNative("org.cinepresence.companion");
    port = connectedPort;
    connectedPort.onMessage.addListener(reply => {
      if (port !== connectedPort) return;
      busy = false; lastReply = Date.now(); status = reply.message ?? (reply.connected ? "Connected" : "Open CinePresence");
      if (pending) { pending = false; flush(); }
    });
    connectedPort.onDisconnect.addListener(() => { void chrome.runtime.lastError; if (port !== connectedPort) return; port = null; busy = false; status = "Connection unavailable. Open CinePresence → Settings → Set up companion."; });
  } catch { status = "Set up the browser connection in CinePresence Settings."; }
}
async function hash(value) {
  return [...new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)))].map(x => x.toString(16).padStart(2, "0")).join("");
}
function packetItems() {
  const now = Date.now(), items = [];
  for (const [id, tab] of tabs) {
    for (const [frameId, frame] of tab.frames) if (now - frame.seen > 6000) tab.frames.delete(frameId);
    const frames = [...tab.frames.values()].filter(x => x.player);
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
  if (busy) { port.disconnect(); port = null; busy = false; connect(); }
  if (!port) return;
  busy = true; lastReply = Date.now();
  const packet = { version: 1, clientId, browser, items: packetItems() };
  while (new TextEncoder().encode(JSON.stringify(packet)).length > 60000) packet.items.pop();
  port.postMessage(packet);
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id) return;
  if (sender.tab) {
    if (!["sample", "gone"].includes(message.type) || sender.tab.incognito) return;
    const topUrl = sender.tab.url ?? "", frameUrl = /^https?:/.test(sender.url ?? "") ? sender.url : sender.origin;
    const topHost = M.host(topUrl), frameHost = M.host(frameUrl);
    if (!topHost || M.excluded(topHost)) { forget(sender.tab.id); flush(); return; }
    if (!frameHost || M.excluded(frameHost)) {
      sequences.set(`${sender.tab.id}:${sender.frameId}`, ++sequence);
      tabs.get(sender.tab.id)?.frames.delete(sender.frameId); flush(); return;
    }
    const frameKey = `${sender.tab.id}:${sender.frameId}`, currentSequence = ++sequence;
    sequences.set(frameKey, currentSequence);
    if (message.type === "gone") { tabs.get(sender.tab.id)?.frames.delete(sender.frameId); flush(); return; }
    const epoch = epochs.get(sender.tab.id);
    // A content script only sends page/player facts. It cannot approve a manual title.
    (async () => {
      const id = sender.tab.id, page = M.page(topUrl), key = await hash(page);
      if (sequences.get(frameKey) !== currentSequence || epochs.get(id) !== epoch) return;
      let tab = tabs.get(id);
      if (!tab && tabs.size >= 64) return;
      if (!tab || tab.page !== page) { tab = { host: topHost, page, key, titles: [], frames: new Map() }; tabs.set(id, tab); }
      const titles = Array.isArray(message.titles) ? message.titles.slice(0, 8).map(x => ({ title: M.text(x.title), subtitle: M.text(x.subtitle), kind: ["Movie", "TVEpisode", "TVSeries"].includes(x.kind) ? x.kind : "" })) : [];
      if (sender.frameId === 0) tab.titles = titles;
      const prior = tab.frames.get(sender.frameId), now = Date.now();
      const p = message.player;
      const player = p && ["playing", "paused", "stopped"].includes(p.state) ? { state: p.state, position: Number.isFinite(p.position) ? p.position : null, duration: Number.isFinite(p.duration) ? p.duration : null, rate: Number.isFinite(p.rate) ? p.rate : 1, live: p.live === true, seekStart: Number.isFinite(p.seekStart) ? p.seekStart : 0 } : null;
      if (tab.frames.size < 20 || tab.frames.has(sender.frameId)) tab.frames.set(sender.frameId, {
        frameId: sender.frameId, host: frameHost, titles, player, seen: now,
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
    const excluded = !current || current.incognito || !M.host(current.url) || M.excluded(M.host(current.url));
    const tab = tabs.get(current?.id);
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
function forget(id) {
  tabs.delete(id); epochs.set(id, (epochs.get(id) ?? 0) + 1);
  for (const key of sequences.keys()) if (key.startsWith(`${id}:`)) sequences.delete(key);
}
chrome.tabs.onRemoved.addListener(id => { forget(id); flush(); });
chrome.tabs.onUpdated.addListener((id, change) => {
  if (change.url || change.status === "loading") { forget(id); flush(); }
});
setInterval(flush, 1000);
flush();
