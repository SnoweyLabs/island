# Attack on WORK-ORDER-5 (ATTACK5, 6 Oct 2026)

Adversarial pass over what WO5 changed or added, in `Island.Core` only (no window, no app, nothing started): `StripScroll`,
`StripLayout`, `CapsuleLayout.SizeFor`, `DragRemove`, `EscapeRule`, `PlusRow`/`PlusList`, `IslandMachine`'s second row,
`GreyIcons`, `IconFit`, `WindowDots`, `PlayingTile`, `Equalizer`, and `SettingsSession.MovePick`.

Branch `attack5`, made from main `f513699`. Nothing under `src/` and no existing test or file was changed. The tests live in
`tests/Island.Attack5.Tests/` (xunit, references `Island.Core` only, not in `Island.sln`). Names only like "Alpha", `alpha.exe`,
`example.org`; settings tests use a temporary folder that is removed afterwards. No existing assertion contradicts a new one.

Command and result:
- `dotnet test tests/Island.Attack5.Tests`: **73 tests, 16 failed, 57 passed.** Every failing test is named `Defect_...` and fails
  because of the defect below; every passing test is named `Holds_...` and is the coverage statement (part 3).

## 1. What I added

| File | Content |
|---|---|
| `Island.Attack5.Tests.csproj` | xunit project, shape of Attack4's, reference to `Island.Core` only |
| `Support.cs` | `Clock` (a machine and whole frames), item and now-playing builders, a registrar that grants every key, a temp folder |
| `StripAttackTests.cs` | wheel storm on forty picks (extremes, sub-notch amounts, `int.MinValue`), 840 one-unit amounts, model check of the notch sum, nonsense counts/room/indexes, drawn-position-never-assigned on every operation, strip geometry against the width rule at every target, `SizeFor` with the + tile first/middle/last/none/two, up to 100 000 picks, `TilesThatFit` |
| `DragAttackTests.cs` | every event in every state, press while pressed, release twice, lift then lose the mouse then release, edges of the drag distance, 5 x 20 000-step random storm with extreme finite points, NaN/infinite points, bad distances |
| `MachineAttackTests.cs` | second row in every phase, non-finite times, Esc order and same millisecond, page change vs row close, `SetPages`, `ContentsChanged`, idle with the row open, time going backwards, two 24 000-tick storms of every input (bursts of up to three inputs on one millisecond, pages and picks coming and going, NaN/infinite times, the M3 spring equation) |
| `LooksAttackTests.cs` | `IconFit` (zero, negative, NaN, infinite, huge scales and sizes, every whole-percent scaling), `GreyIcons` (empty, one pixel, every alpha, lying sizes, threads), `WindowDots`, `PlayingTile.PickIdFor`/`SecondLine`, `Equalizer.Heights` (NaN, infinity, negative, 1e300, `double.MaxValue`, periodic, continuous, staggered) |
| `PlusAttackTests.cs` | `PlusRow`/`PlusList` with 3000 windows, case and package grouping, unstorable names, tabs, folders, add/remove alternation, `AddedTo` refusals |
| `SettingsAttackTests.cs` | `MovePick`: moves, refusals, locked file, read-only file, a thousand picks, an unstorable move |

## 2. What I broke

Severity is my judgement: HIGH = Dan sees it in normal use; MEDIUM = plausible in normal use, or acts on something he did not
mean; LOW = needs an odd input, only bends a stated rule, or is cosmetic; LATENT = nothing in the app produces the input today.
No HIGH defect found.

### 1. A press that cannot lift is still a click however far it was dragged (MEDIUM)
- **Input:** `Press(index, canLift: false, ...)` (the + tile, or any pick while the second row is open), `Move` 200 units away,
  `Release(overCapsule: false)`.
- **What happens:** `Release` returns `Click`. Only a lifted tile has a "not a click" path. In the app (`DragHandler.OnReleased`)
  that calls `TileClicked(index0)`: dragging away from the + tile toggles the row; dragging a pick off while the row is open closes
  the row and jumps to that program, although the person dragged away.
- **Should:** a press that left the drag distance is never a click (return `None` or `Cancelled`).
- **Test:** `DragAttackTests.Defect_A_Press_That_Cannot_Lift_Is_Still_A_Click_However_Far_It_Was_Dragged`.

### 2. After a pick is removed while open, `SelectedItem` points past the end for 120 ms (MEDIUM)
- **Input:** Apps shows five picks; `ItemClick(4)`; the last pick is dragged off (the app calls `ContentsChanged`) so the page has
  three rows.
