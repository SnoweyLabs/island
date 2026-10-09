# H2 keys — report (WO10 section 2, the pure function)

Branch `h2-keys`, from `a31afb5`. Territory used: `src/Island.Core/Keys/`, `tests/Island.Tests/Keys/`, and this report. Nothing else touched.

## 1. What I built

Files:
- `src/Island.Core/Keys/KeyTypes.cs` — `KeyCodes`, `KeyInput`, `SelectedTile`, `KeyContext`, `KeyAction`, `KeyDecision`.
- `src/Island.Core/Keys/IslandKeys.cs` — `IslandKeys.Decide`, `IslandKeys.PageAfterTab`, `IslandKeys.IsOpeningText`.
- `tests/Island.Tests/Keys/IslandKeysTests.cs` — class `IslandKeysTests` (22 tests).

All in namespace `Island.Core`. No state, no WPF, no clock; nothing outside the territory changed.

Public API (exact):

```csharp
public static class KeyCodes { const int Tab=0x09, Enter=0x0D, Escape=0x1B, Space=0x20, Left=0x25, Up=0x26, Right=0x27, Down=0x28, Delete=0x2E,
                               Digit1=0x31, Digit9=0x39, Pad1=0x61, Pad9=0x69; }

public readonly record struct KeyInput(int VirtualKey, bool Shift = false, bool Ctrl = false, bool Alt = false, bool Win = false, bool IsRepeat = false);

public enum SelectedTile { None, Pick, Plus }

public sealed record KeyContext   // all init-only, all default false/0/None
{
    bool HasKeyboard; bool SearchOpen; bool SettingsShowing; bool TileLifted; bool IslandLeaving;
    bool RowIsCurrentPage; bool PageChangeUnderWay;
    bool SecondRowOpen; bool SelectionInSecondRow; bool SecondRowHasItems;
    SelectedTile Selected;          // the first row's selected tile
    bool AtFirst; bool AtLast;      // about the row the selection is in (second row when SelectionInSecondRow); both true for an empty row
    int PageCount;
    bool PlusClickActs;             // a click on the + tile would open/close the second row now
    bool MediaCanPlayPause;         // current page is Media and something plays
    bool DeletePending; bool SelectedIsPendingPick;
}

public enum KeyAction { Nothing, CountsAsUse, MoveLeft, MoveRight, Activate, OpenSecondRowAndEnter, OpenSecondRow, LeaveSecondRow,
                        SecondRowJump, SecondRowAdd, NextPage, PreviousPage, PlayPause, AskRemove, ConfirmRemove, CancelPendingDelete }

public readonly record struct KeyDecision(KeyAction Action, bool Handled, bool CancelsPendingDelete);

public static class IslandKeys {
    public static KeyDecision Decide(KeyInput key, KeyContext context);
    public static int PageAfterTab(int index, int count, bool backwards);   // wraps; <2 pages or index out of range: index unchanged
    public static bool IsOpeningText(string? text);                          // = BlankText.HasVisible(text)
}
```

