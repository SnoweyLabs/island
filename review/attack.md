# Attack report (work order section 8, item 2)

Brief: break it. Territory: `tests/Island.Tests/AttackTests.cs` and this file. No `src/` file was edited.
Tests that demonstrate a real defect are marked `DEFECT` in a comment and FAIL until the defect is fixed
(32 failing test cases, 11 distinct defects, listed below). Everything else is a guard that passed.

## 1. What was added

`tests/Island.Tests/AttackTests.cs`, 62 test cases (30 pass, 32 fail on purpose):

Machine (all pass unless marked D)
- `Key_At_Every_Phase_Boundary_Never_Jumps_And_Keeps_Invariants`
- `Hundreds_Of_Keys_With_Zero_Backwards_And_Repeated_Times_Keep_Invariants`
- `Same_Input_Sequence_Gives_The_Same_Machine_Whatever_The_Frame_Pattern`
- `Key_Exactly_At_The_Idle_Deadline_Follows_One_Consistent_Rule`
- `Input_After_A_Long_Silence_Closes_First_Then_Handles_The_Input`
- `Machine_Tick_With_NaN_Recovers_On_The_Next_Tick`
- `Machine_Tick_After_Eight_Hours_Of_Sleep_Hides_It_And_Stays_Sane`
- `Custom_Pages_With_Zero_And_Huge_Item_Counts_Keep_Invariants`
- `Zero_Item_Page_Selection_Is_Zero_And_Clicks_Select_Nothing`
- `Duplicate_Page_Ids_And_Lookalike_Ids_Do_Not_Crash`
- D `Machine_Key_With_NaN_Time_Does_Not_Wedge_The_Machine`
- D `Machine_Tick_With_Infinity_Does_Not_Hang`
- D `Machine_Tick_After_A_Day_Of_Sleep_Is_Fast`

Springs
- `Spring_Negative_Infinite_And_Enormous_Elapsed_Stay_Finite_And_Never_Run_Backwards`
- `Spring_Target_Change_Every_Step_Never_Jumps_Drawn`
- `Spring_Random_Splits_Give_The_Same_Path_As_One_Advance`
- D `Spring_Frame_With_NaN_Elapsed_Does_Not_Poison_Drawn`

Settings
- `Settings_Broken_Empty_Huge_Binary_Nested_And_Wrongly_Typed_Files_Give_Defaults_And_Are_Left_Alone` (about 75 named files)
- `Settings_Files_That_Are_Valid_In_Odd_Ways_Load` (BOMs, UTF-16/32, comments, 10 MB values, 200k keys)
- `Settings_Duplicate_Keys_Never_Crash_And_Always_Give_A_Valid_Result`
- `Settings_Mutation_Fuzz_Never_Throws_And_Never_Returns_A_Bad_Result` (40,000 mutations)
- `Settings_Random_Bytes_Through_Load_Never_Throw` (1,500 files)
- `Settings_Locked_Or_Unwritable_Or_Odd_Paths_Do_Not_Throw_On_Load`
- `Settings_Tiny_Idle_Seconds_Is_Accepted_And_The_Machine_Survives_It` (observation)
- D `Settings_Lone_Surrogate_Escape_In_A_Text_Value_Does_Not_Throw` (3 failing cases of 4)
- D `Settings_Parse_Of_A_String_With_A_Lone_Surrogate_Character_Does_Not_Throw`
- D `Settings_Load_Detail_Does_Not_Echo_The_File_Path`
- D `Settings_EnsureExists_Does_Not_Throw_When_The_Path_Cannot_Be_Written`

Hotkey text and refusals
- `Hotkey_Fuzz_Never_Throws_And_Every_Accepted_Combo_Round_Trips` (60,000 random strings)
- `Hotkey_Huge_And_Degenerate_Strings_Are_Handled_Quickly`
- `Hotkey_Windows_Key_Spellings_Are_All_Rejected`
- `Refusal_Placeholders_Cannot_Be_Injected_By_A_Page_Name_Or_Combo`
- D `Hotkey_Combos_That_Take_Over_Typing_Or_System_Shortcuts_Are_Rejected` (17 cases, all fail)
- D `Hotkey_F_Keys_Only_Accept_Plain_Spelling` (4 cases, all fail)
- D `Hotkey_Refusal_Fits_The_255_Character_Balloon_For_Every_Built_In_Page`

