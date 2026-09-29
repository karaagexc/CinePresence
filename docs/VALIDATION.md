# Validation status

The automated suite uses controlled HTTP responses and fake clocks; it does not require a TMDB token or publish test activity to Discord.

Covered: filename/episode parsing, remake ranking, original titles and accents, unrelated search results, music exclusion, source priority and pinning, VLC deduplication, stale lookup suppression, immediate clearing, pause/resume, seeks and playback speed, missing timelines, update coalescing, TMDB errors and backoff, offline cache, persistent corrections, DPAPI round trips, VLC authentication, polling failures, and recovery.

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
- [ ] Extract the final self-contained ZIP on a clean Windows 11 x64 account without .NET, enter that account's credentials, and repeat onboarding.

These checks must not be marked complete solely because simulated tests pass. See the implementation report below for observed results on the build machine.

## Observed results

Results are updated after the final build and packaging checks.
