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