Meaning of the decision:
- `Handled == true`: the decision accounts for the key; the caller must NOT also run its existing path (search, Esc, digits, `OtherKey`). `Action == CountsAsUse` means "call `machine.OtherKey(now)` and nothing else".
- `Handled == false` (always with `Action == Nothing`): the key is LEFT TO THE EXISTING CODE, unchanged. These are: every key when there is no keyboard or the island is leaving (the machine ignores them itself); every key while search is open (search's own, repeats included, so a held Backspace still deletes); Esc (unless a Delete is pending, see below); and every key that is not one of Left, Right, Up, Down, Tab, Enter, Space, Delete: digits (row and number pad), Backspace, letters, modifiers themselves. A repeat of those (and a repeat of Esc) is `CountsAsUse`, handled: one long press cannot close the row and then the island.
- `CancelsPendingDelete == true`: forget the pending Delete before running the action. It is true for every key while one is pending, except a held repeat of Delete (the ask stays) and the Delete that removes (it uses the pending up; the app clears it anyway).
- Esc with a Delete pending returns `CancelPendingDelete`, handled: that press does only that (second row and island stay for the next Esc). Esc with a tile lifted stays the existing code's (it cancels the drag first).

Rule summary (all from WORK-ORDER-10 section 2):
- Left/Right: `MoveLeft`/`MoveRight` unless `AtFirst`/`AtLast` (then `CountsAsUse`, no wrap). They act whatever `RowIsCurrentPage`/`PageChangeUnderWay` say (arrows typed while opening are kept). A repeat of Left/Right acts.
- Enter (row ready = `RowIsCurrentPage && !PageChangeUnderWay`, else `CountsAsUse`): first row: `Activate` for a pick or the + tile, `CountsAsUse` for no tile; second row: `SecondRowJump`, or `SecondRowAdd` with Shift.
- Down (row ready): second row closed: `OpenSecondRowAndEnter` if `SecondRowHasItems`, else `OpenSecondRow` (stay), and `CountsAsUse` where `PlusClickActs` is false; second row already open with the selection in the first row: `OpenSecondRowAndEnter` if it has items, else `CountsAsUse`; in the second row: `CountsAsUse`.
- Up (row ready): `LeaveSecondRow` only from the second row; else `CountsAsUse`.
- Tab / Shift+Tab: `NextPage` / `PreviousPage` when `PageCount >= 2`; use `PageAfterTab` for the wrapped index.
- Space (row ready): `PlayPause` only if `MediaCanPlayPause`; else `CountsAsUse`. Space is taken whole (`Handled`), so it never reaches search.
- Delete (row ready, not repeat): on a pick, first time `AskRemove`; second, with `DeletePending && SelectedIsPendingPick`, `ConfirmRemove`. On the + tile, no tile, or in the second row: `CountsAsUse`.
- With Ctrl, Alt or Win held, or `SettingsShowing`: every key of the eight above is `CountsAsUse`. `TileLifted`: every key but Esc is `CountsAsUse`.

## 2. Commands and results

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test tests/Island.Tests --filter "FullyQualifiedName~IslandKeysTests"   -> Passed 22 / 22 (7 s)
dotnet test tests/Island.Tests                                                  -> Passed 1204, Failed 1, Total 1205 (28 s)
```

The one failure is NOT from this work: `ExtensionGuardTests.The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme` compares the zip's bytes with the files of `extension/` in this worktree and differs at a CR LF versus LF (expected `13,10`, actual `10`): the worktree checkout converted line endings of `extension/` files. It touches no file of mine. It should be green in the main folder; I did not run it there (reading only was allowed, not running the suite from the main folder).

## 3. What is proven, and by which test

All in `IslandKeysTests`:
- `Arrows_Move_Along_The_Row_And_Stop_At_The_Ends` — move decisions; the stop is `AtFirst`/`AtLast` in the context (decision is `CountsAsUse` there, never a wrap); also in the second row.
- `Enter_Is_A_Click_On_The_Selected_Tile`, `Down_Opens_The_Second_Row_And_Enters_It`, `Up_Leaves_And_Closes_The_Second_Row`, `Shift_Enter_In_The_Second_Row_Adds`, `Tab_Goes_Round_The_Pages` (decisions and `PageAfterTab` wrap both ways), `Space_Plays_Or_Pauses_Only_On_Media`.
- `Space_Never_Opens_Search` — Space is handled whole; `IsOpeningText` is false for spaces, tab, CR/LF, ESC, NUL, no-break space, zero-width space, braille blank, empty and null; true for letters, digit, accented letter, emoji, " a".
- `Delete_Twice_Removes_And_Anything_Else_Cancels`, `A_Held_Delete_Never_Removes` (50 repeats), `A_Second_Delete_On_Another_Pick_Removes_Nothing`, `Delete_Does_Nothing_On_The_Plus_Tile`, `Escape_Cancels_A_Pending_Delete_First`.
- `Enter_During_A_Page_Change_Opens_Nothing`, `Arrows_Typed_While_Opening_Are_Not_Lost`, `A_Repeat_Acts_Only_For_Left_And_Right`, `No_Key_Acts_With_Ctrl_Alt_Or_Win_Held` (and with the settings screen showing), `No_Key_Acts_Without_The_Keyboard` (also for a leaving island).
- `Search_Digits_And_Other_Keys_Stay_With_The_Existing_Code` — what is left to the existing code.
- `Key_Codes_Are_The_Documented_Virtual_Keys` — pins the constants.
- Sweeps, both against an independent oracle (`AssertPreconditions`, every action's preconditions written out again, plus the cancel flag rule):
  `Sweep_Every_Context_Against_The_Keys_That_Matter` (all 2^16 flag combinations x 3 selected-tile kinds x the 8 keys, Esc and a digit x plain/Shift/Ctrl x repeat or not; it also asserts every `KeyAction` was reached, so the checks are not vacuous) and
  `Sweep_Every_Key_With_Every_Modifier_Never_Throws_And_Unlisted_Keys_Never_Act` (1200 seeded random contexts x virtual keys -2..260 x all 16 modifier combinations x repeat; unlisted keys are only `Nothing`/`CountsAsUse`).

## 4. Not proven

- No real key was pressed; the function is proven on plain values only. Whether the wiring feeds the context correctly (especially `RowIsCurrentPage`, `PageChangeUnderWay`, `AtFirst`/`AtLast`, `PlusClickActs`) is for the main session's self-test.
- `A_Key_Resets_The_Idle_Clock` is machine-level: not mine.
- Shift+Tab is read as "Tab with Shift"; keyboard layouts or an IME that change what Tab reports were not considered.
- That Esc, digits and typed text still work end to end is by leaving them to the existing code (tested only as "Handled == false").

## 5. Requests / doubts for the main session

My choices where the work order is silent (cautious readings; change the function if Dan meant otherwise):
1. Shift is meaningful only for Tab (previous) and Enter in the second row (add). Shift+Left, Shift+Right, Shift+Up, Shift+Down, Shift+Space, Shift+Delete, Shift+Enter in the first row only count as use. (Shift+Delete never asks or removes.)
2. Up and Down also need the "row is the current page's row and no page change under way" rule, like Enter, Space and Delete, because they open or close a row. Only Left and Right act during opening. The work order says "the arrows move the selection" while opening, which I read as Left/Right.
3. A second Delete on a different pick than the pending one cancels and does nothing; it does not start a new ask (so removal needs two further presses). Unreachable if selection moves cancel the ask, as the work order says.
4. "With nothing in the second row, Down opens it and the selection stays on the + tile": I return `OpenSecondRow`, "open it, do not move the selection". If the selection was on a pick, a click on + would have selected the + tile; the main session may select it, as `TileClicked` does.
5. Down with the second row already open and the selection still in the first row (the + was clicked) moves into the row.
6. Enter on the + tile is `Activate` even if `PlusClickActs` is false (it is "the same path a click takes"; that path decides).
7. Esc is not touched except that a pending Delete takes it first. Esc ignores modifiers, as today.
8. `HasKeyboard` false: the pending Delete should be cancelled by the app on focus loss and when the island leaves (`CancelsPendingDelete` is also true for those keys if a Delete is still pending, but the main session should not rely on a key arriving).
9. A tile that is pressed but not yet lifted: fold it into `TileLifted` (every key then only counts as use), or tell me to add a field. Today `PageKey` waits on `IsPressed`.

Nothing outside my territory needs changing for the function to work. No existing assertion needs to change.

## 6. How to wire it in

Facts confirmed on Microsoft Learn (read 2026-10-07):
- WPF repeat: `KeyEventArgs.IsRepeat` (System.Windows.Input, PresentationCore): "true if the key is repeated" (page updated 2026-05-27). Shift in WPF: `Keyboard.Modifiers & ModifierKeys.Shift` (ModifierKeys: Alt=1, Control=2, Shift=4, Windows=8; page updated 2026-05-27). The same pages give Control, Alt and Windows.
- IMPORTANT: the island does NOT read keys through WPF key events. `OverlayWindow.WndProc` takes `WM_KEYDOWN`/`WM_SYSKEYDOWN` and raises `RawKey(int)` from `wParam`. For that path the repeat flag is bit 30 of `lParam` of WM_KEYDOWN ("previous key state: 1 if the key is down before the message is sent"; the page says it tells the first down transition from a repeated one; updated 2025-07-16): `bool isRepeat = ((long)lParam & (1L << 30)) != 0`. I did not confirm that WM_SYSKEYDOWN uses the same bit on its own page: UNVERIFIED, but the keystroke-flag table is shared; check before relying on it for Alt-chords (they do not act anyway).
- Shift/Ctrl/Alt/Win in that path: `GetKeyState(VK_SHIFT / VK_CONTROL / VK_MENU / VK_LWIN, VK_RWIN)`, high-order bit set = down; Microsoft Learn says it "retrieves the state of the key when the input message was generated", for use in response to a keyboard message (page updated 2025-10-07; VK_SHIFT 0x10, VK_CONTROL 0x11, VK_MENU 0x12, VK_LWIN 0x5B, VK_RWIN 0x5C from "Virtual-Key Codes", updated 2025-10-17). It is not a hook and not input. `Native.cs` declares only `GetAsyncKeyState` today (that one reads the hardware state, not the message's): add `GetKeyState` to `Native.cs` in the app. Alternative: `Keyboard.Modifiers` (WPF), which I did not check against a window that never activates.
- Virtual-key values (Learn, "Virtual-Key Codes"): Tab 0x09, Enter 0x0D, Esc 0x1B, Space 0x20, Left 0x25, Up 0x26, Right 0x27, Down 0x28, Delete 0x2E, digits 0x30 to 0x39, number pad 0x60 to 0x69. Note the existing code names the pad digits 1 to 9 as 0x61..0x69: correct.

Wiring sketch for `IslandController.HandleKey(int virtualKey, bool shift, bool ctrl, bool alt, bool win, bool isRepeat)` (the window raises these; no hook):
1. Build `KeyContext` from the machine and the app (the machine's `HasKeyboard`, `SearchOpen`, `SecondRowOpen`, `SelectedItem`; `ContentsPageId == PageId` and `ContentsVisible` for `RowIsCurrentPage`; a page change is under way while the contents swap is queued; `IsLifted`; settings screen visibility; media: `ContentsPage.IsMedia && _media.PlayingPickId != null`; the second row's selection and the pending Delete are the new machine state).
2. `var d = IslandKeys.Decide(new KeyInput(vk, shift, ctrl, alt, win, isRepeat), ctx);`
3. If `d.CancelsPendingDelete`, clear the pending Delete first.
4. If `!d.Handled`: run today's code unchanged (`Search?.Key`, Esc order, digits, `OtherKey`).
5. If `d.Handled`: switch on `d.Action`: `CountsAsUse` -> `m.OtherKey(now)`; moves -> change the selection and bring it into view; `Activate` -> the one click path (`TileClicked(SelectedItem)`); and so on. `NextPage`/`PreviousPage` -> `PageKey(pages[IslandKeys.PageAfterTab(currentIndex, pages.Count, backwards)].Id)`.
6. In `HandleText`: `if (!IslandKeys.IsOpeningText(text)) { m.OtherKey(now); return; }` before offering the text to search (while search is closed; once it is open, SearchKeys already treats the text as today). Also cancel a pending Delete on any typed text.
7. In `OverlayWindow.WndProc`, `RawText`/`RawKey` stay as they are; add the modifier and repeat reads for `WM_KEYDOWN`/`WM_SYSKEYDOWN` as above.
