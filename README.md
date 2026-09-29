# CinePresence

A Windows tray app that turns movie and TV playback into Discord Rich Presence. It reads compatible Windows media sessions and VLC's local HTTP interface, identifies titles through TMDB, and shares a **Watching** activity with artwork, episode details, and playback progress.

## Get started

1. Extract the **entire** release ZIP into a permanent folder. Run `CinePresence.exe`. Windows 11 x64 is the initial supported platform; the release includes .NET.
2. Keep the **Discord desktop app** open and enable activity sharing in Discord's settings.
3. Create a TMDB API credential at [TMDB account settings](https://www.themoviedb.org/settings/api). Paste the **API Read Access Token** (the long bearer token, not the shorter API key) into CinePresence → Settings. Test it, then save.
4. Play a movie or episode. Open Sources to see what Windows exposes. If VLC does not expose enough information, follow [VLC setup](docs/VLC-SETUP.md).
5. Use **Correct match** if the automatic result is wrong. Corrections can include a season and episode and are remembered for the detected title.

The release uses Discord application ID `1554495827219578880`. Most users should leave its override empty. CinePresence never needs a Discord user token, bot token, client secret, or Discord password.

## Behavior

- Any website can participate when its browser exposes a usable Windows media session. There is no website whitelist or browser extension.
- Windows Media Player and Movies & TV work only when their installed versions publish media sessions. Legacy players without a session or adapter are not supported automatically.
- Automatic mode prefers Windows' current playing session, then the most recently active eligible source. Pin a source or exclude an app in Sources. Pinning is temporary; exclusions persist.
- Titles such as `Show.S02E04.1080p.mkv`, `Show 2x04`, and `Show Season 2 Episode 4` are recognized. Movies can include release years. Episode titles require usable episode information or a correction.
- The best plausible TMDB match is published automatically. Generic titles, unrelated search results, and known music sources are skipped. Matching is metadata-based; CinePresence does not recognize video images or audio.
- Pause, stop, source closure, sharing off, and Quit clear the activity. Windows events trigger prompt reads; VLC changes are detected on its next one-second poll. Network failures can take the two-second VLC timeout to detect.
- Player position and duration drive timestamps. Missing timing means no progress bar. Faster/slower playback adjusts Discord's clock to preserve progress and remaining wall time; the app preview shows media time.
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

The scripts install Microsoft's .NET 10 SDK locally into `.tools` if needed. NuGet packages also stay in `.tools`. The release script runs tests and creates `artifacts\CinePresence-0.1.0-win-x64.zip`, an extracted release directory, and `SHA256SUMS.txt`. It requires network access for the SDK, packages, and self-contained runtime packs.

To use your own Discord application identity, pass `-DiscordApplicationId` to the packaging script or use the app's override. Application IDs are public identifiers, not secrets. Never embed a shared TMDB token in a release.

The app includes isolated QA commands:

```powershell
# Render UI fixtures without contacting Discord/TMDB or using normal saved settings.
.\CinePresence.exe --smoke-test C:\temp\cinepresence-ui
# Check Windows sessions and the Discord IPC connection; no activity is published.
.\CinePresence.exe --diagnose C:\temp\cinepresence-diagnostics.json
```

See [validation](docs/VALIDATION.md) for what has actually been verified and remaining manual checks. Source code is organized into the platform-independent Core, WPF App/platform adapters, and tests. Adapters normalize playback snapshots; the engine arbitrates sources and guards asynchronous lookup results; Discord publishing is behind an interface.

## Attribution

This product uses the TMDB API but is not endorsed or certified by TMDB. See [third-party notices](THIRD-PARTY-NOTICES.md). CinePresence is not affiliated with Discord or VideoLAN.
