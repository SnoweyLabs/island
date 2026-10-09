# ATTACK11 - WORK-ORDER-11 (the Terminals page, helpers inside terminals, what a helper is doing, Codex)

Adversarial pass by tests only. Project: `tests/Island.Attack11.Tests/` (not in `Island.sln`: the main session adds it). Branch `attack11`, made from `29e6865`. Nothing under `src/` and no existing test or assertion was
changed; no existing assertion was contradicted. Every window, title, process, id, folder and path in these tests is invented (`Q:\Invented\...`, `Alpha`, `alpha.exe`, `example.org`); the real pipe's name is spelled in no test
(a test checks that); nothing under the real `.claude`, `.codex`, `.gemini` or `.agents` was opened; no terminal, console, AI helper or AI program was started; no keyboard hook; no NuGet package; no push.
The only child process the tests start is the built `Island.Notify.exe`, hidden, with its three streams redirected, on an invented pipe name, exactly as the existing `NotifyTests` of `tests/Island.Agents.Tests` do.
`HelperSessions.cs` of the app is linked into the project (it uses `Island.Core` only), so the app's own file is attacked and not a copy.

Run on `attack11`: **384 test cases, 373 pass, 11 fail. The 11 are the 11 methods named `Defect_*`** (red on purpose, one per finding below, each with a comment that states the expected behaviour and the finding id).
Every `Holds_*` passes and is the coverage statement.

## Findings

| id | grade | area | one line |
|---|---|---|---|
| A11-03 | MEDIUM | sessions to tiles | Two conversations in the Claude program (one process, one window) collapse into the one heard last: an orange "needs you" ring is hidden behind a blue "working" one |
| A11-10 | LOW | session book | Helpers found only by name take slots as "most recent" and push out the sessions that have a state: with 65+ helper processes the rings vanish |
| A11-11 | LOW | helper inside helper | A connected helper inside another (Codex started by Claude Code) is counted: two dots, the tile reads "Codex"; the work order says it "is not counted" |
| A11-01 | LOW | finding helpers | Two helper-named processes that are each other's parent (a reused parent id) both vanish: the terminal shows no helper at all |
| A11-05 | LOW | machine | The second row can be opened on the Terminals page by a + click that reaches the machine during the swap onto it |
| A11-08 | LOW | guard | The guard `A_Title_Or_A_Process_Name_Never_Reaches_A_Log_Call` does not read 20 of the files of the new code, among them `HelperSessions.cs` and `OutsideCodexSettings.cs` |
| A11-09 | LOW | guard | `GuardRules.SensitiveReachesALog` does not see `File.WriteAllText/AppendAllText/WriteAllLines`, an exception carrying a title, or the ids of a process or a window |
| A11-06 | LOW (cosmetic) | installers | Connect then Disconnect does not give back an empty list (`"Stop": []`) or an empty `"hooks": {}` that the person had |
| A11-04 | LATENT | app wiring | `HelperSessions.ApplyReading` uses `ToDictionary` on the process list: one repeated id throws out of an unguarded timer callback |
| A11-02 | LATENT | page logic | Two sessions with no process collide on one key (`-1 - i` against `-chain[0]`) and one erases the other |
| A11-07 | LATENT | Island.Notify | `--event` cannot work: the reader refuses an input that names no event before the argument is looked at |

Grades: no HIGH. Nothing crashed or hung in 20 000 random worlds, 3 000 random byte strings, 300 random settings files for each of the two installers, 200 random books of 400 steps, eight threads on the book and 20 threads on a real pipe. No title, folder,
session id or process name reaches a log or a file anywhere in the new code (the scan below lists every statement it looked at). No user hook line was wrongly deleted; no written text lost someone else's content (A11-06 is two
empty containers). A path-shaped session id is only ever compared as text.

## The findings in detail

Test names are `Class.Method`; classes are in `tests/Island.Attack11.Tests/`.

### MEDIUM

