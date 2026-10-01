# Optional Chrome / Edge companion

Use this when a streaming player gives Windows a generic title, or does not create a Windows media session at all. The companion collects structured movie/episode metadata, relevant headings and video timing. It supports embedded players without a streaming-site whitelist. It does not recognize scenes or audio, bypass protected players, or invent episode information.

## Install

1. Run the CinePresence installer and leave **Prepare the optional Chrome/Edge companion** selected. This installs its files and registers the native messaging host for your Windows account. It does not require administrator rights or change browser policies. Portable users choose **Settings → Set up companion** instead.
2. The guide opens with the installed extension folder path. Open `edge://extensions` or `chrome://extensions`, turn on **Developer mode**, choose **Load unpacked**, and select that folder.
3. Reload existing streaming tabs. Keep CinePresence and the Discord desktop app open. The companion's toolbar popup shows connection status; CinePresence Sources labels its sessions **Browser companion**.

The extension ID is `ndikeejjjaangmgeohkglafbldikbnag`. The public manifest key makes the unpacked ID stable across installations. There is no Chrome Web Store or Edge Add-ons listing yet. Normal Windows installers cannot silently enable an unlisted Chrome extension; store-based distribution needs an owner developer account, submission and browser review. The current installer prepares everything locally and guides the final user-controlled loading step. See [Chrome distribution rules](https://developer.chrome.com/docs/extensions/how-to/distribute/install-extensions) and [Edge native messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging).

## Missing or incorrect metadata

- A site may expose its series name and episode only in JSON-LD or headings. The companion can use these even if the tab is called “Watch” or “Player.” It combines parent-page metadata with a readable HTML video in an iframe.
- Player-adjacent episode labels, release years, Open Graph types and generic movie/TV routes provide additional evidence. Numeric route IDs are only hints: the app checks their TMDB title and type before using them. A movie/series type narrows search; unresolved same-name ties stay unshared instead of selecting the first result. This works across sites, without hardcoded show IDs.
- **Change match** fades the watching card into a searchable poster list, with editable season and episode. The app's correction window uses the same picker. Strong evidence is handled automatically; the picker remains a fallback.
- If nothing usable is exposed, open the companion popup and enter the movie or series name, plus both season and episode if known. **Use for this page** is temporary; navigation or browser restart clears it. Choose **Correct match** in the app for the exact TMDB entry. No episode number or TMDB runtime is guessed.
- If you see “Start a video,” reload the tab after enabling the extension. A video hidden in a closed shadow root, a protected browser page or a player without an accessible HTML video may be unavailable. Pages with ambiguous titles require confirmation.
- A live stream with infinite duration is live immediately. Finite growing/rolling buffers are classified after several observations, so no fabricated programme end time is published.

## Restrictions and privacy

Spotify, Facebook, YouTube (including privacy-enhanced embeds), Twitter/X, Instagram, TikTok, VK, Vimeo, Dailymotion, Reddit, Twitch, Kick, Snapchat, Pinterest and Threads are excluded by exact domain/subdomain checks. Both the outer page and player iframe must be eligible; manual titles cannot bypass these restrictions. Private windows are skipped. Unidentified and non-movie/series metadata stays unshared.

The companion's page permission lets it inspect relevant headings, JSON-LD, Open Graph metadata and video properties. Candidate titles/episode fields, domains, a page hash and playback state/timing go to the local Windows app. Full URLs, query strings, media download URLs, cookies, form inputs and browsing history are not sent. Only the desktop app contacts TMDB/Discord. Communication uses the browser's native messaging API and a Windows pipe restricted to the current account; no server listens on a network port. Payload sizes and session counts are bounded.

While connected, the companion is authoritative for its browser, including empty/excluded pages. Windows duplicates cannot bypass its filters. Removing/disabling the extension restores native fallback after its connection expires. Native Windows metadata cannot provide the same certainty about domain identity or private windows; use Sharing off or app exclusions if needed.

## Updating and removing

Install a newer CinePresence release into the same folder, then reload the unpacked extension in the browser. Portable users moving folders must run **Set up companion** again and load the new extension folder. Remove the extension through the browser's extensions page. The app uninstaller removes its own native host registrations and preserves saved settings/cache.

## Development

Companion sources and the setup-page helper are strict TypeScript in `browser-companion/src`. `npm ci --prefix browser-companion` and `npm run build --prefix browser-companion` compile runtime files into `dist` and type-check the TypeScript test tools; the main build script does both. The browser loads the generated JavaScript because browsers do not execute TypeScript directly. Only runtime files and icons are packaged, not development dependencies. See [Build and development](DEVELOPMENT.md) for prerequisites and test commands.
