declare namespace CinePresence {
  interface Title { title: string; subtitle: string; kind: string; evidence?: string; tmdbId?: number; }
  interface Route { kind?: string; tmdbId?: number; season?: number; episode?: number; }
  interface Player { state: "playing" | "paused" | "stopped"; position: number | null; duration: number | null; rate: number; live: boolean; seekStart: number; }
  interface Frame { frameId: number; host: string; titles: Title[]; player: Player | null; seen: number; started: number; }
  interface Tab { host: string; page: string; key: string; titles: Title[]; frames: Map<number, Frame>; selected?: number; manual?: { title: string; subtitle: string }; }
  interface Item extends Player { id: string; pageKey: string; host: string; frameHost: string; titles: Title[]; manual: boolean; }
  interface Message { type: string; titles?: Title[]; player?: Player | null; page?: string; title?: string; season?: string; episode?: string; }
  interface Reply { connected?: boolean; message?: string; }
  interface PopupState { status: string; excluded: boolean; detected: boolean; title: string; manual: boolean; }
  interface Metadata {
    host(url: string): string; excluded(host: string): boolean; page(url: string): string;
    text(value: unknown): string; route(url: string): Route;
    structured(nodes: unknown): Title[];
    collect(doc: Document, nav: Partial<Pick<Navigator, "mediaSession">>, url?: string): Title[];
  }
}
declare var CinePresenceMetadata: CinePresence.Metadata;
