# B2 media - report

State: done. Branch `b2-media`, from `4c852a1`. Territory respected (only `src/Island.Sources.Media/`, `src/Island.Sources.Media.Smoke/`, `src/Island.Core/Media/`, `tests/Island.Tests/Media/`, this file).

## 1. What was built

Island.Core (pure, no clock, no I/O), folder `src/Island.Core/Media/`:
- `NowPlaying.cs` - `NowPlaying`: the model. `Update(now, sessions, tabs, tabsConnected, policy)` takes the whole picture and returns the line (`NowPlayingView?`); `Current(now)` returns the line as of a later time; `PlanFor(MediaCommand)` returns the command plan. Thread-safe (one lock).
- `NowPlayingTypes.cs` - `NowPlayingPolicy` (+ `ForMediaPagePicks`), `MediaTarget`/`MediaTargetKind`, `NowPlayingView`, `MediaCommandPlan` (+ `Send(IMediaControl, ITabControl)`).
- `MediaNames.cs` - `IsBrowserApp`, `App` (Spotify, Chrome...), `Site` (YouTube, YouTube Music...).

Island.Sources.Media (`net10.0-windows10.0.19041.0`, references Island.Core only, no NuGet):
- `MediaSessionReader.cs` - `MediaSessionReader : IMediaSessionSource, IDisposable`, own thread.
- `OutsideMedia.cs` - `OutsideMedia : IMediaControl`, the only file with TrySkip/TryToggle; asks `OutsideGate.Current.Allow(OutsideKind.MediaCommand)` first.

Island.Sources.Media.Smoke: console, no window, prints counts and booleans only.

Rules implemented (EVALS): N2 the item that most recently started or resumed wins (also across tab and desktop player; ties inside one report: highest tab `LastActiveOrder`, tabs over sessions); N3 all paused keeps the last item with `IsPaused`, nothing played gives null; N4 `PlanFor` gives target and command for the item only; N6 a title change in a report updates the line, no timer anywhere; N7 a tab counts only if its host matches a picked host (`SiteMatch`, so `www.` and case do not matter, `music.youtube.com` is not `youtube.com`); N11 `Progress` is null when length is null or 0 or position is null.
Extra rules (mine, cautious): (a) a source gone for less than `NowPlaying.VanishGrace` (4 s) keeps the line (flicker) but `CanControl` is false and `PlanFor` returns null; after the grace the line clears and the line goes to whatever else is playing; (b) when the held item pauses while another counted source still plays, the line follows the one that plays (the one that started last); both paused: the last item stays; (c) a browser session counts only when `policy.BrowserSessionCounts` and no add-on is connected (`tabsConnected == false`); with the add-on connected the browser session is ignored (tabs speak for it); (d) position moves on with the passed-in time while it plays, clamped to the length.

## 2. Commands run and results

