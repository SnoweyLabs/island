# ATTACK3 — WO3 "break it" report

Branch `attack3` (from `a34d94c`). Territory: `tests/Island.Attack3.Tests/` (xunit, references Island.Core only,
not in `Island.sln`) and this file. Nothing under `src/`, `tools/`, `extension/` or `tests/Island.Tests/` was
changed. No app, window, browser, media session or program was started or touched; every input is made up.

Commands (from the worktree):

- `dotnet test tests\Island.Attack3.Tests` → **Failed 24, Passed 57, Total 81** (the 24 failures are the 15 defects
  below, counted per test case; every one fails for the reason named, checked in the failure output).
- `dotnet test tests\Island.Tests` → Passed 496 / 496 (untouched, still green).

Island.App has no `InternalsVisibleTo` and adding one means editing `src/`, so `PickPages`, `PickBook`, `MediaPage`,
`AppWorld` and `IslandPanels` were read, not run. Their doubts are in the last section.

Severity: HIGH = Dan loses data or the feature does the wrong thing in normal use; MEDIUM = wrong in a plausible
situation; LOW = contract broken, little or no visible harm today.

---

## Defects (each has a test that fails because of it)

### 1. HIGH — Repeated clicks on a program with 3+ windows ping-pong between the two newest

- Test: `PicksAttackTests.Cycling_Three_Windows_Reaches_The_Third_When_Each_Click_Brings_Its_Window_To_The_Top`
- Input: pick `alpha.exe`; windows handles 1, 2, 3 at z-order 0, 1, 2. Click, then put the window that was brought
  forward on top (what Windows does), take a fresh snapshot, click again; three times.
- What happens: targets `[1, 2, 1]`. `PickStates.For` orders targets by z-order; `ClickCycler` remembers the last
  handle and takes "the one after it" in the *new* order. Once window 2 is on top, "after 2" is 1. The third window
  is never reached (EVALS C7 "clicking again goes to the next").
- Should: three clicks reach `[1, 2, 3]`. The cycle needs an order that does not change when a window comes
  forward (for example: remember the handles visited in this cycle and go to the top-most one not yet visited, or
  freeze the order at the first click of a cycle).

### 2. HIGH — Repeated clicks on a site with several tabs never leave the newest tab

- Test: `PicksAttackTests.Cycling_Three_Tabs_Reaches_Every_Tab_When_Activating_Makes_A_Tab_Newest`
- Input: site pick `example.org`; tabs `p:1`, `p:2`, `p:3` with `LastActiveOrder` 3, 2, 1. Each click activates the
  tab (found by its ordering number, as `PickPages.Carry` does), which gives it the next number (as
  `TabModel.Activate` does: `Order = ++_order`).
- What happens: 1 distinct tab in 3 clicks. A site's click targets are ordering numbers, not identities; activating
  changes the number, so the cycler cannot find what it remembered and restarts at the newest — the tab it just
  activated.
- Should: three clicks reach three tabs. Targets need a stable identity (the tab key), the same fix as 1.

### 3. HIGH — A store with more than 1000 picks saves a file that the next start refuses

- Test: `PicksAttackTests.A_Store_That_Was_Allowed_To_Grow_Saves_A_File_That_Loads_Again`
- Input: `PickStore.Add` of 1001 programs `Program {i}` / `p{i}.exe`, `Save`, `Load`.
- What happens: `Add` has no limit, `Save` writes 1001, `Load` says `Unreadable` ("Too many picks"): on the next
  start the island is empty, and because the file is "unreadable" the app no longer saves to it.
- Should: `Save` and `Load` agree. Either `Add` refuses beyond `MaxPicks` (added = false), or `Load` accepts what
  `Save` writes. The test accepts either fix.

### 4. MEDIUM — Different programs get the same id, so the second cannot be picked

