(() => {
  const M = globalThis.CinePresenceMetadata;
  let invalid = false, timer, lastMetadataAt = 0, titles = [], watched = new WeakSet();
  function send() {
    if (invalid) return;
    // The worker checks both the top-level and iframe domains again.
    if (M.excluded(M.host(location.href))) return;
    const now = Date.now();
    if (now - lastMetadataAt > 2000) { titles = M.collect(document, navigator); lastMetadataAt = now; }
    const videos = [...document.querySelectorAll("video")].filter(v => {
      const r = v.getBoundingClientRect();
      return r.width >= 120 && r.height >= 70 && v.readyState >= 1;
    });
    videos.sort((a, b) => Number(b.paused === false && !b.ended) - Number(a.paused === false && !a.ended));
    const video = videos[0];
    for (const v of videos) if (!watched.has(v)) {
      watched.add(v);
      for (const event of ["play", "pause", "ended", "seeking", "seeked", "ratechange", "loadedmetadata", "emptied"])
        v.addEventListener(event, send);
    }
    const player = video ? {
      state: video.ended ? "stopped" : video.paused ? "paused" : "playing",
      position: Number.isFinite(video.currentTime) ? video.currentTime : null,
      duration: Number.isFinite(video.duration) && video.duration > 0 ? video.duration : null,
      rate: video.playbackRate, live: video.duration === Infinity,
      seekStart: video.seekable.length ? video.seekable.start(0) : 0
    } : null;
    chrome.runtime.sendMessage({ type: "sample", titles, player, page: M.page(location.href) }).catch(() => { invalid = true; clearInterval(timer); });
  }
  timer = setInterval(send, 1000);
  addEventListener("pagehide", () => { chrome.runtime.sendMessage({ type: "gone" }).catch(() => {}); });
  send();
})();
