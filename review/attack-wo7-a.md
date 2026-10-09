# Attack on WORK-ORDER-7 sections 1 to 6, the island's states (ATTACK7A, 7 Oct 2026)

Adversarial pass over what WO7 sections 1 to 6 added to `Island.Core`: `IslandMachine` (pill, notice, search, mode-driven leaving), `ShowDecision`,
`NeverOverList`, `NoticeFallback`, `ModeMark`, `PillLayout`, `NoticeLayout`, `SearchLayout`, `SearchKeys`, `SearchMatch`, `SearchServices`, `SiteAddress.ForSearch`,
`LitShare`, `ProgressReport`, `TabMediaTiming`, `RedrawGate`, `NowPlaying`, `NoticeQueue`, `AgentNotice`, `ProjectName`, `AgentWire`, `TerminalChoice`, `ProcessChain`,
`Scene*` (`SceneStore`, `SceneStoreFile`, `ScenePlans`, `SceneRefusals`, `HeldKeyGuard`), scene keys in `SettingsSession`/`KeybindEditor`, the new `Settings` fields, `FirstStart`.

Branch `attack7a`, made from main `8cc1ce8`. Nothing under `src/` and no existing test or file was changed. Tests live in `tests/Island.Attack7A.Tests/` (xunit, references
`Island.Core` only, not in `Island.sln`). Pure and in memory: no pipe (not even an invented one), no window, no sound, nothing started, nothing read from or written to the user's
profile or any `.claude` folder, no "Connect". Search text is invented ("tu", "alpha", noise), project names invented ("island", "work"). Temporary folders are under
`Path.GetTempPath()` and removed afterwards (checked: none left behind). `WORK-ORDER-7.md` is not tracked in git, so it was read from the main folder (read only).

Command and result:
- `dotnet test tests/Island.Attack7A.Tests`: **92 tests, 28 failed, 64 passed.** Every failing test is named `Defect_...` and fails because of the defect it names (17 `Defect_`
  methods; three are theories with 5, 5 and 4 cases, hence 28); every passing test is named `Holds_...` and is the coverage statement (part 3). No `Holds_` test fails and no
  `Defect_` test passes.
- No HIGH defect found. Machine and scene logic held under every storm; the real finds are two MEDIUM-ish state mismatches in the machine and a group of "invisible text" gaps.

## 1. What I added

