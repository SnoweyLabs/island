# ATTACK9 - the add-on walk, the content scripts, the browser fallback, the status row (WORK-ORDER-9)

Branch `attack9`, from e36a0d3. Tests only; no line of the code under test was changed. A test named `Defect_<what>` fails because of the defect it names;
a test named `Holds_<what>` passes and is the coverage statement. Everything ran against pretend browsers, pages and add-ons (Node `vm` contexts; the
island's own `PretendAddon` on a free port). No browser, no app, no self-test, no hook, no network beyond the loopback sockets of the test bridge.

Where the tests are:
- `extension/tests/attack9.test.js` (+ the fixture `extension/tests/attack9-legacy-media-main.js`, the 1.0.0 page script as git holds it): `node --test extension/tests/attack9.test.js`.
  38 tests: 19 pass, 19 fail on purpose. The other 76 add-on tests still pass (`node --test` on the older files).
- `tests/Island.Attack9.Tests/` (not in `Island.sln`; build it by path): 39 tests: 28 pass, 11 fail on purpose (the theory with two cases counts twice).

## Totals

27 defects: HIGH 0, MEDIUM 4, LOW 12, LATENT 11. (30 failing test cases: defect A1 has three, F1 has two cases.)
LATENT = nothing triggers it today, or the trigger needs a thing that is UNVERIFIED in a real browser, or another test already stops it.

## Contradictions with existing tests (the existing test wins; nothing was edited)

- WORK-ORDER-9 section 3 asks for the fallback only when *exactly one* browser session says it is playing; `BrowserFallbackTests.Two_Browsers_And_A_Vanishing_Session_Never_Make_Two_Items_Or_A_Crash`
  pins "the first playing session is taken". See F2. The new test asks only that the answer not depend on the order of the list.
- WORK-ORDER-9 section 2 asks for one refusal line and a total when the listener stops; `AddonStatusTests.A_Knocking_Program_Does_Not_Fill_The_Log` pins "first ten, then every hundredth"
  for 1 to 1000. See B3, B4: the new tests speak only about a million knocks and about the total at the end, which the existing test does not cover.
- WORK-ORDER-9 section 1 says the page-world script has "no versions and no taking over", and that the relay sets a mark in its own world. The code does the opposite on both
  (versioned takeover in `media-main.js`; no mark in `media-relay.js`) and the file `media-relay.test.js` ("a second relay in one world does nothing") does not exist. See A1, A7.

## Defects

### MEDIUM

**A1 - the relay has no mark; a second relay in one world works beside the first.** `extension/content/media-relay.js` (whole file; the order wanted a mark in its own world).
How it happens: `relayAlive()` in `background.js` treats an answer after 1.5 s as no answer (`Holds_Walk_An_Answer_After_The_Wait_Counts_As_No_Answer`), so a tab whose live relay is slow
(a busy page, a tab just restored at browser start) is given the relay again; both run in the one isolated world. Effect: every `media` report is sent twice, and each command is posted to the page
twice, so one press of play/pause toggles the player twice and nothing happens (pause 1, play 1).
Tests: `Defect_Second_Relay_In_One_World_Does_Not_Stand_Down`, `Defect_Second_Relay_Doubles_Media_Reports`, `Defect_Second_Relay_Doubles_Every_Command_So_Play_Pause_Cancels_Itself`.
Control: `Holds_One_Relay_One_Press_One_Toggle`.

**A2 - the 1.0.0 page script is recognised only by the word `lastPlayed` in the text of the current `play()`.** `media-main.js`, `earlierCopyIsHere()`.
Input: the 1.0.0 copy is in the page (it is in every tab that was open before the add-on was updated, which is what the walk is for) and the page then wrapped `HTMLMediaElement.prototype.play`
itself. The text no longer holds the word, the new copy starts beside the unmarked old one, and each press runs in both (two toggles). Whether any of the five sites wraps `play()` is UNVERIFIED.
Test: `Defect_Legacy_Copy_Not_Recognised_When_The_Page_Wrapped_Play_After_It`. Control: `Holds_Legacy_Copy_Is_Recognised_By_Its_Play_Wrapper_And_Left_Alone`.

**F1 - an audible tab that has reported "paused" or "stopped" still counts as "audible and silent".** `NowPlaying.SilentAudibleTab`: `picked.Where(t => t.Audible)` ignores `t.Media`.
The order says a tab that has reported, in any state, speaks for itself. Input: one picked tab, audible, with a `Paused` report; the OS says the browser is playing (another tab of it plays).
Result: a fallback item "playing" with the OS's words is made for the key of that tab; the group-by-key then prefers it to the tab's own paused report, so the line says playing, with another
tab's title, under the picked site's name, for as long as the tab stays audible.
Test: `Defect_An_Audible_Tab_That_Has_Reported_Paused_Is_Still_Taken_For_Silent` (Paused and Stopped).

**F2 - with two playing browser sessions the fallback takes the first one in the list.** `NowPlaying.Candidates`, the `fallbackUsed` branch. The order asks for exactly one playing browser session.
Input: two sessions, both playing, one picked audible silent tab. In one reading order the line shows the first session's title and the buttons go to its session; in the other order, the second.
The title and the buttons' target depend on the order the OS lists the sessions, and a press may pause the other browser. (A tab does not say which browser it belongs to: this is why the order said "exactly one".)
Test: `Defect_Two_Playing_Browser_Sessions_Are_Chosen_By_The_Order_Windows_Lists_Them`. Control: `Holds_Two_Playing_Sessions_Flipping_Their_Order_Between_Readings_Never_Makes_A_Second_Item_Or_A_Gap`
(while the two stay the same pair, the line itself never gaps).

### LOW

**A3 - a frozen tab is not skipped.** `background.js`, `canTakeScripts()` looks at `discarded` and `status`, never at `frozen`. The order: "Tabs that the browser marks as discarded or frozen are skipped"
(and its own test list names a frozen tab; `inject.test.js` tests only a discarded one). A frozen tab is asked and given scripts. Test: `Defect_Frozen_Tab_Is_Not_Skipped`.

**A4 - a page's own `play()` wrapper that mentions `lastPlayed` silences the script on that page.** Same function as A2, the other way round: a false positive. Input: `HTMLMediaElement.prototype.play = function () { this.lastPlayed = Date.now(); ... }` before the
walk's copy runs. The copy returns without starting and without a mark. Test: `Defect_Pages_Own_Play_Wrapper_That_Mentions_lastPlayed_Silences_The_Script`.

**F3 - going back to a tab used a moment ago leaves the line on the other tab for up to the 4 s grace.** `NowPlaying.Candidates` keys the fallback item as `tab:<key of the most recently used audible silent tab>`; `NoteStates` remembers
`Playing` for a key until the grace has passed. Input (OS session playing throughout): tabs a and b audible and silent; b most recent -> line on b; a becomes most recent -> line on a (a new item);
b becomes most recent again at the next reading -> the line stays on a, `CanControl` false, `PlanFor` null, and a click goes to a, until the grace ends.
Test: `Defect_Going_Back_To_A_Tab_Used_A_Moment_Ago_Leaves_The_Line_On_The_Other_One_For_The_Grace_Period`.

**B3 - a refusal flood is written without end.** `AddonLog.ShouldLogRefusal` (first ten, then every hundredth) into `island.log`, which has no size limit (`AppFiles.Log` appends). A page in the browser can open
the port as often as it likes: a million refusals write 10,010 lines. The order said one line, never a line per refusal. Test: `Defect_A_Refusal_Flood_Is_Written_At_One_Line_In_A_Hundred_Without_End`.

**B4 - the total of refusals is not written when the listener stops.** `TabBridge.Dispose` writes nothing. Refusals 11 to 15 of 15 appear nowhere. Test: `Defect_The_Total_Of_Refusals_Is_Not_Written_When_The_Listener_Stops`.

**G1-G7 - the dynamic-code guards (`extension/tests/no_dynamic_code.test.js`, `ExtensionGuardTests.No_Eval_Or_Dynamic_Code` and `Executes_Script_Only_As_Files_Of_The_Addon`) let text through that does what they exist to forbid.**
Not edited. The tests apply a replica of the guards' own patterns (an honesty test checks the replica's patterns are in both guard files, and that the real `background.js` passes) to `background.js` plus one line:
- G1 `chrome.scripting['exec' + 'uteScript']({ target, func: () => 1 })` (computed name; `func` is allowed): `Defect_Guard_Misses_A_Computed_Name_For_ExecuteScript`.
- G2 `chrome.scripting.executeScript({ target, files: [...], ...extra })` (`extra` may hold `func`/`code`): `Defect_Guard_Misses_Options_Spread_Into_ExecuteScript`.
- G3 `const s = chrome.scripting; s.executeScript({ files })` (the order says the full `chrome.scripting.` form only): `Defect_Guard_Misses_A_Namespace_Alias`.
- G4 `files: ['lib/protocol.js']` (the guard checks that the argument says `files:`, not that the files are the manifest's): `Defect_Guard_Does_Not_Check_That_Files_Are_The_Manifests`.
- G5 `globalThis.Function('return 1')()` (the pattern excludes a `.` before `Function`): `Defect_Guard_Misses_Function_Called_Through_A_Global`.
- G6 `(0, eval)('1')`: `Defect_Guard_Misses_Indirect_Eval`.
- G7 `[].constructor.constructor('return 1')()`: `Defect_Guard_Misses_The_Constructor_Chain`.
Control: `Holds_Guards_Catch_The_Plain_Ways` (ten plain spellings are caught, among them `['executeScript']` and a destructured name).

### LATENT

**A5 - a hung `executeScript` keeps the walk open for ever.** `background.js`, `giveScripts()`/`walkTabs()`: the question has a 1.5 s limit, the injection none. One tab whose page never answers keeps `walk` set,
and every later install/start moment of the same worker is swallowed. Test: `Defect_Hung_Script_Injection_Holds_The_Walk_Open_For_Ever`.

**A6 - an orphaned relay would still answer "alive" and still act on commands, if a message reached it.** `media-relay.js`: `connected()` guards the sending side only. Whether a message ever reaches an orphan is UNVERIFIED
(the order says so). If it did: the tab is hidden from the walk, and its commands are doubled by the live relay (as A1). Test: `Defect_Orphan_Relay_Still_Answers_Alive_And_Acts_On_Commands_If_A_Message_Reaches_It`.
Control: `Holds_Orphan_Relay_Sends_Nothing_And_Throws_Nothing`.

**A7 - a newer copy of the page script that takes over loses the media-session handlers the page gave the older copy.** `media-main.js`: `handlers` starts empty in each copy. No newer version exists yet. After a takeover "next" and
"previous" fall back on the guessed buttons. Test: `Defect_Takeover_By_A_Newer_Copy_Loses_The_Handlers_The_Page_Gave_The_Old_One`.

**A8 - the copy throws into the page, and keeps its mark, when `HTMLMediaElement.prototype.play` is read-only.** `media-main.js` is strict; the assignment throws a TypeError after the mark and the media-session wrapper are in and before any
timer or listener. Later copies of the same version find the mark and stay out: the page is silent for good, with one error in its console. Needs a page that froze the prototype. Test: `Defect_Copy_Throws_Into_The_Page_And_Keeps_The_Mark_When_Play_Cannot_Be_Replaced`.

**G8, G9 - the guards do not mention `chrome.userScripts.register` (with `code`) or `chrome.scripting.registerContentScripts`.** Stopped today by `manifest.test.js` (the permission list and the host list are pinned) and by the browser;
reported because the guard's own comment claims script injection is covered. Tests: `Defect_Guard_Does_Not_Mention_UserScripts`, `Defect_Guard_Does_Not_Mention_RegisterContentScripts`.

**G10-G12 - the log guard (`GuardTests.Addon_Log_Never_Names_A_Browser_Or_A_Tab`) is not shown `Log!.Invoke(x)`, an alias of the hook (`var write = Log; write?.Invoke(x)`) or `Log?.DynamicInvoke(x)`.**
Nothing writes these today; the guard also reads only `TabBridge.cs` and `RealWorld.cs` (`Holds_No_Log_Call_In_The_Bridge_Folder_Outside_TabBridge_Exists` shows no other file of the bridge touches the hook).
Tests: `Defect_The_Log_Guard_Misses_A_Null_Forgiving_Invoke`, `..._The_Hook_Called_Through_A_Local_Alias`, `..._A_Dynamic_Invoke`. Control: `Holds_The_Guard_Catches_The_Plain_Ways_To_Put_A_Name_In_The_Log`.

**B1 - a log sink that throws leaks a connection slot.** `TabBridge.Serve`: `Log?.Invoke(Connected)` sits inside the try and `Log?.Invoke(Left)` inside the finally, before `Interlocked.Decrement(ref _open)`.
Input: a sink that throws on those two kinds; after eight attempts every add-on is turned away for the rest of the run (the test shows the counter of turned-away connections at 3 after ten attempts, and a good add-on is then refused
although the sink was fixed). The island's real sink catches only `IOException` and `UnauthorizedAccessException`, so today this needs an unusual log path. A throwing sink in the `Left` call also skips `MarkChanged`, so the row would stay "Connected".
Test: `Defect_A_Log_Sink_That_Throws_Leaks_A_Connection_Slot_Until_No_Add_On_Can_Connect`.

**B2 - a throwing `Changed` subscriber escapes the timer and blocks the ones after it.** `TabBridge.RaiseChanged` has no guard; it runs on a `Timer`, where an exception is unhandled on a pool thread. The app's own listener (`AppWorld.Raise`)
is guarded, the settings row's handler only posts to the screen's dispatcher. The test calls `RaiseChanged` by reflection, so it cannot take the test host down. Test: `Defect_A_Throwing_Changed_Subscriber_Escapes_The_Timer_And_Blocks_The_Subscribers_After_It`.

## What held

Add-on, the walk (`Holds_Walk_*`): a thousand tabs of which a hundred are eligible (only those are asked; 200 injections); a thousand tabs that never answer cost one 1.5 s wait, not a thousand; tab records that are null, strings, a number,
without an address, with a fractional or negative id, with a getter; `sendMessage` or `executeScript` throwing at once instead of rejecting, `query` throwing, rejecting or returning something that is no list, `getManifest` throwing, no `scripting` object:
no unhandled rejection, the other tabs still served, `walk` reset; moments during a walk make one walk and the next one after is a new walk; a tab that closes or moves away mid-walk; answers that are not `{alive: true}` (`undefined`, `{}`, `'true'`, `1`, `[true]`) are not believed.
Lookalike, huge (200 KB), non-https, userinfo, trailing-dot and homoglyph addresses are never asked and never given scripts.

Add-on, the copies: three copies of the page-world script with versions in every order of a pool of nine (including `1.10.0` against `1.9.0`, empty, garbage, a 3000-digit string, an eight-digit part, `1.1`): 729 orders, each ends with one timer, one
command listener, the mark held by the greatest version (the first of equals), and nothing thrown; mixed versions with a relay report once per tick; an old copy that stops while a command is in flight stands aside and the new one answers once (one toggle);
the 1.0.0 copy with its own wrapper intact is left alone (one toggle, no mark); forged marks (numbers, strings, `null`, `{}`, arrays, functions, wrong-typed or nonsense versions) never throw, never run two reporters, and a forged newer mark only keeps the script out of that one page;
forged `state`, `done` and `command` messages from the page (5 MB titles, NaN, negatives, nesting, wrong types) never throw in the relay and its text is cut to 200 characters; an orphaned relay sends nothing and throws nothing.

Island, the fallback (`FallbackAttackTests`): EVALS N7 over 23,000+ pictures (two tabs of six hosts - picked, unpicked, a lookalike, a different site, empty - times audible or not times four report states, against five session pictures, connected or not): the line
never names a site nobody picked, and the whole browser never takes it with the add-on connected; a report arriving and leaving at random for 400 steps keeps one item, `CanControl` true, no gap, the right target for the buttons at every step; a tab whose sound starts and stops at every step
never empties the line (it empties after a real silence); the OS session vanishing for one reading keeps the item for the grace and the buttons dead meanwhile, and the new session id is used when it comes back; null, empty, whitespace, 1 MB and control-character titles with every
combination of NaN, infinite, negative and huge positions and lengths never throw and never invent progress (always null or in 0..1, only with a length above 0); duplicate keys are one source; equal tab ids of two profiles stay apart; a seeded fuzz of 10,000 random pictures
broke no rule above.

Island, the bridge (`BridgeAttackTests`): a profile that connects again and again (20 times, with the old sockets kept) is one browser and all eight slots come back; a storm of twelve browsers at once respects the cap of eight, recovers, and the log holds only kinds and counts;
a second `hello` on one connection and hellos with bad profiles (empty, space, quote, slash, 65 characters, accented) are not counted; `Changed` is never raised after `Dispose` returned (ten rounds); `Dispose` twice and during a connection does not throw;
refusals over real sockets (with a wrong Origin and with none) are counted and written exactly as the cap says.

## What could not be checked

- Anything about a real browser: the semantics of `executeScript` into the same isolated world the manifest uses (A1 assumes both relays share it, as the order does), whether `tab.frozen` exists and when, whether an orphaned relay is ever reached,
  whether a restored or busy tab answers inside 1.5 s, whether any of the five sites wraps `play()` (A2, A4). All of this is a fake here.
- The worker restarting in the middle of a walk (the walk is not resumed; nothing in the fake can kill a worker).
- The settings row on screen, and whether its dispatcher call can throw after the screen is gone.
- The full existing suite, the build of the solution and the self-test (not run: this pass was told to touch only its own project). The fallback and the status tests were run only through the new project.
- Design limits that the order already states and that are not counted above: the OS names one session for the whole browser, so the title shown for a fallback may belong to another tab of the same browser (an unpicked site included) or to another browser without the add-on;
  a click goes to the tab that is making sound, which may not be that one.
- A race in the refusal counter (`Counts.Add` then `Volatile.Read` in `Handshake.Run`): two refusals at the same instant may log the same number twice and skip a hundredth line. Not reproduced over sockets.
- The log line "add-on left" is written by a connection that ends because of `Dispose`, after `Dispose` has returned. Seen in the code, not pinned by a test (harmless unless the sink is closed by then).
- `TabBridge.Start()` called twice (the first listener is orphaned) and `Start()` after `Dispose()`: seen in the code, not tested; nothing calls them.