- Test: `PicksAttackTests.Two_Different_Programs_Never_Share_An_Id` (3 cases)
- Input: `Pick.ForProgram` with exe pairs `αλφα.exe` / `βήτα.exe` (both id `program:`), `al pha.exe` / `alpha.exe`
  (both `program:alpha`), `ålpha.exe` / `lpha.exe` (both `program:lpha`).
- What happens: `Slug` drops every non-ASCII letter and spaces. The second `Add` is refused as "the same thing picked
  twice"; from the + list the "Add" silently does nothing (`IslandPanels.AddEntry` ignores the result).
- Should: two different exe (or package) names never give the same id, e.g. keep every letter (lower-cased) and
  escape only characters that are unsafe.

### 5. MEDIUM — The + list offers sites that cannot be added (a local dev server)

- Test: `PicksAttackTests.Plus_List_Offers_Only_Sites_That_Can_Be_Added`
- Input: add-on connected, one tab with host `localhost`.
- What happens: the entry is offered; `ToPick` returns null (a host must contain a dot), so "Add" does nothing.
  Programs that cannot be stored are left out of the list ("is not offered"), sites are not checked.
- Should: an entry whose `ToPick` is null is not offered (or is offered for "jump" only, without an Add control).

### 6. MEDIUM — Grace is counted from the last report, not from the moment a session vanished

- Tests: `MediaAttackTests.A_Session_That_Vanishes_After_A_Quiet_Spell_Still_Gets_Its_Grace`,
  `MediaAttackTests.A_Background_Player_Back_From_Between_Tracks_After_A_Quiet_Spell_Does_Not_Take_The_Line`
- Input 1: `Update(t=0, alpha Playing)`, then `Update(t=60, no sessions)`.
  What happens: the line is null at once. `NowPlaying` is fed "each time something is reported"; a steady player
  reports nothing (`MediaSessionReader` does not raise `Changed` for a moving position, and `MediaPage` polls only while
  the island is shown), so `Held.Seen`/`Track.Seen` are a minute old when it vanishes and the 4 s grace is already over.
- Input 2: alpha plays from t=0, beta starts at t=1 and holds the line; t=120 alpha missing; t=121 alpha Playing
  again (`Song A2`). What happens: the line jumps to alpha — its state was forgotten with the grace, so its return
  counts as a fresh start.
- Should: the grace starts when the session is first seen missing (keep "seen until" = the time of the report that
  still had it, and stamp "missing since" on the first report without it). Line shows `Song A` with
  `CanControl = false` at t=60; beta keeps the line at t=121.

### 7. MEDIUM — A background player changing track takes the line

- Test: `MediaAttackTests.A_Background_Player_Changing_Track_Does_Not_Take_The_Line`
- Input: alpha plays from t=0; beta starts at t=10 and holds the line; t=60 alpha Paused, t=60.3 alpha Playing with a
  new title, beta still Playing.
- What happens: the line goes to alpha. `MediaSessionReader.MapState` maps Windows' `Changing` state to `Paused`
  (its own comment: "between tracks"), so every track change of a background player is a "resume".
- Should: a source that only blinked out of Playing (a `Changing` state, or a pause shorter than about a second)
  does not count as started. Unverified: how often real players report `Changing` (NEEDS-HUMAN-VERIFY with two
  players); the Core behaviour on the blink is certain.

### 8. LOW — Two sessions with one id take the line back at every report

- Test: `MediaAttackTests.Two_Sessions_With_One_Id_Do_Not_Take_The_Line_Back_At_Every_Report`
- Input: sessions `dup` Playing and `dup` Paused at t=0; beta Playing joins at t=1; the same three at t=2.
- What happens: t=1 beta, t=2 back to `Song A`. Inside one report the remembered state of `dup` flips Playing →
  Paused, so each report sees it "start" again.
- Should: duplicate keys in one report are merged (Playing wins) before the states are noted. Today's reader makes
  ids unique (`UniqueId` adds `#2`), so this needs a reader that does not.