Other pure parts (all pass)
- `Perimeter_Fuzz_Pieces_Join_Cover_The_Asked_Length_And_Never_Seam`
- `Perimeter_NaN_And_Infinite_Fractions_Return_Without_Hanging`
- `Reveal_Timeline_Fuzz_Stays_In_Range_And_Never_Jumps_When_Set_Or_Reset`
- `Colour_Transition_Fuzz_Never_Jumps_At_A_Retarget_Even_With_Backwards_Time`
- `Window_Metrics_Hold_The_Widest_Built_In_Capsule_At_The_Spring_Peak`

## 2. What I broke

Severity is my own judgement: HIGH = Dan hits it in normal use; MEDIUM = Dan can hit it by editing a file or
by a normal Windows event; LOW = needs a caller bug or an unusual file.

1. **App crashes at launch when the settings or data path cannot be written (MEDIUM).**
   Input: `settings.json` is a folder, or the data folder path is a file, or the profile folder is read-only.
   `AppHost.Start` calls `Settings.EnsureExists` first, which has no error handling.
   Observed (run on the built program with a temporary data folder): process dies with exit code
   -532462766 (0xE0434352, unhandled .NET exception) about a second after start; no tray icon, no log line,
   no message. Expected: start on defaults and show a refusal.
   Tests: `Settings_EnsureExists_Does_Not_Throw_When_The_Path_Cannot_Be_Written`.
   Fix: catch `IOException`/`UnauthorizedAccessException` in `EnsureExists` (return false) and log it.

2. **A settings file with half a surrogate pair kills the app (MEDIUM-LOW).**
   Input: `{"hotkeys":{"media":"\ud800"}}` (also `\udc00`, and `"Ctrl+Alt+\ud83d"`).
   It is valid JSON, so `Parse` goes on, `GetString()` throws `InvalidOperationException`, and neither
   `Parse` (catches `JsonException` only) nor `Load` (IOException/UnauthorizedAccess only) catches it.
   Observed on the built program: same crash, exit code -532462766, no log. Expected: Unreadable,
   defaults, file untouched (what the work order promises: "never throw").
   Also `Settings.Parse("{...}\ud800")` (a raw lone surrogate char in the string) throws `ArgumentException`;
   only a direct caller can pass that, `Load` cannot.
   Fix: widen the catch in `Parse` to `JsonException or InvalidOperationException or ArgumentException`.
   Everything else in 40,000 mutations, 1,500 random byte files and ~75 hand-made files was handled.

