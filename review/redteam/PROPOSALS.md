# PROPOSALS — what the red team found that would change something Snowey approved or chose (WORK-ORDER-12 section 4)

Nothing here was built. By "the one rule" each of these would change a look, a movement, a colour, a size, a key, a default or a text that Snowey approved or chose, or a picture the self-test draws.
Each line: what would change for him, the smallest repair, where the evidence is (`REGISTER.md` has the row; the attackers' reports, `code-round-1.md`, `ease-round-1.md`, `design-round-1.md`, have the numbers).
The `Defect_` tests of these rows are in the test projects, switched to Skip with the reason, so that a later yes turns them back on. Order: what a person feels most first.

## A. Seeing and moving (EASE)

1. **Windows' "Show animations" off is honoured by the working ring only** (ease-1-18). With animations off in Windows the island still springs in and out, the light still runs, Vibe still breathes, the Settings screen's ball still opens. Repair: read `SystemParameters.ClientAreaAnimation` once (it is already read for the ring) and hold the light, the breath and the springs still. Dan would see the island appear and leave without movement for a person who asked Windows for that. The most important of this list for someone with a vestibular disorder.
2. **High contrast and the Windows text size are read nowhere** (ease-1-19). WPF does not apply "Make text bigger"; a person who set 150% finds the island and Settings at the old size. Repair: read `UISettings.TextScaleFactor` and `AccessibilitySettings.HighContrast` (both on `Research/polish-facts.md`) and scale the type, or at least use the system colours in high contrast.
3. **Settings over a white window behind it** (ease-1-23, 1-24). The 12.5 px hints are 2.77:1 (Approved glass) or 3.55:1 (Darker) and the refusal colour 2.03:1 when a white page is behind the screen (6 to 8:1 over the dark wall of the pictures). Repair: a higher tint for the Settings screen only, or a solid card behind text.
4. **The two letters of a tile** (ease-1-25): under 4.5:1 on 15 of 24 dark and 17 of 24 light tiles (worst 1.9:1: white on gold or green). Repair: a darker label shadow or a darker tile gradient. The look is Dan's (62% to 40%).
5. **The close cross on the light glass** (ease-1-26): 1.85:1 (3.2:1 on the dark glass). Repair: a stronger alpha of the cross.
6. **Needs-you and finished differ by hue only** (ease-1-29): orange and green are 1.24:1 in luminance; only the selected tile says the state in words. Repair: a mark that is not colour (a notch, a dot, the state word on every tile).
7. **The "no key" cap 4.26:1** (ease-1-27) and **the grey step dots 1.9:1** (ease-1-28): an opacity and a colour. **A closed pick's letters 2.8:1** (ease-1-31): choice 2B, Dan's, listed so that it is a knowing choice.
8. **Focus and Vibe look the same in a still picture** (ease-1-30); the breath is the only difference and the mode's name is nowhere on the island. Repair: the mode's name on hover, or a mark.
9. **The scroll bar's thumb is 4 px wide** (ease-1-17). Repair: 8 px. Wheel, keys and page clicks already work.
10. **The Settings screen's own light runs every frame while Settings is open** (ease-1-20), and is not covered by the Moving light card. Repair: the same kinds.

## B. Words (EASE)

11. **The add-on is explained as "load it from the extension folder, see its README"** (ease-1-13): nothing in the app opens the folder or the file. Repair: say where the folder is and the three steps of loading an unpacked extension, or an "Open the add-on folder" button.
12. **HOTKEY_TAKEN and SETTINGS_UNREADABLE tell the old developer path** (edit the settings file, fix the file) (ease-1-14, 1-15). They have their three parts; rewording them is a text change.
13. **No text says that typing a letter starts the search** (ease-1-22); **words a new person does not know**: pick, DND, Approved, As before / the new light, hook, starter list, Tap to switch (ease-1-32).
14. **The Approved glass card says "The glass you chose: dark and clear."** (ease-1-8) to someone who chose nothing. Repair: "The standard glass: dark and clear." Not applied: the words are in a picture the self-test draws (`review/settings/glass.png`), which makes it a proposal by the one rule's first case, though the words are untrue for a new person. I would say yes to this one.
15. **`[Delete],` is drawn with its brackets on the Your key screen** (ease-1-9 / design-1-01). Repair: in `Parts.Sentence` take `[x]` followed by punctuation as a cap plus text. Two pictures change (`key.png`, `setup/key.png`). I would say yes to this one.
16. **A line of key help is 114 one-word elements for a screen reader** (ease-1-11). Repair: one text element per line (Runs and InlineUIContainers); it changes how the line wraps (a picture) and cannot be tried without a screen reader.
17. **The notice line may not be spoken by a screen reader** (ease-1-16, UNVERIFIED): a new element after every change. A person with Narrator must look.

## C. Looks (DESIGN)

18. **The paused line loses the word "paused"** (design-1-02): "Alpha-player · app · pau…"; YouTube Music and SoundCloud are cut too. Repair: cut the name part, never the end ("YouTube Mu… · tab · paused"). Changes `media-paused.png`. I would say yes.
19. **The + sits 3.5 dp below the middle of its circle** (design-1-03): bold 22 against the reference's 400 at 20. Repair: draw it as a path (as the small + badge is drawn). Seen at every summon.
20. **The glow of the selected first tile ends in a straight line** (design-1-04) where the strip is clipped (13 to 21 per mille of luminance on the four lighter pages). Repair: widen the clip to the glow's reach (about 20 dp).
21. **The heading moves between steps by up to 345 px** (design-1-06) and a scroll bar shifts every centred line by 8.5 dp. The reference centres too, so this is the approved layout. Repair: top-align the page, or reserve the scroll bar's room.
22. Smaller: the empty-page hint is 1 px too wide and "needs you" is cut on a helper tile named by its window (design-1-05); the Close link of the Add panel sits 8 dp inside (design-1-07); 6 of 20 wrapped sentences end on one or two words (design-1-08); the lifted tile is at 74% and the dashed zone shows through (design-1-09); six different fade times and four hand-written durations (design-1-10); 5 of 83 numbers depart from the reference (design-1-11); Terminals lime is 0.080 from its swatch and two swatches are 0.073 apart (design-1-12); the white glyph on the chip is under 3:1 on 4 of 6 pages (design-1-13); the Terminals glyph is the thinnest stroke (design-1-14).

## D. Speed and the light (CODE, and WORK-ORDER-12 section 2)

23. **The rim of the old light has a 0.6 px blur of its own that the graphics-card light does not carry** (code-1-15). It is below one pixel and was left to the compositor's own anti-aliasing. If Dan sees a difference between "As before" and "Graphics card", this is where to look first; the test (`DuplicatedRuleTests`, Skip) goes green when the constant is named in `LightSpec`.
24. **Every mouse move over the island draws a whole frame outside the frame budget** (code-1-22b), and in the quiet states it is the only drawing there is. Repair: poke the machine and draw only when something changed. Not applied: a dropped draw after the last mouse move could leave a state stale, and no screen can be looked at in this run.
25. **Half rate of the light has no saving on this laptop** (the screen is 165 Hz): a fourth kind at a fixed 30 to 40 frames a second (`STATE.md`, OWNER DECISIONS). And **drawing the island at half the screen's rate while only a playing tile or a working ring moves** was tried (6 to 10 points of 24 to 40, inside the spread) and was not applied.
26. **Two Windows accounts on one machine** (code-1-21): one fixed pipe name and the first of five loopback ports, so one user's add-on can be answered by the other user's island. Nobody on one personal laptop meets it; the shop version may. Repair: a per-user name and port, or a token the add-on gets from the app.

27. **A tile that missed its icon three times keeps its two letters until Island is restarted** (code-2-8): the retry has a bound (three tries, 20 s apart) so that a tile on a drive that is off is not read for ever. Repair: no bound and a longer wait each time (20 s, a minute, five minutes, then every five). A hand-made place on a network drive that connects late (a VPN) is the case.
28. **The Moving light card's three pills stand at the left under the text** (design-2-2); every other card of General has its control at the right edge. Repair: the pills at the right, stacked or as a three-way switch, or the hint shortened. The card was not asked for.
29. **The focus ring inside the three text fields is 2.90:1** against the field's fill over a white window behind the Approved glass (7.90:1 on the dark wall) (design-2-6; the same family as 3).

30. **During the fly-in the glow's hole is a stadium round an elliptical ball** (design-3-2): the island stretches a round ball into an ellipse and the old bloom follows the ellipse exactly; the new light's hole is a rounded rectangle (a GDI region cannot be an ellipse), so about 100 px of glow next to the ball are cut where the old bloom shows. It lasts the fly-in only. Repair: an elliptical region (CreateEllipticRgn when the shape is nearly a ball) or a compositor clip; neither can be tried without a screen.

31. **The hole in the glow lies up to 1.3 px inside the capsule's corner arcs** (design-4-1): the straight edges are exact at 11 scales, but a GDI round-rectangle region draws its arc a little inside the outline's at 125%, 225% and 250%, so a hairline of glow shows in the corners. Repair: a path region built from the outline's own arc (or a compositor clip); neither can be tried without a screen. `Round4CutOutTests.Defect_The_Corner_Arcs...` is Skip.
32. **A refused add carries the page down to the words, away from the panel** (design-4-4): on the first page the Add panel's field is 545 px above the room after the redraw. Repair: draw the refusal inside the panel and do not scroll for it, or scroll only when the pressed control and the words fit together. It changes where a text is drawn. `Round4NoticeTests.Defect_A_Refused_Add_Leaves_The_Panel...` is Skip.
33. **Pressing the Graphics card pill when it is already the choice and cannot be had says the card's sentence twice and scrolls 218 px to the second** (design-4-5): the order promises the refusal's words; skipping the copy and the scroll when the notice equals a text already in the room is a look.

## What I would say yes to first

14, 15 and 18 (a false word, a leaked bracket, a lost word), then 1 (Windows' own switch for movement), then 3 (what Dan cannot see on a white page).

---

## Answered on 2026-10-08 (WORK-ORDER-13)

Dan answered every proposal. **Built:** P1, P2 (Settings only: the island's own type is not scaled), P3, P4, P5, P7 (the "no key" cap and the grey dots of the steps only), P8, P11 (a button), P12, P14, P15, P16, P17, P18, P19, P20, P21 (aligned to the top), P22, P23, P24, P25 (both: a fixed rate of 40 a second and half the screen's rate while only a tile or ring moves), P26, P29, P30, P31, P32. **Moot after Q1** (the "Moving light" setting is gone): P10, P28, P33. **Said no:** P6, P9, P13, P27. The answers are in `PROPUNERI.md`; what was built and how it is proved is in STATE.md, `## CLOSE-OUT — WO13-DECIDED`.