### 9. LOW — The tile mark can hold half a character

- Test: `PicksAttackTests.Tile_Mark_Never_Holds_Half_A_Character` (2 cases)
- Input: `PickItems.Mark("a\U0001F600")` → `"A\uD83D"`; `PickItems.Mark("Alpha \U0001D539eta")` → `"A\uD835"`.
- What happens: `Mark` takes `char`s, so a letter outside the basic plane is cut in half: a broken glyph on the tile
  and in the + list (EVALS I4 "never a broken tile"). `IconChoice.TwoLetterMark` does not have this problem; the two
  do the same job differently.
- Should: work on text elements (or `Rune`s), or reuse `IconChoice.TwoLetterMark`.

### 10. LOW — `PickItems.For` returns more tiles than allowed when `maxTiles` is 0 or less

- Test: `PicksAttackTests.Items_Never_Outnumber_The_Tiles_Allowed` (cases 0, -1, `int.MinValue`)
- Input: three closed program rows; `maxTiles` 0 → one "+3" tile; -1 → one tile; `int.MinValue` → `maxTiles - 1`
  overflows: all three rows plus a tile reading `+-2147483644`.
- Reachable: `PageFit.MaxTiles` returns at least 1 and `PickPages` passes `maxTiles - 1` when the + tile is on, so a
  screen too narrow for two tiles gives `For(rows, 0)`: the page draws "+N" plus "+" = 2 tiles while
  `PickPages.WidestCapsule` sized the window for 1.
- Should: never more than `max(0, maxTiles)` items.

### 11. LOW — A + list row per window group, so one program can appear twice

- Test: `PicksAttackTests.Plus_List_Shows_One_Row_Per_Program_Even_When_Only_Some_Windows_Report_A_Package`
- Input: windows `(1, alpha.exe, no package)` and `(2, alpha.exe, Alpha.App_x1)`.
- What happens: two entries, both with key `program:alpha.exe` (the group key is exe + package). The key is
  documented as stable and is used as the icon cache key and in `ListKeys`.
- Should: one row per program (group by exe when there is one, else by package).

### 12. LOW — `NaN` position gives `NaN` progress

- Test: `MediaAttackTests.NaN_Position_Never_Gives_A_NaN_Progress`
- Input: session Playing, position `NaN`, length 100.
- What happens: `Progress = NaN` (`Math.Clamp(NaN, …)` is `NaN`); documented "0 to 1; null when the length is not
  known". Not reachable from today's reader (it clamps) and progress is not drawn tonight; the pill and the progress
  edge will draw it.
- Should: a position that is not finite counts as unknown: `PositionSeconds` and `Progress` null.

### 13. LOW — IslandMachine: `SelectedItem` points past the end after a page shrank while hidden

- Test: `MachineAttackTests.Selection_Is_Inside_The_Items_After_A_Summon_When_The_Page_Shrank_While_Hidden`
- Input: page Apps with 6 items, open, `ItemClick(5)`, dismiss until Hidden, the page now has 2 items, `MainKey`,
  run to rest.
- What happens: `SelectedItem` is 5 with 2 items. A summon with `resetSelection: false` keeps it, and
  `ContentsChanged` is ignored while hidden. `ContentsLayer.Build` clamps it, so nothing crashes today.
- Should: `Summon`/`Expand` clamp the selection to the items the page has.

### 14. LOW — Enum text in the files accepts numbers and comma lists

- Tests: `SettingsGateAttackTests.Glass_Given_As_A_Number_Or_A_List_Is_Refused` (cases `"1"`, `"2"`,
  `"Approved, Darker"`), `PicksAttackTests.A_Kind_That_Is_Not_One_Of_The_Three_Words_Is_Refused` (cases `"2"`,
  `"Program, Site"`).
