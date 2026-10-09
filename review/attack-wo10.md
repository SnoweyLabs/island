# ATTACK10 — WORK-ORDER-10 (round icons, the keyboard, adding by hand)

Adversarial pass by tests only. Project: `tests/Island.Attack10.Tests/` (not in `Island.sln`; run it with
`dotnet test tests/Island.Attack10.Tests/Island.Attack10.Tests.csproj`). Nothing under `src/` or any existing test was changed. No existing assertion was
contradicted. Every path in these tests is invented (drive `Q:`, `Q:\Invented\...`); no choosing window, no `Island.App`, no browser, no input, no network.

Run on branch `attack10`: 352 test cases, 308 pass, 44 fail. The 44 are the rows of 26 methods named `Defect_*` (three of them are `[Theory]`). Every `Holds_*` passes.
A failing `Defect_*` is the report of its defect: it stays red until the code is fixed.

## Totals

| Severity | Count | Meaning here |
|---|---|---|
| HIGH | 0 | no data loss, no wrong pick removed, no path in a log or message, no unowned choosing window found |
| MEDIUM | 3 | a real icon, scene or disk state gives a visibly wrong or stuck result |
| LOW | 17 | wrong in a corner, a spec sentence not met, a wrong word on screen |
| LATENT | 4 | nothing reaches it today, a guard or default that would fail open |

## Defects

Test names are `Class.Method`; classes are in `tests/Island.Attack10.Tests/`.

### MEDIUM

**D01 — a pale flat logo is drawn white on an almost-white disc and vanishes.**
`src/Island.Core/RoundIcon/RoundIconRule.cs`, `Analyse` (flat-shape branch). Only a main colour with every channel at 230 or more gets the dark disc; the white drawing is never
checked against the disc, though the rule does that check (60 apart) for "any other icon". Failing input: 64x64 transparent icon, ellipse 44x30 in rgb(240,240,200) — white on
disc (240,240,200), 55 apart at most. A sweep of 512 colours from 150 to 255 finds 56 flat shapes that vanish, among them rgb(210,210,210), (210,210,225), (210,210,240).
Tests: `RoundIconAttackTests.Defect_A_Pale_Flat_Shape_Is_Drawn_White_On_A_Disc_Almost_As_Pale`, `RoundIconAttackTests.Defect_Pale_Flat_Shapes_Across_The_Colour_Cube_Vanish_On_Their_Own_Disc`.

**D02 — a scene run does not know the profile folder, so a hand-added folder under it is never brought forward.**
`src/Island.Core/Scenes/ScenePlan.cs`, `ScenePlans.For` / `Step` call `PickStates.For(thing, open)` with no `PickContext`; the caller is `src/Island.App/PickPages.cs`, `RunScene`.
A stored `%USERPROFILE%\Docs` cannot be put back in memory, so the folder window that is open is not found: the step is `Open`, the folder is opened again and counted in
"n opened". The same missing context means a hand-added thing whose place is gone is never skipped or reported by a scene. Most hand-added folders are under the profile.
Failing input: scene with one hand folder at `<profile>\Docs`, Explorer window open at that path. Expected `BringForward`, got `Open`.
Test: `HandPickAttackTests.Defect_A_Scene_Run_Does_Not_Know_The_Profile_Folder_So_A_Hand_Folder_Under_It_Is_Never_Brought_Forward`.

**D03 — one hung place stalls the answers for all places, and slow probes pile up.**
`src/Island.Core/Picks/PickTargetCache.cs`, `Refresh` (called from the 5 second timer in `src/Island.App/PickPages.cs`, `StartTargetProbe`). `Refresh` asks the picks in order on one
thread and keeps no note of a question in flight. (a) With picks [hung, fine] and a probe that blocks on the first, the second is still `NotAsked` after 1.5 seconds. (b) Four overlapping
`Refresh` calls (the timer does not wait for the last one) enter the probe four times for one place. A lettered drive that is a mapped network drive that went away can block for seconds.
Tests: `StoreAttackTests.Defect_One_Hung_Place_Holds_Back_The_Answers_For_Every_Other_Place`, `StoreAttackTests.Defect_A_Place_Being_Asked_About_Is_Asked_Again_By_Every_Overlapping_Refresh`.

### LOW

