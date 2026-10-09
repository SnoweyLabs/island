# CODE attacker, round 1 (WORK-ORDER-12 section 4)

Branch `rt-code`, made from `a66c137`. Project `tests/Island.Redteam.Code.Tests/` (in the solution before I started). Nothing under `src/` or `extension/` was changed; no existing assertion was touched. Island.App was never started, no window shown, no
terminal or helper started, nothing under the real `.claude`/`.codex`/`.gemini`/`.agents` opened; every name in the tests is invented (`Alpha`, `alpha.exe`, `example.org`, `Q:\Invented\...`); the real pipe's name is spelled in no test (a guard checks that: I
fixed my own first draft which did). The only network use was one official Microsoft Learn page (`CompositionGeometry.TrimOffset`): it says nothing about range or wrapping, so the compositor facts stay UNVERIFIED.

## 1. Summary

Totals: 22 findings, **0 HIGH, 5 MEDIUM, 13 LOW, 4 LATENT** (nothing reaches a LATENT one today). 20 `Defect_` tests are red on purpose (one or more per finding where a test can show it; 1-18 and 1-19 to 1-22 are by reading only, marked).
Class is my suggestion; the main session decides by "the one rule".

| id | grade | suggested class | area | one line |
|---|---|---|---|---|
| code-1-1 | LATENT | defect | LightClock | One NaN time poisons the held place: every later time returns NaN while only the edge moves |
| code-1-2 | LATENT | safe improvement | ArcClock, CompositorPath | A tiny negative time gives exactly 1.0, not a fraction below 1 |
| code-1-3 | MEDIUM | defect | Settings.Save | settings.json is written in place; pages, picks and scenes are written by temporary file and move |
| code-1-4 | LOW | defect | PageStore.Save | A failed save leaves `pages.json.tmp` behind (PickStore deletes it since attack WO5) |
| code-1-5 | LOW | defect | SceneStore.Save | The same for `scenes.json.tmp` |
| code-1-6 | LOW | safe improvement | the three stores | The first sharing violation of a short-lived reader loses the save; no retry |
| code-1-7 | MEDIUM | defect | add-on frames | The add-on cuts a title to 200 characters (code points); the island accepts 200 UTF-16 units: one emoji refuses the whole snapshot |
| code-1-8 | LOW | defect | AgentPipeServer | The number of connections held at once is not bounded, against its own class text |
| code-1-9 | LOW | defect | NoticeQueue, AgentNoticeHost | The notice's deadline is a wall-clock moment: a clock stepped back keeps it on screen |
| code-1-10 | LATENT | safe improvement | UiWatchdog | A reading before Start is the computer's uptime |
| code-1-11 | MEDIUM | defect | SettingsScreen | Alt+F4 (any close but Finish) leaves the screen "open" for ever: Settings and the setup never open again; the frame handler stays on |
| code-1-12 | LOW | defect | TypedSite | `example.org/go?u=https://x.example` is refused as "not a site" |
| code-1-13 | MEDIUM | defect | IconCache, TerminalsPage | A failed or timed-out icon read is kept as null for the rest of the run |
| code-1-14 | MEDIUM | defect | App.OnStartup | A throw in `AppHost.Start` leaves a window-less process holding the single-instance lock |
| code-1-15 | LOW | proposal (a look) | LightSpec, MovingLight | The old rim has a 0.6 px blur of its own; the graphics-card light has none |
| code-1-16 | LOW | defect | PageStore, Pick | Page and pick names are not held to the rule scenes keep: invisible, reversing, control, half characters |
| code-1-17 | LOW | defect (text) | OutsideAgentConnector, OutsideCodexConnector | A refused Notify path is told as "settings file could not be read" |
| code-1-18 | LOW | defect or proposal | MovingLight, GlassWindow | By reading only: z-order after the capsule is raised, a window inserted after itself, DPI while shown, no catch if the compositor fails later (UNVERIFIED, a person must look) |
| code-1-19 | LOW | safe improvement | App.xaml.cs, AppFiles, TrayIcon | A repeating unhandled exception writes a line per frame to an unbounded log; refusals queue without a cap (reading only) |
| code-1-20 | LATENT | safe improvement | TabBridge.AcceptLoop | `catch (SocketException) { continue; }` with no pause can spin (reading only) |
| code-1-21 | LOW | proposal | pipe name, loopback ports | On a machine with two accounts logged in, one user's add-on can reach the other user's island (reading only) |
| code-1-22 | LOW | safe improvement | IslandController, MovingLight | Every mouse move over the island draws a whole frame outside the budget; `Follow` reads the compositor back on each call (reading only) |