- Input: `{ "glass": "2" }` loads as Blur, `"Approved, Darker"` as Darker; a pick with `"kind": "2"` or
  `"Program, Site"` loads as a site.
- What happens: `Enum.TryParse` takes numbers and flag lists; `Enum.IsDefined` passes because the OR lands on a
  defined value. The refusal text says only the three words are allowed.
- Should: compare against the names only (`Enum.GetNames` with ignore-case), refuse anything else.

(14 defect entries; 15 failing test methods, 24 failing test cases.)

---

## What I could not break (coverage these tests add)

- **Hostile picks files** (`Hostile_Picks_Files_Are_Refused_Quickly_And_Left_Untouched`, 18 cases): empty file,
  `null`, `{}`, BOM + broken text, 100 000 nested arrays (no stack overflow), a 5 MB name, 1001 picks, duplicate
  ids, ids/pages/exes that look like paths (`..\..\x`, `..\apps`, `../alpha.exe`), a NUL in a name, a lone-surrogate
  escape, a number where text belongs, `Downloads\..`, an upper-case host, a host with a port, an unknown kind. All
  `Unreadable`, empty list, under 5 s, the file byte-for-byte untouched by `OpenOrStart`.
- **A lone surrogate in a name** that `Add` accepts survives `Save` + `Load` (`A_Pick_The_Store_Accepts_Survives_Save_And_Load`).
- **5000 windows of one program** (exe case mixed): one pick, count 5000, newest first, the cycle wraps after 5000
  clicks; the + list shows one row with 5000 (`Thousands_Of_Windows_Of_One_Program_Are_One_Pick_And_The_Cycle_Wraps`).
- **A window that closes before the click**: the plan uses a fresh snapshot, so a gone handle is never the target and
  an empty list starts the program (`A_Window_That_Closed_Before_The_Click_Is_Never_The_Target`). The race between
  the snapshot and `BringForward` itself is app-level (below).
- **`PageFit` with NaN, ±infinity, ±1e300, 0** stays in 1..200 (`PageFit_With_Absurd_Widths_Stays_Between_1_And_200`).
- **The + list with hostile exe names** (`..\evil.exe`, `a/b.exe`, `c:alpha.exe`, empty, `.exe`, no extension, 304
  characters, right-to-left override, NUL) and 10 000-character titles with markup: only storable programs are
  offered, no exe with a slash or colon (`Plus_List_With_Hostile_Exe_Names_Offers_Only_Storable_Programs`).
- **5000 media sessions**: one starting wins; at one instant it vanishes and another starts, the new one holds the
  line and receives the commands; under 5 s
  (`Five_Thousand_Sessions_One_Starting_Wins_Quickly_And_A_Same_Instant_Swap_Follows_The_New_One`).
- **Odd positions and lengths** (-30, 500 past the end, +infinity; length NaN, negative, 0, +infinity): progress in
  0..1 or null (`Odd_Positions_And_Lengths_Give_Progress_In_Range_Or_None`, 7 cases).
- **Time going backwards** in `Current`: the position never runs back past the report
  (`Time_Going_Backwards_Never_Moves_The_Position_Backwards_Past_The_Report`).
- **NowPlaying from 8 threads** with random sessions, duplicate ids and random times: no exception
  (`Many_Threads_Updating_At_Once_Never_Throw`).
- **IslandMachine storm with pages and contents changing** (`Pages_And_Contents_Changing_In_Every_Phase_Never_Jump`,
  3 seeds × 20 000 frames): `SetPages` with an empty list, a list without the current page, duplicate ids, 10 000
  pages, a renamed page; `ContentsChanged` with 0..40 items; every key in random order and unknown page ids. At
  every 1/120 s step each spring equals previous + velocity × step (EVALS M3), no snap while in sight, page ids always
  exist, drawn values finite, no keyboard while leaving, no contents while hidden.