| File | Content |
|---|---|
| `Island.Attack7A.Tests.csproj` | xunit project, shape of Attack6's, reference to `Island.Core` only |
| `Support.cs` | `PretendRegistrar` (grants and counts held keys), `TempFolder`, `Clock` (frame clock as the app drives the machine), a settings-session builder with a scenes file |
| `MachineAttackTests.cs` | 6 x 5000-step random storms over 22 inputs (main key, tray, play/pause with wanted/allowed, notice on/off, notice/pill/search widths, open/close search, take keyboard, page key incl. unknown id, digit key 0 to 10, Esc, second row, other key, focus lost, activity, item click, contents changed, idle time, page list change) with 40% of the inputs on the very same step, invariants checked after every step and a settle-and-compare check every 150 steps; 3 storms with a 3 s idle time that must always end Hidden when left alone; every order of 5 out of 8 inputs (play, pause, notice, notice gone, main key, page key, search, tray) on ONE timestamp (6720 orders) from five starting states (hidden, pill, capsule, notice, search); non-finite times and widths; machine time 1e12 |
| `ProgressAttackTests.cs` | `LitShare` over 18 positions x 18 lengths x 11 speeds x 10 report times x playing/paused x 4 clocks (285 120 cases incl. NaN, infinities, negatives, denormals, `MaxValue`, `DateTimeOffset.Min/MaxValue`, Windows' zero date), a length that changes every report, `RedrawGate` over odd shares and outlines, `TabMediaTiming.ToReport` with odd `readAt` and `rate`, 3000 `NowPlaying.Update` calls with odd numbers (session and tab, with and without a raw timeline), a position beyond the length, 1000 vanishing sessions |
| `SearchAttackTests.cs` | 41 hostile texts (10 000 characters, only spaces, 10 000 combining marks, zero-width, astral, 5000 emoji, lone and swapped surrogates, RTL override with and without terminator, `/ ? # & + %`, `..`, ligatures, NUL, U+FFFE) through `SearchLayout`, `SearchMatch.Rank/Fold`, `SearchServices.Tile`, `SiteAddress.ForSearch` for all four services; addresses parsed back with `Uri` (scheme, host, one `=` or one path segment, only unreserved characters, decode equals text); 4 x 30 000-step key fuzz over `SearchKeys.Apply`; look-alike hosts; the field limit |
| `AgentAttackTests.cs` | 1000 notices in one second, repeated Stop, waiting behind the capsule, stale, hover cap, `Seconds` clamp; 30 nasty folders (1 MB names, controls, NUL, CR/LF, direction characters, lone surrogates, both separators, trailing dots, `..`, UNC, 100 emoji, stacked accents) through `ProjectName` and `AgentNotice.From`; `NoticeLayout`/`PillLayout` widths; `AgentWire` round trip with wide characters against the 4096-byte limit and 20 000 random garbage frames; `TerminalChoice` with 20 000 windows; `ProcessChain` loops and length |
| `SceneAttackTests.cs` | 100 scenes x 200 things round trip, the 101st scene and 201st thing, a hostile file of 130 scenes x 250 things, scene names (controls, zero-width, direction, lone surrogates, length, spaces, case, blank-looking and look-alike), a scene of site, program, folder and a program no longer installed, `ScenePlans` with 251 things, `SceneRefusals.PartMissing` length over 3000 random cases, `HeldKeyGuard`, scene keys mixed with page, pick, main and mode keys through `SettingsSession` in 3 x 1500-step storms (create, delete, rename, press, clear, restore all) checking after each step that no combination is shared, Windows holds exactly what the settings say, no key belongs to a missing scene, and both files on disk read back equal; unreadable scenes file, folder in the way; hostile scenes files; `FirstStart.ShouldRun` truth table; `NoticeFallback` 3000 x 400-step storm; `ModeMark`; the new settings fields round trip; `NeverOverList` |

## 2. What I broke

Severity is my judgement: HIGH = normal use shows it; MEDIUM = plausible in normal use, or acts on something not meant; LOW = needs an odd input or only bends a stated rule;
LATENT = nothing in the app produces the input today. Fixes named are suggestions; nothing was changed.

### 1. A page key for the page that is already showing ends search but leaves the capsule at the search width (MEDIUM)
- **Input:** capsule open on Media with the keyboard; search open (any width, e.g. 700); `PageKey("media")` or `DigitKey(1)`. Reachable in normal use: open search, delete the text, press `1`
  (empty field: `SearchKeys` answers `SwitchPage`; page 1 is the page showing).
- **What happens:** `PageKey` sets `SearchOpen = false` and then returns at `if (id == PageId) return;`, before the swap that lays the page out. The width target stays 700, the page row is
  drawn in a search-wide capsule, and nothing brings it back until some later input. Another page's key does it right (`Holds_A_Page_Key_For_Another_Page_...`).
- **Should:** when `SearchOpen` was true, run `SwitchWhileOpen` (or call `CloseSearch`) also for the same page.
- **Test:** `MachineAttackTests.Defect_A_Page_Key_For_The_Page_Already_Shown_Ends_Search_But_Leaves_The_Capsule_At_Search_Width`.

### 2. A new pill title width while the notice is up resizes the notice to the pill's width (LOW)
- **Input:** notice showing (over the pill or alone); the player changes track, so the app calls `SetPillWidth(w)`.
- **What happens:** `SetPillWidth` tests `ShowsPill` and `Open` but not `ShowsNotice`; the notice's width target becomes the pill's (203 to 329) instead of its own (157 to 295). The
  random storm found it (width target 329, flags say 295). A pill narrower than the notice clips the notice's text until the notice ends.
- **Should:** `if (!ShowsPill || ShowsNotice || ...) return;` (the width is kept in `_pillWidth` anyway and used when the pill returns).
- **Test:** `MachineAttackTests.Defect_A_New_Pill_Title_Width_While_The_Notice_Shows_Resizes_The_Notice_To_The_Pill`.

### 3. A second `OpenSearch(width)` while search is open records the width and does not apply it (LATENT)
- **Input:** search open at 500; `OpenSearch(800)`.
- **What happens:** `_searchWidth` becomes 800, the guard `if (Phase != Open || SearchOpen) return;` follows, so the capsule stays at 500. `SearchWidth` and the real target now disagree until a swap
  (second row, content change) jumps the capsule to 800. The storm found it (target 728, flags say 680). Nothing calls `OpenSearch` twice today (`SearchKeys` opens only from closed).
- **Should:** assign `_searchWidth` only after the guard, or route to `SetSearchWidth`.
- **Test:** `MachineAttackTests.Defect_Asking_To_Open_Search_While_It_Is_Open_Records_The_Width_But_The_Capsule_Does_Not_Follow`.

### 4. The machine takes any search width (LATENT)
- **Input:** `SetSearchWidth(1e9)` while search is open.
- **What happens:** the capsule heads for 1e9 pixels. `SearchLayout.Width` never gives more than about 1100, and the pill and the notice go through their layouts' clamps, search does not.
- **Should:** clamp in `OpenSearch`/`SetSearchWidth` the way `SetPillWidth` and `SetNoticeWidth` do.
- **Test:** `MachineAttackTests.Defect_The_Search_Width_Has_No_Bound_In_The_Machine`.

### 5. A Stop right after a "needs your answer" for the same session is swallowed (LOW)
- **Input:** `Post(NeedsYourAnswer, session p1)`; the person answers; two seconds later `Post(Finished, session p1)` while the first notice is still showing.
- **What happens:** `Post` compares only the session key and the new signal (`Finished`), not the signal of the notice showing: the Stop "changes nothing" and the old text "Agent needs your answer"
  stays up. This is the letter of section 4 ("a Stop that arrives while the notice for the same session is still showing changes nothing"), but the intent is to ignore a repeated Stop.
- **Should:** swallow only when the showing notice is also `Finished`.
- **Test:** `AgentAttackTests.Defect_A_Stop_Right_After_Needs_Your_Answer_For_The_Same_Session_Is_Swallowed_And_The_Old_Text_Stays`.

### 6. A notice at the last representable clock moment throws out of the queue (LATENT)
- **Input:** `Post(n, DateTimeOffset.MaxValue)` then `Update(DateTimeOffset.MaxValue, ...)`.
- **What happens:** `now + TimeSpan.FromSeconds(...)` throws `ArgumentOutOfRangeException`. The app's clock never gets there.
- **Test:** `AgentAttackTests.Defect_A_Clock_Reading_At_The_Last_Representable_Moment_Throws_Out_Of_The_Queue`.

### 7. A project name of only invisible characters is drawn as a blank instead of "Agent" (LOW)
- **Input:** folder `C:\work\` followed by U+200B x3, or U+2060, U+FEFF, U+00AD, U+2800.
- **What happens:** `AgentText.Clean` removes controls, direction marks, line separators and lone surrogates but not other format or blank characters, so `ProjectName.From` returns a
  non-empty string that draws as nothing and `AgentNotice` does not fall back to `ProjectName.Unknown`. Bounded (40) and harmless otherwise.
- **Should:** drop zero-width and other format characters too (category Cf), or fall back when the cleaned name has no visible character.
- **Test:** `AgentAttackTests.Defect_A_Project_Name_Of_Only_Invisible_Characters_Is_Drawn_As_A_Blank_Instead_Of_Falling_Back_To_Agent` (five cases).

### 8. A typed invisible character opens search on a field that looks empty (LOW)
- **Input:** `SearchKeys.Apply(Closed, Typed("\u200B"))` (also U+200D, U+2060, U+FEFF, and a lone combining accent U+0301).
- **What happens:** the guard only keeps a space or a control character from opening search (`char.IsWhiteSpace`); zero-width characters open it with text that cannot be seen and match nothing
  ("Nothing found").
- **Should:** treat characters that draw as nothing like white space for the opening and the first-character rule.
- **Test:** `SearchAttackTests.Defect_A_Typed_Invisible_Character_Opens_Search_On_A_Field_That_Looks_Empty` (five cases).

### 9. The service tile's name keeps a right-to-left override from the typed text (LOW)
- **Input:** text `abc` U+202E `def`; `SearchServices.Tile(["YouTube"], text)`.
- **What happens:** the tile's name "Search abc[RLO]def on YouTube" keeps the override, which reorders everything after it ("... eduTuoY no fed"). The notice's name drops these; the search text is not
  stored or sent anywhere, so this is display only. The address itself is safely encoded (`Holds_Addresses_Are_Well_Formed...`).
- **Should:** strip direction characters (or isolate the text with U+2068/U+2069) in the name.
- **Test:** `SearchAttackTests.Defect_The_Service_Tile_Name_Keeps_A_Right_To_Left_Override_That_Reorders_The_Words_After_It`.

### 10. A search service outside the enum throws (LATENT)
- **Input:** `SiteAddress.ForSearch((SearchService)99, "tu")`.
- **What happens:** `KeyNotFoundException` from the table lookup. `Parse` only gives the four.
- **Test:** `SearchAttackTests.Defect_A_Service_Value_Outside_The_Enum_Throws_Instead_Of_Giving_Nothing`.

### 11. A negative position with no length reaches the "now playing" line (LATENT)
- **Input:** a session with `PositionSeconds = -42` and no length.
- **What happens:** `NowPlaying` clamps the position only when the length is known, so `view.PositionSeconds` is -42 (progress is null, so the ring is not affected; `LitShare` refuses it).
- **Test:** `ProgressAttackTests.Defect_A_Negative_Position_With_No_Length_Reaches_The_Line_As_A_Negative_Position`.

### 12. A scene can be named with a blank-looking character (LOW)
- **Input:** `Create` with U+3164 (Hangul filler), U+2800 (braille blank), U+FE0F (variation selector) or U+1160.
- **What happens:** accepted; the scene draws with an empty name in the settings, the tray and the island's "<name> · <n> opened". `HasHiddenCharacters` refuses Cc, Cf, Co, Cn and U+FFFD, and these are Lo, So and Mn.
- **Should:** refuse a name with no visible character (or these categories).
- **Test:** `SceneAttackTests.Defect_A_Scene_Name_Made_Of_A_Blank_Looking_Character_Is_Accepted` (four cases).

### 13. Two scenes whose names look the same are both accepted (LOW)
- **Input:** `Create("caf\u00E9")`, then `Create("cafe\u0301")`.
- **What happens:** the duplicate check is ordinal and case-insensitive, so the precomposed and the combining spelling are two scenes with the same drawn name.
- **Should:** compare after normalising (the way `SearchMatch.Fold` or form C does).
- **Test:** `SceneAttackTests.Defect_Two_Scenes_Whose_Names_Look_The_Same_Are_Both_Accepted`.

### 14. A deleted scene's key that could not be given back goes to the next scene that reuses the id (LOW)
- **Input:** scene "First" (`scene-1`) with a key; `settings.json` becomes read-only; `DeleteScene("scene-1")` (scenes file saved, key not cleared: the answer is `Changed` with "key still held");
  then `CreateScene("Second")`.
- **What happens:** `NextId` reuses `scene-1`; the old `scene:scene-1` entry is still in the settings and still registered with Windows, so "Second" silently owns a key nobody gave it. The sweep
  (`ReleaseKeysOfMissingPicks`) no longer finds it missing.
- **Should:** never reuse an id while a key entry for it exists (or sweep or clear the entry before creating), or use ids that are not reused.
- **Test:** `SceneAttackTests.Defect_A_Deleted_Scenes_Key_That_Could_Not_Be_Given_Back_Goes_To_The_Next_Scene_That_Reuses_The_Id`.

### 15. `HeldKeyGuard` swallows a new press when the clock difference overflows (LATENT)
- **Input:** `Accept("k", -1)` then `Accept("k", long.MaxValue)`.
- **What happens:** `nowMs - last` wraps negative, which is below the gap, so the press counts as the same hold. A monotonic millisecond count never gets there.
- **Test:** `SceneAttackTests.Defect_The_Held_Key_Guard_Swallows_A_New_Press_When_The_Clock_Difference_Overflows`.

### 16. `Save` writes a notice time or a mode that the next start calls unreadable (LATENT)
- **Input:** `Settings.Defaults with { NoticeSeconds = 0 }` (or above 86400) then `Save`; `Mode = (Mode)9`.
- **What happens:** the file is written; `Parse` refuses it, so all keys fall back to the defaults and the file is locked. `Save` guards only the idle time (the same family as ATTACK6 #11). The settings screen
  clamps the notice time, so only the record can make it. (The test fails at the notice time; the mode half is in the same test and was not reached separately.)
- **Test:** `SceneAttackTests.Defect_Save_Writes_A_Notice_Time_Or_A_Mode_That_The_Next_Start_Calls_Unreadable`.

### 17. `ModeMark.Breath` is NaN for a time near the top of the range (LATENT)
- **Input:** `Breath(double.MaxValue)`.
- **What happens:** `t / 2.2 * 2 * pi` overflows to infinity, `Cos(inf)` is NaN. Real times are seconds of uptime; the claim "a time that is not a number reads as 0" covers NaN and infinity, not this.
- **Test:** `SceneAttackTests.Defect_Breath_Is_Not_A_Number_For_A_Time_Near_The_Top_Of_The_Range`.

## 3. What holds (the coverage statement)

64 passing cases, all `Holds_`:
- **Machine (`MachineAttackTests`):** across 30 000 random inputs (6 seeds, 40% on the same step) and 6720 same-step orders from five starting states, after every step: phase legal; every drawn size, position, stretch
  and target finite; a notice is always a pill; search and pill never together; the keyboard only while shown and not as the pill; hidden holds no pill, notice, search or keyboard and has an infinite idle deadline; a
  capsule always has a finite idle end that is not in the past; a notice is never on the idle clock; the pill's idle clock is infinite exactly when wanted and allowed. Left alone for 4 s after any storm segment the
  machine reaches Hidden or Open with the contents in, every spring at rest and width and height targets that match the flags (pill, notice, search, second row, capsule), apart from defects 1 to 3 which the storm
  excludes by name. With a 3 s idle time, whatever the storm did, nothing wanted and nothing allowed always ends Hidden within 20 s. Non-finite times and widths change nothing; a machine time of 1e12 behaves; notice over
  pill returns the pill and never takes the keyboard; a page key for another page during search returns to the page width.
- **Progress (`ProgressAttackTests`):** `LitShare` is null or finite in 0 to 1 for all 285 120 combinations, never NaN, never throws, null in exactly the "no invented progress" cases (unknown/zero/negative/NaN/infinite length,
  zero date, position beyond length or negative, missing position), paused stands still, a clock that goes backwards counts as no time, a stale playing report ends at 0; a changing length each report stays in range;
  `RedrawGate` never throws and treats non-numbers as no progress; `TabMediaTiming` never gives a reading from the future, a non-finite speed is 1; `NowPlaying` under 3000 odd reports keeps progress finite in 0 to 1 and the
  position inside the length; a tab position beyond the length gives full progress but no ring; vanished sessions leave nothing behind.
- **Search (`SearchAttackTests`):** nothing throws for any of the 41 texts; `SearchLayout.Width` finite and below 1500 for any count (int extremes included); 2000 candidates x 10 000-character texts in under 5 s;
  ordering (picks first, starts-with first inside each group, blank and only-combining text matches nothing); every address parses as `https`, has exactly the expected host, contains only unreserved characters and `%`,
  has one `=` or one path segment, stays under 2100 characters, and decodes to the typed text with lone surrogates as U+FFFD; look-alike hosts (`youtube.com.evil.example`, `user@youtube.com`, ...) are refused; 120 000
  random key events keep the text under 256 characters without controls, the selection inside the tiles, `Activate` only with tiles, and a pair is deleted whole.
- **Agents (`AgentAttackTests`):** 1000 notices in one second keep one slot and the newest wins; repeated Stop for the same session changes nothing; a notice behind the capsule is never shown, goes stale after 5 min,
  and a hovering pointer is capped; `Seconds` always 3 to 30; every nasty folder gives a project name of at most 40 characters with no controls, no direction characters, no half surrogate pair, no separator, no trailing
  space, and `AgentNotice` always has a non-empty name; notice and pill widths are finite and inside their ranges; the wire message always fits 4096 bytes and parses back for wide characters and 40 pids (16 kept); 20 000
  garbage frames never throw; `TerminalChoice` handles 20 000 windows and an empty or 100 000-character project name; `ProcessChain` stops at loops and at 16.
- **Scenes and settings (`SceneAttackTests`):** 100 x 200 scenes round trip byte for byte; the 101st scene and 201st thing are refused; a hostile file is cut, not crashed (and said so); names with controls, zero-width,
  direction, lone surrogates, soft hyphen, BOM, private use, 25 characters or empty are refused, white space is tidied, case-different names clash; a mixed scene plans in list order, skips only the missing program,
  opens three, and the only actions are Open, BringForward, Skip; a scene of 251 things plans at most 200; the "could not be opened" message stays within the balloon limit (250 + 3) for 3000 random name lists with RTL,
  controls and `<n>` text and never holds a control or direction character; `HeldKeyGuard` swallows a one-hour hold at 100 ms and runs again after a pause or a release; in 3 x 1500 random steps mixing scene keys with
  page, pick, main and mode keys no combination is shared, Windows holds exactly the keys the settings say, no key belongs to a missing scene, and settings and scenes files read back equal; an unreadable scenes file is
  never overwritten and every edit is refused; a folder where the file should be refuses with "nothing was changed"; hostile scenes files are unreadable, not half read; `FirstStart.ShouldRun` is true only for a fresh
  normal launch; `NoticeFallback` over 3000 x 400 steps: at most one sound, only in Focus, never in DND, a timer exactly while waiting, `ShowHere` only when the table says show; `ModeMark` stays in range for any finite
  or non-finite time except defect 17; mode, notice time, pill and never-over round trip; `NeverOverList` refuses paths and is immutable.

## 4. What is not proven

- Nothing was run in a window: whether the app really asks the table again on every foreground and mode change, whether it calls `OpenSearch` once, whether it clears `SearchOpen` its own way on `SwitchPage`, and what the
  window draws in the states the machine reports are app questions; this pass checks the machine's answers only.
- Observed, not tested as defects: `MainKey` or a page key while the notice is up turns it into the capsule through `GrowToCapsule` and clears `ShowsNotice` while the app's `NoticeQueue` still holds the notice as showing
  (it goes away at its deadline; whether the app dismisses it first is not visible from Core). A tab report whose `readAt` is a small or old positive number (for example a page clock that is not epoch time) is taken
  as a real old reading: a playing tab then shows an empty ring (by design a stale playing report ends at 0). In Focus the sound plays over a presentation and over an exclusive fullscreen program when no second screen
  exists; that is the letter of the order ("otherwise one short system sound in Focus") and may be worth a decision.
- Real Windows media sessions, real browsers, `Island.Notify`, the pipe, the hook installer and the settings of Claude Code (the second attack's ground, and forbidden here), the sound, the first-start screens,
  IME and the real `WM_CHAR` surrogate splitting (a lone high surrogate is accepted into the field and joined by the next call; its address and match are safe), HiDPI and screens.
- Time going backwards inside the machine (only equal or increasing times were fed); NaN or infinite numbers inside `SceneStore` beyond what the file parser reads.
- Only the single-thread use of everything but `NoticeQueue` and `NowPlaying` (both lock; no concurrent test was run).

## 5. Fixed (main session)

All 17 defects are fixed and every `Defect_` test of this project now passes (92 of 92): defects 1 to 4 in `3f8cc2d`, defects 5 to 17 in `3091df9`. Decisions taken: a name that draws as nothing (only format characters, combining marks, white space or the blank letters U+2800, U+3164, U+1160, U+115F, U+FFA0, U+180E, U+034F) is treated as no name (`BlankText`); a project name whose last folder part is invisible falls back to "Agent", not to its parent; a deleted scene's key that could not be given back keeps its id reserved (a new scene takes the next id).