## 2. The findings

Test names are `Class.Method` in `tests/Island.Redteam.Code.Tests/`.

### MEDIUM

**code-1-3 - settings.json is not written the way the other three stores are.** `src/Island.Core/Settings.cs`, `Save`: `File.WriteAllText(path, ToJson())` straight onto the live file (truncates first). `PageStore.Save`, `PickStore.Save`, `SceneStore.Save` write
`<name>.tmp` and `File.Move(..., overwrite: true)`. Settings.Save is what the tray's mode and glass, the settings screen and the first-start run. A reader (an editor, a backup or sync program, or the next start after a power cut) can meet an empty or cut file;
a file cut anywhere is Unreadable (the test `Every_Proper_Prefix_Of_Every_File_Is_Unreadable...` shows all 4 files refuse every prefix, and a zero-filled tail as NTFS leaves after a crash), so every key, the mode and the idle time fall back to the defaults with SETTINGS_UNREADABLE, whose own text promises "nothing you wrote is lost" (the file is left; the person must mend it by hand).
Test: `FileEdgeTests.Defect_Settings_Save_Writes_The_Live_File_In_Place_...`: a handle opened before the save still reads the old, complete text after an atomic replace; after Settings.Save it reads the new text of the same file object. Probability is small (a few hundred bytes, microseconds), hence MEDIUM not HIGH.
Repair: the same temporary file and move as `PageStore.Save` (and 1-4 and 1-6 with it).

**code-1-7 - an emoji in a long title spoils the whole add-on snapshot.** `extension/lib/protocol.js` `cut()` is `Array.from(text).slice(0, 200)` (200 code points) and `extension/PROTOCOL.md` says "title (text, at most 200 chars)"; `src/Island.Core/Tabs/TabMessages.cs` `Str` and
`NullableStr` test `s.Length <= 200` (UTF-16 units; an emoji is two). A page title of 200 characters or more with one emoji (a long post's or article's title) goes out as 200 code points = 201 units; the island answers "bad field". For a snapshot the whole list of tabs
is refused ("one bad tab spoils the frame"), for a tab frame that tab is never known, ten in a row close the connection. The same two counts for the media title and artist (`media.js` `MAX_TEXT`). Tests: `InputEdgeTests.Defect_A_Title_The_Addon_Cut_To_Two_Hundred_Characters_...` (tab: "bad field in tab"; snapshot: "bad field in snapshot")
and `...Defect_A_Media_Title_...` ("bad field in media"); `A_Title_Of_Two_Hundred_Plain_Characters_Is_Taken_And_One_Of_Two_Hundred_And_One_Is_Not` holds. Repair: count code points on the island's side (`EnumerateRunes`), or accept up to 400 units, or cut instead of refusing.

**code-1-11 - Alt+F4 in the settings screen leaves it open for ever.** `src/Island.App/SettingsScreen.cs`: `ScreenWindow` (no title bar, `Topmost`) handles only Esc (`PreviewKeyDown`). The only code that ends the screen is `Finish()`: it removes `CompositionTarget.Rendering -= OnFrame`, sets Phase Closed,
gives the keyboard back and raises `Gone`; `SettingsScreen.Open` clears `_window` only in `Gone`, and its first line is `if (_window is not null) return;`. Nothing handles `Closed`, `Closing` or `OnClosed`. A WPF window closes by itself on Alt+F4 (or any WM_CLOSE): then
`_window` stays set, the tray's Settings and "Run the setup again" never open again until Island is restarted, `OnFrame` keeps running on the dead window on every frame, and the keyboard is not given back. The first-start steps use the same window. UNVERIFIED by running (no window may be shown here).
Test: `SourceFindingTests.Defect_Settings_Screen_Window_Closed_By_Alt_F4_...` (the source has no handler at all). Repair: override `OnClosed` to run what `Finish()` runs (idempotent via `_gone`), or turn Alt+F4 into `BeginClose()`.