- Baseline before my change: `dotnet test "<worktree>\tests\Island.Tests"` -> 248 passed.
- After: same command -> `Passed! Failed: 0, Passed: 285, Skipped: 0, Total: 285` (37 new: 15 `NowPlayingTests`, 22 `MediaNamesTests` cases). All guards (`OutsideGuardTests`, `GuardTests`) green with the new projects in `src/`.
- `dotnet build src\Island.Sources.Media` -> 0 warnings, 0 errors. `dotnet build src\Island.Sources.Media.Smoke` -> 0 warnings, 0 errors.
- `dotnet run --no-build --project src\Island.Sources.Media.Smoke` on this laptop (real sessions, read only), exit 0:
  `readFinished=True`, `managerCreated=True`, `sessions=1`, `browserSessions=1`, `sendReturned=False`, `gateRefusals=1`, `gateAllowed=0`.
  (One session exists and it is a browser's; nothing was sent to it: the self-test gate refused the one send, aimed at a made-up id.)
- Microsoft Learn re-opened 6 Oct 2026 (page dated `updated_at 2025-11-21`): `GlobalSystemMediaTransportControlsSessionManager` has `RequestAsync`, `GetSessions`, `GetCurrentSession`, events `SessionsChanged`, `CurrentSessionChanged`; `...Session` has `SourceAppUserModelId`, `TryGetMediaPropertiesAsync`, `GetPlaybackInfo`, `GetTimelineProperties`, `TryTogglePlayPauseAsync`, `TrySkipNextAsync`, `TrySkipPreviousAsync`, events `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`; Windows 10 1809+. Matches the research file; the code compiles against the 19041 projection, which proves the names.

## 3. Proven (named test or counted result)

- `NowPlayingTests.Most_Recent_Start_Or_Resume_Wins` (two tabs, back to the first, tab vs desktop player both ways), `Paused_Keeps_The_Last_Item`, `Nothing_Played_Shows_Nothing`, `Controls_Target_The_Now_Playing_Item` (session id for a player, tab key for a tab, delivered through `PretendWorld` to the right door), `Title_Change_Event_Updates_The_Line`, `Unpicked_Site_Does_Not_Take_Over`, `Unknown_Length_Shows_No_Progress`.
- More: `Desktop_Players_Only_Count_When_The_Policy_Says_So`, `Browser_Session_Counts_Only_Without_The_Addon_And_With_A_Site_Pick`, `A_Session_That_Vanishes_For_A_Moment_Does_Not_Blank_The_Line`, `A_Session_That_Disappears_For_Good_Clears_The_Line_After_The_Grace`, `A_Vanished_Item_Hands_The_Line_To_Whatever_Else_Is_Playing`, `When_The_Item_Pauses_The_Line_Follows_Another_That_Still_Plays`, `Known_Length_Gives_Progress_That_Moves_While_Playing_And_Stays_When_Paused`, `A_Missing_Title_Falls_Back_To_Where_It_Plays`, and `MediaNamesTests`.
- The reader can obtain the real session manager and read sessions without a window (smoke: `managerCreated=True`, `sessions=1`).
- `OutsideMedia.Send` is refused by the gate and sends nothing (smoke: `sendReturned=False`, `gateRefusals=1`).

## 4. Not proven

- Title/artist/state/position of a real player: the smoke prints counts only, by rule, so I never saw a field. Whether Spotify's desktop app reports its timeline (position, length) is UNVERIFIED; I treat a zero or negative `EndTime - StartTime` as "length unknown".
- `Position` is used as is (not minus `StartTime`); fine when StartTime is 0 (UNVERIFIED for other players).
- Events firing and the debounce under real use; the reader also reconciles every 2 s, which is what is actually relied on if events are lost. `TimelinePropertiesChanged` is not subscribed (a seek shows up at the next 2 s poll; position changes alone do not raise `Changed`).
- Real play/pause/next/previous: never sent (forbidden); `OutsideMedia` after the gate has not been run against any session. `Send` hands the command to a worker thread and returns true when the session was found; it cannot tell whether the player obeyed.
- Browser detection by id fragments: chrome, msedge, firefox's hash `308046b0af4a39cb` are from the research file; brave, opera, vivaldi are guesses (UNVERIFIED). Spotify's real `SourceAppUserModelId` is also unverified (the name mapping matches the text "spotify" anywhere in it, which covers `Spotify.exe` and the Store family).
- Two sessions with the same app id get ids `id`, `id#2` by list order; the order Windows returns them in is not guaranteed stable (rare case).
- Nobody has looked at a drawn Now-playing line.

## 5. Requests to the joints / main session

- None blocking. `IMediaControl.Send` is synchronous in the joint; the real one returns at once (work is on a worker thread), which is the behaviour wanted on the UI thread.
- `IMediaSessionSource.Changed` is raised on the reader's thread: the app must marshal to the UI thread itself.
- Wish for later (not needed now): `TabMedia`/`MediaSessionInfo` carrying an app/site display name would remove `MediaNames` guessing.
- Not added to `Island.sln` (rule); the main session adds `src\Island.Sources.Media` (and optionally `.Smoke`; it is not meant for the solution).

## 6. How to wire it into the app

Created once at start, on any thread:
```csharp
var reader = new MediaSessionReader();        // namespace Island.Sources.Media
reader.Start();                                // starts its own thread; idempotent
var media  = new OutsideMedia(reader);         // IMediaControl for the buttons (replaces PretendWorld's)
var nowPlaying = new NowPlaying();             // namespace Island.Core, one per app
```
On every change (reader.Changed, tab source Changed, picks changed, or a 1 s UI tick only to move the progress), on the UI thread (marshal from the reader's thread):
```csharp
var policy = NowPlayingPolicy.ForMediaPagePicks(mediaPagePicks);   // the Media page's Pick list
NowPlayingView? view = nowPlaying.Update(DateTimeOffset.UtcNow, reader.Sessions, tabs.Tabs, tabs.Connected, policy);
// Before the add-on exists: pass [] and false for the tabs (PretendWorld.Tabs / Connected until B4 is merged).
```
- `view == null`: show the selected pick as other pages do. Otherwise: first line `view.Title`, second line `view.SecondLine` ("Spotify" or "Spotify - paused"); middle button shows play when `view.IsPaused`; `view.Artist`, `view.Progress` (null = no progress; never draw one), `view.CanControl` (false: dim the buttons).
- Buttons: `nowPlaying.PlanFor(MediaCommand.Previous / PlayPause / Next)?.Send(media, tabControl)` where `tabControl` is the `ITabControl` (B4's, or `PretendWorld`); null plan: do nothing. After a click, no manual state change is needed: the player's own report arrives through `Changed`.
- Click on the line (N5): `view.Target.Kind == Tab` -> `tabControl.Activate(view.Target.Id)`; session -> bring its window forward by `view.SourceApp` (an app id / exe name, in memory only) through the existing outside layer.
- Display of "the whole browser" item: `view.IsBrowserSession` is true (title is whatever tab the browser last reported; where = "Chrome" etc.).
- `NowPlaying` is thread-safe; `MediaSessionReader.Sessions` may be read from any thread; call `reader.Dispose()` at app exit.
- Under `--selftest`: the gate already makes `OutsideMedia.Send` return false; to record only the session count use `reader.Sessions.Count`; never print titles.
