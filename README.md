# CinePresence

A Windows tray app that turns movie and TV playback into Discord Rich Presence. It reads compatible Windows media sessions and VLC's local HTTP interface, identifies titles through TMDB, and shares a **Watching** activity with artwork, episode details, and playback progress.

## Get started

1. Extract the **entire** release ZIP into a permanent folder. Run `CinePresence.exe`. Windows 11 x64 is the initial supported platform; the release includes .NET.
2. Keep the **Discord desktop app** open and enable activity sharing in Discord's settings.
3. Create a TMDB API credential at [TMDB account settings](https://www.themoviedb.org/settings/api). Paste the **API Read Access Token** (the long bearer token, not the shorter API key) into CinePresence → Settings. Test it, then save.
4. Play a movie, episode, or live show listed on TMDB. Open Sources to see what Windows exposes. If VLC does not expose enough information, follow [VLC setup](docs/VLC-SETUP.md).
5. A small **Watching** popup appears on the right when a new title is recognized. Use its **Change match** button or **Correct match** in the app if the result is wrong. Corrections can include a season and episode and are remembered for the detected title.

The release uses Discord application ID `1554495827219578880`. Most users should leave its override empty. CinePresence never needs a Discord user token, bot token, client secret, or Discord password.

## Behavior

- Compatible movie/TV websites can participate through Windows media sessions without a browser extension or a list of supported streaming sites. If media metadata has no usable title, CinePresence can use a caption with episode markers, a release year, a video filename, or recognizable watch-page wording when one unambiguous media candidate and session are available. It can fill missing episode numbers when the caption and series title agree. Keep the playing tab selected so its context can be checked; unassociated browser sessions are left unshared.
- Social/feed and general video platforms are excluded in this release: Facebook, YouTube, Twitter/X, Instagram, TikTok, VK, Vimeo, Dailymotion, Reddit, Twitch, Kick, Snapchat, Pinterest, and Threads. Matching a movie title does not bypass these exclusions. Detection uses available media metadata and associated window captions; Windows does not provide a dependable site URL for every browser session. If the source cannot be established, automatic sharing is blocked.
- Generic page names such as “Facebook” and “News Feed” are never treated as movie evidence, even if TMDB has an exact title match. Other ambiguous browser titles need **Correct match** confirmation; clear movie/episode/live-show metadata remains automatic on eligible sites. Only saved explicit corrections can approve ambiguous titles—old automatic cache results cannot. Automatic lookup also requires stronger title similarity.
- Windows Media Player and Movies & TV work only when their installed versions publish media sessions. Legacy players without a session or adapter are not supported automatically.
- Automatic mode stays with the first eligible source observed playing. Other sources cannot take over while it keeps playing. Pausing, stopping, closing, or excluding it hands over to the next playing source; resuming it does not steal presence back. If several sources are already playing at startup, the first observed wins. Pin a source or exclude an app in Sources. Pinning is temporary and holds that source even when paused; exclusions persist.
- Titles such as `Show.S02E04.1080p.mkv`, `Show S1:E7`, `Show S1-E7`, `Show 2x04`, and `Show Season 2 Episode 4` are recognized. Movies can include release years. Episode titles require usable episode information or a correction.
- Common streaming page wording is removed before lookup: for example, `Watch WWE NXT for free on example.tv` becomes `WWE NXT`. This applies to any title or website, including live shows, without a list of sports programmes or sites. Explicit live markers and broadcast-date suffixes are normalized. A live show without season/episode metadata stays series-only; today's date is never used to guess an episode. Bare “Live” in names such as *Saturday Night Live* is preserved.
- The best plausible TMDB match is published automatically. Generic titles, unrelated search results, and known music sources are skipped. Windows' advisory Music flag is not treated as proof of audio: browsers and VLC plugins also use it for videos. Known audio apps and explicit audio filenames remain excluded. Matching is metadata-based; CinePresence does not recognize video images or audio.
- Pause, stop, source closure, sharing off, and Quit clear the activity. Windows events trigger prompt reads; VLC changes are detected on its next one-second poll. Network failures can take the two-second VLC timeout to detect.
- Player position and duration drive timestamps. Missing timing means no progress bar. Faster/slower playback adjusts Discord's clock to preserve progress and remaining wall time; the app preview shows media time.
- Explicit live markers or an observed growing stream buffer switch to a **Live** label with title/artwork and no countdown. Detecting a moving buffer can take a few updates; it is a heuristic because [Windows exposes timelines and seek ranges](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties), not a universal live-stream flag. Unknown timing alone does not imply a live broadcast.
- Compact watching popups include the show's poster and appear near the bottom-right of the active monitor without taking keyboard focus. They slide/fade in, auto-dismiss after 7 seconds, and stay open while hovered. Entrance/exit motion respects Windows' animation setting. Missing artwork uses a placeholder. No repeated popup on pause/resume, timing updates, or the same episode moving between players. Use Settings to disable popups. The correction button always targets the title shown, even if playback changes while searching.
- Closing the window keeps CinePresence in the tray. Use the tray menu to reopen it or quit. Start at login is optional; leave the extracted folder in place if you enable it.
- Discord controls the final layout and may prioritize other activities. The app preview is an approximation. There is no guarantee of a Spotify-identical layout on every Discord client.

## Privacy and local data

Settings and a bounded match cache live in `%LOCALAPPDATA%\CinePresence`. TMDB tokens and VLC passwords are encrypted with Windows DPAPI for the current account. Detected filenames are normalized before lookup; cache lookup keys are hashed. TMDB receives title searches, and Discord receives the selected public activity and TMDB artwork URL. No telemetry or hosted CinePresence backend is used.

Browser sessions may expose private/incognito playback without an incognito flag. Turn sharing off or exclude the browser when you do not want it shared. Exclusions apply to apps, not individual websites, because Windows does not reliably provide site identity.

Clear match cache removes cached matches and saved corrections. To remove the app, first turn off Start at login, quit from the tray, and delete its extracted folder. Delete the local data folder too if you want to remove saved preferences and credentials.

## Build and test

```powershell
.\scripts\build.ps1
.\scripts\package.ps1
```

The scripts install Microsoft's .NET 10 SDK locally into `.tools` if needed. NuGet packages also stay in `.tools`. The release script runs tests and creates a versioned ZIP and extracted release directory under `artifacts`, plus `SHA256SUMS.txt`. It requires network access for the SDK, packages, and self-contained runtime packs.

To use your own Discord application identity, pass `-DiscordApplicationId` to the packaging script or use the app's override. Application IDs are public identifiers, not secrets. Never embed a shared TMDB token in a release.

The app includes isolated QA commands:

```powershell
# Render UI fixtures without contacting Discord/TMDB or using normal saved settings.
.\CinePresence.exe --smoke-test C:\temp\cinepresence-ui
# Check Windows sessions and the Discord IPC connection; no activity is published.
.\CinePresence.exe --diagnose C:\temp\cinepresence-diagnostics.json
# Sample playback for ten seconds, then identify it using the saved token.
# Does not publish to Discord or use saved match corrections.
.\CinePresence.exe --diagnose C:\temp\cinepresence-live.json --media-only --resolve --sample
```

See [validation](docs/VALIDATION.md) for what has actually been verified and remaining manual checks. Source code is organized into the platform-independent Core, WPF App/platform adapters, and tests. Adapters normalize playback snapshots; the engine arbitrates sources and guards asynchronous lookup results; Discord publishing is behind an interface.

## Attribution

This product uses the TMDB API but is not endorsed or certified by TMDB. See [third-party notices](THIRD-PARTY-NOTICES.md). CinePresence is not affiliated with Discord or VideoLAN.