**A11-03 - two conversations in the Claude program collapse into one.**
`src/Island.Core/Terminals/HelperFinder.cs`, `Collect`, with `src/Island.App/HelperSessions.cs`, `Facts`. The work order says the Claude program owns a window and "can hold many conversations at once", and the
ring is "the most urgent - needs you, then working, then finished". The book does hold both conversations (they hang on the one process, 500, which owns the AI window, so they are not settled into one). But `Collect` keys a session that hangs
on a process by that process id, so the second `instances[key] = ...` replaces the first. Failing input: Claude program window (handle 5, owner process 500, `Claude.exe`); conversation `c1` sends `Notification/permission_prompt`
at t=10, conversation `c2` sends `UserPromptSubmit` at t=20, both with chains ending in 500; one reading of the process list. `helpers.Facts()` has 2 facts; the tile's ring is `Working` (expected `NeedsYou`, and 2 dots,
got 0): the question that waits for the person is hidden, which is the whole point of the ring. It happens only for the one program that is itself named like a helper (its sessions hang on its own process); sessions inside
Cursor and the others hang on their own child process and are unaffected. The HelperSessionFact carries no session id; `LastHeard` is the book's session id, which is unique and would do as the key.
Test: `HelperSessionsAttackTests.Defect_Two_Conversations_In_The_Claude_Program_Collapse_To_The_One_Heard_Last`. (Holds beside it: one conversation gets the ring and keeps the tile's texts.)

### LOW

**A11-10 - helpers found only by name push out the sessions that have a state.**
`src/Island.Core/Agents/Sessions/SessionTracker.cs`, `Create` (the limit) with `SessionTracker.Process.cs`, `FoundByName`. At most 64 sessions are kept and "the one heard from least recently goes first". A helper found by its name
is a session never heard from, but it is made at its first reading and so ranks as the newest. With a hundred terminals each running a helper (the brief's own case), or only 65 helper processes, the sessions that really
have a state (heard) are dropped first, the nameless ones are made again at every reading (every 2 seconds), and the rings do not come back until the helper sends another message. Measured on the real `HelperSessions` joined to
`TerminalPage`, ten of the helpers working: 40 and 64 helpers: 10 rings on every reading; 65: 9, then 0 from the second reading on; 70: 4, then 0; 100: 0. Dan's own number of helpers is far below 64, hence LOW.
Test: `HelperSessionsAttackTests.Defect_Helpers_Found_Only_By_Name_Push_Out_The_Sessions_That_Have_A_State`. (Holds beside it: with 60 terminals each with a helper, every ring is right; with 100, the page keeps 100 tiles and at most 64 states, no exception.)

**A11-11 - a connected helper inside a helper is counted.**
`src/Island.Core/Terminals/HelperFinder.cs`, `FindByName` (the exception) against `Collect`. Work order section 2: "a helper process that has another helper process among its ancestors is not counted: it belongs to that one." The exception
is applied to helpers found by name. A Codex that Claude Code started (a tool call) is connected, reports through a hook, and its session hangs on its own process, which `Collect` does not drop. Failing input: one terminal window,
`claude.exe` (300) idle, its child `codex.exe` (310), one `UserPromptSubmit` message of helper `codex` with a chain through 310 and 300. The tile has 2 dots and reads "Codex" (the most urgent); expected: no dots, "Claude Code".
T1's report states the same doubt (its section 4, third bullet), taken the other way. A literal reading; if the owner meant "the inner one's state shows on the outer's tile", close this one.
Test: `HelperSessionsAttackTests.Defect_A_Connected_Helper_Inside_A_Helper_Is_Counted_Though_The_Work_Order_Says_It_Is_Not`. (Holds: a helper inside a helper inside a helper, found by name, is one helper, the outermost.)

**A11-01 - two helper-named processes that are each other's parent both vanish.**
`HelperFinder.FindByName`: the filter `!h.Chain.Skip(1).Any(countedIds.Contains)`. Windows reuses process ids, so a stale parent id can name a newer process that is itself a descendant: `claude.exe` 300 with parent 301 and `claude.exe` 301 with
parent 300. Each is "inside the other", both are dropped, the terminal shows no helper. The loop itself is handled (`ProcessChain` stops at a repeat; a three-process loop and a process that is its own parent neither hang nor throw).
Expected: at least one of them still counts. Test: `HelperAttackTests.Defect_Two_Helpers_That_Are_Each_The_Others_Parent_Both_Vanish`.

**A11-05 - the second row opens on the Terminals page during the swap onto it.**
`src/Island.Core/IslandMachine.cs`, `ToggleSecondRow`: it refuses when `ContentsPageId == Terminals`, and `ContentsPageId` lags `PageId` by the swap delay (120 ms). After `PageKey("terminals")`, a `ToggleSecondRow` 30 ms later
(a click on the old page's + tile: whether that tile is still clickable while the contents go out was not checked in the WPF code) sets `SecondRowOpen` and the height target to two rows; the swap then lays the Terminals page out with `SecondRowOpen == true`. After 2 seconds
it is still true. Work order: "On this page there is no + tile and no second row." The keyboard cannot do it (`IslandKeys` asks `PageChangeUnderWay`); only a click on the + tile can. Expected: the guard also looks at `PageId`.
Test: `MachineAttackTests.Defect_The_Second_Row_Can_Be_Opened_On_The_Terminals_Page_During_The_Swap_Onto_It`. (Holds: once laid out it cannot be opened; a page key closes a row that was open.)

**A11-08 - the log guard does not read all the new files.**
`tests/Island.Tests/GuardTests.cs`, `A_Title_Or_A_Process_Name_Never_Reaches_A_Log_Call`: a named list plus three folders. Not read: `src/Island.App/HelperSessions.cs` and `OutsideCodexSettings.cs`, `src/Island.Notify/ProcessSnapshot.cs`,
`src/Island.Agents/AgentPipeSecurity.cs`, `src/Island.Core/TerminalsPageRefusals.cs` and the fifteen files directly in `src/Island.Core/Agents/` (`HookInput`, `AgentNotice`, `AgentSignalTables`, `NotifyArguments`, `HookInstaller`, `AgentWire`,
`AgentText`, `ProjectName`, ...). The attack's own scan (below) reads them all and finds no leak, so nothing is wrong in them today; the guard just does not look. Expected: every file of the set is read.
Test: `SourceScanAttackTests.Defect_The_Guard_That_Reads_The_New_Files_Does_Not_Read_All_Of_Them`.

**A11-09 - the guard's rule has holes.**
`tests/Island.Tests/GuardRules.cs`, `SensitiveReachesALog`: `LogCall` needs `Write` followed at once by `(` or `.`, so `File.WriteAllText`, `WriteAllLines` and `File.AppendAllText` are not calls it sees; an exception carrying a title
(`throw new ...`) is not seen; `Sensitive` has no `ProcessId`, `OwnerProcessId`, `Handle`, `Pid`, which the work order keeps out of every file as well. Six invented lines pass the rule; all six should be hits.
Test: `SourceScanAttackTests.Defect_The_Guard_Rule_Does_Not_See_A_File_Write_Or_An_Id_Of_A_Process`.

**A11-06 - Disconnect does not give back an empty list or an empty `hooks` object that the person had.** (cosmetic)
`HookInstaller.Disconnect` and `CodexHooks.Disconnect`: the comment says "a list the person left empty stays", but a list that Connect added our group to is taken out when it is emptied, and so is a `"hooks": {}`.
`{"hooks":{"Stop":[]}}` -> Connect -> Disconnect gives `{}`; `{"a":1,"hooks":{}}` gives `{"a":1}`. Both mean "no hooks", nothing runs differently; the file is not the file Connect was given. Disconnect cannot know whether the empty
container was the person's or ours once ours was added; closing this with a note is fair. Test: `InstallerAttackTests.Defect_Disconnect_Does_Not_Give_Back_An_Empty_List_Or_An_Empty_Hooks_Object_That_The_Person_Had`.

### LATENT

**A11-04 - one repeated process id throws out of an unguarded timer callback.**
`src/Island.App/HelperSessions.cs`, `ApplyReading`: `processes.ToDictionary(p => p.Id, ...)`. `TerminalsPage.ProbeOnce` calls it outside any `try`, on a `System.Threading.Timer` thread, where an unhandled exception ends the app. Every other reader of
the same list (`HelperFinder.FindByName`, `SessionTracker.ApplyReading`) uses `TryAdd` for exactly this. One snapshot of the process list names each id once, so nothing reaches it today.
Test: `HelperSessionsAttackTests.Defect_A_Process_List_That_Names_One_Id_Twice_Throws_Out_Of_The_Reading_Of_The_App` (`ArgumentException: An item with the same key has already been added. Key: 300`).

**A11-02 - two sessions with no process collide on one key.**
`HelperFinder.Collect`: a session with no process is keyed `-chain[0]`, or `-1 - i` (its place in the sorted list) when its chain is empty. A session with chain `[5]` is keyed -5; the fifth session (i = 4) with an empty chain is keyed -5 too and
replaces it. The app hands over only shown sessions, which always have a chain, so nothing reaches it today. Test: `HelperAttackTests.Defect_Two_Unplaced_Sessions_Collide_On_One_Key_Though_Their_Chains_Differ`.

**A11-07 - `--event` cannot work.**
`src/Island.Core/Agents/HookInput.cs`, `HookInputReader.Read` refuses an input with no `hook_event_name`; `Island.Notify` ends there, before `options.Event` is read. Work order: "`--event` is for a helper whose input does not name its event."
Input `{"session_id":"s9","cwd":"Q:\\Invented\\Alpha"}` with `--agent codex --event Stop`: the program ends with code 0 and sends nothing (expected one message, event `Stop`). Gemini and Antigravity's terminal program, the helpers it is for, are BLOCKED, so no
connected helper needs it. Test: `NotifyAttackTests.Defect_Notify_With_Event_Argument_And_An_Input_Without_An_Event_Name_Sends_Nothing` (through the built program).

## Readings the tests could not prove (the code is WPF or touches the real disk)

Each is a reading of the source, not a failing test. Graded the cautious way.

- **R-1 (LOW) A click is resolved by index, not by window.** `src/Island.App/IslandRuntime.cs` (`ItemActivated`) passes the clicked index to `PickPages.Click(page, index)`, which looks the tile up in `ItemsOf(page)` at that moment, i.e. in the current list. The row on screen is
  redrawn by `ui.BeginInvoke(Controller.ItemsChanged)` after the list changed. A click handled in between, with a window to its left closed, brings the neighbouring window forward. Tiles never reorder and a new tile goes on the right, so only a close to the left of the clicked
  tile, within one dispatcher hop, can do it. The tile has the identity (`WindowKey`); `Click` could take it.
- **R-2 (LOW) `CopyNotify` can leave a mixed copy.** `OutsideAgentConnector.CopyNotify` (`OutsideClaudeSettings.cs`, also used by Codex) copies `.exe`, `.dll`, `.deps.json`, `.runtimeconfig.json`, then `Island.Core.dll`, over the running copy. While a
  hook is running (every tool call of a busy Claude Code starts one) the copy of a loaded file fails; if it fails after `Island.Notify.dll` was replaced and before `Island.Core.dll`, the folder holds a new `Island.Notify.dll` and an old `Island.Core.dll` until the next
  successful Update, and the program may not even start (a type-load failure before `Main`'s `try`, so a non-zero exit and text on stderr, against "ends with code 0 on every path"). The refusal shown is `NOTIFY_MISSING` ("not next to Island"), which is not what happened.
  Copying `Island.Core.dll` first, or to a new folder and then naming it, would close it. Not run: `CopyNotify` writes under the real `%LOCALAPPDATA%`.
- **R-3 (doubt) The shell form of Codex's entry.** `"<path>" --agent codex` is the only form Codex's page gives (T3 says so). In PowerShell a line that starts with a quoted string is a parse error; under `cmd /c` the rule "strip the first and last quote" applies when the path holds
  `&`, `(`, `)`, `^` and the quotes are not the two only ones: `CodexHooks.IsAcceptableCommand` accepts `&`, `(`, `)`, `^` (a test pins that). Which shell Codex uses is UNVERIFIED; no shell was started. Dan's check ("does its tile get a ring?") settles the plain case; a profile folder with `&` or `(`
  is the case it will not settle.
- **R-4 (observation) The probe says the fact did not hold here.** `review/probe-console.md`: no console window owned by this process or an ancestor was found. For a classic console window (`ConsoleWindowClass`), the process that owns it is thought to be the console host, which is thought to be a child of the program running in it, not an ancestor (UNVERIFIED); if so, rule 1 and rule 2 (the chain)
  cannot reach it. Windows Terminal (`CASCADIA_HOSTING_WINDOW_CLASS`, owned by `WindowsTerminal.exe`, an ancestor of the shell) is reached by rule 2. Whether a console host is a child of its client was not shown on this laptop (no console was attached to, none started).
- **R-5 (observation) A new session shows up to two seconds late.** A session is shown only after the reading that chooses its process, and `HelperSessions.Apply` raises `Changed` only when a shown session changed, so the first message after a helper starts wakes nothing; the ring waits for the next reading (up to `ProbePeriod`, 2 seconds). Pinned by
  `HelperSessionsAttackTests.Holds_The_First_Message_Of_A_Session_Raises_No_Change_Until_A_Reading_Has_Placed_It`. It follows the work order ("chosen at the first reading ... after its first message").
- **R-6 (observation) Two processes, one session id.** The key is helper + session id, so a conversation taken up in a second terminal under the same id is one session and its states land on the first process's window; the second window gets no ring. By the work order's own key. Whether Claude Code reuses an id on resume is UNVERIFIED.
  Ids that differ only by characters that are cleaned away (a zero-width space) are one id (both pinned by `Holds_` tests).
- **R-7 (observation) A symbolic link as the settings file.** `Write` moves a temporary file over `settings.json` / `hooks.json`: a link is replaced by an ordinary file (the copy beside it holds the content). Rare on Windows.
- **R-8 (observation) The face letters of a helper tile** are `PickItems.Mark(project)`, which takes whole text elements: a project folder named with a letter and forty combining marks gives forty characters of "two letters". Cosmetic; the project name is cleaned and cut to 40.

## What held (by test name)

Classes are in `tests/Island.Attack11.Tests/`.

**Tiles and windows (section 1), `TileAttackTests`, `FuzzAttackTests`:** `Holds_A_Hundred_Windows_Are_A_Hundred_Tiles_In_The_Order_They_Were_First_Seen` (shuffled readings, one removed in the middle, one new on the right);
`Holds_A_Hundred_Windows_Each_With_A_Helper_Work_Out_In_Bounded_Time`; `Holds_A_Thousand_Windows_Do_Not_Throw_Or_Grow_Without_Bound` (1000 windows 10 s bound; measured 1000 windows with 2000 helpers: about one second);
`Holds_A_Window_That_Closed_Leaves_The_Other_Tiles_Where_They_Were`; `Holds_A_Closed_Window_That_Comes_Back_Under_The_Same_Handle_Goes_To_The_Right`; titles: empty, blanks, zero-width, control, bidi, braille blank, a lone surrogate (all read as the program's name),
a megabyte (cut to 40, quick), a megabyte of astral letters (no half pair), control characters and escape sequences (none reaches a tile), 500 readings with a new title each (`Holds_A_Title_That_Changes_On_Every_Reading_Never_Moves_Or_Duplicates_A_Tile`),
the face letters never change with the title; null fields (`Holds_Null_Fields_From_A_Marshalling_Slip_Never_Throw`), a null reading, one handle twice, class beats program, empty tables, a null table, an empty package prefix that must not take every window.
`FuzzAttackTests.Holds_Twenty_Thousand_Random_Worlds_Give_Sound_Tiles_And_The_Same_Tiles_Twice` (random windows, processes with loops and repeated ids, console windows, sessions with odd chains and states out of range: the tiles are exactly the page's windows,
lines bounded and free of control characters, dots at most 5, an AI tile never names a helper, a helper disc only on a terminal, the same reading twice gives the same tiles) and `Holds_Random_Readings_One_After_The_Other_Never_Move_A_Tile_That_Stays` (300 runs of 15 readings).

**Helpers (section 2), `HelperAttackTests`:** a helper by name under a shell takes its window's tile; a process list that loops, a process that is its own parent, a chain that loops, an empty chain, chains of 0 / negative / gone processes, a chain of ten thousand (cut at 32, quick);
a console window whose owner window is gone falls back to the old guess; a console window that names a window that is not a terminal is not followed; a helper inside a helper inside a helper is one helper, the outermost; two separate helpers are two; the Claude program under its own window is not a helper and changes no tile,
without a window it is a helper with no tile and the terminal stays plain, a hook session inside it gains only the ring and the dots; a hook session never makes a tile for a window that is not on the page; sessions with odd fields and a state out of range never throw; the most urgent of three helpers decides the ring and the texts;
five hundred sessions cap the dots at five; two unplaced sessions with one first process are one helper.

**Sessions and messages (section 3), `SessionAttackTests`, `WireAttackTests`, `HelperSessionsAttackTests`, `FuzzAttackTests`:** `Holds_Every_Sequence_Of_Four_Signals_Ends_In_The_State_The_Work_Order_Gives` for Claude Code and for Codex (8 signals, 4096 sequences each, against a model written from the work order, state and held tool name checked after every step);
a late tool message never revives a finished session; messages out of order, 1000 repeats (one session), 10 000 different sessions (64 kept), a time from the future (counts as now, blocks nothing later), zero and negative times, an end from the future, an end heard first; a session id of path characters
(twelve shapes: `..\..`, `../../etc`, drive paths, `\\?\`, UNC, `CON`, `NUL:`, `%USERPROFILE%`, `$(whoami)`, `.`, `..`) is only text compared with other ids, in the book, on the wire, through a real pipe and through the built program, which opens no file; an id over the limit is cut by the book and refused on the wire;
the same id on two helpers is two sessions; a process id reused by another program, by the same program under another parent, and a reused id whose old session ended: the session ends and a new one starts; a process list with one id twice does not throw in the book;
found by name twice and its first message are one session; a hook chain through three helpers hangs on the nearest; one helper's table does not answer for another; a helper's name in another case is the same helper; eight threads on the book (no exception, no deadlock, at most 64);
every Codex row of the joined table maps to the signal its row names; signal names as text. `HelperSessionsAttackTests`: a helper in a terminal goes through every signal to its ring (and the text under the tile) and back to a plain terminal when its process is gone; sixty terminals, each with its own helper and state, are all right;
one conversation in the Claude program gets the ring and keeps the texts; an empty reading drops every session that hung on a process; a message for a helper with no rows changes nothing and raises nothing.
`FuzzAttackTests.Holds_Random_Messages_And_Readings_Keep_The_Book_Within_Its_Rules` (200 books of 400 random steps: at most 64, ids unique, every shown session has a process or a chain, a tool name only while it needs you).
`WireAttackTests`: the good message; 23 shapes out of shape refused (version 0 and 3, a repeated property, an unknown one, a missing one, a wrong-typed first property that must not let a second through, null, negative / fractional / string / over-int64 time, chain entries 0, negative, fractional, nested, an object, 17 entries, an array, `null`, trailing text);
version one still taken as Claude Code with no id and no time; version three refused by both parsers and by the old one; the limit of 4096 bytes to the byte (taken at 4096, refused at 4097); a megabyte refused at once; every text taken at its limit and refused one over; the encoder never writes more than 4096 bytes and what it writes is taken back (300 random inputs);
3000 random byte strings never throw; a real pipe takes only version 1 or 2 and survives version 3, a megabyte, 5000 braces and binary; 200 messages from 20 threads all arrive; a handler that throws does not stop the server; the notice of a version-2 message follows the table
(Claude Code's `Stop` yes; `StopFailure`, `UserPromptSubmit`, an unknown helper no; Codex's `PermissionRequest` yes, `Interrupt` no).

**Pages and picks (section 1), `PagesAttackTests`:** an old pages file gains Terminals at the end and is not rewritten; `terminals` twice (also under one name) is unreadable; in the middle (position 1, 3, 6) it keeps its place and its digit; renamed it keeps its name and stays built in; under another built-in's name or with a capital in the id it is unreadable;
another built-in missing beside it is still unreadable; nine pages without it say there is no room and nothing is added, eight leave exactly one place (its digit is 9), nine with it load and ten do not; room is looked at again at every load; a store with no room refuses a tenth page;
a page of his named "Terminals" in four spellings makes the built-in "Terminals 2", with "Terminals" and "terminals 2" it is "Terminals 3", names never clash and the result saves and loads back; the built-in page cannot be deleted or renamed onto a taken name, and a page cannot be created under its name;
a pick that names the page is not loaded and the file is not rewritten; no way puts a pick on it (add, move, replace page, restore, the settings session's five doors, suggestion, starter picks); a settings file with no key entry, an empty one, or a valid one for `terminals` loads.

**The machine and every key (section 1), `MachineAttackTests`:** the selection follows its window when tiles to its left close or come; when its own window closes it goes to the tile in its place, or the last, or to nothing and then to the first that arrives; a change of tiles is no page swap, does not count as use, and the width follows through the springs (0, 1, 3, 7, 8, 40, 100 tiles: the same width from 7 up);
`Holds_Tiles_Coming_And_Going_On_Every_Frame_Of_A_Page_Change_Leave_A_Sound_Machine` (four page changes, 30 trials of 90 frames each, a tile added, removed or all removed at random on every frame: selection in range, width finite and equal to the layout's at rest, no pending Delete, the page arrived);
tiles changing during the fly-in and while hidden; every one of 13 keys, plain, shifted and held, with 0, 1 and 4 tiles, never breaks the page and never opens a second row; Delete twice does nothing to a tile; Space and Down only count as use; Enter acts only when there is a tile; Left and Right stop at the ends; Tab and Shift+Tab go round; the sixth digit reaches the page and the seventh does nothing;
a click on a tile that is gone (index past the end, negative, `int.MaxValue`) selects nothing and throws nothing; the machine never takes a tile for a pick; a page list that loses the page falls back; a page list that puts Terminals first is sound.

**Installers (section 3 and 5), `InstallerAttackTests`, both `HookInstaller` and `CodexHooks` on every case:** an empty file (null, empty, blanks, a byte-order mark alone, with blanks) is connected from nothing and disconnected to `{}`; a file with a byte-order mark is read and its content comes back (the mark is not written back: fine for Claude Code and Codex);
someone else's hooks (six events, matchers, `args`, `async`, `timeout`, an `http` handler, numbers `1E5`, `-0`, `12345678901234567890`, `1e999`, non-ASCII and escapes) are kept in their order and come back after Disconnect; Connect twice changes nothing the second time; Disconnect takes out only what runs Island.Notify and not eleven lookalikes
(`Island.Notify2.exe`, `NotIsland.Notify.exe`, `Island.Notify.exe.bak`, a folder named Island.Notify, `node ...Island.Notify.js`, `python ...`, `echo Island.Notify`, ...); our handler between two of theirs is taken out and they close ranks;
`Holds_Random_Foreign_Files_Round_Trip_Exactly` (300 random files for each: Connect gives `Connected`, a second Connect changes nothing, Disconnect gives back the file); old entries (`args: []`) read as connected, older, are updated only where missing, are not doubled even under another folder, an update interrupted halfway is finished by the next, Disconnect removes old and new alike;
Codex with only some of its events is connected, older and Connect fills only what is missing; 33 files that cannot be read (17 for Claude Code, 16 for Codex) (not JSON, an array, `null`, a number, `hooks` an array / a number / null, an event an object / a string / a number, repeated properties, a trailing comma, a comment, two values) come back unchanged with `HOOKS_FILE_UNREADABLE`;
a lone surrogate escape in someone else's value leaves the file as it is; nesting at 200 is refused, at 60 it is handled; a 3 MB file with 20 000 groups round trips in seconds; what Claude's Connect will write (an absolute local path to its own program, 14 cases) and what Codex's will quote (a quote, `%`, `$`, a backtick, a control character, a trailing blank refused; `&`, `(`, `)`, `^` accepted, see R-3);
the question shows exactly what Connect writes; every entry starts the program directly (type `command`) in the background (`async`).

**Island.Notify (section 3), `NotifyAttackTests`:** the parser against 39 wrong arguments (an option without its value, a repeated option, a name that is not plain, a hyphen where the pipe name should be, `--agent=x`, `-agent`, `--AGENT`, `--`, `-`, a path, a pipe path, a name over the limit or a megabyte): `null` every time, and never the real pipe; the good forms in any order;
a second pipe name or an unknown option after the pipe name is ignored as it always was. The built program: a good call reaches the island as version 2 with agent, session, kind and a time that lies between the test's two readings of the counter, with the test's own process id first in the chain; the old entry with no `--agent` still means Claude Code;
every one of 15 wrong calls (each followed by the invented pipe name) sends nothing, prints nothing and ends with 0; an agent or an event the table does not know stays silent; Claude Code's events do not pass for Codex and back; a permission request carries the tool's name; a path-shaped session id arrives as text and opens no file; nobody listening is quick and silent for both agents;
two calls one after the other carry times that do not run backwards by more than a timer tick. The reader: the session id is read from a whole input and from one cut at the limit, never half of one; another type gives "".

**The source search, `SourceScanAttackTests`, `GateAttackTests`:** the scan reads 42 files (`src/Island.Core/Terminals`, `src/Island.Core/Agents` with `Sessions` and `Connect`, `src/Island.Agents`, `src/Island.Notify`, `TerminalsPage.cs`, `HelperSessions.cs`, `OutsideCodexSettings.cs`, `TerminalProbe.cs` of the sources and of the core, `TerminalsPageRefusals.cs`), strips comments, and
lists every statement that calls something that logs, writes, serializes or throws: **48 statements, 12 of them name something sensitive (title, folder, session id, process name or id, chain, helper, project, handle, key; case ignored), none leaks.** The 12: the two wire encoders write the session id, the folder's end, the helper and the chain into the message that goes through the pipe
(`AgentWire.cs`, `SessionWire.cs`: a memory stream, then the pipe: the island's memory, by design), `AgentPipeSecurity.cs` names the Windows user's SID and a fixed sentence, `OutsideCodexSettings.cs` creates Codex's own folder and copies its own file. The other 36 are the JSON writers of the installers (our own command line), `AgentText`'s string appends, `Console.OpenStandardInput`,
the pipe write, `File.Exists/ReadAllText/Copy/WriteAllText/Move` of the one connector. `Holds_Every_File_Write_Of_The_New_Code_Is_In_One_Outside_File_That_Asks_The_Gate` (the only file with a file write is `OutsideCodexSettings.cs`, and it names `OutsideGate`);
`Holds_Nothing_Of_The_New_Code_Reads_A_Session_Id_Or_A_Title_Into_A_Path_Call`; `Holds_The_New_Code_Has_No_Attach_Read_Memory_Or_Command_Line_Call` (no `AttachConsole`, `FreeConsole`, `ReadProcessMemory`, `NtQueryInformationProcess`, `GetCommandLine`, `OpenProcess`, `MainModule`, `GetProcessesByName`, `Process.Start`, `SendInput`, `SetForegroundWindow`, `PostMessage`, ...);
`Holds_No_Real_Pipe_Name_Is_Spelled_Out_In_The_New_Tests_Of_This_Attack`. `GateAttackTests`: in both connectors every way in (`State`, `ConnectOrUpdate`, `Disconnect`) asks the gate before it reads or writes; `Connect` and `Update` only forward; no public member takes a string, a path or a stream; the place of the settings file is spelled once, in one private method;
only the settings screen's section and the session press a connector.

## Commands and totals

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test ".worktrees\attack11\tests\Island.Attack11.Tests"
    Failed!  - Failed: 11, Passed: 373, Skipped: 0, Total: 384, Duration: 15 s
    (the 11 failures are exactly the 11 Defect_ methods; every Holds_ passes)
dotnet test ".worktrees\attack11\tests\Island.Tests"
    Failed!  - Failed: 1, Passed: 1636, Skipped: 0, Total: 1637, Duration: 28 s
    the one failure: ExtensionGuardTests.The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme (a byte comparison of an add-on file, CR LF against LF in this worktree's checkout; the three
    piece agents T1, T2 and T3 each reported the same one in their own worktrees). `git diff --stat 29e6865 HEAD` touches only tests/Island.Attack11.Tests/ and this report, so it is not from this work.
dotnet test "...\tests\Island.Attack11.Tests" --filter "FullyQualifiedName~Defect_"      (the 11 red ones alone)
dotnet test "...\tests\Island.Attack11.Tests" --filter "FullyQualifiedName~Holds_"       (all green)
```

The 11 `Defect_` tests, one per finding: A11-01 `HelperAttackTests.Defect_Two_Helpers_That_Are_Each_The_Others_Parent_Both_Vanish`; A11-02 `HelperAttackTests.Defect_Two_Unplaced_Sessions_Collide_On_One_Key_Though_Their_Chains_Differ`;
A11-03 `HelperSessionsAttackTests.Defect_Two_Conversations_In_The_Claude_Program_Collapse_To_The_One_Heard_Last`; A11-04 `HelperSessionsAttackTests.Defect_A_Process_List_That_Names_One_Id_Twice_Throws_Out_Of_The_Reading_Of_The_App`;
A11-05 `MachineAttackTests.Defect_The_Second_Row_Can_Be_Opened_On_The_Terminals_Page_During_The_Swap_Onto_It`; A11-06 `InstallerAttackTests.Defect_Disconnect_Does_Not_Give_Back_An_Empty_List_Or_An_Empty_Hooks_Object_That_The_Person_Had`;
A11-07 `NotifyAttackTests.Defect_Notify_With_Event_Argument_And_An_Input_Without_An_Event_Name_Sends_Nothing`; A11-08 `SourceScanAttackTests.Defect_The_Guard_That_Reads_The_New_Files_Does_Not_Read_All_Of_Them`;
A11-09 `SourceScanAttackTests.Defect_The_Guard_Rule_Does_Not_See_A_File_Write_Or_An_Id_Of_A_Process`; A11-10 `HelperSessionsAttackTests.Defect_Helpers_Found_Only_By_Name_Push_Out_The_Sessions_That_Have_A_State`;
A11-11 `HelperSessionsAttackTests.Defect_A_Connected_Helper_Inside_A_Helper_Is_Counted_Though_The_Work_Order_Says_It_Is_Not`.

The `Notify` tests start the built `Island.Notify.exe` some thirty times (about 0.1 s each); the project references `Island.Notify` and `Island.Agents` so the exe is built beside the tests, as `Island.Agents.Tests` does. The project is `net10.0-windows` for that reason.

## What could not be checked

- Anything WPF (`IslandController`, `TerminalsPage`, `ContentsLayer`, `TileView`, `PickPages`, the settings screens): read, and rebuilt over Core where the logic is in Core (the keys and the machine, `MachineAttackTests`; `HelperSessions` linked as is). The ring's drawing, the in-place redraw of a changed tile, the X button's dimming and the search that leaves the page out are
  not looked at by any test here.
- The real Windows facts: whether a console host is a child of its client (R-4); which shell Codex uses for a hook (R-3); whether Claude Code and Codex accept the entries; whether a hidden console window leads to its terminal on Dan's laptop (the probe says it did not here).
- `TerminalProbe` and `ProcessList` (they read the real windows and the real process list; no test calls them). Read: the structure of `PROCESSENTRY32W` matches the one in `Island.Notify`, the snapshot handle is closed in a `finally`, a failed call gives an empty list.
- `OutsideCodexConnector` and `OutsideAgentConnector` on a disk: read, and checked by `GateAttackTests`; nothing under the real profile was opened.
- The self-test, `selftest.json`, STATE.md and LOG.md (the self-test must not be run here).
- Timing of two real hooks within one timer tick (about 15 ms): the two messages can swap order; the only pairs that matter (`SessionStart`/`UserPromptSubmit` and `Stop`/`SessionEnd` of a `claude -p` run) end in states that the cleanup of a gone process hides.

## Requests to the main session

All of these are changes outside this project; none was made. After each fix, its `Defect_` test turns green; do not weaken the test (the expected behaviour is in its comment).

1. Add `tests/Island.Attack11.Tests` to `Island.sln` (and it needs `src/Island.Notify` and `src/Island.Agents` built, which its project references do).
2. **A11-03:** key a session that hangs on a process that owns an AI window by the session itself, not by the process (`HelperSessionFact.LastHeard` is the book's unique session id: a key such as `-(1_000_000 + LastHeard)`), so each conversation is its own helper (dots and the most urgent state follow).
3. **A11-10:** do not let a session that was never heard from push out one that was: made-by-name sessions should rank as the oldest, or not count toward the 64 until a message arrives.
4. **A11-11:** decide the reading (inner connected helper folded into the outer, or shown); if folded, give `Collect` the same exception as `FindByName` (a session whose process has a helper among its ancestors is not a helper of its own).
5. **A11-01:** break a loop of two helper-named processes before the "inside another helper" filter (drop an ancestor that is itself a descendant of the helper), so at least one counts.
6. **A11-05:** `ToggleSecondRow` should refuse also while `PageId == Terminals` (the page the swap is heading for).
7. **A11-08, A11-09:** read all of the new files in the guard (the folders `Island.Core/Agents` and the files listed above) and widen `LogCall` (`Write\w*`, `Append\w*`, `throw new`) and `Sensitive` (`ProcessId`, `OwnerProcessId`, `Pid`, `Handle`); then the real code must still be clean (the attack's scan says it is).
8. **A11-04:** `HelperSessions.ApplyReading` with `TryAdd`; put `TerminalsPage.ProbeOnce`'s call to it inside the same `try` as the probe.
9. **A11-02, A11-07:** only if a helper that needs them is connected (both are for the blocked helpers); the fix of A11-07 is a `HookInputReader.Read` that takes the event from the argument when the input has none.
10. **A11-06:** close with a note, or leave the `Defect_` test out; Disconnect cannot tell an empty container the person had from one it emptied.
11. The three readings that need a person or a disk: R-1 (resolve a click by `WindowKey`), R-2 (copy `Island.Core.dll` first, or copy to a new folder and name that), R-3 (Dan's check: "after Connect, does Codex's tile get a ring?"; if Codex runs hooks through PowerShell, the entry needs `commandWindows` with `& "<path>" --agent codex`, which T3 found on the page and did not add from a guess).
12. For the close-out's "not proven": the probe's UNVERIFIED (R-4); the ring's look; whether Claude Code reuses a session id on resume (R-6).
