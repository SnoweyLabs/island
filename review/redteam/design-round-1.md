# DESIGN, round 1 — "does it look like one thing, finished?"

WORK-ORDER-12 section 4, attacker DESIGN. Branch `rt-design`, made from `a66c137`. Project `tests/Island.Redteam.Design.Tests/`. Nothing under `src/` or `extension/` was changed and no existing assertion
was touched. No window was shown, no screen captured, nothing started; the pictures the self-test drew were only decoded (read-only, from the main folder's `review/`) and the settings view was built on an
STA thread, laid out (Measure, Arrange) and read, never shown and never rendered to a bitmap. Every name in the tests is invented. Temporary files of the settings world sit under the test's own `bin/` and are
deleted. (Some pictures were cropped and enlarged into the session's scratch folder to look at them; those files are not in the repository.)

Under the one rule almost everything below is a **proposal** (a repair would change pixels of a picture the self-test already draws, and most of it is a look Snowey approved). Three things carry a `Defect_`
test because a picture or the island's own text shows broken text. For each finding I say what would change in numbers and what Snowey would see; the class is the main session's call.

## 1. Summary

15 findings: **0 HIGH, 4 MEDIUM, 11 LOW.** 3 `Defect_` tests (red on purpose), covering 2 findings.

| id | grade | suggested class | area | one line |
|---|---|---|---|---|
| DESIGN-1-01 | LOW | defect (the repair changes two pictures: case 1 makes it a proposal) | settings, first start: Key step | `[Delete],` is drawn with its brackets: the key-cap markup leaks into the text |
| DESIGN-1-02 | MEDIUM | defect (text cut in a picture) | Media page, text block | the paused line `Alpha-player · app · pau…` loses the word "paused"; 2 of the app's 5 starter media names do the same |
| DESIGN-1-03 | MEDIUM | proposal | + tile | the `+` is 3.5 dp below the middle of its dashed circle (bold 22; the reference is 400 at 20) |
| DESIGN-1-04 | MEDIUM | proposal | every page, first pick selected | the glow of the selected tile ends in a straight vertical line where the strip is clipped |
| DESIGN-1-05 | LOW | proposal | 130 wide text block | the empty-page hint is 1 px too wide; a helper line with a window title loses its state word; the tightest fitting line has 4 px to spare |
| DESIGN-1-06 | MEDIUM | proposal | settings, first start | the heading moves between steps by up to 345 px; a scroll bar shifts every centred line by 8.5 dp |
| DESIGN-1-07 | LOW | proposal | settings, Add panel | the "Close" link is 8 dp inside the other links of the card |
| DESIGN-1-08 | LOW | proposal | settings | 6 of 20 wrapped sentences end on a line under 15% of the box ("wait.", "README.", "back here.") |
| DESIGN-1-09 | LOW | proposal | drag off | the lifted tile is at 74%: the dashed line of the drop zone shows through it |
| DESIGN-1-10 | LOW | proposal | movement | six different times for fades; four hand-written durations; the lifted tile returns by an ease, not the island's spring; the capture pulse is linear where the reference eases; the step dots do not fade |
| DESIGN-1-11 | LOW | proposal | all | 83 numbers compared with the reference: 78 agree exactly; 5 depart (notice gap, search inset, + glyph size and weight, heading weight); the row sits 1 dp below the reference's centre |
| DESIGN-1-12 | LOW | proposal | colours | Terminals lime #B8F03A is 0.080 from the swatch #7CE04A; two swatches are 0.073 apart; the working ring is exactly the Antigravity disc (latent) |
| DESIGN-1-13 | LOW | proposal (the lime is Claude's own choice) | page chips | the white glyph on the chip is under 3:1 on 4 of 6 pages; weakest is Terminals (2.11:1 dark, 1.73:1 light) |
| DESIGN-1-14 | LOW | proposal | glyphs | the Terminals glyph is the thinnest stroke (1.2 on a 16 grid; 1.35 px at 100%); every picture is drawn at 2 pixels to a dp, so softness at 100% was never seen |
| DESIGN-1-15 | LOW | not a repair: pictures to draw | the pictures | states with no current picture (dots, missing, empty page, light backdrop for all `choices/` pictures) |

Not findings, because Snowey has them recorded: the white title on the light glass (3.64:1, STATE.md OWNER DECISIONS), the missing white ring on a selected tile that has a state ring (WO11 §3; measured below only
to say how strong the remaining sign is), the dimmed X that never works on the Terminals page (WO11 §1), the stale pictures `plus-list.png`, `pick-menu.png`, `closed-pick.png`, `count-badge.png`,
`icon-in-tile.png` (`review/choices/README.md` calls them records).

## 2. The findings

Test names are `Class.Method` in `tests/Island.Redteam.Design.Tests/`. Pixel numbers from the capsule pictures are at 2 pixels to a dp (the pictures are 192 dpi); settings pictures are 1 to 1.

### DESIGN-1-01 — `[Delete],` is drawn with its brackets (LOW, `Defect_`)

Where: `src/Island.SettingsUi/Parts.cs`, `Sentence` (splits on spaces; a piece becomes a key cap only if it starts with `[` and ends with `]`); the text is `SettingsText.IslandKeys`, line 6:
`"[Delete], then [Delete] again, removes the selected pick (nothing that is open is closed)"`. The first word is `[Delete],` and ends with a comma, so it stays text.
Evidence: `SettingsDefectTests.Defect_A_Key_Name_Followed_By_A_Comma_Is_Drawn_With_Its_Brackets` (red: `text drawn with its key-cap markers: [Delete],`); pictures `review/settings/key.png` and
`review/setup/key.png`, line at y = 405: "[Delete], then (Delete) again".
Smallest repair: in `Sentence`, take a piece of the form `[x]` followed by punctuation (`^\[(.+)\]([,.;:]*)$`) as a cap plus the punctuation as text.
Snowey would see: the first "Delete" as a key cap like every other key in that list. Two pictures change (so by case 1 the class may be "proposal").

### DESIGN-1-02 — the paused line loses "paused" (MEDIUM, `Defect_`)

Where: `src/Island.Core/Media/PlayingTile.cs`, `SecondLine` (`"{Where} · {kind}"` plus `" · paused"`) drawn in the 130 wide block of `ContentsLayer` (`LookConstants.WidthTextBlock`, trimmed at the end).
Evidence: `PausedLineTests.Defect_The_Paused_Line_Of_The_Picture_Is_Wider_Than_The_Text_Block_And_Loses_The_Word_Paused` ("Alpha-player · app · paused" is 138.6 wide in 130) and
`PausedLineTests.Defect_The_Second_Line_Of_The_Paused_Picture_Runs_To_The_Edge_Of_Its_Block`: in `review/media-paused.png` the ink of the second line ends at x = 904, 4 px from the block's edge at 908 (the
playing line, `media-playing.png`, ends at 832); the picture reads "Alpha-player · app · pau…". `PausedLineTests.The_Starter_Media_Names_Paused_Are_Counted_Against_The_Text_Block` measures the app's own
starter names: Spotify 109.2, YouTube 114.8, Twitch 103.1 fit; **YouTube Music 147.7 and SoundCloud 133.4 are cut** (2 of 5). The playing lines all fit (at most 102.2). The middle button also shows play, so the
state is not lost, but the line that exists to say it ends in an ellipsis for the two longest names.
Smallest repair: trim the name part, never the end: build the line as `[Where, cut to what is left] + " · tab" + " · paused"` (no word changes; only where the ellipsis goes). Or put "paused" first.
Snowey would see: "YouTube Mu… · tab · paused" instead of "YouTube Music · tab · pa…". (Picture `media-paused.png` changes.)

### DESIGN-1-03 — the + glyph sits 3.5 dp low (MEDIUM, proposal)

Where: `src/Island.App/Visuals/TileView.cs`, `BuildFace` (`face.Children.Add(Letters("+", 22))`; `Letters` is bold, line height 1.45 of the size, centred in the 40 disc).
Evidence: `PictureMeasureTests.The_Plus_Glyph_Is_Lower_Than_The_Centre_Of_Its_Tile`: in `choices/plus-row.png`, `strip.png` and `drag-off.png` the glyph is 20 by 21 px with its centre at y = 113 while the tile's centre is
y = 106: **7 px = 3.5 dp low**; the letters in the other tiles are 1.5 px (0.75 dp) low (`The_Ring_Of_The_Selected_Tile_Is_Centred_On_The_Page_Chip_In_Every_Picture` shows the chip glyphs within 1 px).
The + tile is on every page that holds picks, so this is seen at every summon. Reference: `.t.plus{font-size:20px;font-weight:400}` in a grid with `place-items:center` (`ReferenceAgreementTests`: size 20 against 22, weight 400
against 700, both listed as departures).
Smallest repair: draw the + as a path, as `AddPlusBadge` already draws the small + (`M3.2 0V6.4M0 3.2H6.4`, centred by its own box), or move the text up by 3.5; with the reference's 20 at weight 400 if the departures are taken too.
Snowey would see: the + exactly in the middle of the dashed circle, lighter in weight if the weight is taken.

### DESIGN-1-04 — the glow of the first selected tile ends in a straight line (MEDIUM, proposal)

Where: `src/Island.App/Visuals/StripView.cs` (`Slack = 6`; the row is clipped to `Rect(0, -60, Width, height + 200)` with `Width = strip + 2 * Slack`), against `LookConstants.SelectedGlowRadius = 14` (the glow
of the selected tile, in the page colour, is drawn inside the tile and so inside the clip).
Evidence: `PictureMeasureTests.The_Glow_Of_The_First_Selected_Tile_Ends_In_A_Straight_Line_At_The_Strip_Clip`, on all twelve page pictures: along the column 12 px (6 dp) left of the first tile, the luminance jumps
between two neighbouring pixel columns by 4 to 5 per mille on Media and Apps (red and blue, faint) and by **13 to 21 per mille on the four lighter page colours** (per picture, dark / light: Media 4.1 / 4.6, Apps 4.4 / 3.7, Folders 13.4 / 17.8,
Vibe 13.6 / 15.2, Browser 13.2 / 15.4, Terminals 17.3 / 21.4) more than the gradient does a few columns further in (the test prints every row). It is visible by eye as a straight vertical edge between the page chip and the first tile
in `review/terminals-dark.png` (x ≈ 397) and, fainter, in the other page pictures; it is strongest where the page colour is light. The reference's box-shadow has no such edge. The first pick is the usual selection, so this is the usual look.
The same clip cuts the glow of the last tile at the right end and of a hovered tile at the end of the second row (by the code; no picture shows it).
Smallest repair: draw the glow outside the clipped canvas (the clip is only for sliding tiles), or widen the clip to the glow's reach (about 20 dp) in the same places `Slack` is used (the fade ramp is computed
from it: `edge = Slack / total`).
Snowey would see: the glow around the selected tile fading out softly into the glass, as in the reference, with no line left of it.

### DESIGN-1-05 — texts that do not fit the 130 wide block (LOW, proposal)

Where: `ContentsLayer.ApplySelection` and `TerminalTiles.TileOf` (second lines), slot `LookConstants.WidthTextBlock = 130`, 11.5 normal, cut at the end with an ellipsis.
Evidence (`TextFitTests`, measured with a TextBlock set up as `ContentsLayer.Label`; plain Segoe UI measures the same here):
- `The_Empty_Page_Hint_Is_About_One_Pixel_Wider_Than_Its_Slot`: "press + to add something" is **131.03** in 130 on a page that holds only the + tile: the last letter is replaced by an ellipsis. All other fixed second lines fit
  (`Every_Fixed_Second_Line_Of_The_Island_Fits_Its_Slot_Except_The_Ones_Listed`: largest "open · not on the island" 120.6, "Delete again to remove" 118.8, "runs as administrator" 106.8). Fixed titles fit (`Every_Fixed_Title_Of_The_Island_Fits_Its_Slot`: "No terminal is open" 121.2).
- `A_Window_Title_Of_The_Usual_Length_With_A_State_Word_Is_Cut_At_The_State_Word`: when a helper tile has no project name its second line is the window's title plus the state words: "Windows PowerShell · needs you"
  is 166.1, "Command Prompt · needs you" 154.6; what is cut is "needs you". (With a project name the line is the helper's name and fits.)
- `The_Twelve_Helper_Lines_Fit_The_Slot_And_The_Tightest_Margin_Is_Recorded`: all twelve (four helpers × three states) fit; the tightest is "Claude Code · needs you" 125.8, **4.2 to spare**.
Smallest repair: for the state words, put them first or cut the title instead of the end (as DESIGN-1-02); for the hint, a shorter word ("press + to add") is a text change: proposal.
Snowey would see: the state word always present on a helper's tile; the hint complete.
No picture shows these states: see section 6, request 1.

### DESIGN-1-06 — the heading is not where the last one was (MEDIUM, proposal)

Where: `src/Island.SettingsUi/SettingsView.cs` (`_page.VerticalAlignment = Center`, `_scrollContent.MinHeight`; the page is centred in the room above the buttons when it is short and starts at the top when it is tall).
The reference does the same (`.c-mid{justify-content:center}`), so this is the approved layout, not a departure.
Evidence: `SettingsLayoutTests.The_Heading_Top_Per_Section_Is_Recorded_For_The_Settings_And_For_The_First_Start` (1920 by 1080): settings: Key 82, Pages 247.9, On the island 82, Scenes 426.6, Mode 300.1, Glass 415.0,
Coding agents 273.8, General 82; first start: Welcome 450.4, Key 82, Pages 247.9, On the island 82, Mode 300.1. Spread **344.6 px** (`The_Heading_Jumps_Between_Sections_By_More_Than_A_Hundred_Pixels`); the pictures agree
(`SettingsPictureTests.The_Heading_Top_In_The_Settings_Pictures_Is_Not_Where_The_Last_One_Was`: ink at y 95 to 102 on the tall pages, 268 to 429 on the short). The buttons are at 1016 on every step (fixed).
Also `A_Scroll_Bar_Narrows_The_Viewport_And_Moves_The_Centred_Heading`: Key, On the island and General scroll, the viewport narrows from 1872 to 1855 and the centred heading and sentence move **8.5 dp** left (951.5 against 960);
`SettingsPictureTests.The_Title_Centre_Moves_When_A_Scroll_Bar_Is_Shown` reads 10.5 px in the pictures.
Smallest repair: top-align the page (heading at y = 82 for every step) or keep the heading fixed and centre only the body; reserve the scroll bar's room always (`ScrollBarVisibility.Visible`, or a fixed gutter).
Snowey would see: pressing Continue changes the body and leaves the title where it was.

### DESIGN-1-07 — the Close link of the Add panel (LOW, proposal)

Where: `src/Island.SettingsUi/AddByHand.cs`, `Build` (`close.Margin = new Thickness(0, 6, 0, 0)` inside a panel padded 16 on the right) against the head row of `PicksSection.PageCard` (margin 8 on the right).
Evidence: `SettingsDefectTests.The_Close_Link_Of_The_Add_Panel_Is_Eight_Pixels_Inside_The_Other_Links`: right edge of the words 1286.5 against 1294.5 for "Restore starter list" (picture `review/settings/add-panel.png`: 1287 against 1294).
Smallest repair: `Margin = new Thickness(0, 6, -8, 0)`. Snowey would see "Close" under "Restore starter list", flush right.

### DESIGN-1-08 — sentences that end on one or two words (LOW, proposal)

Where: the wrapped descriptions of `GeneralSection`, `AgentsSection`, `ModeSection` (`Look.HintSize` 12.5, wrapped at the card's width).
Evidence: `SettingsDefectTests.Wrapped_Sentences_That_End_On_A_Line_Under_Fifteen_Percent_Of_The_Box_Are_Counted` (last line read from the position of the text's last character after layout): **6 of 20 wrapped sentences**:
Mode "... fullscreen." 8% (also in the first start's Mode step), Coding agents "... a helper / wait." 4%, General "... see its / README." 9%, "... put the old one / back here." 8%, "... From 2 / to 60 seconds." 15%
(the range is split from its unit). Seen in `review/settings/general.png`, `agents.png`, `mode.png`.
Smallest repair: a narrower `MaxWidth` for the descriptions of those cards, or a non-breaking space between the last words (a text change in the source: proposal). Snowey would see fewer one-word last lines.

### DESIGN-1-09 — the lifted tile is see-through (LOW, proposal)

Where: `src/Island.App/Visuals/DragView.cs`, `Lift` (`clone.SetSelected(false, colour)`: an unselected tile is drawn at `ItemUnselectedOpacity` 0.74).
Evidence: `PictureMeasureTests.The_Dashed_Line_Of_The_Drop_Zone_Shows_Through_The_Lifted_Tile`: along the row of the zone's top line, 7 light/dark alternations across the tile's inside against 1 on a clear row below
(`review/choices/drag-off.png`, the lifted "Al" tile, enlarged: dashes over its top). Reference `.t.drag`: tile at its own strength with a shadow, nothing shows through.
Smallest repair: draw the lifted clone at full strength (one more switch on `TileView`: opacity 1 without the white ring). Snowey would see a solid tile in his hand.

### DESIGN-1-10 — movement: what is a constant and what is written by hand (LOW, proposal)

Evidence (`SourceScanTests`, `MotionTableTests`):
- `Hand_Written_Durations_Of_Visual_Movement_Are_Exactly_The_Ones_Recorded`: in `Island.App/Visuals` and `Island.SettingsUi` there are exactly four: the drop zone fades in in 150 ms and out in 120 ms and the lifted tile springs back in 260 ms with a
  `BackEase` of amplitude 0.6 (all `DragView`), and the capture pulse of 1 s (`KeySection`). A new one turns the test red.
- `The_Fade_Durations_Of_The_Island_Are_Recorded_With_Their_Names`: opacity or colour changes take 110 (`DismissFadeMs`), 120, 150, 200 (`ClosedFadeMs`), 200 (`RowFadeInMs`), 280 (`ContentsFadeInMs`) and 350 ms (`ColorChangeMs`): **six different times**; the drop zone's 120 and the capsule's dismiss 110 are the same kind of movement 10 ms apart.
- `The_Lifted_Tile_Comes_Back_By_An_Ease_Not_By_The_Island_Spring`: every other shape on the island follows `LookConstants.SpringMass/Stiffness/Damping`; the lifted tile's return uses a back-ease in 260 ms.
- `The_Capture_Pulse_Is_Linear_Where_The_Reference_Eases`: reference `pulse 1s infinite` has the CSS default ease between keyframes; the app's keyframes are linear.
- `The_Step_Dots_Of_The_Reference_Fade_And_The_Dots_Of_The_App_Do_Not`: reference `.c-dots i{transition:background .3s}`; the app rebuilds the dots at each step, no transition.
Smallest repair: move 120/150/260 into `ChoiceConstants` (and, if wanted, 120 to 110); the rest are choices. Snowey would see no change except where he picks a time.
Held (below): the three ease-in-out curves agree within 0.03.

### DESIGN-1-11 — the numbers that depart from the reference (LOW, proposal)

Evidence: `ReferenceAgreementTests.Every_Number_Of_The_Island_That_Has_A_Reference_Agrees_With_It_Or_Is_A_Known_Departure`: **83 numbers read out of the reference's own style rules** (`island-choices.html`, `island-setup-previews.html`)
and compared with the constants or with the literals in the drawing code; **78 agree exactly**, 5 depart, each by a stated amount (the test turns red on any other change):
| number | reference | island | where |
|---|---|---|---|
| notice: gap between the check disc and the words | 8 | 10 (`NoticeLayout.Gap`) | picture: 11.5 dp measured to the first ink |
| search: the typed text's inset in the field | 14 | 16 (`SearchView`, `x + 16`) | |
| the + tile's glyph size | 20 | 22 | with DESIGN-1-03 |
| the + tile's glyph weight | 400 | 700 | with DESIGN-1-03 |
| settings heading weight | 650 | 600 (`FontWeights.SemiBold`) | whether WPF can draw 650 in Segoe UI Variable is UNVERIFIED |
Also: the contents' centre is 39 dp from the capsule's top (`ContentsLayer.RowCentre`, `SearchView.Centre`), the reference's content box is centred at 1 + 37 = 38: the row sits 1 dp low by the code
(`SourceScanTests.The_Row_Centre_Is_The_Same_In_The_Capsule_And_In_Search_And_Sits_One_Dp_Below_The_Middle`); in the six dark pictures it is 0.5 to 1 dp below the middle between the rim lines
(`PictureMeasureTests.The_Row_Of_Tiles_Stands_About_One_Dp_Below_The_Middle_Of_The_Capsule`; the light pictures have no clean rim line to read). The second row's centre is 72 below the first (111 against 39) where a row is 74 high.
Snowey would see, if all taken: words 2 dp nearer the check, the typed letters 2 dp nearer the field's edge, everything in the capsule 1 dp higher. Each is small; none was asked for.

### DESIGN-1-12 — colours that are close (LOW, proposal)

Evidence (`ColourFamilyTests`, OKLab distance; about 0.02 is the least seen, 0.10 clearly another colour):
- `A_Swatch_Offered_For_A_New_Page_Is_Close_To_The_Terminals_Lime`: the Terminals page colour #B8F03A (Claude's choice, WO11) is **0.080** from the swatch #7CE04A (an invented page of the settings pictures is that colour); in `settings/pages.png` the two dots sit one above the other.
  `The_Terminals_Page_Colour_Against_The_Other_Page_Colours_And_The_Swatches`: also #E9A0FF and #B9A7FF **0.073**, #B8F03A and #7CE04A 0.080, #19E6B3 and #7CE04A 0.111, #E9A0FF and #FF7AB6 0.117; nearest to the lime besides: #19E6B3 0.155.
- `The_Working_Ring_Is_The_Colour_Of_The_Antigravity_Disc`: the working ring (#4C8DFF) is **exactly** the Antigravity disc (0.000) and 0.091 from Gemini's (#8E75FF) — the ring would be the colour of the disc it surrounds.
  Latent today: Antigravity's terminal program and Gemini are BLOCKED (cannot connect), so only Claude Code and Codex show rings, and for those every ring/disc pair is at least 0.112 (`Ring_Against_Disc_Distances_Are_Recorded_For_Every_Helper_And_State`: 13 of 15 pairs clear).
- `The_Three_Ring_Colours_Differ_By_Hue_Only_Two_Of_Them_In_Lightness`: needs-you orange L 0.813, finished green L 0.725 (a difference of 0.09): the two full rings are told apart almost by hue alone (EASE has the colour-blindness side).
Smallest repair: drop the swatch #7CE04A (or move the lime); give the working ring a colour no helper disc has. Snowey would see: nothing today.

### DESIGN-1-13 — the white glyph on the page chip (LOW, proposal)

Evidence: `PictureMeasureTests.The_White_Chip_Glyph_Against_The_Chip_Fill_Is_Weakest_On_The_Terminals_Page`, WCAG relative luminance, white glyph against the chip fill just above it, dark and light backdrop:
Media 4.81 / 3.78, Folders 2.61 / 2.10, Apps 5.62 / 4.33, Vibe 2.48 / 2.02, Browser 2.84 / 2.27, **Terminals 2.11 / 1.73**. WCAG 2.2 1.4.11 asks 3:1 for a mark that must be understood (`Research/polish-facts.md`).
Four of six pages are under it; Terminals is the weakest, and its lime is Claude's own choice, "changeable by asking". (The six page colours of the reference are Snowey's; the glyph colour is not a number in the reference's constants.)
Smallest repair: a dark glyph on the light page colours (as the notice's check is dark on its mint: `NoticeLayout.CheckColour #06281F`), or a darker lime. Snowey would see: a dark prompt symbol on the lime chip.
Cross-reference EASE (contrast); I measured the chip only.

### DESIGN-1-14 — the Terminals glyph is the thinnest, and softness at 100% was never seen (LOW, proposal)

Evidence: `SourceScanTests.The_Glyph_Stroke_Widths_Are_Recorded_And_The_Terminal_Glyph_Is_The_Thinnest`: strokes on the 16 grid: terminal 1.2, globe 1.3, search 1.6, close 1.6, term 1.7 (the reference's own: 1.6, 1.7, 1.3).
Drawn at the chip's 18 px the Terminals glyph is 1.35 px wide at 100% scaling (Vibe's `>_` 1.91, the globe 1.46); the 14 px control glyphs (close, search) are 1.4 px. Every picture of the self-test is drawn at 2 pixels to a dp,
where these are 2.7 and 2.8 px and crisp (enlarged crops of the media buttons and the Terminals chip look sharp): **no picture shows what a 100% screen draws.** At 2 px the pause bars and the arrows are crisp; I could not check the strokes at 1.
Smallest repair: 1.2 to 1.6 for `terminal` (the same family as `term`) — a look change, so a proposal; and request 5 below. Snowey would see: a heavier prompt symbol.

### DESIGN-1-15 — which states have a current picture (LOW; asks for pictures, not a repair)

`PictureMeasureTests.The_Redrawn_Choices_Pictures_Are_All_On_The_Dark_Backdrop`: strip, plus-row, drag-off, playing-tile, pill, notice, search, modes, terminals-page and terminal-states are all on the dark backdrop; only the six page pictures have a light one.
State by state, the pictures the self-test draws now (D dark, L light):
| state | current picture |
|---|---|
| unselected, selected tile | all six pages, D and L |
| closed (grey) | `choices/search.png` (D, one tile); the record `closed-pick.png` |
| missing ("not found") | none |
| several windows (dots) | none; only the record `count-badge.png` (README says every snapshot draws them: none of the twelve page pictures has one) |
| playing (bars), paused | `choices/playing-tile.png` (D), `media-paused.png` (D) |
| dragged | `choices/drag-off.png` (D) |
| second row | `choices/plus-row.png` (D); no row tile selected, no "Nothing else is open" |
| + tile selected, empty page | none |
| working, needs you, finished rings, selected with a ring | `choices/terminal-states.png`, `terminals-page.png` (D) |
| pill (75%, 25%, no length), notice (finished) | D only; no paused pill, no "needs your answer" notice |
| search | one result set, D; no "no match", no long text |

## 3. What held (the coverage statement; all pass)

- `ReferenceAgreementTests` (2 tests): 83 numbers against the references; 78 exact, the 5 departures above and no other. Exact among them: capsule 76 / 38 / row start 19, chip 36 and its gap 12, tile 40 and its letters 12, tile gap 8, text block 130 with 13.5 and 11.5 at 80%,
  controls 28 / 14 / 2 / 10, selected ring 2 and glow 14, closed strength 0.85, dots 4 and 3, badge + 16 reaching 5, arrows 16 at 85%, second row 74 high and its label 12 at 85%, the playing bars (3 wide, 2.5 apart, 5 to 17, 0.9 s), the small pill (44, 22, gap 6, pads 7 and 8,
  tile 30, title 150 at 12.5, buttons 26, ring 2.4), the notice (50, 25, pads 7 and 14, disc 36, edge 1.5, 12.5 and 11 at 80%), search (40, 20, least width 120, 14, gap 12), the drop zone (260 by 40, 1.5 dashed, 12, tilt -8), the settings (top island 44 with left padding 16, step dashes 22 by 6,
  heading 34, sentence 15, group radius 22, row 52, button 38, key caps 30 / 14 and 22, swatch 24, page dot 16).
- `PictureMeasureTests.The_Selected_Tile_Stands_On_One_Line_In_All_Twelve_Page_Pictures` (centre y 105.5 or 106 in all twelve, ring 87 to 88 px = 44 dp), `The_Ring_Of_The_Selected_Tile_Is_Centred_On_The_Page_Chip_In_Every_Picture` (chip glyph within 1 px of that line),
  `The_Width_Each_Page_Picture_Shows_Is_The_Width_Rule_For_A_Whole_Number_Of_Picks` (466, 454, 502, 454, 502, 406 dp for 3, 4, 5, 4, 5, 3 picks: every picture is the width rule to within 0.5 dp), `The_Notice_Picture_Has_The_Sizes_Of_The_Notice_Layout` (disc 71 to 72 px = 36 dp),
  `The_Rings_Of_Two_Neighbouring_Tiles_Are_The_Size_And_The_Distance_The_Work_Order_Gives` (rings 92 px = 46 dp across against 46.5, neighbours 2 dp apart against 1.5, thickness 2 dp against 2.5: pixel steps of 0.5 dp).
- `SettingsPictureTests.The_Controls_Of_The_General_Cards_End_On_One_Line`: six cards, right edge 1294 (1293 once), within 1 px. `SettingsLayoutTests.On_Smaller_Screens_The_Buttons_Stay_On_The_Screen_And_The_Page_Scrolls` (1366 by 768 and 1280 by 720: every foot button inside the screen).
- `SettingsDefectTests.No_Single_Line_Text_Of_Any_Step_Is_Wider_Than_Its_Box`: **459 single-line texts** over the eight settings sections and the five first-start steps, none cut by its box. (The first version of this test looked at nothing, because `IsVisible` is false for a view in no window; fixed and made to assert it looked at more than 100.)
- `TextFitTests.The_Notice_Lines_Fit_The_Notice_Text_Width` (160.5 and 123.0 in 230), `Every_Fixed_Title_Of_The_Island_Fits_Its_Slot`, `The_Twelve_Helper_Lines_Fit_The_Slot_And_The_Tightest_Margin_Is_Recorded` (all twelve fit).
- `SourceScanTests.Every_Dimmed_Control_Is_Drawn_At_The_Same_Strength` (0.4 in three places, all equal), `The_Glass_Base_Colour_Is_The_Same_Rgb_Wherever_It_Is_Written_By_Hand` (20,22,32 in the ball, the step island and `GlassBaseColor`), the row centre written twice in the same formula.
- `MotionTableTests.The_Curves_At_A_Quarter_Half_And_Three_Quarters_Are_Recorded`: the three symmetric in-and-out curves (the breath's cosine, the mode cross-fade's smoothstep, the playing bars' CSS ease-in-out) differ from each other by at most 0.029; the colour change (linear) and the mode cross-fade (smoothstep), both 350 ms, by at most 0.096.
- Also held, by reading code: the settings screen's opening uses the same `Spring` as the capsule (EVALS G1); the notice, the pill and search take the same `Pose` entrance as the contents; the globe icon is drawn as the reference draws it (butt ends, 1.3).
- Recorded, not findings: a selected tile with a state ring is told from its neighbour only by a disc 1.43 times as light (`A_Selected_Tile_With_A_State_Ring_Differs_From_Its_Neighbour_Only_By_The_Strength_Of_The_Disc`; Snowey decided this, WO11 §3); the dimmed X on Terminals.

## 4. Commands and totals

```
export PATH="$PATH:/c/Program Files/dotnet"
dotnet test ".worktrees\rt-design\tests\Island.Redteam.Design.Tests"
```
**53 tests: 50 pass, 3 fail on purpose** (`PausedLineTests.Defect_The_Paused_Line_Of_The_Picture_Is_Wider_Than_The_Text_Block_And_Loses_The_Word_Paused`, `PausedLineTests.Defect_The_Second_Line_Of_The_Paused_Picture_Runs_To_The_Edge_Of_Its_Block`,
`SettingsDefectTests.Defect_A_Key_Name_Followed_By_A_Comma_Is_Drawn_With_Its_Brackets`; two for DESIGN-1-02, one for DESIGN-1-01). The picture tests read the main folder's `review/` and `reference/` (found above `.worktrees/`); if that folder is not
there they return at once, and `The_Pictures_These_Tests_Read_Are_Found_When_The_Main_Folder_Is` says which. The source tests read this worktree's `src/`. The test project's `csproj` gained one line (`<Using Include="System.IO" />`); the solution file was not touched.
Run time 5 to 8 s (run four times in a row: always 53 / 50 / 3); no process started, no file written outside the test's own `bin/`. xUnit runs the test classes in parallel; all WPF work goes through one STA thread that lives for the run (`Sta` in `Wpf.cs`), because the settings view keeps templates in static fields that belong to the thread that made them (a first version with a thread per body failed one run in three with "a different thread owns it").

## 5. What I could not check

- How it feels: no motion was seen; every movement finding is from code and constants. The graphics-card light cannot be pictured (no capture), so DESIGN does not cover the new light at all, only the old drawing the self-test draws.
- 100%, 125% and 150% scaling of the capsule: all pictures are at 200%. Soft icons and 1 px lines at the real pixel grid (DESIGN-1-14) are unseen.
- Real icons: the tiles in the pictures are invented letters, and the round-icon pictures are synthetic samples (blocky by construction). Whether a real program's icon looks soft on its disc is for a person.
- Blur and Darker glass, hover and press states in the settings (hover tint `#12FFFFFF`, pressed 0.8), the focus ring (EASE), disabled controls, the first-start flow's own opening, high contrast.
- The pictures are 12 page pictures, 11 `choices/` pictures and 5 records, 18 `settings/` and 5 `setup/` pictures, `ball-*` and `glass-*`; I looked at each by eye and measured the ones named above. `review/blur/` and `review/glass/` (probe pictures) were not measured.
- WCAG ratios are from `Research/polish-facts.md` (read 2026-10-07 by the main session); I did not re-read w3.org. The OKLab distances use the formula of `ColorMath`; the "0.02 / 0.10" reading of them is a rule of thumb, `UNVERIFIED`.
- The WPF text engine is the measure of text width (a TextBlock set up like the island's); `Segoe UI Variable Text` is installed here and measures the same as plain Segoe UI. A computer with other fonts may differ by a few pixels; the 4.2 px margin of DESIGN-1-05 is the one to look at.

## 6. Requests to the main session

Pictures I may not draw and ask for (invented names, the island's own off-screen drawing). Each says what to draw and in which state.
1. **Round 2 check of DESIGN-1-02 and -05:** the Media page paused with the names "YouTube Music", "SoundCloud", "Spotify", "YouTube", "Twitch" (dark and light); the Terminals page with a helper tile that has a project name in each of the three states selected and not, and one without a project name whose window title is "Windows PowerShell", state needs-you; an Apps page with nothing on it (only the + tile), selected.
2. **States with no picture (DESIGN-1-15):** a pick that is closed and one that says "not found"; a pick with 2, 3 and 6 windows (dots); the + tile selected and not; the second row with a tile selected (hovered) and with "Nothing else is open".
3. **Light backdrop for the `choices/` pictures:** strip, plus-row, drag-off, playing-tile, pill (playing and paused), notice (finished and needs your answer), search (matches, no match, a long text), terminals-page and terminal-states.
4. **After DESIGN-1-04 is touched:** the page pictures with the first, a middle and the last pick selected (the last one is where the right end's clip would show), on seven picks that slide, dark and light.
5. **At the pixel grid of a real screen:** the page pictures and the + tile at 100% and 125% (1 and 1.25 pixels to a dp), at least Media, Terminals and the second row; this is where the 1.2 to 1.7 strokes and the 14 px glyphs are soft or not.
6. **Settings, after DESIGN-1-01 and -06:** the Key step (settings and first start) with the repaired line, and the Key, Pages, Scenes and General steps one after another at 1920 by 1080 to see where the heading stands.
7. For the register: `PausedLineTests.Defect_The_Paused_Line_Of_The_Picture_Is_Wider_Than_The_Text_Block_And_Loses_The_Word_Paused` measures the string `PlayingTile.SecondLine` gives; it turns green only if the repair shortens that string. If the repair is in the drawing (the ellipsis moves into the name), that test measures the wrong thing and may be changed by the main session to read the drawn line; the picture test (`..._Runs_To_The_Edge_Of_Its_Block`) turns green either way once `media-paused.png` is redrawn. The bracket test turns green when no key-cap marker is left in any text of the Key step.
