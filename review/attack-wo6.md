# Attack on WORK-ORDER-6 (ATTACK6, 6 Oct 2026)

Adversarial pass over what WO6 sections 1 to 5 changed or added, in `Island.Core` only (no window, no app, nothing started, no
registry, no real screen): `ScreenChooser`, `ScreenPicker`, `ScreenFit`, `ScreenInfo`, `StripLayout.VisibleLimit`; `ShowDecision`,
`NeverOverList`, `FrontClassifier`, `NoticeFallback`; `KeybindEditor`, `Settings`, `PickKeysJson`, `PickKeyHandler`,
`SettingsSession` (keys, pages, picks, idle time, start with Windows); `CloseButton` and the close frames of `TabMessages`;
`StartupSwitch` with `MemoryStartupRegistry`.

Branch `attack6`, made from main `b698372`. Nothing under `src/` and no existing test or file was changed. The tests live in
`tests/Island.Attack6.Tests/` (xunit, references `Island.Core` only, not in `Island.sln`). Names only like "Alpha", `alpha.exe`,
`example.org`; settings tests use a temporary folder that is removed afterwards (checked: none left behind). Where the app's source was
read to understand a joint (`ScreenPlacer`, `CloseHandler`, `HotkeyHost`, `OutsideStartupRegistry`, `WindowFacts`), nothing was run or built.

Note on the branch: while this report was being written the main session merged `attack6` (merge `85a34af`, which holds the first two test commits
`b27bba1` and `ff33299`) and removed the worktree and the branch. The two small test edits and this report made after that were therefore re-made in a
new worktree `.worktrees/attack6-final` on a new branch `attack6` that starts at `85a34af`; its commits are the last two (tests, then this report).
The old directory `.worktrees/attack6` still holds leftover build output and copies of tracked files; it is not a worktree any more and I did not delete it.

Command and result:
- `dotnet test tests/Island.Attack6.Tests`: **404 tests, 23 failed, 381 passed.** Every failing test is named `Defect_...` and fails
  because of the defect below (20 `Defect_` methods; one is a 4-case theory, hence 23); every passing test is named `Holds_...`
  (151 methods, 381 test cases) and is the coverage statement (part 3). No `Holds_` test fails and no `Defect_` test passes.
- No HIGH defect found. Five MEDIUM (1 to 5), the rest LOW or LATENT.

## 1. What I added

