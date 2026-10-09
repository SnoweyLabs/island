# DESIGN, round 4 — "does it look like one thing, finished?"

WORK-ORDER-12 section 4, attacker DESIGN, round 4 (checks the repairs of round 3 that touch a look, then what they broke, then anything new).
Branch `rt4-design`, made from `ec974c8` (every repair of rounds 1 to 3). Project `tests/Island.Redteam.Design.Tests/` (new files `Round4*.cs`; no existing test touched). Nothing under `src/` or `extension/` was changed. No window was shown, no screen captured, no Island process started, no self-test run.
The pictures of `dist/` and `review/` were only decoded and compared; the settings view was built, laid out and read on the one STA thread, never shown, never rendered; GDI regions were made as plain objects (no window). Every name is invented.
Official page read (Learn, 2026-10-07): `UIElementAutomationPeer.FromElement` (returns null "if the peer was not created by the CreatePeerForElement method").

## 1. Summary

6 findings: **0 HIGH, 0 MEDIUM, 6 LOW.** 5 `Defect_` tests (red on purpose). **No HIGH or MEDIUM defect: by the order's own rule this ends the rounds.**
Every repair of round 3 that touches a look held in what it was meant to do: the glow's hole is on the capsule's outline to the pixel on every straight edge at 11 scales; the newest full self-test run is pixel-equal to `review/` in 79 of 80 pictures, and the 80th (`settings/glass.png`) is the one that was meant to change.