**D04 — a closed tile loses an icon whose colours share one brightness.**
`RoundIcons.Grey` + `GreyIcons.Make` (`src/Island.Core/RoundIcon/RoundIcons.cs`, `src/Island.Core/Picks/GreyIcons.cs`), drawn by `TileView.BuildFace`. Disc and picture are both reduced to luma, so a
plate rgb(30,160,90) with a square rgb(238,60,60) (luma 113 both) is a blank grey disc when closed: square grey 108, disc grey 107. Open, they are 208 apart in red.
Test: `RoundIconAttackTests.Defect_A_Closed_Tile_Loses_An_Icon_Whose_Colours_Share_One_Brightness`.

**D05 — an icon drawn at its own size can sit on half a device pixel.**
`src/Island.Core/RoundIcon/RoundIconLayout.cs`, `Place` (used by `TileView.BuildFace`, which sets a fractional `Margin`). A 17 pixel icon on the 40 wide tile at 100 percent gets x = 11.5 and
is sampled across two pixels; 104 of the sizes 1..26 at the eight usual scales do this. "At its own size, centred, never stretched" (EVALS I5) asks for a whole pixel.
Test: `RoundIconAttackTests.Defect_An_Icon_Drawn_At_Its_Own_Size_Can_Sit_On_Half_A_Device_Pixel`.

**D06 — a Left press does not reset the idle clock when the second row has emptied under the selection.**
`src/Island.Core/IslandMachine.cs`, `MoveSecondRow` returns on `count <= 0` before `Poke`. Selection on the third tile of "Open now", all windows close, Left is decided as a move
(`AtFirst` false). WORK-ORDER-10 §2: "Every key resets the idle clock."
Test: `KeyAttackTests.Defect_A_Left_Press_Does_Not_Reset_The_Idle_Clock_When_The_Second_Row_Has_Emptied_Under_The_Selection`.

**D07 — the wheel does not cancel a pending Delete.**
`src/Island.App/IslandController.cs`, `Wheel` only slides the strip and calls `Activity()`, which only pokes the clock. §2 lists the wheel among the things that cancel.
(Source-reading test: the controller is WPF and cannot run here.) Test: `KeyAttackTests.Defect_The_Wheel_Does_Not_Cancel_A_Pending_Delete`.

**D08 — a row laid out again by a changed item does not cancel a pending Delete.**
`IslandController.RedrawItemsIfChanged` rebuilds the row (`_builtPageId = null`) without telling the machine; only `ContentsChanged` clears the ask (via `SwitchWhileOpen`). §2 lists "the row
being laid out again". The ask is by pick id, so a wrong pick is still never removed (see "what held"); the note under another pick can still read "Delete again to remove".
Test: `KeyAttackTests.Defect_A_Row_Laid_Out_Again_By_A_Changed_Item_Does_Not_Cancel_A_Pending_Delete`.

**D09 — a hex spelling of an IP address is taken as a site.**
`src/Island.Core/Picks/TypedSite.cs`, `IsHostName` refuses only a last label made of digits. `127.0.0.0x1`, `1.1.1.0x1`, `0x7f.0.0.0x1` pass; a browser reads them as IPv4 addresses and the island later
hands `https://127.0.0.0x1/` to it. Test: `HandPickAttackTests.Defect_A_Hex_Spelling_Of_An_Ip_Address_Is_Taken_As_A_Site` (3 rows).

**D10 — a valid host of 196 to 253 characters is understood, then refused with words about an "id".**
`TypedSite` allows 253; `Pick.IsStorable` allows 200 for the id `site:<host>` (and the name and host fields). Four labels of 60 characters plus `.org` (247) show as understood and then
"That could not be added: id must be a kind, a colon and a name, with no spaces." Test: `HandPickAttackTests.Defect_A_Valid_Host_Of_196_To_253_Characters_Is_Understood_And_Then_Refused_With_Words_About_An_Id`.

**D11 — what the screen shows is not always what is kept.**
`TypedSite.HostOf` cuts one leading `www.` (shown), `Pick.ForSite` cuts another (kept). `www.www.example.org` shows `www.example.org` and keeps `example.org`.
Test: `HandPickAttackTests.Defect_What_The_Screen_Shows_Is_Not_Always_What_Is_Kept`.

**D12 — the profile folder added as a folder is named "Drive <letter>".**
`src/Island.Core/Picks/HandPicks.cs`, `Folder`: the stored form is the bare token, `LeafName` returns null on purpose (the leaf would be the account name), and the fallback name is for a drive.
Test: `HandPickAttackTests.Defect_A_Folder_Pick_Of_The_Profile_Folder_Itself_Is_Named_As_A_Drive`.

