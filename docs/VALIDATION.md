# Validation status

The automated suite uses controlled HTTP responses and fake clocks; it does not require a TMDB token or publish test activity to Discord.

Covered: filename/episode parsing, remake ranking, original titles and accents, unrelated search results, music exclusion, first-playing source selection, simultaneous playback, pause handoff, resume without stealing selection, pinning/exclusions, VLC deduplication, stale lookup suppression, immediate clearing, pause/resume, seeks and playback speed, missing timelines, update coalescing, TMDB errors and backoff, offline cache, persistent corrections, DPAPI round trips, VLC authentication, polling failures, and recovery.

The WPF smoke command renders the actual UI with isolated settings, including a clearly labeled sample presence. Fixture screenshots are UI tests, not proof of real media identification. The diagnostics command checks Windows media-session access and Discord IPC without publishing an activity.

## Release acceptance checks

- [ ] Enter a real TMDB API Read Access Token; verify a movie and a series episode resolve with artwork and episode names.
- [ ] Inspect the actual Discord profile activity for Watching, timestamps/progress, artwork, and the TMDB button.
- [ ] Compare progress against the player; seek, pause/resume, change speed, and advance episodes.
- [ ] Test a browser site that provides title and timing, then one missing episode metadata; confirm the latter does not invent episode details.
- [ ] Play a video in an installed modern Windows Media Player or Movies & TV version and record fields exposed.
- [ ] Restart Discord during playback and confirm the activity recovers.
- [ ] Test source pinning/exclusions and sharing off while a lookup is pending.
- [ ] Validate tray reopen/quit and opt-in login startup under a normal Windows account.
- [ ] Check popup placement on monitors with mixed DPI and taskbar positions; test clicking Change match during a media change and hovering to extend its lifetime.
- [ ] Extract the final self-contained ZIP on a clean Windows 11 x64 account without .NET, enter that account's credentials, and repeat onboarding.

These checks must not be marked complete solely because simulated tests pass. See the implementation report below for observed results on the build machine.

## Observed results

- The initial release passed 45 automated tests, a self-contained ZIP extraction/launch test, and WPF render checks. A live isolated VLC 3.0.23 instance passed playback, pause, seek, resume, 2× speed, and stop detection through HTTP. Discord IPC connected using the supplied application ID. These checks did not establish real browser/Windows-player metadata compatibility.
- User testing then exposed a real Windows metadata issue: both VLC video and Edge video were labeled Music by their SMTC integrations, causing the original implementation to skip them. Version 0.1.1 removes that assumption across all Windows sources and preserves known-audio exclusions. Regression cases cover multiple players, movies, series, and both filename and browser episode formats.
- Version 0.1.1 adds a conservative native caption fallback for empty/generic browser metadata; multiple ambiguous windows are not silently matched. The browser still needs to expose playback state/timing through Windows.
- A live media-only diagnostic with the user's saved token identified Edge's session as TMDB series 95350, Lanterns, Season 1 Episode 7, “The Jordan Boys' Legacy,” with artwork, player position, and duration. It recovered the title from the selected tab despite another unrelated browser window. The corrected classifier no longer marks the video as music. The session was paused and intentionally not published.
- All 68 automated cases passed after the 0.1.1 detection and first-playing source selection fixes. Tests include matching browser/filename episode keys, generic media metadata, episode enrichment from a matching window caption, competing sources, pause/stop/closure handoff, no takeover on resume, and switching between duplicate VLC adapters.
- Version 0.1.2 identified the user's playing live Edge source from its page title, “Watch WWE NXT for free on …,” as TMDB TV series 31991 (WWE NXT, 2010), with artwork and no invented episode. This diagnostic used an empty in-memory cache, not the user's saved correction. Sampling for ten seconds also detected its growing buffer as live (the player reported a finite buffer duration), so the generated presence omits end/start timestamps.
- The 0.1.2 WPF smoke render covers the live preview and watching popup, and invokes the real popup button to verify it passes the captured title into its correction action. Automated cases cover live title cleanup across shows/sites, ordinary titles containing “Live,” moving/fixed timelines, date-stable correction keys, notification deduplication, disable/pause behavior, and captured correction targets. Mixed-DPI placement and the real Discord layout remain manual checks.
- Version 0.1.3 adds a conservative browser eligibility guard before matching/cache lookup: generic page names and excluded social/video platforms never publish, ambiguous browser titles require explicit correction, and unassociated browser windows are not silently attributed. Regression cases cover the Facebook/movie-name collision, real episode titles on excluded platforms, allowed movie/episode/live-show sites, cache vs correction authorization, and immediate clearing/no popup on transition into a feed. The popup is reduced to 356px wide, includes artwork, and animates in/out with a seven-second timer.
- All 124 tests passed for 0.1.3. The WPF smoke run rendered the actual popup with a locally supplied TMDB poster, exercised its animated correction button, and measured auto-dismissal at 7.31 seconds. No current media sessions were exposed during the attempted live feed check; platform blocking is verified by regression tests, not a claimed live Facebook playback test.
- Actual Windows built-in player playback, a full clean-machine installation, login startup, and the final Discord profile rendering remain manual acceptance checks. A successful IPC handshake alone does not establish that activity sharing is enabled or that Discord rendered the progress bar.

