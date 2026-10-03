# Build and development

The desktop app uses C#/.NET 10 and WPF. The browser companion, setup-page helper, and Node-based test tools are maintained in TypeScript.

## Prerequisites

- Windows 11 x64
- Node.js 24 or newer, including npm
- Network access for the initial SDK, NuGet and npm restores

```powershell
.\scripts\build.ps1
.\scripts\package.ps1
```

The build script installs the .NET 10 SDK into `.tools` if needed, restores pinned dependencies, compiles the TypeScript companion and type-checks the TypeScript tools. It then runs the .NET suite and `tests/browser-companion.test.mts` using Node's native TypeScript support.

The packaging script creates a self-contained ZIP, extracted release directory, Windows installer and `SHA256SUMS.txt` under `artifacts`. Inno Setup is downloaded from its official release, signature-checked and installed locally in `.tools/inno`. Release users need neither Node.js nor a separate .NET installation.

## Source layout

| Folder | Responsibility |
| --- | --- |
| `src/CinePresence.Core` | Parsing, eligibility, TMDB resolution, source selection, cache and playback engine |
| `src/CinePresence.App` | WPF interface, tray lifecycle, Windows/VLC/browser adapters and Discord publishing |
| `src/CinePresence.BrowserHost` | Browser native-messaging bridge over a same-user Windows pipe |
| `browser-companion/src` | TypeScript metadata extraction, player observation, worker, popup and setup guide helper |
| `tests` | .NET tests and TypeScript companion integration fixtures |
| `scripts` / `installer` | Build, package, icon generation and installer tooling |

Browser-ready scripts are generated into the ignored `browser-companion/dist` directory. Browsers execute these compiled files; there are no maintained `.js` or `.cjs` source files. The installer removes the old root-level companion scripts during upgrades. Development dependencies and TypeScript test tools are not shipped with the app.

## Focused checks

```powershell
npm.cmd run check --prefix browser-companion
node --test tests/browser-companion.test.mts
# Close CinePresence before this test: it temporarily owns the app's local pipe.
node scripts/test-browser-host.mts artifacts/CinePresence-0.2.4-win-x64/CinePresence.BrowserHost.exe
```

The native-host test runs under the current Windows account. Its pipe identity must match the account that launches the executable; it does not publish Discord activity.

## App diagnostics

```powershell
# UI fixtures with isolated settings; no Discord/TMDB connection.
.\CinePresence.exe --smoke-test C:\temp\cinepresence-ui
# Windows sessions and Discord IPC; no activity published.
.\CinePresence.exe --diagnose C:\temp\cinepresence-diagnostics.json
# Sample playback, then resolve using the saved TMDB token, without saved corrections.
.\CinePresence.exe --diagnose C:\temp\cinepresence-live.json --media-only --resolve --sample
```

The public Discord application ID is `1554495827219578880`. A development override is available in Settings or through `package.ps1 -DiscordApplicationId`. Never embed a shared TMDB token, Discord user token or client secret in a release.

See [validation](VALIDATION.md) for observed results and outstanding manual acceptance checks. Historical release notes live in `docs/releases`.
