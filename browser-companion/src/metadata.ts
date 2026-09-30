(() => {
  const blocked = ["facebook.com", "fb.com", "fb.watch", "youtube.com", "youtube-nocookie.com", "youtu.be", "twitter.com", "x.com", "instagram.com", "tiktok.com", "vk.com", "vkvideo.ru", "vimeo.com", "dailymotion.com", "dai.ly", "reddit.com", "twitch.tv", "kick.com", "snapchat.com", "pinterest.com", "threads.net", "threads.com"];
  const text = (value: unknown): string => typeof value === "string" ? value.replace(/\s+/g, " ").trim().slice(0, 300) : "";
  const host = (url: string): string => { try { return new URL(url).hostname.toLowerCase().replace(/\.$/, ""); } catch { return ""; } };
  const excluded = (value: string): boolean => blocked.some(x => value === x || value.endsWith("." + x));
  const page = (url: string): string => { try { const u = new URL(url); return u.origin + u.pathname; } catch { return ""; } };
  const integer = (value: unknown): number | undefined => /^\d{1,3}$/.test(String(value ?? "")) ? Number(value) : undefined;
  const object = (value: unknown): Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
  function route(url: string): CinePresence.Route {
    try {
      const u = new URL(url);
      const match = u.pathname.match(/(?:^|\/)(tv|series|movie)\/(\d{1,9})(?:\/(\d{1,2})\/(\d{1,3}))?(?:\/|$)/i);
      if (!match) return {};
      const kind = match[1]!.toLowerCase() === "movie" ? "Movie" : "TVSeries";
      const season = kind === "TVSeries" ? integer(match[3] ?? u.searchParams.get("season")) : undefined;
      const episode = kind === "TVSeries" ? integer(match[4] ?? u.searchParams.get("episode")) : undefined;
      return { kind, tmdbId: Number(match[2]), season, episode: episode && episode > 0 ? episode : undefined };
    } catch { return {}; }
  }
  function structured(nodes: unknown): CinePresence.Title[] {
    const titles: CinePresence.Title[] = []; let visited = 0;
    function visit(node: unknown, depth = 0): void {
      if (!node || typeof node !== "object" || depth > 8 || ++visited > 200) return;
      if (Array.isArray(node)) { node.slice(0, 30).forEach(x => visit(x, depth + 1)); return; }
      const value = object(node);
      const types = Array.isArray(value["@type"]) ? value["@type"] : [value["@type"]];
      const kind = types.find((x): x is string => typeof x === "string" && ["Movie", "TVSeries", "TVEpisode"].includes(x));
      if (kind) {
        const seasonData = object(value.partOfSeason);
        const series = object(value.partOfSeries ?? value.partOfTVSeries ?? seasonData.partOfSeries);
        let title = text(kind === "TVEpisode" ? series.name : value.name);
        const year = String(value.datePublished ?? "").match(/^(19\d{2}|20\d{2})(?:-|$)/)?.[1];
        if (kind === "Movie" && title && year && !title.includes(year)) title += ` (${year})`;
        const season = integer(seasonData.seasonNumber ?? value.seasonNumber), episode = integer(value.episodeNumber);
        const subtitle = season !== undefined && episode !== undefined && episode > 0 ? `S${season}E${episode}` : "";
        if (title) titles.push({ title, subtitle, kind, evidence: "structured" });
      }
      for (const [key, item] of Object.entries(value)) if (key !== "potentialAction") visit(item, depth + 1);
    }
    visit(nodes); return titles.slice(0, 5);
  }
  function collect(doc: Document, nav: Pick<Navigator, "mediaSession">, url = doc.URL ?? ""): CinePresence.Title[] {
    const data: unknown[] = [];
    for (const script of [...doc.querySelectorAll('script[type="application/ld+json"]')].slice(0, 12)) {
      const json = script.textContent ?? "";
      if (json.length > 131072) continue;
      try { data.push(JSON.parse(json) as unknown); } catch { }
    }
    const meta = (name: string): string => text(doc.querySelector<HTMLMetaElement>(`meta[property="${name}"],meta[name="${name}"]`)?.content);
    const hint = route(url);
    const kinds: Record<string, string> = { "video.movie": "Movie", "video.tv_show": "TVSeries" };
    const kind = hint.kind ?? kinds[meta("og:type")] ?? "";
    const heading = doc.querySelector("h1") ?? doc.querySelector('[itemprop="name"],[data-testid="video-title"],[class*="player-title"],[class*="video-title"]');
    const h1 = text(heading?.textContent);
    const regions = [heading?.textContent ?? ""];
    let parent = heading?.parentElement;
    for (let i = 0; i < 3 && parent; i++, parent = parent.parentElement) {
      const region = parent.textContent ?? "";
      if (region.length > 1800 || /\b(?:recommended|you may also like|related movies)\b/i.test(region)) break;
      regions.push(region);
    }
    // Read local player-title context, not arbitrary page text/comments.
    const nearby = regions.join(" ") + " " + [...doc.querySelectorAll("h1,h2,[itemprop='episodeNumber'],[itemprop='seasonNumber']")].slice(0, 12).map(x => text(x.textContent)).join(" ");
    const explicitEpisode = nearby.match(/\b(?:S\d{1,2}[\s.:_-]*E\d{1,3}|\d{1,2}x\d{1,3}|Season\s+\d{1,2}\s+(?:Episode|Ep)\s+\d{1,3})\b/i)?.[0] ?? "";
    const routeEpisode = hint.season !== undefined && hint.episode !== undefined ? `S${hint.season}E${hint.episode}` : "";
    // Explicit on-screen episode labels take precedence over route hints.
    const episode = explicitEpisode || routeEpisode;
    const year = kind === "Movie" ? nearby.match(/\b(19\d{2}|20\d{2})\b/)?.[1] : undefined;
    const brand = host(url).replace(/^www\./, "").split(".")[0] ?? "";
    const normalize = (s: string): string => s.toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
    const clean = (s: string): string => {
      const parts = s.split(/\s+[|–—-]\s+/);
      if (parts.length > 1 && [normalize(meta("og:site_name")), normalize(brand)].filter(Boolean).includes(normalize(parts.at(-1)!))) parts.pop();
      let value = parts.join(" — ");
      if (year && !value.includes(year)) value += ` (${year})`;
      return value;
    };
    const candidates: CinePresence.Title[] = [...structured(data),
      { title: text(nav.mediaSession?.metadata?.title), subtitle: episode, kind, evidence: "media" },
      { title: h1, subtitle: episode, kind, evidence: "heading" },
      { title: meta("og:title"), subtitle: episode, kind, evidence: "og" },
      { title: text(doc.title), subtitle: episode, kind, evidence: "document" }];
    return candidates.filter(x => x.title && !/^(?:watch|watch now|play|player|loading|home|video|media|untitled)$/i.test(x.title))
      .map(x => ({ ...x, title: clean(x.title), subtitle: x.subtitle || episode, kind: x.kind || kind,
        tmdbId: hint.kind && (!x.kind || x.kind === hint.kind || hint.kind === "TVSeries" && x.kind === "TVEpisode") ? hint.tmdbId : undefined })).slice(0, 8);
  }
  globalThis.CinePresenceMetadata = { host, excluded, page, structured, collect, text, route };
})();