- **`SetPages`** with an empty list changes nothing; without the current page it falls back to the first and lays
  out again (`SetPages_With_An_Empty_List_Changes_Nothing_And_A_Missing_Current_Page_Falls_Back_To_The_First`).
- **Every glass word** in any case loads and round-trips through `ToJson`; a glass of the wrong type or value
  (`1`, `true`, `null`, `[]`, `{}`, `""`, `"frosted"`, `"3"`, `"Darker, Blur"`, a lone surrogate, `1e400`) gives the
  defaults with a reason (`Every_Glass_Word_Loads_And_Round_Trips`, `A_Glass_Of_The_Wrong_Type_Or_Value_Gives_The_Defaults`).
- **`OutsideGate` under 16 threads × 20 000 calls** with a reader of the counts running at the same time: allowed +
  refused = calls exactly, per-kind counts add up, only a bring-forward of the own process passes, an unknown owner
  (0), a negative id, and an undefined kind are refused (`Outside_Gate_Counts_Exactly_Under_Many_Threads`).

Not attacked tonight: a glass switch in the middle of an animation (needs the app and a window; prohibited here).

---

## Doubts about app-level code I could not run (no InternalsVisibleTo)

1. **`PickPages.ItemsOf` can cache stale items.** It computes the rows outside the lock and stores them after. If a
   reader raises `Changed` (→ `Invalidate` clears the cache) while the rows are being built, the old rows are stored
   after the clear and stay until the next change. Fix: a version counter checked before storing.
2. **`PickPages.TabIconOf` keys the decoded icon by `tab.Key + png.Length`.** A tab that moves to another site with an
   icon of the same byte length keeps the old picture (EVALS I7). `TabModel` clears the icon on a host change, but the
   cache here does not know. The dictionary also never shrinks.
3. **`PickPages.Carry` ignores `BringForward`'s result.** A window that closes between the snapshot and the call gives
   one dead click (the next click starts the program). A handle Windows has reused for another program's window would
   bring that window forward instead (rare).
4. **`MediaPage.BringLineForward` matches too loosely:** `app.Contains(GetFileNameWithoutExtension(exe))`. A window of
   any program whose file-name stem is a substring of the session id can be chosen, top-most first: a made-up
   `ph.exe` window matches a session id `Alpha.exe`. An exe named `.exe` (empty stem) matches every session. Match
   the whole file name, or the package family before the `!`.
5. **`MediaPage` polls every 500 ms with positions the reader read up to ~2.15 s earlier** (`MediaSessionReader`
   reads every 2 s + 150 ms and extrapolates only at read time). `NowPlaying` treats each fed position as "now", so
   the progress would saw-tooth backwards. Invisible tonight (progress is not drawn); it matters for the pill.
6. **`MediaPage` and `IslandController` use `DateTimeOffset.UtcNow`** (wall clock). A clock step backwards holds a
   vanished item and its remembered state that much longer. A monotonic clock (`Stopwatch`) would not.
7. **`IslandPanels.AddEntry` and `PickBook.Apply` ignore failures:** `Add` returning false (defects 4, 5) and `Save`
   returning false both leave Dan without a word; the change shows in memory and is lost at the next start.
8. **`PickPages.WidestCapsule` vs `ItemsOf` with `maxTiles` 1** (defect 10): window sized for 1 tile, 2 drawn.
9. **`PickStore.Load` reads the whole file into memory** and catches only `IOException` and
   `UnauthorizedAccessException`; a multi-gigabyte picks file would throw `OutOfMemoryException` at start-up. Not
   tested (it needs a file of that size).
10. **Picks on a page that does not exist** (a hand-edited file) load, count towards the 1000 limit, and are never
    shown anywhere; nothing tells Dan.
11. **`PickStore.Parse` puts `JsonException.Message` into `Detail`;** that text can quote a character of the file and
    its position. Harmless for a picks file (names only); worth a fixed text if `Detail` is ever logged, as the
    settings loader already does for I/O errors.