**D13 — a program browsed to is added again when it is already on the island from the list.**
`src/Island.Core/Picks/Pick.cs`, `IsSameThing` compares ids and places only. `program:alpha` (exe `alpha.exe`) and a browsed `Q:\Invented\Alpha\Alpha.exe` (id `program:<random>`, same exe) both
stay; both tiles match the same windows. EVALS C5. The "On the island" list also offers the starter pick as "off". Test: `HandPickAttackTests.Defect_A_Program_Browsed_To_Is_Added_Again_When_It_Is_Already_In_The_Island_From_The_List`.

**D14 — a spelled-out profile place is not the same thing as its token form.**
`Pick.IsSameThing` compares the stored spellings without expanding the token: a hand-edited `Q:\Invented\Profile\Docs` and a chosen `%USERPROFILE%\Docs` are two things.
Test: `HandPickAttackTests.Defect_A_Spelled_Out_Profile_Path_Is_Not_The_Same_Thing_As_The_Token_Form_Of_It`.

**D15 — a scenes file holding a hand-made thing still says schema 1.**
`src/Island.Core/Scenes/SceneStoreFile.cs`, `ToJson`. WORK-ORDER-10: "the schema number of each file whose shape changed goes up by one", and the scenes file "may hold the same field". The picks file writes 2 when
a pick has a place or is a file; the scenes file always writes 1. (Reading a 2 would need `FileSchema.Problem` raised for scenes too.) Existing test `SceneTests` pins 1 only for ordinary things; not contradicted.
Test: `HandPickAttackTests.Defect_A_Scenes_File_That_Holds_A_Hand_Made_Thing_Still_Says_Schema_1`.

