(() => {
  const M = globalThis.CinePresenceMetadata;
  let invalid = false, suspended = false, timer: number | undefined, lastMetadataAt = 0, lastPage = "";
  let titles: CinePresence.Title[] = [];
  function failed(error: unknown) {
    // A sleeping/restarting worker is recoverable. Only a replaced extension
    // context needs a page reload; a transient send error must not silence a tab.
    if (!chrome.runtime.id || /extension context invalidated/i.test(String(error))) {
      invalid = true; clearInterval(timer);
    }
  }
  function transmit(message: CinePresence.Message) {
    if (invalid) return;
    try { void chrome.runtime.sendMessage(message).catch(failed); } catch (error) { failed(error); }
  }
  function send() {
    if (invalid || suspended) return;
    // The worker checks both the top-level and iframe domains again.
    if (M.excluded(M.host(location.href))) return;
    const now = Date.now();
    const currentPage = location.href;
    if (currentPage !== lastPage || now - lastMetadataAt > 2000) { titles = M.collect(document, navigator, currentPage); lastMetadataAt = now; lastPage = currentPage; }
    const videos = [...document.querySelectorAll("video")].filter(v => {
      const r = v.getBoundingClientRect();
      return r.width >= 120 && r.height >= 70 && v.readyState >= 1;
    });
    videos.sort((a, b) => Number(b.paused === false && !b.ended) - Number(a.paused === false && !a.ended));
    const video = videos[0];
    const player: CinePresence.Player | null = video ? {
      state: video.ended ? "stopped" : video.paused ? "paused" : "playing",
      position: Number.isFinite(video.currentTime) ? video.currentTime : null,
      duration: Number.isFinite(video.duration) && video.duration > 0 ? video.duration : null,
      rate: video.playbackRate, live: video.duration === Infinity,
      seekStart: video.seekable.length ? video.seekable.start(0) : 0
    } : null;
    transmit({ type: "sample", titles, player, page: M.page(location.href) });
  }
  // Capture media events before a dynamically inserted player is discovered by
  // the next timer tick. Pausing must reach the worker even in a background tab.
  for (const event of ["play", "playing", "pause", "ended", "seeking", "seeked", "ratechange", "loadedmetadata", "durationchange", "emptied"])
    document.addEventListener(event, send, true);
  document.addEventListener("visibilitychange", send);
  chrome.runtime.onMessage.addListener((message: CinePresence.Message, sender, reply) => {
    if (sender.id !== chrome.runtime.id || message.type !== "sample-now") return;
    lastMetadataAt = 0; send(); reply({ received: true });
  });
  timer = setInterval(send, 1000);
  addEventListener("pagehide", () => { suspended = true; transmit({ type: "gone" }); });
  addEventListener("pageshow", () => { suspended = false; lastMetadataAt = 0; send(); });
  send();
})();