| File | Content |
|---|---|
| `Island.Attack6.Tests.csproj` | xunit project, shape of Attack5's, reference to `Island.Core` only |
| `Support.cs` | `PretendRegistrar` (answers like the real `HotkeyHost`: asking for a combination it already holds answers true; counts double registrations), `TempFolder`, a settings-session builder |
| `ScreenAttackTests.cs` | no/forty/4000 screens, zero/negative/inverted/int-extreme rectangles, NaN/infinite/zero/negative scale, 11 scales from 1.0 to 5.0 incl. odd ones, pointer on no screen / int extremes / half-open edges / overlap / ties, window wider than the screen, taller than the work area, work area outside or larger than the full rectangle, a screen vanishing between two calls, `ScreenPicker` 5000 visible frames with changing lists and sizes and 4000 hidden/visible alternations, `ScreenFit` at extremes and monotonicity, `VisibleLimit` clamp and 2000 parallel writes |
| `FrontAttackTests.cs` | all 72 cells of the WO7 table against an oracle written from the document, enum casts outside every enum, exclusive fullscreen in every mode/origin/thing/unknown mode, `NeverOverList` (invalid names, nulls, case, repeats, 1 MB name, immutability), `FrontClassifier` (every reason to say Clear, empty/inverted/extreme rectangles, odd class names, Windows' own answer incl. unknown numbers, failed reading), `NoticeFallback` (idle, unknown mode, sound once, stale boundary, clock backwards and extreme dates, DND, order of the steps) |
| `KeyAttackTests.cs` | the same combination offered to main key, page and pick in every order and 1/2/5 times, a 5 x 600-step random storm over press/clear/restore/remove pick/add pick/move pick/create page/delete page checking after each step that no two things share a key, Windows holds exactly what the settings say, no combination is registered twice, no key is held for a missing pick or page, and the file on disk reads back to the same keys; capture of every virtual key -2 to 0x2FF with every modifier subset; unsafe combinations for all three kinds of action; save failures and refusals by Windows in the editor; `ReleaseKeysOfMissingPicks` failing half way; removing a pick or page; ids with colons, look-alike ids; `PickKeyHandler`; `SetIdleSeconds` with 19 values and the three non-numbers |
| `SettingsFileAttackTests.cs` | `idleSeconds` with 16 unusable and 17 usable spellings, 26 nonsense `pickKeys`, 200/201-character names, 10 000 entries, 500 safe combinations, 300 random settings round-tripped through `ToJson`/`Parse`, nonsense `startWithWindows`, deep and 200 000-entry files, comments, BOM, invalid bytes, never-written-when-unreadable |
| `CloseAttackTests.cs` | `FactsPlay` (a pretend world whose answers change between calls and which can throw on a chosen question), every click kind on window, folder and site picks, window vanished / became the island's / became elevated / became disabled between click and press, new showing between, two clicks one press, press after press, site with the add-on connected then gone, tab gone, odd window handles, 12 facts-that-throw cases, an 8 x 6000-step storm checked against a model, `TabMessages.CloseTab`/`ParseIsland` close frames (huge, negative, float, string, nested, extra fields, repeated names, 1 MB, multibyte oversize, deep nesting, lone surrogates, BOM, 20 000 garbled frames), `ResultMessage` with cmd "close" |
| `StartupAttackTests.cs` | 12 good and 22 unwritable paths, the written line split with the C runtime's own rules, the 260/261 boundary (line and path), unicode by characters, a million-character path, 20 000 random on/off/is-on steps against a model, values that are not ours, case, a registry that refuses, `--autostart` spellings, and the switch inside `SettingsSession` |

## 2. What I broke

Severity is my judgement: HIGH = normal use shows it; MEDIUM = plausible in normal use, or acts on something not meant; LOW = needs an
odd input or only bends a stated rule; LATENT = nothing in the app produces the input today. Fixes named are suggestions; nothing was changed.

### 1. Start with Windows says "nothing was changed" while the registry was written (MEDIUM)
- **Input:** `SettingsSession.SetStartWithWindows(true)` while `settings.json` cannot be saved (read-only file, full disk).
- **What happens:** `TurnOn` writes the registry value first, then the settings save fails and the answer is "settings.json could not be saved,
  so nothing was changed." Windows now starts the island at every sign-in; the switch (which reads the registry) shows on while the message says nothing changed.
- **Should:** the answer and the registry agree: save the settings first and write the registry only if that worked, or take the value back
  (`TurnOff`) when the save fails.
- **Test:** `StartupAttackTests.Defect_Start_With_Windows_Says_Nothing_Was_Changed_But_The_Registry_Was_Written_When_The_Settings_File_Cannot_Be_Saved`.

### 2. Deleting a page that cannot be fully deleted says "nothing was changed" but has already let go of its key, and perhaps its picks (MEDIUM)
- **Input:** `DeletePage(id)` on a custom page that has a key (and a pick), with `picks.json` or `pages.json` unable to be written.
- **What happens:** the page's key is cleared, released to Windows and saved first. With `picks.json` unsavable the answer is "picks.json could not be
  saved, so nothing was changed": the page is still there but its key is gone. With `pages.json` unsavable the picks of the page are already removed
  from `picks.json` and from memory (and their keys released), the page stays, and the answer again says nothing was changed.
- **Should:** either all of it or none of it (do the saves that can fail first, keep the old stores to put back), or an answer that says what did change.
- **Tests:** `KeyAttackTests.Defect_Delete_Page_Says_Nothing_Was_Changed_But_The_Page_Key_Is_Already_Gone_When_Picks_Cannot_Be_Saved`,
  `KeyAttackTests.Defect_Delete_Page_Says_Nothing_Was_Changed_But_Picks_And_Key_Are_Gone_When_Pages_Cannot_Be_Saved`.

### 3. A removed pick keeps its key held by Windows, silently, when the settings file cannot be saved (MEDIUM)
- **Input:** a pick has a key; `settings.json` becomes read-only; `SetPick(row, on: false)` (`MovePick`, `RestorePage` and `DeletePage` end the same way).
- **What happens:** `picks.json` is saved, the pick is gone, `ReleaseKeysOfMissingPicks` cannot save the settings and does nothing; `SetPick`
  answers "done" with no warning. The combination stays registered for a pick that no longer exists, so it is taken from other programs and
  does nothing when pressed; nothing tries again until some later pick change. WO6 section 4 says the key goes back "at once".
- **Should:** at least a `Warning` in the result (the class has the field), and a retry at the next key or pick edit and at the next start.
- **Test:** `KeyAttackTests.Defect_A_Removed_Pick_Keeps_Its_Key_Registered_Silently_When_The_Settings_File_Cannot_Be_Saved`.

### 4. "Restore default keys" is refused when a page or pick holds the main key's default (MEDIUM)
- **Input:** main key changed to Ctrl+Alt+A; the Apps page given Ctrl+Q (free now); `RestoreAllKeys()` (or `RestoreKey("showHide")`).
- **What happens:** `RestoreDefaults` gives the main key back first; Ctrl+Q still belongs to Apps, so the editor refuses ("Ctrl+Q already belongs to
  Apps ...") before any page key is cleared. The whole restore does nothing. The person can clear Apps' key by hand and try again, as the text says.
- **Should:** clear the page and pick keys first and give the main key back last.
- **Test:** `KeyAttackTests.Defect_Restore_All_Keys_Is_Refused_When_A_Page_Holds_The_Default_Main_Key`.

### 5. Pressing the key an action already has never asks Windows again (MEDIUM)
- **Input:** at the start another program held Ctrl+Q (a documented outcome, HOTKEY_TAKEN), so the main key is in the settings but not held. The
  other program is closed; the person presses Ctrl+Q again in the settings screen, or presses "restore default".
- **What happens:** `Assign` finds `old == combo` and returns "nothing to do" without calling `TryRegister`; the key stays dead until the next start or
  until the person picks another combination and then this one. The same for a page key or a pick key that failed at the start.
- **Should:** call `TryRegister` in that case too (the real `HotkeyHost` answers true for a combination it already holds, so it costs nothing).
- **Test:** `KeyAttackTests.Defect_Giving_An_Action_The_Key_It_Already_Has_Does_Not_Try_To_Register_It_Again`.

### 6. A repeated pick name in `pickKeys` loads and then makes every save fail (LOW)
- **Input:** `{"pickKeys": {"program:a": "Ctrl+Alt+A", "program:a": "Ctrl+Alt+B"}}` (a hand edit; different keys, so the "same combination" rule does not catch it).
- **What happens:** `JsonDocument` keeps both; `Parse` returns Loaded with two entries for one pick; `ToJson` builds a dictionary and throws; `Save` swallows the
  `ArgumentException` and answers false, so the screen says "settings.json could not be saved" to every change from then on.
- **Should:** `Parse` refuses a repeated name (as it refuses a repeated combination), or the later one wins.
- **Test:** `SettingsFileAttackTests.Defect_A_Repeated_Pick_Name_Loads_And_Then_Every_Save_Of_The_Settings_Fails`.

### 7. Two settings that are equal read back as "not equal" when page keys were given out of page order (LOW)
- **Input:** two custom pages; a key given to the second, then to the first; `Settings.Load` of the saved file.
- **What happens:** `PageKeys` is a list in the order the keys were first given; `Parse` builds it in page order; `Settings.Equals` compares the lists in order, so a
  faithful read-back is "different". The random storm in this project hit it at step 277 of seed 2 until its comparison was made order-free. Nothing in the
  app acts on that comparison today (`SetStartWithWindows` and the glass use it only to decide "changed").
- **Should:** compare page keys as a set (by page id), or keep `PageKeys` in page order.
- **Test:** `KeyAttackTests.Defect_Settings_Read_Back_Are_Not_Equal_When_Page_Keys_Were_Given_Out_Of_Page_Order`.

### 8. A pick id that `picks.json` accepts but `pickKeys` refuses makes the whole settings file unreadable (LATENT)
- **Input:** a pick with id `program:a b`, `program:a<TAB>b`, `program:a<U+0001>b` or `x:` (all pass `Pick.IsStorable`; `ForProgram` escapes such characters, so only a hand-edited
  `picks.json` or an older file has them); `PressKey(id, ...)`.
- **What happens:** the key is accepted and saved; `PickKeysJson.IsUsableId` refuses the name on the next read, so `settings.json` is Unreadable: every key, the idle time and
  the glass go back to the defaults and the file is locked against edits.
- **Should:** one rule for ids: either `IsStorable` forbids whitespace, controls and ids of two characters (the colon rule too, see 9), or the key editor refuses a key for an id the file cannot hold.
- **Test:** `KeyAttackTests.Defect_A_Storable_Pick_Id_That_The_Key_File_Refuses_Makes_The_Whole_Settings_File_Unreadable` (four cases).

### 9. A pick id without a colon is accepted, and its key would go to a page entry (LATENT)
- **Input:** `new Pick("apps", Program, "Odd", "media", ExeName: "odd.exe")` added to a `PickStore` (a hand-edited `picks.json`).
- **What happens:** `PickStore.Add`/`Parse` accept it although `PickKeysJson` says a pick id "always has a colon". `KeybindEditor` decides pick versus page by the colon alone,
  so a key pressed for this pick would replace the key of the Apps page.
- **Should:** `IsStorable` requires the colon.
- **Test:** `KeyAttackTests.Defect_A_Storable_Pick_Id_Without_A_Colon_Is_Accepted_And_Its_Key_Goes_To_A_Page_Entry`.

### 10. A key can be given to a page or pick that does not exist and stays registered (LATENT)
- **Input:** `PressKey("ghost-page", ...)`, `PressKey("program:ghost", ...)` (the screen only sends real rows).
- **What happens:** both are registered with Windows and saved; the pick key is only swept at the next pick change, the page key never (a page entry for a missing page is dropped
  silently by the next read).
- **Should:** refuse an id that is not in `Pages`/`Picks`.
- **Test:** `KeyAttackTests.Defect_A_Key_Can_Be_Given_To_A_Page_Or_A_Pick_That_Does_Not_Exist_And_Stays_Registered`.

### 11. `Save` writes an idle time that the next start calls Unreadable (LATENT)
- **Input:** `Settings.Defaults with { IdleSeconds = 0 }` (or negative, 86401, 1e300) then `Save`.
- **What happens:** the file is written; at the next start `Parse` refuses it ("above 0 and at most 86400"), so all keys are lost and the file is locked. `SetIdleSeconds` clamps, so the
  screen cannot make it; the record allows it. (NaN and infinity make `Save` answer false: that part is fine, `Holds_Save_Refuses_To_Write_A_Number_That_Is_Not_One_And_Leaves_The_Old_File`.)
- **Should:** `Save` refuses what `Load` would refuse.
- **Test:** `SettingsFileAttackTests.Defect_Save_Writes_An_Idle_Time_That_The_Next_Start_Calls_Unreadable`.

### 12. `Settings.ClampIdle(NaN)` returns NaN (LATENT)
- **Input:** `Settings.ClampIdle(double.NaN)`.
- **What happens:** `Math.Clamp` lets NaN through; the doc says "a whole second between the minimum and the maximum". `SetIdleSeconds` refuses NaN first and a file cannot hold it.
- **Should:** NaN reads as the default or the minimum.
- **Test:** `KeyAttackTests.Defect_ClampIdle_Lets_NaN_Through_Though_It_Promises_A_Whole_Second_Between_Two_And_Sixty`.

### 13. `Capture` accepts modifier bits that the text of a combination cannot carry (LATENT)
- **Input:** `KeyPress('W', Control | (HotkeyModifiers)8)` (the screen sends the Windows key as its own flag).
- **What happens:** the combination keeps bit 8; its text is "Ctrl+W" and reads back as plain Ctrl+W, so Windows would be asked for something the file cannot hold.
- **Should:** mask to Ctrl, Alt and Shift (or refuse).
- **Test:** `KeyAttackTests.Defect_Capture_Accepts_Modifier_Bits_Outside_Ctrl_Alt_Shift_And_Registers_What_It_Cannot_Store`.
  (In the first merged version this test used Ctrl+X, which `Capture` refuses as an editing key, so it passed by accident; it is fixed in the commit after the merge.)

### 14. A work area that is not inside the full rectangle puts the window off the screen (LATENT)
- **Input:** full 0,0 to 1920,1080 with work 5000,0 to 6920,1080; or work larger than full (-2000,-50 to 2000,400 on a 400 x 400 screen).
- **What happens:** `Choose` trusts the work area completely; the window lands at 5680 (outside the screen) or at -150 (hanging over the screen's edge). Windows does not report such
  a pair; a misreading or a stale work area after a screen change could.
- **Should:** cut the work area to the full rectangle (empty after the cut: the full rectangle, as for an empty work area).
- **Tests:** `ScreenAttackTests.Defect_A_Work_Area_Outside_The_Full_Rectangle_Puts_The_Window_Off_The_Screen`, `ScreenAttackTests.Defect_A_Work_Area_Larger_Than_The_Full_Rectangle_Hangs_The_Window_Over_Its_Edge`.

### 15. A "never over this" entry that can never match is accepted (LATENT)
- **Input:** `new NeverOverEntry("Alpha", " alpha.exe ")` or `("Alpha", "alpha")`.
- **What happens:** `IsValid` accepts both (it only refuses blanks, separators and invalid file-name characters); the front reader hands `alpha.exe`, so the entry silently never matches
  and a game the person meant to protect gets the island over it. WO7 says the list is "stored like a pick", and a pick must end in `.exe` with no padding.
- **Should:** require `.exe` at the end and no leading or trailing white space (or trim).
- **Tests:** `FrontAttackTests.Defect_An_Entry_With_Padding_Spaces_Is_Accepted_And_Can_Never_Match_The_Real_File_Name`, `FrontAttackTests.Defect_An_Entry_Without_Exe_Extension_Is_Accepted_And_Can_Never_Match`.

### 16. An unknown front state plays the sound in Focus (LATENT)
- **Input:** `NoticeFallback.Next` with `Front = (FrontState)42`, mode Focus, nothing else.
- **What happens:** the table answers StayAway for a value outside the enum (its documented rule), the fallback treats that as "the table says no" and plays the one system sound.
  An unknown *mode* is handled ("above all no sound"); an unknown front state is not. `FrontClassifier` never produces such a value.
- **Should:** unknown front drops or waits like an unknown mode.
- **Test:** `FrontAttackTests.Defect_An_Unknown_Front_State_Plays_The_Sound_In_Focus`.

### 17. `TabMessages.CloseTab` writes a frame its own reader rejects for a negative id (LATENT)
- **Input:** `TabMessages.CloseTab(-1)`.
- **What happens:** `{"type":"close","id":-1}`; `ParseIsland` (the protocol: id is 0 or more) rejects it. Tab ids come from the add-on and are never negative.
- **Should:** the builder refuses or the reader accepts what the builder writes.
- **Test:** `CloseAttackTests.Defect_CloseTab_Builds_A_Frame_Its_Own_Reader_Rejects_For_A_Negative_Id`.

## 3. What held

Every item names the rule and the tests that prove it. Tests prefixed `Holds_` in the file named.

**Screens (WO6 section 1)**
- *Never throws, always a usable window or a flagged fallback:* `ScreenAttackTests.Holds_The_Chooser_Never_Throws_And_Always_Gives_A_Usable_Window_For_Absurd_Lists` (11 lists: none, empty, zero-size, inverted,
  int-extreme, NaN/infinite/negative/zero scale, work area at int.MaxValue, forty screens, 4000 screens; 8 pointers incl. null and int extremes; 7 window sizes incl. NaN, infinity, 1e300); `..._No_Screen_At_All_Gives_The_Flagged_Fallback_Centred_At_The_Top`; `..._A_Screen_Of_Zero_Or_Negative_Size_Is_Skipped_For_A_Usable_One`; `..._A_Window_At_The_Top_Of_A_Work_Area_Near_Int_Max_Is_Clamped_Not_Wrapped`.
- *A bad scale reads as 100 %:* `Holds_A_Bad_Scale_Reads_As_One_Hundred_Percent` (5 values). *Same size in units on every scale, inside the work area, centred, below a top bar:* `Holds_The_Window_Is_The_Same_Size_In_Units_On_Every_Scale_And_Fits_The_Work_Area_Centred` (11 scales, negative coordinates).
- *Which screen:* `Holds_Pointer_Edges_Are_Half_Open_And_Overlap_Takes_The_First`, `Holds_A_Pointer_On_No_Screen_Takes_The_Nearest_And_A_Tie_The_First`, `Holds_A_Pointer_At_Int_Extremes_Takes_The_Nearest_Without_Overflow`, `Holds_Pointer_Unreadable_Takes_Primary_Else_First_Usable`, `Holds_Forty_Screens_Pick_By_Pointer`, `Holds_More_Than_MaxScreens_Looks_Only_At_The_First_MaxScreens_As_Documented`.
- *Wide and tall windows:* `Holds_A_Window_Wider_Than_Every_Screen_Stays_Centred_And_Overhangs_Both_Sides_Equally`, `Holds_A_Taller_Window_Than_The_Work_Area_Is_Only_Top_Aligned`, `Holds_A_Work_Area_Without_Pixels_Is_Replaced_By_The_Full_Rectangle`.
- *A screen that vanishes:* `Holds_A_Screen_That_Vanishes_Between_Two_Calls_Gives_A_Fresh_Valid_Choice`.
- *Never changes screen while visible (W2):* `Holds_While_Visible_The_Placement_Never_Changes_Whatever_The_Screens_And_Pointer_Do` (5000 frames, lists changing between two, none and null, window size changing), `Holds_Alternating_Visible_And_Hidden_Chooses_Exactly_On_Every_Hidden_Frame` (4000 alternations, `Choices` counted), `Holds_Visible_With_Nothing_Chosen_Yet_Chooses_Once_Then_Keeps_It`, `Holds_The_Picker_Survives_Absurd_Input_On_Every_Frame`.
- *Narrow screens:* `Holds_Tile_Count_Is_Monotone_In_Width_And_Within_One_And_Wanted`, `Holds_A_Bad_Or_Narrowest_Width_Gives_One_Tile`, `Holds_Nothing_Wanted_Gives_Zero`, `Holds_Huge_Width_Or_Huge_Wanted_Is_Bounded_And_Never_Throws`, `Holds_Tiles_Of_A_Chosen_Screen_Follow_Its_Work_Width_In_Units`, `Holds_Every_Count_The_Fit_Allows_Really_Fits_The_Capsule_In_The_Work_Width` (both the media and the other capsule), `Holds_VisibleLimit_Is_Clamped_To_One_To_Seven_And_Is_Safe_From_Many_Threads`.

**What is in front (WO6 section 2, WO7 section 1)**
- *The whole table equals the document:* `FrontAttackTests.Holds_Every_Cell_Of_The_Table_Matches_The_Document_For_Every_State_Thing_Origin_And_Mode` (72 cells against an oracle written from the WO7 table, not from the code).
- *Never over an exclusive-fullscreen game:* `Holds_The_Island_Never_Covers_Exclusive_Fullscreen_In_Any_Mode_Origin_Or_Thing` (including four modes outside the enum); `Holds_Never_Over_List_Counts_A_Listed_Fullscreen_Program_As_Exclusive_And_Nothing_Else`.
- *A value outside an enum answers StayAway, and the zero value of the answer is StayAway:* `Holds_Any_Value_Outside_The_Enums_Answers_StayAway_Never_Throws` (six odd numbers in each of the four parameters).
- *A reading that cannot be made has one documented outcome, Clear (and Clear shows):* `Holds_A_Failed_Reading_Is_Clear_As_The_Work_Order_Says`, `Holds_A_Combined_Reading_Is_Never_Outside_The_Enum`, `Holds_Windows_Hard_States_Win_And_Everything_Else_Falls_To_The_Rectangle_Test` (ten unknown notification numbers). Note: that outcome means a failed reading lets the island appear over a game; it is what section 2 prescribes, so it is not counted as a defect.
- *Rectangle test:* `Holds_A_Borderless_Window_Equal_To_Its_Screen_Is_A_Fullscreen_Program`, `Holds_Every_Single_Reason_To_Say_Clear_Says_Clear` (title bar, sizing border, island window, shell, tool window, one pixel off, the six system classes in any case), `Holds_Empty_And_Inverted_Rectangles_Are_Never_Fullscreen_Even_When_Equal`, `Holds_Negative_And_Extreme_Rectangles_Equal_To_Their_Screen_Are_Fullscreen_Without_Overflow`, `Holds_Odd_Class_Names_Never_Throw_And_Only_The_Listed_Ones_Are_Skipped` (1 MB name, NUL, lone surrogate).
- *Never-over list:* `Holds_An_Entry_That_Is_Not_A_File_Name_Is_Refused` (13 cases), `Holds_Null_And_Blank_Names_And_Null_Entries_Are_Refused_Without_Throwing`, `Holds_Repeats_In_Another_Case_Are_One_Entry_And_Without_Ignores_Case`, `Holds_The_Entries_Of_A_List_Cannot_Be_Changed_From_Outside`, `Holds_A_Huge_Or_Odd_Name_Is_Kept_Whole_And_Matching_Works_On_Unicode_Case`.
- *Notice fallback:* `Holds_Nothing_Waiting_Means_No_Action_No_Timer_Whatever_The_Facts`, `Holds_An_Unknown_Mode_Drops_The_Notice_With_No_Sound`, `Holds_The_Sound_Plays_At_Most_Once_And_Only_In_Focus_Over_A_Hard_State_Without_A_Second_Screen`, `Holds_Stale_Is_Exactly_Ten_Minutes_And_A_Clock_That_Went_Back_Is_Never_Stale` (including `DateTimeOffset.MinValue`/`MaxValue`), `Holds_In_Dnd_A_Notice_That_May_Not_Show_Is_Dropped_Even_With_A_Second_Screen`, `Holds_A_Second_Screen_Comes_Before_The_Sound_And_The_Sound_Before_Waiting`.

**Keys (WO6 section 4)**
- *No two things share a key; what Windows holds equals what the settings say:* `KeyAttackTests.Holds_The_Same_Combination_Offered_To_Main_Page_And_Pick_In_Every_Order_And_Count_Leaves_One_Holder` (6 orders x 1/2/5 repeats), `Holds_A_Refused_Duplicate_Names_The_Owner_And_Changes_Nothing` (the duplicate never reaches Windows), `Holds_Random_Storm_Of_Key_And_Pick_And_Page_Operations_Keeps_Windows_And_Settings_And_File_In_Step` (3000 steps in 5 seeds; after every step: no shared key, `registrar.Held` equals the combinations in the settings exactly, zero double registrations, no key for a missing pick or page, file on disk read back to the same keys, status Loaded).
- *Same rules as the main key (K2 to K7, K9):* `Holds_Every_Accepted_Capture_Is_Stored_And_Read_Back_As_The_Same_Combination` (every virtual key -2 to 0x2FF x 8 modifier subsets x Windows flag), `Holds_Windows_Keys_And_Modifier_Only_Presses_Never_Make_A_Combination`, `Holds_Unsafe_Combinations_Are_Refused_For_The_Main_Key_A_Page_And_A_Pick` (10 combinations x 3 kinds), `Holds_A_Save_That_Fails_Leaves_The_Old_Key_Held_And_Releases_The_New_One`, `Holds_A_Registrar_That_Refuses_Leaves_Everything_As_It_Was_With_Its_Own_Words_For_1409`, `Holds_A_Key_Press_On_A_Locked_Settings_File_Changes_Nothing`.
- *Removing a pick or page gives its key back (K11 and WO6):* `Holds_Removing_A_Pick_Releases_Its_Key_At_Once_And_A_Moved_Pick_Keeps_It`, `Holds_Deleting_A_Page_Releases_Its_Key_And_The_Keys_Of_Its_Picks`, `Holds_Releasing_Keys_Of_Missing_Picks_Keeps_What_It_Could_Not_Save_And_Finishes_On_The_Next_Try` (a save that fails half way), `Holds_Releasing_Keys_Of_Missing_Picks_Keeps_The_Keys_Of_Picks_That_Exist_And_Ignores_A_Pick_That_Never_Had_One`, `Holds_Deleting_A_Built_In_Or_Missing_Page_Is_Refused_And_Touches_Nothing`.
- *Restore and clear (K8):* `Holds_Restore_All_Clears_Every_Page_And_Pick_Key_And_Brings_Back_The_Main_Key`, `Holds_The_Main_Key_Cannot_Be_Cleared_And_Clearing_Twice_Is_Quiet`.
- *Ids with colons and look-alikes:* `Holds_ActionFor_Tells_Main_Page_And_Pick_By_Their_Ids_Including_Ids_With_Several_Colons`, `Holds_Pick_Ids_That_Look_Alike_Are_Different_Actions` (case, trailing space, `%0020`, accents, composed versus decomposed), `Holds_A_Custom_Page_Id_Can_Never_Contain_A_Colon` (`PageStore.Parse` refuses 7 ids).
- *Pick key handler:* `Holds_A_Pick_Key_Jumps_First_Then_Comes_In_On_The_Picks_Own_Page_And_A_Missing_Pick_Does_Nothing`, `Holds_Pressing_The_Key_Twice_Quickly_Does_The_Thing_Twice_Not_Once_And_Never_Out_Of_Order`.
- *Idle time, screen side:* `Holds_Idle_Seconds_Are_Kept_Between_Two_And_Sixty_As_A_Whole_Number_And_Saved_And_Read_Back` (19 values: MaxValue, 1e300, 86401, 60.5, 59.49, 2.49, 1.5, 0.0001, 0, -5, MinValue ...), `Holds_A_Negative_Zero_Idle_Time_Is_Two_And_Is_Saved_As_A_Readable_Number`, `Holds_An_Idle_Time_That_Is_Not_A_Number_Is_Refused_And_Changes_Nothing` (NaN, both infinities; file untouched), `Holds_Setting_The_Same_Idle_Time_Again_Does_Not_Save_Or_Raise_Changed_And_A_Failed_Save_Changes_Nothing`, `Holds_Idle_Rounding_Is_To_A_Whole_Second_Even_At_Halves_NoteBankersRounding` (on record: halves round to even, 2.5 gives 2 and 3.5 gives 4; the work order says only "whole seconds").
- *Idle time, file side:* `SettingsFileAttackTests.Holds_An_Idle_Value_That_Is_Not_A_Number_Above_Zero_Up_To_86400_Makes_The_File_Unreadable_And_Gives_The_Defaults` (16 spellings incl. `1e999`, `-0`, `"5"`, null, `[]`, `true`, 86401), `Holds_A_Usable_Idle_Value_Is_Kept_Between_Two_And_Sixty_As_A_Whole_Second_At_A_Real_Start` (17 spellings incl. 0.0001, 2.5, 59.5, 60.4, 86400), `Holds_The_Self_Test_Reading_Keeps_The_Idle_Time_As_It_Is`.
- *The key file:* `Holds_Nonsense_In_PickKeys_Makes_The_File_Unreadable_Never_Throws` (26 nonsense bodies: object/array/number/null/bool values, unsafe combinations, Win keys, the same combination twice, the main key's combination, names without colon, empty, `a:`, with space, tab, NUL, backslash, no-break space, lone surrogate), `Holds_A_Name_Of_200_Characters_Is_Accepted_And_201_Is_Not`, `Holds_Empty_And_Missing_Entries_Mean_No_Key_And_A_Missing_Object_Means_None`, `Holds_Ten_Thousand_Empty_Entries_And_Ten_Thousand_Equal_Combinations_Are_Handled_Quickly`, `Holds_Every_Safe_Combination_Can_Be_A_Pick_Key_And_Round_Trips_Through_The_File` (500), `Holds_Two_Page_Entries_With_The_Same_Name_Take_The_Last_And_Stay_Saveable`, `Holds_Broken_Files_Are_Unreadable_With_The_Defaults_And_Never_Throw` (15 files), `Holds_Very_Deep_And_Very_Large_Files_Are_Unreadable_Not_A_Crash`, `Holds_Comments_Trailing_Commas_And_A_Byte_Order_Mark_Are_Read_And_Loading_Never_Writes_A_Broken_File`, `Holds_Load_Never_Writes_An_Unreadable_File_And_The_Defaults_File_Is_Written_Only_When_Missing`.
- *Written by `ToJson`, read back equal:* `Holds_Settings_Written_By_ToJson_Are_Read_Back_Equal_For_Many_Random_Settings` (300 random settings with custom pages, page and pick keys, glass, start flag; also writing again is byte-identical), `Holds_Self_Test_Settings_With_Any_Positive_Idle_Time_Read_Back_Equal_When_Not_Bound`, `Holds_Save_Refuses_To_Write_A_Number_That_Is_Not_One_And_Leaves_The_Old_File`, `Holds_Equal_Settings_Have_Equal_Hash_Codes_And_Different_Pick_Keys_Make_Them_Unequal`. (Exceptions are findings 7, 8, 11.)
- *Start with Windows flag in the file:* `Holds_Start_With_Windows_Must_Be_True_Or_False` (6 spellings), `Holds_Start_With_Windows_Defaults_Off_And_Reads_True_And_False`.

**Close button (WO6 section 5)**
- *Dimmed and silent until a click in this showing; a new showing dims it; only the last click counts; press after press does nothing:* `CloseAttackTests.Holds_Dimmed_And_Silent_Until_A_Pick_Was_Clicked_In_This_Showing_And_After_A_New_Showing`, `Holds_A_Press_Closes_The_Clicked_Window_Once_And_A_Second_Press_Does_Nothing`, `Holds_Two_Clicks_Then_One_Press_Closes_Only_The_Window_Of_The_Last_Click`, `Holds_A_Later_Click_That_Arms_Nothing_Disarms_The_Earlier_One`, `Holds_Click_Then_New_Showing_Then_Press_Closes_Nothing_Even_If_Everything_Is_Still_There` (50 rounds).
- *Only a click that brought a window forward arms it:* `Holds_A_Click_That_Only_Starts_Or_Opens_Something_Does_Not_Arm_The_Button_Even_With_A_Window_Named` (4 kinds), `Holds_A_Folder_Pick_Never_Arms_Whatever_The_Plan_Says`.
- *The facts are asked again at the press:* `Holds_A_Window_That_Vanishes_Between_The_Click_And_The_Press_Is_Not_Closed_And_The_Button_Dims` (and a recycled handle is not remembered), `Holds_The_Islands_Own_Window_Is_Never_Armed_Even_When_It_Becomes_One_After_The_Click`, `Holds_A_Window_That_Becomes_Elevated_After_The_Click_Is_Refused_With_A_Reason_On_Every_Press_Until_It_Is_Not`, `Holds_A_Window_That_Is_Elevated_At_The_Click_Dims_With_The_Admin_Line_And_A_New_Showing_Removes_It`, `Holds_An_Elevated_Window_That_Vanishes_Is_Forgotten_Not_Refused`, `Holds_A_Disabled_Window_Is_Sent_Nothing_And_Is_Closed_Once_Its_Question_Is_Answered`, `Holds_A_Window_Disabled_At_The_Click_Is_Still_Armed_And_Sent_Nothing_Until_Enabled`, `Holds_Odd_Window_Handles_Are_Only_Ever_Closed_When_The_Facts_Say_They_Exist` (0, -1, 1, 0xFFFF, long extremes).
- *Website picks:* `Holds_A_Site_Pick_Closes_The_Tab_With_The_Add_On_And_Nothing_Without_It`, `Holds_A_Site_Pick_With_The_Add_On_Gone_Or_The_Tab_Gone_At_The_Press_Closes_Nothing`, `Holds_A_Site_Click_That_Opened_A_New_Tab_Does_Not_Arm_The_Button`, `Holds_A_Site_Click_With_No_Tab_Key_Does_Not_Arm_And_A_Program_Pick_Never_Closes_A_Tab`, `Holds_A_Site_Click_Then_A_Program_Click_Closes_The_Window_And_Not_The_Tab`, `Holds_An_Empty_Tab_Key_Is_Treated_Like_Any_Other_Key`.
- *Facts that throw:* at the click, `Holds_A_Fact_That_Throws_At_The_Click_Leaves_The_Button_Unable_To_Close_Anything` and its site twin: the exception reaches the caller, the button is not Ready (it stays dimmed) and a later press does nothing. At the press, `Holds_A_Fact_That_Throws_At_The_Press_Never_Yields_A_Close_And_The_Next_Press_Asks_Everything_Again` (4 facts) and `Holds_A_Fact_That_Throws_At_A_Tab_Press_Never_Yields_A_Close`. What the code does today: **the exception goes up to the caller untouched, and after a throw at the press the button stays armed (State Ready)** until the next press. That does not break the contract "never armed on a window that may not be closed", because every press asks every question again (proved: after the throw the window is made elevated and the next press refuses it). It does mean a throwing fact is a crash in `CloseHandler.Press` (no try/catch there) and the X stays drawn bright after the throw (`Show()` is skipped). Not counted as a defect: the real `WindowFacts` calls do not throw. See part 5.
- *Everything at once:* `Holds_Random_Storm_Of_Clicks_Showings_Presses_And_Changing_Facts_Never_Closes_Anything_It_Should_Not` (48 000 steps, a model written from the work order: every `CloseWindow`/`CloseTab` outcome is the model's armed target with every fact true at that moment, every refusal and every silence matches, `Line` is the admin line exactly when `NeedsAdmin`, never Ready when nothing is armed; more than 50 windows closed and 20 refusals in the run, so the storm is not tame).
- *Frames:* `Holds_The_Close_Frame_Is_The_Protocol_Example_And_Reads_Back`, `Holds_A_Bad_Close_Frame_Is_Rejected_With_A_Reason_And_Never_Throws` (20 frames: missing/negative/2^31/2^63/`1e999`/1.5/string/null/array/object/bool ids, wrong type case, arrays, empty, truncated; reasons are fixed phrases of at most 40 characters), `Holds_Odd_But_Valid_Close_Frames_Read_As_The_Same_Integer` (extra nested fields, field order, `-0`, `11.0`, `1.1e1`, int max: accepted or refused, never another number), `Holds_A_Repeated_Id_Name_Never_Reads_A_Negative_Or_Out_Of_Range_Number`, `Holds_A_Frame_Of_One_Megabyte_Is_Refused_As_Oversize_And_One_Just_Under_Is_Read` (to the byte), `Holds_A_Frame_Whose_Characters_Are_Few_But_Bytes_Are_Many_Is_Refused_As_Oversize`, `Holds_Deep_Nesting_Lone_Surrogates_And_Control_Characters_Never_Throw` (50 000-deep nesting, `\ud800`, NUL, two frames in one, BOM), `Holds_Random_Garbage_And_Truncated_Frames_Never_Throw_In_Either_Direction` (20 000 garbled and cut frames, both readers), `Holds_The_Add_On_Answer_To_A_Close_Reads_The_Protocol_Example_And_Rejects_Variants` (`ResultMessage` with cmd "close": 12 variants), `Holds_An_Island_Frame_Fed_To_The_Add_Side_Reader_And_Back_Is_Rejected`.

**Start with Windows (WO6 section 3)**
- *The line written is exactly the quoted path, a space, `--autostart`, and splits back into those two arguments under the C runtime's rules:* `StartupAttackTests.Holds_A_Command_Line_Splits_Into_Exactly_The_Path_And_The_Autostart_Argument` (12 paths: spaces, unicode, UNC, `\\?\`, forward slashes, `&^()`, `%`, apostrophe).
- *A path that cannot be written safely is not written:* `Holds_A_Path_That_Cannot_Be_Written_Safely_Is_Not_Written_And_Has_No_Command_Line` (22 cases: empty, blank, relative, drive-relative, embedded or surrounding quotes, CR/LF/NUL/tab/DEL/NEL, trailing backslash or slash, root), `Holds_A_Null_Path_Is_Handled_As_Not_Writable`.
- *The length (STARTUP_PATH_TOO_LONG):* `Holds_The_Whole_Command_Line_Of_260_Characters_Is_Written_And_261_Is_Refused_With_The_Reason_And_Nothing_Is_Written`, `Holds_A_Path_Of_Exactly_260_And_261_Characters_Alone_Is_Too_Long_Because_The_Line_Is_Longer`, `Holds_A_Huge_Path_Is_Refused_Not_Written_And_Does_Not_Throw`, `Holds_Unicode_Counts_In_Characters_As_The_Page_Says_Not_In_Bytes`.
- *Any order of on, off and is-on:* `Holds_Every_Order_Of_On_Off_And_IsOn_Matches_A_Simple_Model` (20 000 random steps), `Holds_Turning_On_Twice_Writes_Once_And_Turning_Off_Twice_Removes_Once`, `Holds_Nothing_Writes_At_Launch_Or_When_Asking`, `Holds_A_Registry_That_Refuses_Changes_Fails_Without_Lying_And_Leaves_What_Was_There`.
- *A value that is not ours:* `Holds_A_Value_That_Is_Not_Ours_Reads_As_Off_And_Turning_Off_Leaves_It` (7 variants: empty, garbage, another folder, no quotes, no argument, extra space), `Holds_Turning_On_Replaces_A_Value_That_Is_Not_Ours_With_Ours_And_Turning_Off_Then_Clears_It`, `Holds_A_Value_That_Differs_Only_In_Letter_Case_Is_Ours_As_Windows_Paths_Are_Not_Case_Sensitive`.
- *The launch argument:* `Holds_Only_The_Exact_Argument_Makes_A_Silent_Start`.
- *Inside the session:* `Holds_The_Session_Switch_Writes_Once_Shows_The_Registry_And_Keeps_Only_Yes_Or_No_In_The_File` (no path in the file; a value removed behind the app's back is shown as off and not repaired), `Holds_A_Too_Long_Path_Is_Refused_In_The_Session_With_Its_Words_And_The_File_Is_Untouched`, `Holds_A_Registry_That_Fails_Leaves_The_Setting_Off_And_Says_So`, `Holds_Turning_It_Off_Removes_The_Value_And_Saves_No`, `Holds_A_Session_Without_A_Switch_And_One_On_A_Locked_File_Write_Nothing`. (The exception is finding 1.)

## 4. What I could not test and why

- **A real second screen, real DPI, a real taskbar, `ScreenReader` and `ScreenPlacer`:** need Windows and a window; the placement read-back (up to three moves) and the framework moving a window on a scaling change are in the app. Only the pure choice and fit were attacked.
- **`HotkeyHost` and a real `RegisterHotKey`:** `PretendRegistrar` copies what the source says (a combination already held answers true; release by combination). Whether Windows really accepts every combination the editor accepts (for instance Alt+F9, Ctrl+Space) is not tested; the error code 1409 is marked UNVERIFIED in the source and I could not confirm it either (no network).
- **The real registry:** forbidden. `OutsideStartupRegistry` was read, not run. Not tested: that a Run-key value containing `%` is expanded by Windows when it starts the program (UNVERIFIED; the source writes a plain REG_SZ and `IsWritablePath` allows `%`, so a program path containing `%NAME%` could be changed at the next sign-in); a trailing space or dot in the path (Windows normalises them); 260 counted in characters as the Learn page says (the code and the tests agree; I could not re-read the page).
- **Whether the foreground window is still the one the click brought forward:** `ICloseFacts` has no such question, so a test cannot even state it. WO6 says the button "acts on the one window that click brought forward — the window now in front of them". If the person clicks a pick (window A comes forward) and then switches by hand to window B while the island is still visible, the X still closes A, which they are not looking at: the very danger the section is written around. By the letter of the work order this is as designed, so it is not a finding, but it is the weakest point of the button (part 5).
- **Window handle reuse by Windows:** `WindowExists` is a handle check; a handle recycled for another window between click and press cannot be told apart. Not testable without a real window.
- **Threads:** the picker, the chooser and the session are single-thread by their docs; only `VisibleLimit` has a parallel test. The real reader raising `Changed` while a summon computes was not exercised.
- **Exceptions from `jump`/`comeIn` in `PickKeyHandler`:** the handler does not catch; an exception from `jump` skips `comeIn` and travels to whoever raised the key (the window procedure of the hotkey host). Not tested because the contract does not say jump may throw.
- **The narrowest screens:** `ScreenPlacer.ApplyLimit` sets `VisibleLimit = tiles - 1` and the setter clamps to at least 1, so on a screen where only one tile fits the capsule shows one pick plus the + tile (two tiles) and is clipped. `ScreenFit`'s doc allows the clipping ("never absent"), so it is not a finding; the app part was read, not run.
- **The end-to-end claims** (the island really comes on the pointer's screen, sharp; a pick key jumps; the X closes only the clicked window; start with Windows after a restart): all NEEDS-HUMAN-VERIFY in the work order.

## 5. Requests to the main session

1. **Start with Windows (finding 1):** in `SetStartWithWindows`, save `settings.json` before writing the registry (and keep the old value to put back if the registry then refuses), or call `TurnOff`/`TurnOn` to undo when the save fails; never answer "nothing was changed" after the registry changed.
2. **Delete page (finding 2):** build the three new stores, do the three saves that can fail (picks, pages, settings), and only then release keys and swap the in-memory stores; if one fails, put back the ones already written. Or make the message say what did change.
3. **Keys (findings 3, 4, 5):** (a) put a `Warning` in `SessionResult` when `ReleaseKeysOfMissingPicks` could not save, and call it again from every later `PressKey`/`ClearKey`/pick edit and when the settings screen opens; (b) in `RestoreDefaults` clear page and pick keys first and give the main key back last; (c) in `Assign`, when `old == combo`, still call `TryRegister` (the real host is idempotent).
4. **Ids (findings 8, 9, 10):** one rule for what a pick id may be, in one place: `IsStorable` requires the colon, no white space or control characters, at least three characters; `SettingsSession.PressKey` refuses an id that is not in `Pages`/`Picks`.
5. **`Settings` file (findings 6, 7, 11, 12):** `Parse` refuses a repeated pick name; `Equals` compares page keys by page id; `Save` refuses an idle time that `Parse` would refuse; `ClampIdle` maps NaN to the default.
6. **Close button:** consider adding `bool IsForeground(long window)` to `ICloseFacts` and refusing (dimming) when the clicked window is no longer the one in front; and a `try/catch` in `CloseHandler.Press` that treats a throwing fact as "dimmed" and calls `Show()`. Both are outside what the tests can prove from Core.
7. **Screens (finding 14):** cut the work area to the full rectangle in `ScreenInfo.SafeWork`.
8. **Never-over list (finding 15) and notice fallback (finding 16):** require `.exe` and no padding in `NeverOverList.IsValid`; treat an unknown `FrontState` like an unknown `Mode` in `NoticeFallback.Next`.
9. **Close frame (finding 17):** clamp or refuse a negative id in `TabMessages.CloseTab`.
10. **Branch housekeeping:** `attack6` was merged and its worktree removed before the report was finished (see the note at the top). The new branch `attack6` (worktree `.worktrees/attack6-final`) holds the one small test fix and this report on top of `85a34af`; merge it, then remove `.worktrees/attack6-final` and the leftover directory `.worktrees/attack6` (not a worktree any more; I did not delete it).
11. When the defects above are fixed, the matching `Defect_` tests turn green as they are; rename each to `Holds_` then and keep it. None of the tests was weakened or removed to reach these totals.