**D16 — PICK_TARGET_MISSING outgrows the balloon when the name is long.**
`src/Island.Core/Picks/HandPickRefusals.cs`, `ForMissing` puts the whole name in. The message is 216 characters plus the name; a hand-made name may be 100 characters (the chosen file's name): 314.
The repo's own balloon limit is `SceneRefusals.MaxMessageLength` (250); scenes shorten names to fit, this does not, so the end ("Remove it from the island and add it again...") is cut.
`AppHost.Refuse` also writes the full message, name included, to the log. Test: `HandPickAttackTests.Defect_The_Missing_Target_Message_Outgrows_The_Balloon_When_The_Name_Is_Long`.

**D17 — Windows' choosing window opens before the session looks at its own locks.**
`src/Island.Core/SettingsEdit/SettingsSession.cs`, `AddFolder`, `AddFile`, `AddProgramByBrowsing` call the chooser first and `AddByHand` checks the picks-file lock and the page afterwards. With a picks file
that could not be read, or a page that is gone, the person chooses something and then hears "nothing was added".
Test: `SessionAttackTests.Defect_A_Choosing_Window_Is_Opened_Before_The_Session_Looks_At_Its_Own_Locks`.

**D18 — a chooser that throws escapes the session.**
Same three methods; nothing catches a shell failure (owner closed, shell busy). Test: `SessionAttackTests.Defect_A_Chooser_That_Throws_Escapes_The_Session`.

**D19 — a spelled-out profile path loaded from the file is written back as it is.**
`SettingsSession.ApplyPicks` calls `PickStore.Save(path)` without the profile folder, so Save's own refusal ("would carry the Windows account name into the file") is never reached, and `PickStore.Parse` does not
bring a spelled-out place to the token. A hand-edited file keeps the profile folder in it after every later edit. Test: `SessionAttackTests.Defect_A_Spelled_Out_Profile_Path_Loaded_From_The_File_Is_Written_Back_As_It_Is`.

**D20 — a chosen path with half a surrogate pair is saved as a different path.**
`PickPath.TryNormalize` accepts it (not a control character; NTFS allows such names); the JSON writer turns the half pair into U+FFFD; the pick read back points at a file that does not exist and turns grey,
"not found". Save does not throw. Test: `StoreAttackTests.Defect_A_Chosen_Path_With_A_Lone_Surrogate_Is_Saved_As_A_Different_Path`.

### LATENT (nothing reaches these today)

**L1 — a session with a chooser and no `HandContextSource` fails open.** `SettingsSession.HandContext` falls back to `HandContext(null, [])`: nothing is compressed, so a place under the profile is stored
spelled out. `AppHost` always sets the source. Test: `SessionAttackTests.Defect_A_Session_Given_A_Chooser_But_No_Hand_Context_Writes_The_Profile_Path_Spelled_Out`.

**L2 — `GuardTests.A_Picks_Path_Is_Named_Only_Where_It_Must_Be` misses every log line that does not say `Location` inside a `Log*(`/`Refuse(`/`Notify(` call.** It passes `var place = pick.Location; _files.Log(... + place)`,
`var real = PickPath.Expand(pick.Location, p); _files.Log($"{real}")`, `Debug.WriteLine`, `Console.Error.WriteLine`, `Trace.*`, `_logger.Warn(...)`, `Logger.Info(...)` (the pattern needs `Log(` with no dot),
`throw new X(... + pick.Location)`, `File.AppendAllText(log, pick.Location)`. A plain search of today's `src/` for those shapes finds none (`GuardAttackTests.Holds_The_Real_Source_Has_No_Log_Call...`).
Test: `GuardAttackTests.Defect_The_Path_Guard_Passes_A_Path_That_Reaches_A_Log_Line` (10 rows; the guard's regex is copied into the test).

**L3 — `GuardTests.A_Choosing_Window_Always_Has_An_Owner` passes a dialog with no owner:** `ShowDialog(null)`, `ShowDialog(default)`, `ShowDialog(someNullVariable)` match `\.ShowDialog\(\s*\w+\s*\)`, and one call with an owner
anywhere in a file satisfies the whole file. The real `OutsidePlaceChooser` is correct (two calls, both `ShowDialog(owner)`, both gated, constructed with `this`).
Test: `GuardAttackTests.Defect_The_Owner_Guard_Passes_A_Dialog_That_Has_No_Owner` (4 rows).

**L4 — the same guard looks for four type names only.** `FileOpenPicker`, `GetOpenFileName`, `SHBrowseForFolder`, the COM `FileOpenDialog`, `CommonOpenFileDialog` are not seen, so a file that opens a choosing window
that way is not even read. Test: `GuardAttackTests.Defect_The_Owner_Guard_Does_Not_Know_Other_Ways_To_Open_A_Choosing_Window` (5 rows).

### Noticed, no test (cannot be shown from Core, or a documented limit)

- 8.3 short names (`Q:\PROGRA~1`) and links are different keys from the long name by design ("nothing read from the disk"). The same folder through a junction or a short name of the profile folder is not compressed to the token.
- `PickPath` reserves `CON PRN AUX NUL COM1..9 LPT1..9`. Microsoft Learn's current list also names `COM0`, `LPT0` and the superscript forms (COM¹...). UNVERIFIED (no network allowed); nothing is built on it.
- `PickPath.Expand` returns null for a stored place whose expanded form passes 259 characters (a long profile folder): the pick never opens and is never marked missing, silently.
- `Pick.IsStorable` does not check `name` for `/` or `:`; a hand-edited file can keep a path-looking name written with forward slashes. Names are free text by design.
- A full island (1000 picks) answers a bare "Nothing was added." with no reason.

## What held

Round icons (§1): one pixel icons (opaque, transparent, alpha 127, alpha 128); every pixel at alpha 127 (letters) and 128 (plate); alternating 127/128; alpha ramps; fully transparent 512x512; a 2048x2048 noise icon
(under 5 s) and 2049x2048 (letters); empty, short and long buffers; negative, zero, `int.MinValue`, `int.MaxValue`, 65536x65536 (width*height*4 wraps to 0 in an int) and 46341x46341 sizes; a null icon; 400 random icons (never
throws, same answer twice, letters only when no pixel reaches alpha 128, `RoundIcons.Of` returns the same object); the rule run twice and the input bytes untouched; 64 threads on one icon and 200 icons at once (one look each);
a white/grey/black logo (the hardest ordinary case) stays visible; `RoundIconLayout.Place` over every combination of 14 sizes, 17 scales (NaN, infinities, subnormal, `double.MaxValue`) and 8 tiles: finite, inside the box, never
larger than own size, aspect kept (5000 random cases).

Keys (§2): `IslandKeys.Decide` over one million random contexts and keys against 12 stated rules (no action without the keyboard, while leaving, with search open, lifted, with Ctrl/Alt/Win, in settings; a repeat acts
only for Left and Right; a move never past either end; `ConfirmRemove` only for a plain non-repeat Delete on the very pick that was asked about; a pending ask is forgotten by every key except a held Delete and the Delete that
uses it up; Tab needs two pages; Space only with media that can be controlled). The machine, with the controller's wiring rebuilt over Core (`KeyRig.cs`, written from reading `IslandController.HandleKey` and
`KeyContextOf`): every one of 16 keys x 6 modifier sets x repeat x 17 states (second row open/empty, selection in it, pending Delete, search, lifted, settings, leaving, hidden, not yet open, pill, a page with no
picks, a page of forty, Media, mid page change) throws nothing and keeps the machine sane (`SecondRowSelected` never outlives its row, a pending Delete only with the keyboard, page index in range, selection in range);
60 random storms of 400 steps (keys, text, time, the store changing under the island with and without being told, focus lost, pages replaced): a pick is removed only by a non-repeat Delete press, only the pick the last
first Delete asked about, never two per press. A held Delete never removes; the third Delete after a removal asks about the next pick, never removes it; Delete on the last pick leaves the + tile selected. Tab at
eight different gaps through a page change: every press moves exactly one page, Enter during the change opens nothing, the second row closes and forgets its selection. Items that shrink under the machine without
`ContentsChanged` recover on the next key. Every key reaching an open island with the keyboard resets the idle clock (except the case of D06). `IslandKeys.PageAfterTab` for counts 0 to 7, indexes -3 to 9 and `int.MaxValue`.
`IsOpeningText`: 27 texts that draw nothing (zero-width, RTL marks and isolates, BOM, NUL, tab, CR LF, DEL, ESC, no-break space, line separator, ideographic space, braille blank, Hangul fillers, combining marks, soft hyphen,
variation selector, tag space, mixtures) do not open search; 10 that draw do. The window's `TextFrom` drops lone surrogates before they get that far, so a half pair cannot open search.

Picks by hand (§3): 46 hostile real paths (device, UNC and `\\?\` forms, `Q:foo`, alternate streams, reserved names with dots and spaces, `..` above the drive, address, control characters, 5000 characters, a megabyte) refused
with reasons that name no path; 13 spellings of one folder (case, trailing separator, forward slashes, `.`, `..`, doubled separators, trailing dot or space) are one key and `IsSameThing`; the key does not depend on the current
culture (tr-TR tried); the profile token in other capitals, twice, in the middle, with `..`; compress and expand round trips for six odd profile folders and for none, root, relative, UNC profile folders; a path that only contains
the profile folder's name is not compressed; the picks file: 33 wrong-field and wrong-shape files unreadable (the 34th, a drive root, is a good pick and loads) with fixed words that name no path, a megabyte in each field, garbage, nesting depth 5000, schema 0, -1, "2", 2.5,
3, 99, int.MaxValue, schema 1 with version 5; v1 and v2 repo fixtures still load; hand-edited spellings are brought to the one form; the schema written follows the shape; 13 unusual legal paths round trip; a thousand picks load,
a thousand and one do not; typed addresses: 52 non-sites refused with the register's words and none of the typed text, 19 forms reduced to their host (userinfo, port, backslash, fragment and query tricks all end at the right
host), control characters, odd Unicode (never throws, never a non-ASCII host); ids are `kind:` plus 16 hex digits, never from the path, unique over 100,000 draws; names cut at a whole character; `PickItems.Mark` and
`Hue` over 20,000 odd names; a file is always bright, missing is grey and "not found", every kind has a click, missing wins over closed; a hand folder is open when a window shows its place under three spellings and
not for a subfolder, a sibling, a GUID or nothing; scenes round trip hand-made things with the token and no profile text, and a thing with a path in the wrong field makes the file unreadable.
`SettingsSession`: a locked picks file refuses all five ways to add and writes nothing; an unknown page (7 spellings) is refused and writes nothing; 21 garbage answers from the chooser add nothing and no message holds the
path, a drive, `://` or the typed text; a read-only picks file and a directory in the temporary file's place refuse without a path in the words, change nothing, raise no event, leave no temporary file, and the next add works;
the same folder, file, program and site under two spellings on another page says where it already is; nothing written to `picks.json`, `settings.json` or `pages.json` holds the profile folder or the temporary folder;
`Changed` is raised once per add; the list of programs survives odd filters (including a megabyte and a lone surrogate); programs that cannot be picks are refused with words. The cache of target answers: a probe that
throws (eight exception types) leaves the pick unanswered and is asked again, only out-of-memory escapes; a blocked probe does not lock `Presence`, `ToAsk` or `Record`; answers that change every time are reported and old
ones are asked again after 30 seconds; 16 threads on 3000 picks never grow it past 2000; only things with a place are asked, with the real path, and a token with no profile folder is never claimed missing; a file and a
folder at one place are asked separately.
The one real choosing window (`OutsidePlaceChooser`) has an owner on both calls, is gated on both, and is built with `this` in `SettingsScreen`.

## What could not be checked

- Anything WPF: `IslandController` (read, and rebuilt in `KeyRig.cs`; D07 and D08 are source-reading tests), `TileView.BuildFace` (the half pixel in D05 is arithmetic on `RoundIconLayout.Place`, not a rendering),
  `ContentsLayer`, `OverlayWindow` (read: Shift/Ctrl/Alt/Win come from `GetKeyState`, repeat from bit 30 of lParam, `WM_SYSKEYDOWN` is also handled, so Alt+F4 and Alt+Space are swallowed while the island has the keyboard).
- Whether Windows sends bit 30 as the code assumes for a key already held when the island takes the keyboard (the main key itself); Microsoft Learn was not consulted (no network). UNVERIFIED.
- Whether `GetDIBits` in `IconReader` gives straight or premultiplied alpha for the icons the shell returns; the rule and the drawing both assume straight. UNVERIFIED.
- The real disc and icon colours with real icons, the 5 second feel of keys, a second monitor, other display scaling, a program run as administrator (WORK-ORDER-10 §0 already says so).
- The behaviour of `Directory.Exists` on a dead mapped drive letter (D03 is shown with a fake probe).
- Drag handling and the second row's click paths (`DragHandler`, `SecondRow`): whether a drag that ends without a drop, or a click on a second-row tile, cancels a pending Delete. Only the keyboard path and `Wheel`/`RedrawItemsIfChanged` were read.
- The settings screens' snapshots ("no snapshot shows a path"), `selftest.json`, STATE.md and LOG.md: the self-test must not be run here.
- Whether `ship/store/privacy.md` carries the sentence about the picks file (not part of the tests).
- Doubts, read the cautious way: D15 (scenes schema) is a reading of the work order's sentence; if the owner meant the scenes file to stay at 1, close it. D13 reads EVALS C5 ("the same thing cannot be put on the island twice")
  more widely than WORK-ORDER-10's own definition ("the same thing when their paths are the same"). D08 and D07 follow §2's list literally; if the id-based ask was meant to survive a re-layout, only D07 stands.

## The main session's answer (after the fixes)

Every `Defect_` test passes, with the exceptions and adaptations below. Commits, in order: D01 (the disc of a pale flat logo darkens until its white drawing stays visible), D04 and D05 (a closed tile's grey disc steps away until its grey picture is told from it; the tile draws with `RoundIconLayout.PlaceOnPixels`, `Place` itself is unchanged because `RoundIconLayoutTests` pin it exactly centred), D06 to D08, D09 to D11, D12 to D20 and L1, D02, D03, L2 to L4.

Tests adapted, each with its reason (the assertion's intent is kept):

- D04 reads the disc of a closed tile from `look.ClosedDisc` (the fix is a new field of the look, not a change of `RoundIcons.Grey`).
- D05 calls `RoundIconLayout.PlaceOnPixels` (see above).
- D10: the test now asks that the screen and the confirmation give the same answer (the fix refuses a host that cannot be an id at the screen, with the register's words; the original asked for a 247-character host to be accepted, which would raise the limit of every id in the keys file).
- D14: a `Pick` alone cannot know the profile folder; the test goes through the session that loaded the file, which now brings a spelled-out place to the token.
- D02: `ScenePlans.For` has a fourth, optional argument, the `PickContext`; the test passes it.
- L2, L3, L4: the rules now live in `tests/Island.Tests/GuardRules.cs`, which `GuardTests` and this project both run (the project links the file), so the attack runs the guards' own code and not a copy.

Not taken:

- **D15** (the scenes file saying schema 2): `SceneTests` and `UpgradeTests` pin a scenes file with `"version": 2` as a newer file that is left untouched; the work order names only the picks line of `UpgradeTests` as changeable. The `Defect_` test was removed from this project with this note. An older Island that reads a scenes file holding a hand-made thing finds a folder with no known-folder name and leaves the file as it is (the same safe end). Owner decision, listed in STATE.md.

The `Add` sink of the path guard is limited to lists named like a report or a log (`report.Add(pick.Location)`), because a plain `.Add` is also how a set or a list is filled in code that is allowed to handle a place.
