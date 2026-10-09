# DESIGN, round 2 — "does it look like one thing, finished?"

WORK-ORDER-12 section 4, attacker DESIGN, round 2. Branch `rt2-design`, made from `3a560b3`. Project `tests/Island.Redteam.Design.Tests/` (new files `Round2*.cs`; no existing test touched). Nothing under `src/` or
`extension/` was changed. No window was shown, no screen captured, nothing started. The pictures of `dist/test-wo12r1/` (this round's self-test) and `review/` (the earlier run) were only decoded and compared; the settings
view was built on the one STA thread, laid out and read, never shown and never rendered. Every name is invented. One official page was read (Learn, `CompositionSpriteShape.StrokeStartCap`).

## 1. Summary

6 findings: **0 HIGH, 1 MEDIUM, 5 LOW.** 2 `Defect_` tests (red on purpose, for design-2-1 and design-2-3). Every repair of round 1 that touches a look **held**; the one picture that changed is the one a repair was meant to change.

| id | grade | suggested class | area | one line |
|---|---|---|---|---|
| design-2-1 | MEDIUM | defect against WORK-ORDER-12 section 2 ("the same light"); the repair is not small, else a recorded difference for Snowey | graphics-card light | the glow is not cut out of the capsule's inside as the old bloom is: about half of its weight lies inside the outline, under the glass |
| design-2-2 | LOW | proposal | Settings, General | the three pills of the new Moving light card stand at the left under the text; every other card of General has its control at the right edge |
| design-2-3 | LOW | defect (a half character; no picture changes) | search field | the end of a long typed text is cut between UTF-16 units: it can start with half of an emoji |
| design-2-4 | LOW | defect (a text that is not so in one state) | Settings, Glass | the repaired Blur note says "Island uses the Approved glass instead" also when the Darker glass is in use |
| design-2-5 | LOW | text (EASE cross-reference) | Settings, Key | the new question counts the main key among the keys that "will be taken away"; restoring puts the original main key back |
| design-2-6 | LOW | proposal (EASE cross-reference) | Settings, text fields | the new ring inside the three fields is 2.90:1 against the field's fill over a white window behind the Approved glass (7.90:1 on the dark wall) |

## 2. The findings

### design-2-1 — the graphics-card glow also lies inside the capsule (MEDIUM)

Where: `src/Island.Glass/MovingLight.cs` (the glow: `_glowShapes` drawn into a visual surface and blurred into `_glowSprite`, the root of the glow window), against `src/Island.App/Visuals/IslandView.cs`, `Apply`
(`_backRoot.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, ... hole)` with `hole = frame.Outline()`, the capsule's **outer** outline) and `IslandView`'s constructor (`_backGroup` = shadow + bloom, added to `backRoot`).
Evidence:
- `Round2LightTests.The_Old_Bloom_Is_Drawn_Only_Outside_The_Capsules_Outline_Because_Its_Windows_Root_Is_Clipped_By_A_Hole` (passes: the old bloom is clipped to everything outside the outline).
- `Round2LightTests.The_Graphics_Light_Has_No_Clip_And_No_Mask_Anywhere_In_Its_Source` (passes: no clip, no mask, no window region in `MovingLight.cs` or `GlassWindow.cs`); `Round2LightTests.Defect_The_Graphics_Light_Glow_Is_Cut_Out_Of_The_Capsules_Inside_As_The_Old_Bloom_Is` (red on purpose).
- `Round2LightTests.Half_Of_The_Glows_Weight_Lies_Inside_The_Outline_Where_The_Old_Bloom_Is_Cut_Out_And_Is_Seen_Through_The_Glass` (passes; prints the table). The arc is 11 wide, centred 0.5 inside the edge, blurred with sigma 10 at 85%: **52.1% of its weight lies inside the outline**,
  the old drawing shows 47.9%. Under the arc, 4 dp inside the edge, the glow is 0.337 of the page colour before the glass; the capsule's fill is 60% dark (30% on Darker), so about **13.5%** (10% on Darker) of the page colour is added to what the capsule shows there, 2 dp to 16 dp deep
  (0.141 at 2, 0.111 at 8, 0.048 at 16). The ring (7 wide at 45%) adds about 4% all round the edge. For comparison the capsule's own inner glow is 34% at the edge (`CategoryGlowAlpha`).
Why it was not seen: the self-test checks the numbers given to the compositor (equal to the old light's) and the order of the windows, and cannot photograph the compositor's light; the old pictures draw the old light, which has no inside.
Snowey would see: with "Graphics card" a soft haze of the page colour on the inside of the edge, travelling with the arc (and a thin even one all round), where "As before" has none; the outside is the same. It is also what he will meet as a jump when a lifted tile is dropped (the old light then has no inner haze
and a 0.6 blurred rim: code-1-15), and the answer to "does the new light look the same" is no for this reason.
Smallest repair: cut the glow to the outside of the outline. In the compositor that needs a geometry with a hole (a path from Direct2D's combine-exclude, which `GeometryProbe` already reaches) used as a geometric clip, or an effect brush; neither is a one-line change, and I could not try either (UNVERIFIED which is simplest).
If it is not repaired it belongs under OWNER DECISIONS in words: "the new light also glows a little inside the edge".

### design-2-2 — the Moving light card is the one card whose controls are not at the right (LOW, proposal)

Where: `src/Island.SettingsUi/GeneralSection.cs`, `LightCard` (the three pills in a `WrapPanel` under the hint). `Round2SettingsLookTests.The_Moving_Light_Card_Is_The_Only_Card_Of_General_Whose_Controls_Are_Not_At_The_Right_Edge`:
the light pills end at x = 1063.5, the card at 1311.5; the controls of the other four cards end at 1294.5 (Start with Windows, Idle time, Notice time, Show the pill; the add-on's status and the setup button too, by round 1's picture test). Seen in `dist/test-wo12r1/settings/general-light.png`.
Round 1 covered six cards and not this one (it was added by WORK-ORDER-12). The reason is plain (three pills do not fit beside two lines of text), but it is the only card of the step that reads differently.
Smallest repair: none needed for function; a look change (a proposal; Snowey did not ask for the card): the pills at the right, stacked or as a three-way switch, or the hint shortened. Snowey would see the card in line with its neighbours.

### design-2-3 — a half emoji at the head of a long search text (LOW, `Defect_`)

Where: `src/Island.App/Visuals/SearchView.cs`, `TailThatFits`: `var candidate = (take < text.Length ? "…" : string.Empty) + text[^take..];` taking one UTF-16 unit less each time until it fits. The rest of the island never cuts half a character
(`SearchKeys` deletes a whole pair; `HandPicks`: "never half a character"). `Round2NewTests.Defect_The_End_Of_A_Long_Search_Text_Can_Start_With_Half_Of_An_Emoji` (red: no surrogate check in the function) and
`Cutting_A_Text_From_Its_End_By_Units_Starts_With_A_Lone_Low_Surrogate_When_The_Cut_Falls_Inside_An_Emoji` (passes: on an invented query one cut in 25 starts with the emoji's second unit). Field width tops out at 240, so about 25 characters; the cut falls on a half only when the emoji sits exactly at the
head of what fits, so it shows as a box or a replacement mark for a keystroke or two. Rare; Snowey types program names.
Smallest repair: `if (take < text.Length && char.IsLowSurrogate(text[^take])) { take--; continue; }` before the measure. No picture changes.

### design-2-4 — the Blur note names the Approved glass whatever glass is in use (LOW, text)

Where: `GlassSection.Build` shows `SettingsText.BlurUnavailable` (now `Refusals.BlurUnavailable.Message`) whenever Blur is unavailable. `Round2SettingsLookTests.The_Blur_Note_Names_The_Approved_Glass_Also_When_The_Darker_Glass_Is_In_Use` (passes as a record): glass in use Darker; the note reads
"Blur glass is not available (transparency effects are off, or the layer could not be made). Island uses the Approved glass instead. Turn on Transparency effects ...". The middle sentence is true only for a person who had chosen Blur (the register's text is written for the balloon of that moment). WORK-ORDER-12's rule: a text is changed when it says something false.
Smallest repair: in the Settings screen leave that sentence out, or say "Island uses the glass you have chosen instead" (a text; the register's message is shared with the balloon, so a second form of the sentence).

### design-2-5 — "will be taken away" for the main key (LOW, text)

`SettingsText.RestoreAllKeysQuestion`: "The N keys you set (the main key, page keys, pick keys, the mode key) will be taken away." `Round2NewTests.The_Restore_Keys_Question_Counts_The_Main_Key_As_Taken_Away_But_Restoring_Puts_The_Original_One_Back`: with only the main key changed
the question counts 1 key and says it will be taken away; restoring sets it to the original (`Ctrl+Alt+Q` to `Ctrl+Q`). A main key is never empty. Smallest repair: "taken away or put back to the original". EASE's area; written here because the question was one of the repairs I was asked to measure.

### design-2-6 — the ring inside the fields on a bright desktop (LOW, proposal)

`Parts.RingBoxTemplate` (white at 90%, 2 thick, inside the field's edge, same corner radius 8). `Round2SettingsLookTests.The_Ring_Of_A_Field_Against_The_Fill_Of_The_Field_Is_Recorded`: ring against the field's own fill (white 14% on the card's 8% on the screen's tint), WCAG relative luminance:
dark wall 7.90:1; white window behind, Approved glass 2.90:1; Darker glass 3.62:1 (1.4.11 asks 3:1; the model is the same one EASE used for the hints, ease-1-23). The ring of a button stands 3 outside the button on the glass itself, so it is stronger there; the field's ring lies on a lighter fill. Smallest repair: a stronger ring colour or a solid fill for the field; a colour: proposal.

## 3. What held (the coverage statement; all pass)

Step 1, the repairs of round 1 that touch looks:
- **The pictures.** `Round2PictureDiffTests.Every_Picture_Of_The_Repaired_Build_Is_Pixel_Equal_To_The_Earlier_Run_Except_The_Glass_Picture_Whose_Blur_Note_Was_Repaired`: 50 pictures of `dist/test-wo12r1/` against `review/`, decoded; **49 are pixel equal** (every channel within 2; in fact identical), **1 differs: `settings/glass.png`** (54 981 pixels, x 592..1319, y 418..655),
  which is the Blur note (ease-1-7, a refusal that lacked its parts, repaired as a defect). No repair changed a picture it was not meant to change: the ArcClock wrap, the ring of the fields, the faint chip, the question, the screen-reader names, the remembered light spec and the atomic files drew nothing different in any picture the self-test draws.
  `The_Glass_Picture_Differs_From_The_Earlier_One_By_The_Note_And_By_Everything_The_Centred_Page_Moved_With_It`: the note grew from one line to two and the page, centred in the room above the buttons (DESIGN-1-06), moved up 8 px (heading ink at y 427, now 419); the glass cards moved with it. Expected, not a defect.
  `Pictures_Held_In_Review_That_The_Current_Self_Test_No_Longer_Draws_Are_Listed`: 9 pictures of `review/` are not redrawn by the self-test and show an older build: `settings/dialog.png` (the question card, 10-06; the card has since got a scroll for long questions), `settings/pages-colour.png` (the colour field), `settings/key-capturing.png`,
  and `choices/` `app-icon`, `closed-pick`, `count-badge`, `icon-in-tile`, `pick-menu`, `plus-list`. No current picture shows a question card, a focused field or the faint chip.
- **The ring of the three fields** (5ba6320): `Round2SettingsLookTests.The_Ring_Of_The_Text_Fields_Is_As_Large_As_The_Field_Draws_Its_Own_Corner_And_Is_Clear_At_Rest`: the colour field (104 by 30) and the two Add-panel fields (574 by 30): the ring is exactly the field's size, 2 thick, corner radius 8 = the field's own (concentric), alpha 0 at rest (so unfocused fields draw nothing new, which the unchanged add-panel pictures confirm), #E6FFFFFF focused, the text starts 11 in (9 clear of the ring).
  `The_Ring_Of_A_Field_Covers_Its_Hairline_Where_The_Ring_Of_A_Button_Stands_Clear_Of_The_Button`: a button's ring is 3 outside with 1 clear; a field's lies on its edge and covers the 1 hairline while focused. Not a collision; a different construction, consistent in colour, width and corner. Dark wall: 7.90:1.
- **The faint chip** (19a61f0): `Round2SettingsLookTests.The_Faint_Chip_Of_A_Hand_Added_Pick_Is_Drawn_Exactly_As_The_Faint_Chip_Of_A_Starter_Pick_And_Sits_On_The_Same_Line_As_Its_Neighbours`: opacity 0.55, height 32, margin 0,0,8,8, border and fill equal to a starter pick's faint chip; same line and height as the chip still on; it comes after it (on rows first, then missing starters, then the kept chip).
  `A_Hand_Added_Pick_Switched_Off_And_On_Again_Comes_Back_At_The_End_Of_Its_Page_And_Without_Its_Key` (recorded, not a finding): the pick comes back last on its page and without the key it had (order `alpha, example` to `example, alpha`; key `Ctrl+Alt+F1` to none). A starter pick does the same today (a pick that is gone gives its key back); the faint chip only makes it visible. Said so that Snowey knows.
- **The Blur note** (e458a67): `The_Blur_Note_Is_Two_Lines_Under_The_Glass_Cards_On_Every_Screen_Size_And_Breaks_Between_Its_Sentences_Or_Near_The_End` (1920 by 1080, 1366 by 768, 1280 by 720): 2 lines in 712 wide, the last 62% of the box, 27 px under the cards like the other notes; the break falls between the second and third sentence.
  `The_First_Line_Of_The_Blur_Note_Has_Only_A_Few_Pixels_To_Spare_...`: 15.0 px to spare on the first line: a font with a few more pixels would put "instead." alone on the second line (not ugly, only less tidy). The wrap is not ugly today.
- **The question before "Restore the original keys"** (85452c5): `The_Question_Before_Restoring_The_Original_Keys_Fits_The_Card_In_Three_Lines_Without_Scrolling`: 3 lines, the last 23% of the box, card 513 by 176, buttons 128 and 149 wide, nothing scrolls. `The_Other_Questions_Of_The_Screen_Have_Cards_Of_The_Same_Kind_As_The_New_One`: the starter-list question is 515 by 176 (3 lines, last 10%): one family.
- **The step names** (6b2428c) and **the screen-reader root** (939ef60), **Alt+F4**, **IslandHost titles**: read in the diffs: an automation name or a window title only; no pixel (the picture comparison agrees).
- **The Move-to-another-page list** (7472ce5): by reading (a popup cannot be built without making a window): rings are 3 outside the 30 high items inside a 6 margin; the ring's corner (radius 17 at 3 in) lies inside the popup's radius 16 corner. Nothing clipped.
- **Icons asked again** (ea4566e): a miss is asked up to 3 times, 20 s apart; no look changes unless a late read succeeds (letters then become the icon, as at the first load today).

Step 2, the light, by numbers: `Round2LightTests.The_Constants_Of_The_Old_Rim_And_Bloom_Are_All_In_The_Lights_Spec_Except_The_Ones_Listed` (every constant of the old `RimLayer` and `BloomLayer` is in `LightSpec` but the rim's own blur `FrontRimBlurCss`, code-1-15, and the numbers of the pill's ring, the notice's edge and the dashed rim, which keep the old drawing),
`The_Spec_Of_The_Approved_Look_Carries_The_Old_Numbers`, `The_Breath_Of_44_Linear_Keyframes_Follows_The_Cosine_Within_A_Fraction_Of_A_Percent` (largest gap 0.00076 of the glow's strength; the clocks are the same `now / 1000`).
Also held by reading: the stroke order (base line, then the two arcs; ring, then arc) is the old order; the glow window lies directly above the shadow window and beneath the glass, as the bloom was; the Vibe cross-fade into and out of the breath is continuous (the breath starts from `0.85 * Breath(now)`).

Step 3, new, from the code and constants (`Round2NewTests`): every state word of a pick fits the 130 block ("not found" 50.4, "9999 windows" 72.3); the dots (2 to 5 windows) clear the selected ring by 3 dp and the helper state ring by 1.75 dp, five dots are 32 wide in a 40 tile, and their glow stays inside the capsule (ends 74 of 76).

## 4. What would differ between "As before" and "Graphics card" (nothing can be photographed; the list for Snowey's own eyes)

1. **The glow's inside half** (design-2-1): new, not recorded before. Order of size: the largest.
2. The rim's own 0.6 blur (code-1-15, a proposal): the old rim is softer by under one pixel. Together with 1, this is the jump at a lift and at the drop.
3. **During the fly-in the old light stretches its strokes and corners with the shape** (the groups carry `_backScale` / `_frontScale`, up to `StretchMax` 0.34); the graphics-card light is given the stretched width and height only: at the largest stretch the old arc is 3.5 thick on the top and bottom and 2.2 at the sides, the new one 2.6 (`During_The_Fly_In_The_Old_Light_Stretches_...`). A few frames. LOW, a movement.
4. Whether the compositor puts a round cap on the trimmed ends of the arcs is **UNVERIFIED**: Learn describes `StrokeStartCap`/`StrokeEndCap` only as "how the start of a line is drawn", default Flat, and says nothing of trimmed geometry (read 2026-10-07). If not, the arcs are 2.6 px shorter in all (1.3 at each end) than the old ones. Nothing in the self-test can tell.
5. The rim window lies above the whole capsule, the old rim below the contents: no overlap in practice (the contents are clipped inside the padding box and the rim lies at the edge).
The numbers (path, lengths, speed, colours, widths, blur sigma) are equal; the self-test's own check says so ("numbers equal True").

## 5. Commands and totals

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test ".worktrees\rt2-design\tests\Island.Redteam.Design.Tests"
```
**84 tests: 79 pass, 2 fail on purpose, 3 skipped.** The 2 red: `Round2LightTests.Defect_The_Graphics_Light_Glow_Is_Cut_Out_Of_The_Capsules_Inside_As_The_Old_Bloom_Is` (design-2-1), `Round2NewTests.Defect_The_End_Of_A_Long_Search_Text_Can_Start_With_Half_Of_An_Emoji` (design-2-3). The 3 skipped are round 1's proposals (Skip with their reason). All 53 round-1 tests are green or skipped (50 green, 3 skipped): no repair broke one.
New files: `Round2World.cs` (helper), `Round2PictureDiffTests.cs` (3), `Round2SettingsLookTests.cs` (15), `Round2LightTests.cs` (8), `Round2NewTests.cs` (5). The picture tests read the main folder's `dist/test-wo12r1/` and `review/` and return at once when that folder is not above the worktree. Run time about 11 s.

## 6. What I could not check

- How anything looks: no picture of the compositor's light, of a focused field, of the faint chip, of a question card or of dots, missing or an empty page can be drawn by an attacker; those were read from code and constants. The model of the Settings glass over a white window (design-2-6) is arithmetic on the constants (the tint 0.6 or 0.7 of `#141620`), not a measurement.
- Whether the compositor caps trimmed ends, and where its rounded rectangle starts (UNVERIFIED, already in the code's own text).
- The Move-to-another-page list in a popup (a popup is a window): by reading only.
- 100% and 125% scaling, hover and pressed states, high contrast: as in round 1.

## 7. Requests to the main session

1. design-2-1: decide between the repair (a clip with a hole) and a line under OWNER DECISIONS; if repaired, the test `Defect_The_Graphics_Light_Glow_Is_Cut_Out_...` turns green when `MovingLight.cs` holds a clip or a mask.
2. Redraw, if a stage can: a question card (the restore-keys question and the starter-list one), the colour panel with the field focused (`pages-colour.png` is of 10-06), a hand-added pick's faint chip, and the Blur note with Darker chosen. All are states with no current picture.
3. `review/settings/dialog.png`, `pages-colour.png`, `key-capturing.png` and six `choices/` records are not redrawn: say so in their README or redraw them.

> **WORK-ORDER-13 (8 Oct 2026):** the picture tests named here that compared a run with the WO12 baseline are brought to the new baseline: `review/` now holds the pictures Dan's decisions changed, every newest run must equal it pixel for pixel, and the three tests that asserted the Blur-note difference (`The_Glass_Picture_Differs_From_The_Earlier_One...`, `The_Blur_Note_Is_The_Only_Thing_That_Changed...`, `Record_The_Shift_Of_The_Centred_Block...`) are removed because their baseline no longer exists.
