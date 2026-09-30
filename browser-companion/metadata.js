(() => {
  const blocked = ["facebook.com", "fb.com", "fb.watch", "youtube.com", "youtube-nocookie.com", "youtu.be", "twitter.com", "x.com", "instagram.com", "tiktok.com", "vk.com", "vkvideo.ru", "vimeo.com", "dailymotion.com", "dai.ly", "reddit.com", "twitch.tv", "kick.com", "snapchat.com", "pinterest.com", "threads.net", "threads.com"];
  const text = value => typeof value === "string" ? value.replace(/\s+/g, " ").trim().slice(0, 300) : "";
  const host = url => { try { return new URL(url).hostname.toLowerCase().replace(/\.$/, ""); } catch { return ""; } };
  const excluded = value => blocked.some(x => value === x || value.endsWith("." + x));
  const page = url => { try { const u = new URL(url); return u.origin + u.pathname; } catch { return ""; } };
  const integer = value => /^\d{1,3}$/.test(String(value ?? "")) ? Number(value) : null;
  function structured(nodes) {
    const titles = []; let visited = 0;
    function visit(node, depth = 0) {
      if (!node || typeof node !== "object" || depth > 8 || ++visited > 200) return;
      if (Array.isArray(node)) { node.slice(0, 30).forEach(x => visit(x, depth + 1)); return; }
      const types = Array.isArray(node["@type"]) ? node["@type"] : [node["@type"]];
      const kind = types.find(x => ["Movie", "TVSeries", "TVEpisode"].includes(x));
      if (kind) {
        const series = node.partOfSeries ?? node.partOfTVSeries ?? node.partOfSeason?.partOfSeries;
        let title = text(kind === "TVEpisode" ? series?.name : node.name);
        const year = String(node.datePublished ?? "").match(/^(19\d{2}|20\d{2})(?:-|$)/)?.[1];
        if (kind === "Movie" && title && year && !title.includes(year)) title += ` (${year})`;
        const season = integer(node.partOfSeason?.seasonNumber ?? node.seasonNumber);
        const episode = integer(node.episodeNumber);
        const subtitle = season !== null && episode > 0 ? `S${season}E${episode}` : "";
        if (title) titles.push({ title, subtitle, kind });
      }
      for (const [key, value] of Object.entries(node)) if (key !== "potentialAction") visit(value, depth + 1);
    }
    visit(nodes); return titles.slice(0, 5);
  }
  function collect(doc, nav) {
    const data = [];
    for (const script of [...doc.querySelectorAll('script[type="application/ld+json"]')].slice(0, 12)) {
      if (script.textContent.length > 131072) continue;
      try { data.push(JSON.parse(script.textContent)); } catch { /* A malformed page block isn't playback metadata. */ }
    }
    const meta = name => text(doc.querySelector(`meta[property="${name}"],meta[name="${name}"]`)?.content);
    // An episode's display name alone does not identify its parent series.
    const kind = { "video.movie": "Movie", "video.tv_show": "TVSeries" }[meta("og:type")] ?? "";
    const h1 = text(doc.querySelector("h1")?.textContent);
    const headings = [...doc.querySelectorAll("h1,h2,[itemprop='episodeNumber'],[itemprop='seasonNumber']")].slice(0, 12).map(x => text(x.textContent)).join(" ");
    const episode = headings.match(/\b(?:S\d{1,2}[\s.:_-]*E\d{1,3}|\d{1,2}x\d{1,3}|Season\s+\d{1,2}\s+(?:Episode|Ep)\s+\d{1,3})\b/i)?.[0] ?? "";
    const titles = [...structured(data),
      { title: text(nav.mediaSession?.metadata?.title), subtitle: episode, kind: "" },
      { title: h1, subtitle: episode, kind },
      { title: meta("og:title"), subtitle: episode, kind },
      { title: text(doc.title), subtitle: episode, kind: "" }];
    return titles.filter(x => x.title && !/^(?:watch|watch now|play|player|loading|home|video|media|untitled)$/i.test(x.title)).slice(0, 8);
  }
  globalThis.CinePresenceMetadata = { host, excluded, page, structured, collect, text };
})();