## 0.2.0 browser companion and installer

- Fixed a 0.1.3 regression: generic browser media metadata could be rejected because a separate excluded-site window was open. Tests run the actual shared adapter decision path with a Regular Show S6/E13 caption and unrelated YouTube/Facebook windows. Explicit excluded metadata, competing show captions and multiple anonymous sessions remain unshared. The live diagnostic during development found no current Windows media sessions; this regression is covered by tests, not a claimed new live-site verification.
- Also found the user was still running the original `0.1.0` extracted executable. The installer provides a stable per-user installation and shortcut so launching an old extracted folder does not hide the newer fixes.
- 147 .NET cases pass, including browser structured identification, top-level and embedded platform exclusions, manual-title restrictions, unknown metadata, Windows duplicate suppression, first-playing selection/pause handoff, live buffer detection, expiry/disconnect, malformed frame handling and a real Windows named-pipe round trip.
- 12 Node tests cover the extension's metadata extraction and worker message flow: parent-page plus iframe metadata, vague tab titles, structured episode names, movie release years, excluded domains, private windows, playback changes, temporary manual overrides and late messages after closure. These tests use simulated browser messages/DOM; they are not proof that the extension has been loaded into Chrome/Edge or works on every live streaming site.
- The packaged native host passed a real process test under the desktop Windows account: fragmented stdin, UTF-8 characters, named-pipe forwarding, framed stdout response and disconnect notification. The sandbox account could not connect to the desktop-account pipe, consistent with the same-user boundary. No test presence was published.
- The self-contained WPF smoke test passed with poster artwork and its correction action; popup auto-dismissal measured 7.29 seconds. Settings include companion setup and extension-folder actions.
- The installer was installed into an isolated workspace folder on the existing Windows account. App/extension files, native-host manifest and both HKCU Chrome/Edge registrations were verified. Uninstall removed the test executable and its owned registrations while preserving the existing settings directory. This is not a clean-account or clean-machine test.
- The installer was then installed normally under the user's local Programs directory and replaced the running 0.1.0 process. The settings file hash was unchanged. The installed 0.2.0 executable stayed running and its media-only diagnostic completed; Windows exposed no media sessions during that check. The companion guide was opened, but browser loading is a separate user-controlled step.
- Browser-store publication and one-click store installation are not complete: the companion is currently an unpacked preview. A real Chrome/Edge load, playback on the user's streaming sites, private-window behavior in the real browser, and the final Discord activity remain manual acceptance checks. The installer does not change enterprise policies or bypass browser approval.

## 0.2.1 matching and correction UI