3. **The log can get a path under the profile folder, with the account name (MEDIUM, breaks a HARD PROHIBITION in spirit).**
   Input: settings file held open by another program (or access denied). `Settings.Load` puts the raw
   `IOException` message in `Detail` ("The process cannot access the file 'C:\Users\<name>\AppData\Roaming\Island\settings.json'..."),
   and `AppHost.Refuse(..., load.Detail)` writes that into `%LOCALAPPDATA%\Island\island.log`.
   Test: `Settings_Load_Detail_Does_Not_Echo_The_File_Path`. Fix: use a fixed text ("the file is in use or
   not readable") instead of `e.Message`.

4. **One huge elapsed time freezes the UI thread (MEDIUM for a laptop that sleeps with the island open).**
   `IslandMachine.StepSprings` loops once per 1/120 s of the whole gap; the 5 s cap inside `Spring.Frame` never
   applies because the machine passes one step at a time. After the idle deadline passes inside one big gap the
   machine still steps four springs through the rest of the gap.
   Measured here (Debug build): `Tick(24 h)` on an open island = about 2.4-2.6 s; 8 h = about 0.8 s, so a
   weekend (60 h) is about 6 s of frozen island, tray and keybinds. `Tick(+infinity)` never returns (test hangs
   its worker thread and fails after 5 s). Whether Windows' monotonic clock counts sleep time is UNVERIFIED;
   if it does, this happens on every wake-up with the island open.
   Tests: `Machine_Tick_After_A_Day_Of_Sleep_Is_Fast` (limit 250 ms), `Machine_Tick_With_Infinity_Does_Not_Hang`.
   Fix: in `StepSprings`, clamp `dt` to a few seconds (the spring has settled by then, as `Spring` already says), or
   stop stepping once the island is Closing, fly-out issued and settled.

5. **NaN time wedges the machine (LOW).** `ShowHideKey(double.NaN)` from Hidden arms the expand timer and the
   idle deadline with NaN, which never compare as due: the island stays FlyingIn for ever (never opens, never
   idles out). `Tick(NaN)` alone is survivable (the next tick overwrites the clock; that stretch of time is skipped).
   The controller feeds a Stopwatch, so this needs a caller bug. Test: `Machine_Key_With_NaN_Time_Does_Not_Wedge_The_Machine`.
   Fix: ignore non-finite times at the top of every input and of `AdvanceTo`.

6. **`Spring.Frame(NaN)` poisons the spring (LOW).** `Math.Max(0, NaN)` and `Math.Min(NaN, 5)` are NaN, so the
   leftover becomes NaN and `Drawn` is NaN for ever. Test: `Spring_Frame_With_NaN_Elapsed_Does_Not_Poison_Drawn`.
   Fix: `if (!(elapsedSeconds > 0)) elapsedSeconds = 0;`.

7. **Keybinds that take over typing or system shortcuts are accepted (MEDIUM).** The parser only demands one of
   Ctrl/Alt/Shift, and its own message says that is what keeps a keybind from taking over normal typing.
   Accepted today: `Shift+A`, `Shift+Z`, `Shift+1`, `Shift+Space`, `Shift+Enter`, `Shift+Tab` (every capital letter,
   space, Enter or Tab on the machine), `Ctrl+C/V/X/Z/A` (copy, paste, cut, undo, select all), `Alt+F4`, `Alt+Tab`,
   `Alt+Space`, `Ctrl+Esc`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Delete`. Fact 2 of the work order: Windows grants a
   combination nobody registered, and the other programs silently lose it. Windows itself will probably refuse
   only a few (Ctrl+Alt+Delete at least; which others is UNVERIFIED). Not tested but the same class: `Ctrl+Alt+<letter>`
   is AltGr+letter on many layouts (types a character), and the work order's own fact 8 is why the defaults use
   Ctrl+Alt+Shift. Test: `Hotkey_Combos_That_Take_Over_Typing_Or_System_Shortcuts_Are_Rejected` (17 cases).
   Fix: a small deny-list (Shift-only plus a printable key/Space/Enter/Tab; Ctrl+C/V/X/Z/Y/A/S; Alt+F4/Tab/Space/Esc;
   Ctrl+Esc; Ctrl+Shift+Esc; Ctrl+Alt+Delete), or require Ctrl+Alt (or two modifiers) as a minimum.

8. **F-key spelling is looser than meant (LOW).** `F 1`, `F 12`, `F01`, `F<TAB>5` parse as F1, F12, F1, F5, because the rest of
   the name goes to `int.TryParse`. Effect is harmless (they round-trip as `F1`), but it is a spelling nobody intended.
   Test: `Hotkey_F_Keys_Only_Accept_Plain_Spelling` (4 cases).
   Fix: require all-ASCII digits, no leading zero.

9. **HOTKEY_TAKEN balloon text is longer than Windows allows (LOW).** `szInfo` holds 256 characters including the
   terminator (Microsoft Learn, NOTIFYICONDATAW, page updated 2024-11-20), and Microsoft recommends staying under
   200 for English. Built text for the longest default combination: Folders 256, Browser 256, Vibe coding 260
   characters, so the end ("restart Island.") is cut off; Media and Apps are 254 and 253, one or two characters inside the 255 limit and far over the 200 recommendation. The refusal
   register requires the "what to do now" part to survive. What WinForms does with an over-long string
   (truncate or fail) is not verified by me; I did not show the balloon. Test:
   `Hotkey_Refusal_Fits_The_255_Character_Balloon_For_Every_Built_In_Page`. Fix: shorten the three parts (the log
   keeps the long text), aiming for under 200.

Observations that are not failing tests:
- Duplicate JSON keys never crash; the later or earlier value wins silently (`{"idleSeconds":5,"idleSeconds":-1}` is
  accepted or rejected depending on which one the reader returns). Mis-cased keys (`"Hotkeys"`, `"idleseconds"`) are ignored
  silently, so a typo changes nothing and nothing says so. A valid file with a 70-deep unrelated value is Unreadable
  as a whole (JsonDocument depth limit 64).
- `"idleSeconds": 1e-9` is accepted (the stated rule is "above 0") and the island then leaves on its first frame.
- `SelectedItem` is 0 for a page with no rows (no valid index exists); the view clamps it, so nothing breaks.
- `IslandMachine` with an empty page list throws from its constructor (index out of range); callers must not pass one.
- The log has "→" in the HOTKEY_TAKEN line; it is written as UTF-8 without BOM, so an old ANSI reader shows mojibake.
- A page key arriving during the fly-in restarts the 320 ms expand timer, so keys arriving more often than every
  320 ms keep the island a ball. Matches the existing test and the spec ("restarts the summon"), so by design.
- Idle at the exact deadline instant: the island is dismissed first, so an Activity at that very instant is lost and
  a ShowHide/page key at that instant reverses the closing. Consistent, tested.

## 3. What I could not break (coverage statement)

Every case below ran green.

- **Machine, key at every instant (8,268 runs):** 10 scenarios (summon, dismiss from open, page switch, dismiss
  before/after the expand, page key restarting the fly-in, switch restarted before its swap, reversing a closing island,
  idle poked once, idle firing during a switch) x 30-39 instants each (exactly at, 1e-6 ms before and after each of
  90 / 110 / 120 / 210 / 320 / 410 / 560 ms and at 60,000 / 60,110 / 60,560 ms) x frames at 60 Hz or no frames after
  the setup x 13 inputs (ShowHide, three page keys, unknown/null/empty page id, Activity, item clicks -1 / 1 / int.Max /
  int.Min, Tick). Checked against a reference machine that only ticked: the drawn shape never moved at the instant of
  a key (no jump while visible), phase results were right, values finite, selection in range, no contents while
  hidden, idle deadline armed iff shown, and every island left alone for 80 s ended Hidden.
- **Machine, random storms:** 300 seeds x 600 operations = 180,000 operations with gaps of exactly 0, 1e-9, 1e-4 and
  0.3 ms, ordinary, a minute, repeated times and times going backwards up to 400 ms: no exception, no jump larger
  than 6000 px/s x dt (and none at all for a repeated or backwards time), always ends Hidden.
  60 seeds x 40 keys compared at 60 Hz, 700 ms and 3.1 ms frame patterns: same machine.
  Custom pages (zero rows, three rows, 1,000 rows, mixed with the built-in five): 40 seeds x 500 operations, plus
  duplicate and look-alike ids: fine.
- **Springs:** 20,000 retargets with random elapsed times, drawn value never jumped at a retarget; 400 random
  splits (zero, 1e-9, whole steps, random) equal one advance (same steps, value within 1e-7); negative, infinite,
  1e300, 5.0000001 and denormal elapsed times stay finite and never run backwards (except NaN, defect 6).
- **Settings:** about 75 broken, empty, binary, wrongly typed, deeply nested (100,000 levels), 10 MB and duplicate-key
  files; BOM-prefixed UTF-8, UTF-16 LE/BE and UTF-32 files load; 40,000 mutations of a valid file and 1,500 random or
  byte-flipped files: never an exception from `Load` except the surrogate case, always defaults when unreadable, the
  file's bytes and write time never changed, no extra file created beside it, accepted results always valid (idle in
  range, combos distinct and round-trip). A locked file, a read-only file, a folder or empty/NUL/5,000-char path
  where the file should be: `Load` returns Unreadable/Missing without throwing.
- **Hotkey text:** 60,000 random strings from 60 atoms (unicode digits and case-folding look-alikes such as the Kelvin
  sign and long s, zero-width and no-break spaces, NUL, surrogates, empty parts, many plus signs): never an exception,
  every accepted combo has a modifier, a sane key, is not a Windows key and round-trips through `ToString`. 5 MB
  strings of `+`, `a`, spaces, a million `Ctrl+` parts: all handled in well under 5 s in total. Fourteen Windows-key
  spellings in five shapes: all rejected.
- **Refusals:** placeholder injection through a page name cannot happen (replacement order is safe).
- **Perimeter, reveal, colour:** 6,000 random rounded rectangles (zero, 1e-6, huge radius): pieces join, cover exactly the
  asked length, the outline never jumps at the wrap; NaN/infinite fractions return without hanging; 300 x 80 random
  reveal operations (backwards times, resets): continuous at every Set, always in range; 200 x 200 colour retargets
  with backwards time: continuous.
- **Built program, temporary data folder (screen lock `attack` held, released afterwards):**
  - random 300-byte `settings.json`: starts, logs SETTINGS_UNREADABLE and the six keybind results, the file hash is
    unchanged afterwards, `--quit` closes it;
  - a second copy while the first runs: exits with code 4 (ALREADY_RUNNING), the running copy logs ALREADY_RUNNING;
  - two keybinds (show/hide and Media) registered first by another process: the island still starts, logs
    `refused by Windows, last error 1409` and a HOTKEY_TAKEN refusal for exactly those two, and accepts the other four;
  - the three crash cases are defects 1 and 2 above.
  The built program's tray, balloon, mouse and real key presses were not exercised (no synthetic input allowed).

## 4. Reasoned, not run

- **Second copy racing the first (SingleInstance).** The mutex is taken before the two named events are created, and the
  ALREADY_RUNNING handler is attached after `runtime.Show()`. A second copy started in that window finds no event, exits
  with code 4 and nothing is shown or logged. Only milliseconds wide.
- **Quit then start.** `Dispose` releases the mutex last. `stop.cmd` followed at once by `run.cmd` can see the mutex still held and
  exit with ALREADY_RUNNING while the first copy is closing; Dan ends up with no island. Retry fixes it.
- **Notification queue is unbounded and not de-duplicated.** Every second copy started queues another ALREADY_RUNNING
  balloon, 7 s apart; 50 double-clicks of `run.cmd` mean about six minutes of balloons. Any local process can also set
  the named event. The log gets a line each time.
- **Hotkey handler exceptions.** `OnHotkey` runs inside the window procedure; an exception from the controller would
  reach the dispatcher with no handler (`App` has no `DispatcherUnhandledException`) and end the app. I found no throwing
  path in the controller today.
- **Colour snap on summon.** `IslandController.Input` reads `wasHidden` before the machine advances. If the island is
  settled but still Closing at the moment of a page key (inside the last frame or two), the machine goes Hidden and summons in
  the same call, `wasHidden` is false, and the colour fades over 350 ms instead of arriving already lit. Cosmetic, at most
  16 ms wide.
- **Selection after a stalled frame.** The controller resyncs the contents only when `ContentsPageId` changes. A page
  switch A to B to A inside one stalled frame skips the contents' out/in animation. Cosmetic.
- **Custom pages and the fixed window.** `WindowMetrics` is sized for the widest built-in page (5 rows, 502 wide). A custom
  page of 7 or more rows is wider than the window can show, with the spring overshoot and shadow it is cut off. Not
  reachable until pages can be created (EVALS D1, later).
- **`App.OnStartup` is `async void`.** An exception after an `await` (self-test path) is unhandled and ends the process;
  the normal start path has no `await` before `AppHost.Start`.
- **Windows' own refusal of some combos** (Ctrl+Alt+Delete, Alt+Tab, ...) is UNVERIFIED; the app would log HOTKEY_TAKEN.

## 5. Test-suite numbers

`dotnet test tests/Island.Tests` (Debug), final run:
- existing tests: 123 passed, 0 failed (unchanged from the run before I added anything);
- attack tests: 62 cases, 30 passed, 32 failed on purpose (the defects above; each says so in its comment);
- total 185, 153 passed, 32 failed. The 32 failures are all in `AttackTests`; run time about 40 s, most of it the
  boundary and storm tests and the 5 s wait for the infinity hang.
- Note: `Machine_Tick_With_Infinity_Does_Not_Hang` leaves one busy background thread in the test host until the host
  exits, while the defect exists. It disappears once `StepSprings` is fixed.

## 6. State on exit

No `.screen-lock` file exists and no `Island.App` process is running (checked with `tasklist` after the last run).
Only temporary folders under the system temp directory were used, and they were removed. Dan's settings and log
were never touched. No git command was run.

## 6. Status after the fixes (main session)

All 32 failing test cases pass; the whole suite is 185 passed, 0 failed. Each fix is its own commit, named in the commit message:

| Defect | Fixed in |
|---|---|
| 1 settings path unwritable crashes the app | 24016d6 |
| 2 lone surrogate in the settings file; 3 file path in the error text | 900ef35 |
| 4 huge elapsed time; 5 NaN time wedges the machine; 6 NaN poisons the spring | cc00beb |
| 7 shortcuts that take over typing or belong to Windows; 8 lenient F-key spelling | 0f36dcb |
| 9 refusal text over the notification limit | f6241c0 |

Not fixed, by decision (listed in STATE.md): the "reasoned, not run" items of section 4, and Ctrl+Alt+<letter> (AltGr) not being refused.