- **What happens:** `ContentsChanged` only schedules the swap 120 ms later; until then `SelectedItem == 4` and `ContentsItems` has
  three entries. This breaks the invariant that Island.Tests' own storm asserts (`SelectedItem` inside the rows). The app's
  `SetSelected` happens to ignore an out-of-range index, so today the effect is a ring left on a tile that is gone; any consumer
  that indexes `ContentsItems[SelectedItem]` throws.
- **Should:** clamp the selection in `ContentsChanged` (as `Summon` and `Expand` already do).
- **Test:** `MachineAttackTests.Defect_Selection_Points_Past_The_End_After_The_Selected_Pick_Is_Removed_While_Open`.

### 3. A pointer position that is not a number lifts the tile (MEDIUM)
- **Input:** `Press(...)` then `Move(new DragPoint(NaN, 100))`, or `+/-Infinity`; or `Press` at NaN and then any ordinary `Move`.
- **What happens:** every comparison with NaN is false, so "inside the drag distance" is false and the move returns `Lifted`;
  letting go anywhere off the capsule then removes the pick. A press at a non-finite point makes the very next move a lift.
  (WPF does not hand out NaN positions today; the state machine is documented as "given points as plain values".)
- **Should:** ignore non-finite points (no lift, no start).
- **Test:** `DragAttackTests.Defect_A_Pointer_Position_That_Is_Not_A_Number_Lifts_The_Tile`.

### 4. A package family that starts with an underscore claims every playing app (MEDIUM, odd input)
- **Input:** `Pick.ForProgram("Alpha", "media", null, "_abc1234")` (storable) and a session whose app is `other.exe`.
- **What happens:** `PlayingTile.IsPlayerOf` takes `package.Split('_')[0]` (empty) and tests `StartsWith`; every app id starts with an
  empty string, so this pick's tile dances for whatever plays.
- **Should:** no match when the part before the underscore is empty (or too short to mean anything).
- **Test:** `LooksAttackTests.Defect_A_Package_Family_That_Starts_With_An_Underscore_Claims_Every_Playing_App`.

### 5. The Store package match has no word boundary (LOW)
- **Input:** a pick with package family `Alpha_pub12345` and a session app `AlphaBeta_pub999!App`.
- **What happens:** bare `StartsWith("Alpha")` matches; two Store apps whose names begin alike take each other's tile.
- **Should:** require the app id to continue with `_`, `!` or `.` after the package name.
- **Test:** `LooksAttackTests.Defect_The_Package_Prefix_Match_Has_No_Word_Boundary`.

### 6. A negative or NaN drag distance lifts with no movement (LOW)
- **Input:** `new DragRemove(-1, -1)` (or `NaN`), `Press` at a point, `Move` to the same point.
- **What happens:** `Math.Abs(0) <= -1` is false, so the tile lifts; no click can ever happen. The constructor trusts
  `SystemParameters`. Zero works correctly (verified).
- **Should:** treat a bad distance as the default (4) or as zero.
- **Test:** `DragAttackTests.Defect_A_Negative_Or_NaN_Drag_Distance_Lifts_The_Tile_Without_Any_Movement`.

### 7. A re-summon in the first 110 ms of leaving keeps the capsule two rows high and empty (LOW, cosmetic)
- **Input:** second row open, `ShowHideKey` (leave), 50 ms later `ShowHideKey` (back).
- **What happens:** `Summon` closes the row (`SecondRowOpen == false`) but does not retarget the height spring, which stays 150 until
  `Expand` fires 320 ms later: an empty 150-high capsule is drawn for a third of a second.
- **Should:** `Summon` retargets the height to the one-row value when it closes the row.
- **Test:** `MachineAttackTests.Defect_Resummon_Within_The_First_110_Ms_Of_Leaving_Keeps_The_Capsule_Two_Rows_High_But_Empty`.

### 8. Esc that closes the second row does not reset the idle clock (LOW)
- **Input:** keyboard summon, row open, 4 s idle, `EscapeKey`.
- **What happens:** digits, other keys, clicks and the + all poke the idle clock; `CloseSecondRow` does not, so the island can leave a
  moment after the person's Esc.
- **Should:** `EscapeKey` pokes the idle clock when it closes the row.
- **Test:** `MachineAttackTests.Defect_Closing_The_Row_With_Escape_Does_Not_Reset_The_Idle_Clock`.

### 9. A row closed by the island leaving is not counted as closed (LOW)
- **Input:** row open, `ShowHideKey` (or the idle clock) sends the island away.
- **What happens:** `Dismiss` and `Summon` clear `SecondRowOpen` without `RowClosedCount++` or a `RowClosedOrder`; the properties are
  documented "how many times the second row has closed" and the work order lists "with the island" among the ways it closes. Nothing
  in the app reads them today.