- Strict TypeScript now drives the companion. Tests compile and execute its generated runtime. 17 browser tests include generic movie/TV routes, scoped episode labels, movie release years, iframe forwarding and recommendation-text rejection.
- 155 .NET tests pass. New cases cover verified direct TMDB identities for same-named series/movies, unrelated or missing URL IDs falling back to typed search, remake ambiguity, corroborating page evidence, hint-aware cache keys, offline corrections and changed lookup identity. Earlier source competition, pause clearing and exclusion cases remain in the suite.
- Read-only live TMDB checks with the saved credential verified TV 95350 as Lanterns S1E7 / The Jordan Boys' Legacy, TV 4656 as Raw S34E14 / April 6, 2026, and movie 1368337 as The Odyssey (2026). These establish the supplied IDs and episode metadata, not a claim of live DOM-to-Discord validation on those pages.
- Actual WPF smoke rendering exercised the popup-to-poster-picker fade, preserved its correction target and rendered the poster list with episode fields. Normal popup dismissal measured 7.55 seconds. Editing keeps the picker open; closing it cancels pending searches/artwork.
- Older automatic cache entries are re-evaluated under the new matcher. Explicit saved corrections remain stored. Numeric URL IDs must agree with TMDB identity; titles that remain ambiguous are not guessed.
- User screenshots confirm the installed 0.2.0 companion connected in Edge and supplied video timelines, while exposing the incorrect matches addressed here. Live playback after reloading the 0.2.1 extension, final Discord rendering, multi-monitor placement and clean-account installation remain manual acceptance checks.

## 0.2.2 installer handoff

The 0.2.1 upgrade check exposed an installer race: the browser respawned its native helper after Restart Manager closed it, locking the executable. Publication was stopped. The installer now suspends only its own native-host registrations before checking open files and restores them after completion or cancellation, preventing this reconnect race without closing the browser. Source tag 0.2.1 is retained; the corrected release is 0.2.2.

The installer handoff integration check passed on the existing Windows account with a native helper held open and a simulated browser reconnect loop. Restart Manager detected and closed the helper; both owned registrations were restored, the settings hash was unchanged, and setup exited 0 without a reboot. This is an upgrade test, not a clean-machine test.

## 0.2.3 Spotify exclusion

Spotify web domains and embeds are filtered in both the TypeScript worker path and desktop protocol policy. Native browser labels/domain metadata are excluded, while the desktop Spotify source filter remains active. 166 .NET and 18 compiled-companion tests passed, including manual-title resistance, real episode-like metadata on excluded hosts, desktop identities, and unrelated hostname suffixes. No live Spotify-to-Discord test is claimed.

The setup-page helper and Node test tools are now TypeScript. Both runtime sources and development tools pass strict type checking; the companion suite runs from `.mts` files on Node 24 against compiled runtime output. README screenshots and relative guide links were checked. Browser runtime output remains generated JavaScript.

## 0.2.4 browser handoff recovery

- 167 .NET tests and 25 TypeScript companion tests pass, with strict TypeScript checking and no build warnings/errors. The desktop integration test sends two titles from the same browser through `BrowserAdapter` and `PresenceEngine`: the first playing tab stays selected, pausing it transfers presence, resuming it does not steal presence back, and pausing/closing all tabs clears presence.
- The previous 0.2.3 content script fails the new transient-message regression: after a failed pause report it permanently stops sampling and the last reported state remains Playing. The fixed script retries and reports Paused. Other fixtures cover dynamically mounted players, tab activation/loading, worker rediscovery, stale background samples, cached-document races and history restoration.
- The full suite retains Spotify, social/video platform, private-window, manual-title and iframe restrictions. Browser permissions and source-selection policy are unchanged.
- NuGet's package and vulnerability-only endpoints both timed out during this build. The successful build used the existing cached packages with auditing disabled for that invocation only; no dependency versions or repository audit settings were changed. A fresh NuGet vulnerability check was unavailable.
- Live playback on the user's streaming sites and the final Discord activity remain manual acceptance checks after reloading the companion and refreshing playback tabs. The tests exercise compiled extension scripts and the desktop pipeline; they do not claim a live browser-to-Discord observation.