**code-1-13 - a failed icon read is remembered for the run.** `src/Island.App/PickPages.cs` `IconCache.Fetch`: `_icons[pick.Id] = icon;` whatever came back (null included), `Get` trusts the entry and `_requested` stops a second ask; `TerminalsPage.Fetch` does the same (`_icons[key] = icon`), `SecondRow.IconOf` too.
`ProgramIcons.ProgramIcon` took care not to cache a miss ("the catalog may not have been read yet") and its single STA thread gives up after 8 s (`OnOwnThread`), so the layer above defeats it. Two ways to a null: (a) the first draw happens at start-up (`IslandRuntime`'s constructor makes the first page's rows,
`IslandMachine`'s constructor reads `ContentsItems`), a few hundred ms after `InstalledProgramCatalog`'s thread began, and nothing subscribes to its `Loaded` event: a program not in the Windows folders is looked up in an empty catalog (UNVERIFIED race on this laptop); (b) a hand-made
place on an offline network drive holds the one icon thread (a UNC `ReadTarget` can block for the share's time-out) and every other ask times out at 8 s. The tiles then show their two letters until Island restarts. Test: `SourceFindingTests.Defect_IconCache_Remembers_...` (source only). Repair: do not store a null (or store it with a time and ask again after a while), and raise a redraw when `Catalog.Loaded` fires.

**code-1-14 - a failed start leaves a window-less process holding the lock.** `src/Island.App/App.xaml.cs`: `OnStartup` calls `AppHost.Start(...)` in no `try`; `Start` takes the single-instance mutex first, then builds the world, windows, tray and pipe. A throw after that reaches the `DispatcherUnhandledException`
handler, which sets `args.Handled = true` and writes through `_host?.Files.Log(...)` where `_host` is still null: nothing is logged, the process does not exit (`ShutdownMode.OnExplicitShutdown`), no tray icon, no window. The mutex stays held, so `run.cmd` says already running, and `stop.cmd`'s event has no subscriber yet
(`single.QuitRequested` is wired at the end of `Start`). Only Task Manager ends it. Rare (needs an exception in start-up), hence not HIGH. Test: `SourceFindingTests.Defect_A_Throw_In_AppHost_Start_...` (source only). Repair: `try`/`catch` round `AppHost.Start` (log the type, `Shutdown(code)`), or `Shutdown` in the handler while `_host` is null.

### LOW

**code-1-4 / code-1-5 - a failed save leaves the temporary file.** `PickStore.Save` deletes `<name>.tmp` when the move fails (attack WO5 A5-13: "the list that was refused must not stay behind in a file of its own"); `PageStore.Save` and `SceneStore.Save` never got it.
Input: the file read-only (or held) -> Save returns false and `pages.json.tmp` / `scenes.json.tmp` stays with the whole refused list. Tests: `FileEdgeTests.Defect_Pages_Failed_Save_Leaves_A_Temporary_File_...`, `...Defect_Scenes_Failed_Save_...`; `Picks_Failed_Save_Leaves_No_Temporary_File_Behind` holds. Repair: the same `try { Move } catch { Delete(temp); throw; }`.

**code-1-6 - no retry on a transient sharing violation.** The three atomic stores move once. A program that has the live file open for a moment (a scanner, an indexer, a backup, an editor) makes `File.Move` fail; Save says false and the change lives in memory only (a pick dragged off is only logged, `book.SaveFailed`, and is back after the next start; the settings screen shows "could not be saved"). Test:
`FileEdgeTests.Defect_Stores_Give_Up_At_The_First_Sharing_Violation_...` (a reader that lets go after 150 ms: all three stores return false). Repair: a bounded retry (for example 3 times, 50 ms apart) and delete the temporary file at the end.

**code-1-8 - the pipe server does not bound what it holds.** `src/Island.Agents/AgentPipeServer.cs` class text: "a fixed number of acceptors each take one connection at a time ... later clients wait their turn ... nothing grows". `Accept` now hands each connection to `Task.Run(Receive)` and makes the next instance at once. 400 clients that connect and say nothing: all 400 connect at once (Acceptors is 64);
each costs a pipe instance, a task and a 4 KB buffer for the first-byte limit (1 s). Only the same Windows user can open the pipe (deny network, allow the user's SID). Test: `InputEdgeTests.Defect_Pipe_The_Number_Of_Connections_Held_At_Once_...` ("400 of 400 silent clients were all held at once"). Repair: a `SemaphoreSlim` of 64 (or so) round `Receive`, or correct the text.
Held beside it: 150 clients delivering at once all arrive; garbage, 10 KB, 5000 braces, a bare newline and 20 silent clients do not stop a good one.

**code-1-9 - the notice's clock is the wall clock.** `AgentNoticeHost` passes `DateTimeOffset.UtcNow` to `NoticeQueue.Post/Update`; every other timer of the island is steady. The deadline is a wall-clock moment, so after a step back of one hour (a time sync, a manual change, a resumed laptop with a wrong clock) a notice due in 6 s is still shown
an hour later, over whatever is open, until clicked; `NoticeFallback` says "a clock that went backwards is never stale" and a waiting notice waits for the clock to come back. Test: `InputEdgeTests.Defect_NoticeQueue_A_Wall_Clock_That_Steps_Back_...`. Repair: the queue treats a time earlier than the one it started from as a restart, or the host passes a steady clock (`Stopwatch`).

**code-1-12 - a bare address with a web address in its query is refused.** `src/Island.Core/Picks/TypedSite.cs` `HostOf`: the first `://` anywhere is taken as the end of a scheme. `example.org/go?u=https://other.example` -> "scheme" `example.org/go?u=https`, not http(s), so NOT_A_SITE although `https://example.org/go?u=...` and `example.org/page?x#y` work.
Test: `TypedInputTests.Defect_TypedSite_A_Bare_Address_...`. Repair: look for `://` only before the first of `/ ? # @ :`.

**code-1-15 - the rim's own blur is not in the light's numbers (a look: needs a person).** `src/Island.App/Visuals/Layers.cs` `RimLayer()`: `Effect = Units.Blur(Units.RadiusForFilterBlur(LookConstants.FrontRimBlurCss))` (0.6): the whole old rim (base line and both arcs) is softened. `LightSpec` (whose text says its numbers are the ones the old drawing gives its pens) has no such number and `MovingLight` draws the rim sharp;
`LightStage` compares only the pens. So the graphics-card rim is a little sharper than "As before". Test (a statement of source): `DuplicatedRuleTests.Defect_The_Old_Rim_Has_A_Blur_...` fails until `LightSpec.cs` names the constant. Either carry it (and give the compositor an equivalent) or record it for Dan under OWNER DECISIONS; every other number of the light was compared against `Layers.cs` and `LookConstants` and agrees (see section 3).

**code-1-16 - page and pick names are not held to the rule scenes keep.** `SceneStore.CheckName` refuses a hidden, control, private-use or unassigned character, a lone surrogate (it reads as U+FFFD) and a name that draws as nothing, after attack WO7 (A7A-12/13); its comment says it follows "the way PageStore tidies a page name". `PageStore.CheckName` only trims, collapses white space and counts: a page named U+200B, U+3164 or
U+0301 (draws as nothing), with U+202E (reverses the text round it) or with a control character is accepted. A lone surrogate is accepted too, written as U+FFFD; two pages `a`+U+D800 and `a`+U+FFFD are two names in memory and one in the file, and the next start refuses the whole pages file ("Two pages have the same name"). `Pick.IsStorable` asks only that the name is not blank: a lone surrogate comes back as another name.
Tests: `StoreRoundTripTests.Defect_PageStore_Accepts_A_Name_That_Draws_As_Nothing_...`, `...Defect_PageStore_A_Name_With_A_Lone_Surrogate_...`, `...Defect_Pick_A_Name_With_A_Lone_Surrogate_...`. Repair: one shared name rule (`SceneStore`'s) used by pages and picks.

**code-1-17 - a refused path is told as an unreadable file.** `HookInstaller.Connect`/`CodexHooks.Connect` return `NOTIFY_PATH_INVALID` for a command that is not an absolute local path of Island.Notify (Codex also refuses a quote, %, $, backtick or control character, because its entry is one shell line; a profile folder with `%` or `$` in its name gives such a path). `OutsideClaudeSettings.cs` and
`OutsideCodexSettings.cs` turn any `Reason` into `AgentRefusals.HooksFileUnreadable` ("settings file could not be read ... Open the file, fix it"): false, the file was not read. Test: `InstallerEdgeTests.Defect_A_Notify_Path_...` (the two connectors never name `NotifyPathInvalid`). Repair: a refusal of its own with its three parts (or NOTIFY_MISSING's words).

**code-1-18 - the light's windows: four things by reading, none runnable here (UNVERIFIED).** `src/Island.Glass/MovingLight.cs` and `GlassWindow.cs`. (a) The two windows are placed only in `Show` (`PlaceAbove`); `Follow` never places them. `KeyboardFocus.Take` (a click into the search field after the keyboard was lost, `TakeKeyboard`) calls `SetForegroundWindow` on the capsule, which raises it to the top of the topmost band, above the rim window: the lit rim would go under the glass until the island leaves.
(b) After a `Hide`, the hidden rim window is still directly above the anchor, so on the next `Show` `GetWindow(anchor, GW_HWNDPREV)` returns the rim window itself and `SetWindowPos(rim, rim, ..., SWP_SHOWWINDOW)` asks to insert a window after itself (the glow the same, after the shadow); what Windows does is not on Learn; if it refuses, the move and the show are lost and the light is missing after the first pill, notice or lifted tile. `LightStage` checks `IsShown` after the re-show, not `IsWindowVisible` or the z-order.
(c) Size, place and scale are read once in `Show`; a DPI change while the island is open (the island changes screen only while hidden, `ScreenPicker`) leaves the two windows and `_scale` stale until the next summon. (d) `Show`/`Follow`/`Hide` catch nothing after the constructor; a `COMException` later (device lost) would escape `Draw` at `UpdateMovingLight`, before `UpdateFrameSource`, on every frame or beat.
Repairs (all cheap and safe): in `PlaceAbove` skip the insert-after (`SWP_NOZORDER`) when `above == window`, and re-place when the window directly above the anchor is not the rim (checked in `Follow` at rest, a `GetWindow` per beat); read the rect again when `WM_DPICHANGED` arrives; catch `COMException` in `Follow`/`Show`, record `DEVICE_LOST` and let `IsAvailable` go false (the controller already falls back to the old drawing). `SourceFindingTests.The_Light_Windows_Are_Placed_With_Windows_Own_Insert_After_Rule_Only_In_Show` passes: it only records where the windows are placed.
For a person: after Ctrl+Q, click search without the keyboard, does the rim stay? After a notice or a tile lifted and let go, is the light back?

**code-1-19 - a repeating exception floods the log and the tray (reading only).** `App.xaml.cs` writes a line for every unhandled exception and sets `Handled`; an exception thrown at every frame (165 a second) writes about 16 KB a second to `island.log`, which `AppFiles.Log` appends without a size limit (open, append, close per line; attack WO9 B3 named the same for refusals); `TrayIcon._pending` takes refusals without a cap and shows one balloon every 7 s (ten copies started by hand give ten `ALREADY_RUNNING` balloons, a minute and a half). Repair: log a repeated exception type once per few seconds with a count; rotate the log at a size; collapse identical queued refusals.

**code-1-21 - two Windows accounts on one machine (reading only, a proposal).** The pipe name is one fixed name for the machine (the DACL keeps the other user out; the second user's server fails with "the name is taken" and has no notices); the add-on dials the first of five loopback ports that answers and the handshake checks only the Origin, which any local program can send. With two accounts logged in, user B's add-on can be answered by user A's island: A's island then holds B's tab titles and hosts (the + list shows open sites).
Nobody on one personal laptop meets this; the shop version may. Repair belongs to a design choice (a per-user name and port, or a token the add-on gets from the app).

**code-1-22 - the mouse draws outside the budget (reading only).** `OnMouse` -> `Controller.Activity()` -> `Input(...)` -> `Draw(now)`: every mouse move over the island draws a whole frame at the mouse's own message rate (a gaming mouse: hundreds a second), not through `FrameBudget`, and in the quiet states (Do not disturb, the graphics-card light) it is the only drawing there is; `MovingLight.Follow` reads five brush colours, five thicknesses, three trims and the glow opacity back from the compositor on every call to find out nothing changed, where `GlassLayer.Apply` compares managed copies. Not measured (no compositor here).
Repair: `Activity` pokes the machine and draws only when something changed (or admits the draw through the budget); keep the last applied values in `MovingLight`.

### LATENT

**code-1-1 - one NaN time poisons the half-rate light.** `LightClock.Head(NaN, HalfRate, 165, true)` stores `_heldAtMs = NaN` and `_held = NaN`; the next ordinary time then sees `nowMs - NaN >= two intervals` false and returns NaN, for as long as only the edge moves. The controller's clock is a Stopwatch, so nothing reaches it today. Test: `ClockEdgeTests.Defect_LightClock_A_NaN_Time_...`. Repair: `if (!double.IsFinite(nowMs)) return _held` before touching the state, or do not store a non-finite time.

**code-1-2 - `f - Math.Floor(f)` is 1.0 for a tiny negative f.** `ArcClock.Head(-1e-20)` = 1.0 (and the same wrap in `CompositorPath.Wrap`); the text says 0 up to 1. Harmless to the drawing (0 and 1 are one place). Test: `ClockEdgeTests.Defect_ArcClock_A_Tiny_Negative_Time_...`. Repair: `if (r >= 1) r = 0`.

**code-1-10 - `UiWatchdog.LongestSilenceMs` before `Start`** reads "pending = now - _sentAt" with `_sentAt` 0: the computer's uptime (137,283,004 ms here). `SpeedStage` reads only after Start. Test: `SpeedEdgeTests.Defect_Watchdog_A_Reading_Before_Start_...`. Repair: `_replied` starts set.

**code-1-20 - `TabBridge.AcceptLoop`:** `catch (SocketException) { continue; }` with no pause; if `AcceptSocketAsync` keeps throwing (handles used up, a listener in a bad state) the loop spins a core. `AgentPipeServer` pauses 200 ms in the same case. Repair: `await Task.Delay(100, token)` before `continue`.

## 3. What held (the coverage statement)

Every test below passes.

- **Clocks of the new code** (`ClockEdgeTests`): `FrameBudget` with NaN, infinities, negatives, 1e300, 2^53 and every interval odd value never throws and works again after them; over random call streams much faster than the screen the frames admitted in any window never exceed the refreshes plus 1.5; a steady stream that matches the screen with plus or minus 1 ms of jitter loses under 1% of its frames; a clock going back adds nothing. `LightClock` holds a place for two refresh intervals and never longer, never holds for the other kinds, without a rate or while something else moves, and recovers from infinity and from a far step back. `ArcClock` stays in [0,1) for 200,000 times.
  `CompositorPath` offsets keep the arcs' distances at eight capsule sizes (zero size included); `RimOutline` gives finite numbers for zero, negative, tiny and huge sizes; `LightSpec` is null exactly where the old drawing draws something else (Do not disturb, and half way through the cross-fade into it); `ModeMark.Breath` is periodic as the compositor keyframes assume.
- **The machine** (`MachineClockTests`): every input ignores NaN and infinity and changes nothing; 2^53, 1e17, 1e21 and 1e300 start times end (the island leaves after its idle time, no loop); eight hours' sleep with the island open wakes to a hidden island that summons again; a clock stepped back never throws and the island still leaves; 400 random runs of 150 inputs at random, sometimes backward, times keep every drawn number finite; `Spring` catches up from any stall in bounded work.
- **Files** (`FileEdgeTests`): `movingLight` accepts the three names in any case, refuses 12 wrong values naming the field and changing nothing else, an old file loads with the default and the next save writes the field, the unknown enum is never written, the session refuses an unknown kind and the unavailable card without touching the file; every proper prefix of every file is Unreadable and never throws; a zero-filled tail, deep nesting, odd roots, a 40 MB unknown field (loads in time), a directory in the way, a read-only file: plain answers; a newer schema is refused by all four stores with the newer words and the file is never written; round trips of random settings, pages, picks and scenes (`StoreRoundTripTests`, 400 to 800 steps each) come back equal.
- **Outside** (`InputEdgeTests`, `TypedInputTests`): both wires encode within 4095 bytes with every field full of the widest characters; 40,000 fuzzed messages, 30,000 argument lists, 30,000 hook inputs and 40,000 add-on frames never throw and what they accept is inside every limit; the stdin limit and the cut-off first fields hold; the add-on's constants (ports, path, client name, 200, 64 KB, 20 s, five media hosts, 1 MB, 2000, 8 connections) agree with `TabProtocol`, and the accepted Origin is exactly the id Chrome derives from the manifest's key; the pipe takes 150 clients at once and a good message after garbage, an oversized one and 20 silent clients; 60,000 random typed texts through `TypedSite`, `PickPath` and `SearchMatch` never throw and what is accepted is stable when understood again; 50 million characters are refused unread; page and scene names collapse spacing and compare alike.
- **Threads** (`ThreadEdgeTests`): the session book (4 writers, a process reader, a drawing reader), the tab model (4 connections writing, the drawing reading), the target cache (with a probe that throws and stalls) and the now-playing line from several threads: no exception, ids unique, limits kept.
- **Installers** (`InstallerEdgeTests`): 24 odd texts (BOM, comments, null, arrays, deep nesting, a repeated key, a lone surrogate) give a plain answer and unreadable text comes back unchanged; connect then disconnect gives the user's own file back equal (DeepEquals); nothing is added twice; a 200,000-entry file is edited in about a second; a path that is not Island.Notify's own absolute local path is never written.
- **Speed code** (`SpeedEdgeTests`): the watchdog reports a 250 ms block within 200 to 420 ms and a free thread under 60 ms, sees a block still going on, disposes at once even if the thread never answers, ends quietly when the queue is gone; `SpeedRow` middle and spread; the table is culture-proof; `PerfSections` keeps all four heading families, a second rewrite changes nothing, CRLF and odd texts do not loop.
- **Duplicated rules** (`DuplicatedRuleTests`): the numbers in the settings words (2 to 60, 3 to 30, nine pages, six built-in, 100 scenes, 200 things, name lengths) are the constants; the light's numbers are `LookConstants`' and `Layers.cs` uses the same names; the register's 14 refusals have three parts, fit a balloon and include LIGHT_UNAVAILABLE; the three light names are the enum's in lower case.
- **Read and found sound** (no test): `MovingLight` Dispose twice, Follow before Show, Show with a dead anchor (no-ops); the breath animation starts from `ModeMark.Breath` and loops (it is periodic); the hand-over of the light at a lift, a pill or a notice is by design; `ScreenRefresh`'s two structs have the sizes Windows says (220 and 104 bytes, counted); `IslandWarmUp` subscribes to nothing that outlives it (its settings view is never Loaded, and `GeneralSection` subscribes on Loaded); the tray icon rebuilds itself after the shell restarts (WinForms `NotifyIcon` handles `TaskbarCreated`: from memory of the framework source, UNVERIFIED).

## 4. Commands and totals

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test ".worktrees\rt-code\tests\Island.Redteam.Code.Tests"
  -> Failed: 20, Passed: 131, Total: 151   (the 20 are the methods named Defect_*, red on purpose; run three times, the same)
dotnet test ".worktrees\rt-code\tests\Island.Tests"
  -> Failed: 1, Passed: 1677, Total: 1678
```
The one failure in `Island.Tests` is `ExtensionGuardTests.The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme` (line endings: expected CR LF, got LF in the zip's bytes, at byte 109). My branch only adds files under `tests/Island.Redteam.Code.Tests/`; I did not run that test on `main` to prove it fails there too, so I say only: it does not read anything I added. (A first run also had `GuardTests.Real_Pipe_Name_Is_Not_In_Tests` red: my own tests spelled the real name; fixed.)
The project switches off test parallelism (`Support.cs`), because some tests time milliseconds or open hundreds of pipes.

## 5. What I could not check

Anything that needs a window, the compositor, the real icon reader, the app's start or the screen: the light's geometry (where the compositor's rounded rectangle starts, which way it runs, whether `TrimOffset` wraps: Learn's page for `TrimOffset` says only "the amount to offset trimming ... default 0"), whether the light looks the same, 1-11 (Alt+F4) and 1-14 (start-up throw) by running, 1-13's race with the catalog, the z-order and insert-after questions in 1-18, a DPI change, the cost claims of 1-22. A hand-edited 2 GB settings file (`File.ReadAllText` is not guarded for size in `Settings.Load`, `PageStore.Load`, `PickStore.Load`; only `SceneStore.Load` checks the length: an `OutOfMemoryException` is not caught): reasoned, not run.
Multi-user behaviour (1-21), sleep and wake of the real compositor and the add-on's service worker restarting, were read, not run.

## 6. Requests to the main session

1. Take 1-3 with 1-4/1-5/1-6 as one change: one `WriteAtomically(path, text)` used by the four files (temporary file, retry, delete on failure).
2. 1-11, 1-14, 1-18 and 1-13 are for a person to look at too: they cannot be shown here. 1-11 first (Alt+F4 in Settings, then try to open Settings again).
3. 1-7: the two sides of PROTOCOL.md must count the same thing; the repair is on the island's side (code points) so the add-on in the store need not change.
4. 1-15 and 1-21 are proposals by the one rule (a look; a design choice).
5. Round 2 should re-run `dotnet test tests/Island.Redteam.Code.Tests`: each repaired finding's `Defect_` test turns green and keeps its name.