| id | grade | suggested class | area | one line |
|---|---|---|---|---|
| design-4-1 | LOW | defect (a limit of GDI's arc: the straight edges are exact, the corners are not) | graphics-card light, glow cut | the hole's corner arcs lie up to 1.05 px (125%), 1.28 px (225%) and 1.03 px (250%) inside the outline: a hairline of glow in the corners |
| design-4-2 | LOW | safe improvement (nothing a person sees) | search field | `TailThatFits` lays the text out about 230 times for a 256-unit query (85 ms per key at the end of the field's range, 27 ms at 120 units) |
| design-4-3 | LOW | defect (a stale text, now with a scroll) | Settings, Add panel | closing the panel after a refused add leaves the refusal's words and the page jumps down to them (0 to 500 px) |
| design-4-4 | LOW | proposal (where a refusal is drawn: a look) | Settings, Add panel | a refused add scrolls the page to the notice at the foot of the section: on the first page the panel being worked in is 545 px above the room |
| design-4-5 | LOW | proposal (a look; the words are promised by WO12 section 2) | Settings, General, Moving light | on Graphics card, not available, pressing its pill gives a refusal that is the sentence the card already shows, and the page scrolls 218 px (at 125%) to show the copy |
| design-4-6 | LOW | safe improvement (words only), UNVERIFIED without a screen reader | Settings, screen reader | the heading's live-region event reads `FromElement(heading)`, which is null for a heading that is new on every redraw: nothing is raised unless a client had made the peer |

## 2. The findings

Test names are `Class.Method` in `tests/Island.Redteam.Design.Tests/`.

### design-4-1 — the hole's corner arcs lie inside the capsule's corner arcs (LOW, `Defect_`)

Where: `src/Island.Glass/MovingLight.cs`, `PlaceShapes` (the call `GlassWindow.CutOutRoundedRectangle(..., (int)Math.Round(radius.X + grow))`) and `GlassWindow.CutOutRoundedRectangle` (`CreateRoundRectRgn(..., 2 * radius, 2 * radius)`).
Evidence:
- `Round4CutOutTests.The_Replayed_Arithmetic_Is_The_Sources_Arithmetic` (passes: the replay is the source's arithmetic, four lines compared).
- `Round4CutOutTests.The_Straight_Edges_Of_The_Hole_Are_On_The_Outline_At_Every_Scale_Width_And_Height` (passes): 11 scales (100, 110, 125, 133, 150, 167, 175, 200, 225, 250, 300 percent) x 10 capsule widths (200 to 640 dips, quarter-dip steps) x 2 heights (76 and 132): **0 straight-edge pixels shown inside the outline and 0 cut outside it** (220 cases, real GDI regions, pixel centre against the signed distance to the outline). `The_Straight_Edges_Stay_On_The_Outline_While_The_Shape_Moves_To_Fractional_Places` (540 places: scales 100 to 300, tops -30 to 14, fractional lefts and width 306.37): 0. `The_Straight_Edges_Of_A_Ball_And_A_Short_Capsule_At_Rest_Are_On_The_Outline` (six small shapes, four scales): 0. So the repair of design-3-1 holds for what it was about, on every scale and at fractional places: rounding each edge on its own agrees with a centre-of-pixel test.
- `Round4CutOutTests.Record_How_Deep_The_Glow_Shows_Inside_The_Outline_At_The_Corners_By_Scale` (passes, records): the corner arcs (capsule 306 wide, 76 high), pixels shown although their centre is inside the outline, and the deepest:

| scale | px shown inside (arcs) | deepest | px cut outside | outline long |
|---|---|---|---|---|
| 100% | 88 | 0.42 | 0 | 764 |
| 125% | 244 | **1.05** | 0 | 955 |
| 150% | 40 | 0.42 | 40 (0.19 deep) | 1146 |
| 175% | 8 | 0.10 | 108 (0.50) | 1337 |
| 200% | 228 | 0.52 | 0 | 1528 |
| 225% | 537 | **1.28** | 0 | 1719 |
| 250% | 456 | **1.03** | 0 | 1910 |
| 300% | 318 | 0.63 | 0 | 2292 |

  The 466-wide capsule gives the same depths. Trying the radius one or two less or one more (a throw-away sweep, not kept) moves the worst depth between 0.8 and 1.5 px: it is the shape GDI gives a rounded-rectangle region, not a mistake of the rounding.
- `Defect_The_Corner_Arcs_Of_The_Hole_Are_Within_A_Pixel_Of_The_Outline_At_Every_Windows_Scale` (red: `a corner pixel is 1.28 px on the wrong side of the outline (scale 2.25: 537 px shown inside, 0 px cut outside)`).
What Snowey would see: with "Graphics card", at 125%, 225% or 250%, a haze of the page colour one to one and a quarter pixels deep inside the four corner arcs of the capsule, under its 1 dip border (the strongest band of the glow: 0.185 of the page colour through the Approved glass, round 3). I could not look at it (UNVERIFIED how visible; the straight sides, which are the long part of the outline, have none).
Smallest repair: none that removes it (a different integer radius only moves it from the corners to the arcs' other side); accept it as the limit of a GDI region, or build the hole from a polygon of the analytic arc (`CreatePolygonRgn`), which is more than a hairline is worth. I would accept it. Nothing a self-test picture draws changes.

### design-4-2 — the tail of a long search text takes about 230 layouts (LOW, `Defect_`)

Where: `src/Island.App/Visuals/SearchView.cs`, `TailThatFits` (the loop over `StringInfo.ParseCombiningCharacters`, each candidate laid out in a TextBlock).
Evidence: `Round4SearchTests.Record_The_Cost_Of_The_Loop_For_A_Typed_Text_Of_This_Length` (passes; the loop replayed with the same probe, checked line by line against the source by `The_Replay_Is_The_Sources_Loop`): 20 units 1 layout and 0.3 ms; 60 units 33 layouts and 6.5 ms; 120 units 94 layouts and 27 ms; 256 units (`SearchKeys.MaxFieldChars`) 230 layouts and 85 ms, one draw of the field, on the UI thread, for every key typed (`SearchView.Show` is cached by the data, so once per change). `Defect_The_Tail_Of_A_Long_Typed_Text_Is_Found_In_A_Handful_Of_Layouts` (red: `230 layouts of the text for one draw of a 256-unit query (a binary search over the cuts needs 9)`).
What Snowey would see: nothing for an ordinary query (under a millisecond for a word or two); a short hitch per key at the far end of the field's range. The old loop (one unit at a time) had the same cost; the repair of design-3-4 kept it.
Smallest repair: the width of "the text from here on" only falls as the cut moves on: a binary search over `starts` finds the first cut that fits in nine layouts.
What held in `TailThatFits` (what the brief asked): an empty text returns an empty string (`An_Empty_Text_Is_An_Empty_Field_And_A_Text_Is_Never_An_Empty_Field`: six non-empty texts, among them 256 wide letters, 256 CJK characters, one letter with 250 combining marks, three joined family emoji, return something non-empty and never wider than the room unless it is the ellipsis); a single element wider than the field returns the ellipsis alone (`One_Element_Wider_Than_The_Field_Gives_The_Ellipsis_Alone_Never_An_Empty_Field`: a chain of twenty joined people is one cluster of 59 units and 175 wide in a room of 206, so it fits and is returned whole; the test asserts the ellipsis for the case where a font draws it wider); the tail always ends with the last character typed (`A_Tail_Always_Ends_With_The_Last_Character_Typed`).

### design-4-3 — closing the Add panel leaves the refusal's words and scrolls to them (LOW, `Defect_`)

Where: `src/Island.SettingsUi/AddByHand.cs`, `Close` (sets `OpenPanel = null` and `Refresh`; `Toggle` clears `host.Notice = null` when the panel opens or closes by the head button, `Close` does not) with `SettingsView.Rebuild` (since 956ac18: a held notice is brought into view on every redraw).
Evidence: `Round4NoticeTests.Record_What_Closing_The_Add_Panel_After_A_Refused_Add_Does_To_The_Notice_And_The_Scroll` (passes, records): a site typed that is already on the island is refused ("Example is already on the island, on the page Alpha games."), the page is scrolled to the top, Esc closes the panel: the notice line still holds the sentence and the scroll offset goes from 0 to 500 (viewport 554, extent 1076, 1536 by 700), for the first page and for the last. `Defect_Closing_The_Add_Panel_Puts_The_Words_Of_A_Refused_Add_Away` (red: `the panel is closed and the notice line still says: Example is already on the island, on the page Alpha games.`).
What Snowey would see: he tries to add something that is already there, reads the refusal, scrolls up, and closes the panel with Close or Esc: the page runs down 500 px to the same words about a panel that is gone. (The stale words were there before round 3; the scroll to them is new.)
Smallest repair: `host.Notice = null;` in `AddByHand.Close`, as `Toggle` already does. No picture changes.

### design-4-4 — a refused add carries the page away from the panel (LOW, proposal class, `Defect_`)

Where: the same rule, seen from the other side: `SettingsView.Rebuild` scrolls to the notice line, which is the last child of the section, and the Add panel is in the middle of it.
Evidence: `Round4NoticeTests.Defect_A_Refused_Add_Leaves_The_Panel_Being_Worked_In_In_The_Room_With_The_Words_That_Say_Why` (red: `the site field is at -545, the Add button at -487 and the words end at 554 in a room of 554`; 1536 by 700, panel of the first page). On the last page the panel stays in the room (the notice is beside it). The Record test of design-4-3 prints both.
What Snowey would see: he types a site into the panel of the first page, presses Add, and the screen flies down by more than a screenful to a line of words at the foot of the section; the field he was typing in is out of the room. Before 956ac18 the words were out of the room and the panel in it (design-3-3); now it is the other way round. Where the keyboard goes after the redraw (the pressed Add button, 487 px above the room) and whether WPF scrolls to a control given the keyboard (which would undo the scroll to the notice) needs a window: UNVERIFIED, for the self-test or a person.
Smallest repair: draw the Add panel's refusal inside the panel (a notice line under its Close link) and do not scroll for it; or scroll only when the pressed control and the notice fit the room together. Either changes where a text is drawn: a proposal.

### design-4-5 — the Graphics card pill repeats the card's own sentence and scrolls to it (LOW, proposal class)

Where: `GeneralSection.LightCard` (shows `LightRefusals.LightUnavailable.Message` in the card when the choice is Graphics card and it cannot be had), `SettingsSession.SetLight` (the refusal comes before the "already the choice" check; WO12 section 2 and `SettingsStage.LightChoices` promise the refusal's words), `SettingsView.Rebuild`.
Evidence: `Round4NoticeTests.Record_What_Pressing_The_Graphics_Card_Pill_Does_When_It_Is_Already_The_Choice_And_Not_Available` (passes; the assertion is that the two texts are equal): viewport 718, extent 958, offset after pressing 218; the card and the notice line say, word for word, "The graphics-card light cannot be shown on this computer, so Island draws the light the lighter way instead. Windows did not give Island what it needs (the same as for the Blur glass); nothing else changes. Pick another light in Settings, General."
What Snowey would see: the same two sentences twice on one screen, the second one reached by a scroll of 218 px; and the instruction "Pick another light in Settings, General" given in Settings, General (the picture `settings/general-light-unavailable.png` already shows it once; not new).
Smallest repair: none without changing a text or a look: skip the scroll (and the copy) when the notice equals a text already in the room.
The brief's question about the refusal for a person on As before: that text (`LightRefusals.MessageFor(AsBefore)`, 232 characters, "...so Island keeps the light you have...") is not in the Moving light card: the card shows nothing for As before; the words go to the notice line at the foot of General. At the page width of 712 it is 2 lines, last line 73%; at 592 3 lines, last 7% (a one-word last line: design-1-08's class, recorded, not new); at 440 3 lines, 81%. (`Round4NoticeTests.Record_How_The_Notices_That_Grew_Lay_Out_At_Three_Widths_Of_The_Notice_Line`.)

### design-4-6 — the heading's live-region event is read through a peer that does not exist (LOW, `Defect_`, UNVERIFIED)

Where: `SettingsView.Rebuild`: `if (UIElementAutomationPeer.FromElement(heading) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);` (the repair of ease-3-13; the brief names the heading, "no pixel": the pictures confirm none moved; this is the other half of the question).
Evidence: Learn says `FromElement` returns null "if the UIElementAutomationPeer was not created by the CreatePeerForElement method". `Round4LiveRegionTests.Record_That_A_Heading_Built_By_A_Rebuild_Has_No_Peer_Until_Someone_Creates_One` (passes): a heading of the settings view after layout: `FromElement` null; a fresh TextBlock: null, then `CreatePeerForElement` a peer, then `FromElement` the same peer. A new heading is made by every `Rebuild`, so a client that has never walked to it leaves no peer. `Defect_The_New_Heading_Gets_A_Peer_Before_The_Live_Region_Event_Is_Raised` (red: `Not found: "CreatePeerForElement(heading)"`).
What a person would see: nothing; what a screen-reader user may not hear: the new section's name when Continue builds it (the ease-3-13 repair would then not work). UNVERIFIED: whether WPF makes peers for new children when a client is listening for the event; only Narrator on a real window settles it.
Smallest repair: `UIElementAutomationPeer.FromElement(heading) ?? UIElementAutomationPeer.CreatePeerForElement(heading)`, guarded by `AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)` so that it costs nothing without a client. The same pattern is in `IslandRoot.Describe` (`FromElement(this)`), where the root has a peer once a client connects (it has `OnCreateAutomationPeer`): not the same case.

## 3. What held (the coverage statement)

- **The glow's hole** (replayed with GDI regions, `Round4CutOutTests`): straight edges exact at 11 scales, fractional widths (233.25, 306.5, 399.75, 466.25, 520.5) and lefts, a moving top, both capsule heights, balls and short capsules at rest: 0 pixels on the wrong side. The one limit is the corner arc (design-4-1). The stretched ball of the fly-in is the recorded proposal (PROPOSALS 30) and is not reported again.
- **The pictures of the newest full run** (`dist/test-full`, drawn 21:17 to 21:23 on 2026-10-07, after the repairs of round 3, 50 pictures) and of the two later single-stage runs (`dist/test-cap`, 15 pictures; `dist/test-settings`, 15 pictures) against `review/` (`Round4PictureTests.Every_Picture_Of_The_Newest_Runs_Is_Pixel_Equal_To_The_One_In_Review_Except_The_Blur_Note`): **80 pictures compared, 79 with 0 pixels over 2 apart (largest step 0), none without an earlier picture.** The one that differs is `settings/glass.png` (54981 px, box 592,418 to 1319,655, 8.4% of the picture): the Blur note grew from one line to two, the page is centred in its room, so the heading, the sub heading and the two cards moved up by 9 rows (8.5 dip, half a line) and the note changed; `Record_The_Shift_Of_The_Centred_Block_In_The_Glass_Picture` finds that shift. Nothing else changed: the glow's repair, the live region, the notice scroll, the search tail, the key texts and the question's help text changed no pixel the self-test draws. (That the Darker note's picture was not drawn is a gap: `glass-darker.png` is the strip, not the settings screen; the Darker note's lay-out is `Round3SettingsTests`, 3 sentences, 2 lines at 1920 by 1080.)
- **The notice scrolled into view** (`SettingsView.NoticeLine`, `Rebuild`): in a section that fits the room the page does not move (`A_Notice_Moves_The_Page_Only_In_The_Sections_That_Are_Longer_Than_The_Room`: all eight sections at 1920 by 1080, 1536 by 864 and 1280 by 720: offsets 0 wherever extent is at most the room, 6 px for Pages at 1280 by 720 where it is 28 px over, and the notice ends inside the room in every case; Key holds its notice at the top, offset 0). In a section that scrolls it moves the least that shows the whole line (`A_Refusal_Below_The_Fold_Shows_The_Whole_Notice_Line_With_The_Least_Scroll`: 1536 by 600 and 1280 by 420; at 1280 by 420 the pressed pill ends 29 px above the room, recorded). Typing in a field does not redraw the screen (the only `TextChanged` handlers update a hint line, a preview or a list), so the page cannot be moved from under the person's typing; a redraw comes only from a commit (Enter or losing the keyboard), which reports and redraws as before.
- **The texts that grew**, as they lay out (`Round4NoticeTests`): the key-not-given-back warning with a refusal's own message, longest it can be (a 24-letter page, a 100-letter pick, a four-key combination; 394 characters): 5 lines (80 high) at the notice line's full width of 712, 6 lines at 592, 8 lines (128) at 440; the usual ones (the key is taken inside, Windows refused, the file could not be saved) 2 or 3 lines; "already on the island under another name" with a 100-letter pick 3 lines. `The_Longest_Notice_Fits_In_The_Room_Of_The_Smallest_Window_Once_It_Is_Scrolled_Into_View`: at 640 by 360 (1280 by 720 at 200%) the longest warning is 96 high and ends exactly at the foot of a room of 214: fully on show after the scroll. The lines wrap; none is clipped. Darker Blur note: round 3's tests (three sentences; one to three lines at four window sizes) still pass.
- **`SearchView.TailThatFits`**: see design-4-2 for what held.
- **The diffs** of every commit since 341cc71 that touched `src/Island.SettingsUi`, `src/Island.Glass`, `src/Island.App/Visuals` (956ac18, 3e7ec3d, bf24c4d and the others are test-only for these folders), read in full: `ConfirmOverlay` (a help text on two buttons: no pixel), `KeySection` (the name of a key button while a key is awaited: not drawn), `Parts.Heading` (a live-region property), `SettingsView` (design-4-3, 4-4, 4-6), `MovingLight` (the hole; two fields remembered for the self-test: `_offsetsSetAtMs`, `_firstArcOffset`, set where the offsets are, and the first arc found by position: no look), `IslandRoot`/`IslandView.ForgetWords` (a string), `SearchView`. One tidiness item, no behaviour: in `SettingsScreen.cs` and `OutsideClaudeSettings.cs` a new member was put between a doc comment and the member it described, so `Phase` and `CopyNotify` carry the wrong `<summary>` (and each new member has two). Not graded.

## 4. Exact commands and totals

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test ".worktrees\rt4-design\tests\Island.Redteam.Design.Tests"
```
**Failed 5 (the five `Defect_` tests of this round, red on purpose), Passed 155, Skipped 4 (the recorded proposals of earlier rounds), Total 164.** The five: `Round4CutOutTests.Defect_The_Corner_Arcs_Of_The_Hole_Are_Within_A_Pixel_Of_The_Outline_At_Every_Windows_Scale`, `Round4SearchTests.Defect_The_Tail_Of_A_Long_Typed_Text_Is_Found_In_A_Handful_Of_Layouts`, `Round4NoticeTests.Defect_Closing_The_Add_Panel_Puts_The_Words_Of_A_Refused_Add_Away`, `Round4NoticeTests.Defect_A_Refused_Add_Leaves_The_Panel_Being_Worked_In_In_The_Room_With_The_Words_That_Say_Why`, `Round4LiveRegionTests.Defect_The_New_Heading_Gets_A_Peer_Before_The_Live_Region_Event_Is_Raised`. All earlier tests of the project are green or skipped. Run time about 30 seconds.

## 5. What I could not check

- How visible design-4-1's hairline is, and whether the compositor clips the glow at the region's edge as the order assumes (round 3's UNVERIFIED stands): only a person at the screen, at 125% or 225%.
- Where the keyboard and the scroll end after a refused add (design-4-4), and whether WPF scrolls to a control that is given the keyboard (which would hide the notice again): needs a real window; the self-test could press `add:site-add` on the first page at 1536 by 700 and read the scroll offset.
- Whether design-4-6 matters: needs Narrator.
- The pixel offset of the glow window against the island window at fractional scales (the region is in the glow window's own pixels): needs windows; the self-test's own check reads the region at 6 px outside and inside at the top centre only.
- The Darker Blur note as a picture (the self-test draws `settings/glass.png` for Approved only).

## 6. Requests to the main session

- Repair design-4-3 (one line) and decide design-4-4 and 4-5 (where a refusal of the Add panel and a repeated sentence are drawn: yours or Snowey's).
- design-4-6: add `CreatePeerForElement` (and a listener check) or have the self-test raise the section change on a real window with a UIA client.
- design-4-2: optional; a binary search, no look.
- The rounds can end here: nothing above LOW.

> **WORK-ORDER-13 (8 Oct 2026):** the picture tests named here that compared a run with the WO12 baseline are brought to the new baseline: `review/` now holds the pictures Dan's decisions changed, every newest run must equal it pixel for pixel, and the three tests that asserted the Blur-note difference (`The_Glass_Picture_Differs_From_The_Earlier_One...`, `The_Blur_Note_Is_The_Only_Thing_That_Changed...`, `Record_The_Shift_Of_The_Centred_Block...`) are removed because their baseline no longer exists.