- **Should:** count every close in one place.
- **Test:** `MachineAttackTests.Defect_A_Row_Closed_By_The_Island_Leaving_Is_Not_Counted_As_Closed`.

### 10. A strip with no room shows a right arrow that does nothing (LOW)
- **Input:** `new StripScroll(10, maxVisible: 0)` (the second row is built as `new StripScroll(0, 0)` until it knows its width).
- **What happens:** `Visible == 0`, `HiddenRight` is true (arrow drawn), but `ArrowRight` moves by `Visible` = 0 tiles: a dead arrow;
  the wheel can slide every pick out of sight (`MaxPosition == PickCount`).
- **Should:** with no room, hide nothing or move by at least one tile.
- **Test:** `StripAttackTests.Defect_A_Page_With_No_Room_Shows_A_Right_Arrow_That_Does_Nothing`.

### 11. A tab on an IP address is offered as a site (LOW)
- **Input:** open tabs on `127.0.0.1` and `192.168.0.1` with the add-on connected.
- **What happens:** `PlusList` comments say an address "cannot be a pick" and is not offered, but `Pick.IsStorable` only needs a dot in
  the host, so both are offered and the small + stores them as website picks.
- **Should:** leave out hosts that are IP addresses (as `localhost` is).
- **Test:** `PlusAttackTests.Defect_An_Ip_Address_Tab_Is_Offered_As_A_Site`.

### 12. `MovePick` to the page the pick is already on fails with a read-only file (LOW)
- **Input:** picks file read-only; `MovePick(id, samePage)`.
- **What happens:** `ApplyPicks` saves even when nothing changed, so the person is told "picks.json could not be saved" for a move that
  needed no saving.
- **Should:** return `Changed = false`, no refusal, without saving.
- **Test:** `SettingsAttackTests.Defect_A_Move_To_The_Page_The_Pick_Is_Already_On_Fails_When_The_File_Is_Read_Only`.

### 13. A refused save leaves `picks.json.tmp` behind (LOW)
- **Input:** picks file read-only; any `MovePick` to another page.
- **What happens:** `PickStore.Save` writes the `.tmp` file, then `File.Move` onto the read-only target fails and the `.tmp` stays,
  holding the refused list. (Older code, now reached by the new setting.)
- **Should:** delete the temp file when the move fails.
- **Test:** `SettingsAttackTests.Defect_A_Refused_Save_Leaves_A_Temporary_File_Beside_The_Picks`.

### 14. A move that cannot be stored reports success (LOW, LATENT)
- **Input:** a `PageStore` holding a page whose id the picks cannot hold (`odd/page`; the page store's own rules forbid it, its
  constructor does not); `MovePick(alpha, "odd/page")`.
- **What happens:** `PickStore.Move` keeps the pick where it was; the session returns `Ok` with `Changed = false` and no words.
- **Should:** a refusal in plain words.
- **Test:** `SettingsAttackTests.Defect_A_Move_That_Would_Make_The_Pick_Unstorable_Reports_Success_With_Nothing_Changed`.

### 15. A folder that cannot be stored is offered in the second row (LOW, LATENT)
- **Input:** `FolderWindow(7, "Gaming", ...)`.
- **What happens:** programs and sites are filtered with `ToPick` before being offered; folders are not, so the tile is offered and
  its small + does nothing. Today the folder reader only sets one of the six known names.
- **Should:** the same `ToPick` filter for folders.
- **Test:** `PlusAttackTests.Defect_A_Folder_That_Cannot_Be_Stored_Is_Offered_Although_Its_Small_Plus_Does_Nothing`.

### 16. Two + tiles share one slot (LOW, LATENT)
- **Input:** items `[A, B, C, +, +]`.
- **What happens:** `ShownTiles` gives the width rule room for 5 tiles, `TileLeft` puts both + tiles at the same left and leaves the last
  slot empty. The page builders only ever add one + tile.
- **Should:** give `TileLeft` a way to tell the + tiles apart, or document one + per page.
- **Test:** `StripAttackTests.Defect_Two_Plus_Tiles_Share_One_Slot_While_The_Width_Rule_Makes_Room_For_Both`.

## 3. What I could not break

Each is a passing `Holds_...` test.

- **Strip:** 20 000 wheel steps with `int.MinValue`, `int.MaxValue`, 0, +/-1, 119, 120, 121, +/-12000 and +/-12001 mixed with
  `Tick(NaN, +/-Infinity, -1, 1e300, double.MaxValue, Epsilon, 0)`, arrows at both ends, on forty picks: the target never leaves
  0..`MaxPosition`, only lands on whole tiles, the position stays finite and settles on the target. 400 notches each way return to the
  start; 840 one-unit amounts are exactly seven notches; over 3000 random sub-notch amounts the target stays within one notch of the
  summed amount. `SetCount`/`SetMaxVisible`/`BringIntoView`/`Reset` with `int.MinValue`..`int.MaxValue`, zero picks, fewer picks than the
  target, never throw and stay in range; no operation moves the drawn position by itself (5 000 random operations); fewer picks than the
  target bring it back through the spring, not by assignment. At every target 0..33 exactly seven picks are inside the strip, the +
  tile starts one gap after the last of them, and the strip plus the + tile equal what the width rule gives.
- **Width rule:** `SizeFor` with the + tile first, in the middle, last, absent, alone, with 0 to 100 000 picks, on a Media and a
  non-Media page: width is always the rule for at most seven picks plus the + tile, height 76, radius 38. `TilesThatFit` agrees with
  `StripWidth` for 1..1000 tiles to the micro-pixel and survives NaN, infinity and negatives.
- **Drag:** move before press, release without press, cancel and lost mouse in every state, press while pressed or lifted, lift then
  release twice (removes once), lift then lose the mouse then release (never removes), the drag-distance edge on each axis alone,
  distance 0, coming back inside the distance (no second lift, no un-lift); five random storms of 20 000 events with extreme finite
  points keep "lifted implies pressed", "lifted at most once per press" and the result of every event consistent with the state.
  `EscapeRule` truth table.
- **Machine:** the row never opens while hidden, flying in or flying out; non-finite times change nothing for every row input; one Esc
  does one thing, also three on one millisecond; no keyboard means Esc does nothing; a page change on the same millisecond closes the
  row first (order numbers); the same page key or digit leaves the row open; `ContentsChanged` keeps the row and the height target;
  `SetPages` closes the row and replaces a removed page; idle dismisses an open row; time going backwards never throws. Storms of 24 000
  ticks with five seeds (static pages) and three (pages and picks changing, selection range excluded because of defect 2): no throw, no
  spring jump (M3 equation), `SecondRowOpen` implies Open, Hidden implies nothing shown and no row, no keyboard while leaving, and
  while Open the height target is always 150 with the row and 76 without it.
- **Looks:** `IconFit` never returns NaN, infinity or a negative size for any width and height from `int.MinValue` to `int.MaxValue`,
  and a bad scale counts as 1; the picture exactly the tile's pixel size fills the tile at every whole percent from 100 to 500.
  `GreyIcons`: empty picture, one pixel, every alpha 0..255 kept, no colour left, nothing brighter than 242, the original untouched,
  lying `Width`/`Height` (65 536 x 65 536, negative) and short or long arrays neither throw nor allocate by size, 64 threads get one
  copy. `WindowDots` over the whole `int` range. `PlayingTile.PickIdFor`: look-alike hosts (`notexample.org`, `example.org.evil.net`,
  `sub.example.org`) never match, `www`, case and a trailing dot do; null view, no picks, the whole-browser session and a program/site
  kind mix-up give nothing. `Equalizer.Heights` is finite and within 5..17 for NaN, both infinities, negatives, 1e15..1e300,
  `double.MaxValue` and 100 000 random times, stands at 5 when paused, is periodic every 0.9 s, staggered by exactly 0.2 and 0.45 s, and
  never moves a bar by 0.1 in a millisecond.
- **Second row's list:** 3000 windows give at most 40 tiles, top-most first; a picked program is never offered whatever its case;
  windows of one program, and a package-only window, group into one tile; unstorable names and the island's own program are left out;
  sites group by normalised host, newest first, only with the add-on connected; 2000 alternations of add and drag-off keep the store
  consistent and never offer a pick; `AddedTo` refuses a blank, path-like, too long or unknown page, a full store and an entry with no
  host or folder, and never touches the old store.
- **`MovePick`:** moves a pick to exactly one other page, saves at once (round trip through the file), raises one event; a missing page
  or pick gives the right words and writes nothing; a locked picks file refuses everything first; a read-only file refuses and changes
  nothing in memory; a thousand picks keep all of their order.

## Notes that are not defects

- `IconFit.Place` with a NaN or non-positive tile size returns non-finite or negative numbers; the only caller passes the constant 40.
- `IconFit.Place` with a picture wider than the tile but shorter than it is "smaller": it is drawn at its own size, centred, so it
  overhangs the tile on the long side. The work order does not say what a non-square picture should do.
- The strip's `Reset(index)` takes a pick index; the app already passes -1 for the + tile.
- A huge finite time (for example 1e300) given to the machine and then a normal one would wedge it (the clock only moves forward);
  the machine's time is a monotonic millisecond clock, so I did not write a test for it.
