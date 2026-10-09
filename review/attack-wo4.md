# Attack on WORK-ORDER-4 (ATTACK4, night of 2026-10-06)

Adversarial pass over what WO4 added: the island's side of the add-on protocol (`TabMessages`, `TabModel`,
`TabBridge`, `Handshake`), the + list (`PlusList`), pick edits (`PickStore`), the settings logic (`PageStore`,
`SettingsSession`, `KeybindEditor`, `PicksOnIsland`) and `IslandMachine` with pages changing under it.

Branch `attack4`, made from main `a34d94c`. Nothing under `src/`, `tools/`, `extension/` or the existing tests was
changed. The tests live in `tests/Island.Attack4.Tests/` (not in `Island.sln`); every one of them fails, each because
of the defect it names. Bridge tests use a `TabBridge` on a free loopback port of their own (never 47653-47657) and the
`PretendAddon`; no browser, no window, no real program, made-up names only.

Commands and results:
- `dotnet test tests/Island.Attack4.Tests`: **14 failed, 0 passed** (all failures are the defects below).
- `dotnet test tests/Island.Tests` (untouched): 496 passed, 0 failed.

## Defects

Severity is my judgement: HIGH = Dan sees it in normal use; MEDIUM = plausible in normal use or loses data/keys
silently; LOW = needs an odd input or only bends a stated rule.

### 1. A tab title cut inside an emoji throws out of `ParseAddon` and kills the connection (HIGH)
- **Input:** a `tab` (or `snapshot`, or `media`) frame whose title ends in a lone surrogate, e.g.
  `{"type":"tab","tab":{"id":11,"windowId":1,"title":"aaa…(199 a)\ud83d","host":"youtube.com","audible":false,"active":true}}`;
  also `{"type":"\ud800"}`. This is what the real add-on sends: `tabObject` cuts titles with `title.slice(0, 200)`
  (UTF-16 units), which splits an emoji at position 200, and `JSON.stringify` writes the half as `\ud83d`.
  YouTube titles with emoji are common.
- **What happens:** `JsonElement.GetString()` throws `InvalidOperationException` ("incomplete UTF-16"). `TryOpen`
  catches only `JsonException`, so `ParseAddon` throws; `TabBridge.ReadLoop`/`Serve` do not catch
  `InvalidOperationException`; the connection ends uncounted (no bad-frame count), all of the profile's tabs vanish,
  and the exception is lost in an unobserved task. Because the add-on's next `snapshot` (on every reconnect) carries the
  same title, the island never shows any tab: a reconnect loop every 5 s.
- **Should:** `ParseAddon` never throws (its own contract). Read strings so that a lone surrogate becomes U+FFFD (for
  example catch `InvalidOperationException` around `GetString` and fall back to reading `GetRawText()` / replacing), and
  accept the tab. Also on the add-on side (request, not mine to change): cut titles on a code-point boundary.
  Belt and braces: `Serve` should catch any exception from one frame without ending the app's view of the connection.
- **Tests:** `TabAttackTests.ParseAddon_Throws_On_A_Title_Cut_Inside_An_Emoji`,
  `TabAttackTests.Bridge_Drops_The_Connection_On_A_Title_Cut_Inside_An_Emoji`.

### 2. The `$` of the id rules lets a trailing newline through (LOW)
- **Input:** `hello` with `"profile":"p1\n"`; a `pages.json` page with `"id":"games\n"`.
- **What happens:** both are accepted. .NET's `$` matches before a final `\n`, so `^[A-Za-z0-9_-]{1,64}$` and
  `^[a-z0-9-]{1,40}$` pass a value ending in a newline. A second "profile" `p1\n` stands beside `p1` (it does not replace
  it, so a reconnecting add-on with a slightly different value would duplicate every tab), and tab keys become `p1\n:11`.
- **Should:** refuse. Use `\z` instead of `$` (or `RegexOptions.None` with `\A…\z`). The add-on's JS rule is fine (JS `$`
  does not match before a newline).
- **Tests:** `TabAttackTests.Hello_With_A_Newline_After_The_Profile_Is_Accepted`,
  `SettingsAttackTests.Pages_File_With_A_Newline_After_A_Page_Id_Is_Loaded`.

### 3. The + list offers sites that can never be added (MEDIUM)
- **Input:** tabs whose host is `localhost` (from `http://localhost:3000`) or `[::1]` (from `http://[::1]/`); also any
  host with a port from a hostile sender.
- **What happens:** `PlusList.Candidates` lists `site:localhost` and `site:[::1]`, but `ToPick` returns null for both
  (`Pick.IsStorable` wants a dot and no colon). `IslandPanels.AddEntry` drops a null pick silently: the row's add control
  does nothing. Program rows are already filtered with `entry.ToPick("x") is null`; site rows are not.
- **Should:** skip a site entry whose `ToPick` is null, as for programs (or make such hosts storable, a decision).
- **Test:** `PickAttackTests.Plus_List_Offers_A_Localhost_Tab_That_Can_Never_Be_Added`.

### 4. One packaged program appears twice in the + list when one of its windows is minimised (MEDIUM)
- **Input:** windows `(1, "alpha.exe", "Alpha.App_1")` and `(2, null, "Alpha.App_1")`. `WindowReader.ProgramOf` really
  produces the second shape: a minimised UWP frame window has no child, so only the package family is known.
- **What happens:** `PlusList` groups by the pair `(exe, package)`, so the same program gives two rows ("open" each,
  keys `program:alpha.exe` and `program:alpha.app_1`), while its pick on the island would count 2 windows (`PickStates`
  matches exe OR package).
- **Should:** one row with Count 2: group windows that share an exe or a package family.
- **Test:** `PickAttackTests.Plus_List_Shows_One_Packaged_Program_Twice_When_One_Of_Its_Windows_Is_Minimised`.

### 5. The machine keeps a selection beyond the end of the page (MEDIUM)
- **Input A:** Apps open with 5 picks, `ItemClick(4)`, dismiss; while hidden 3 picks are switched off (settings screen),
  `ContentsChanged`; `MainKey`. **Input B:** custom page `page-1` with 6 items, `ItemClick(5)`, dismiss; while hidden
  `SetPages(Pages.BuiltIn)` (the page deleted); `ShowHideKey`.
- **What happens:** `SelectedItem` stays 4 (2 items) / 5 (Media, 2 items). `Summon` resets the selection only when
  `ContentsPageId != PageId`, and `SetPages`/`ContentsChanged` outside `Open` do not touch it. The existing invariant
  (Island.Tests storm tests: `SelectedItem` within the items) is broken. The app's `ContentsLayer.Build` clamps, so Dan sees
  the *last* tile selected instead of the first. A random storm with `SetPages` and `ContentsChanged` added gave 1243 out-of-range states over 8 seeds.
- **Should:** clamp (or reset to 0) the selection whenever the contents page or its item count may have changed:
  in `SetPages` when `ContentsPageId` is replaced, and in `Summon`/`Expand` against the current item count.
- **Tests:** `MachineAttackTests.Selection_Is_Out_Of_Range_After_Picks_Are_Removed_While_The_Island_Is_Hidden`,
  `MachineAttackTests.Selection_Is_Out_Of_Range_After_The_Selected_Page_Is_Removed_While_The_Island_Is_Hidden`.

### 6. `PickStore.Move` accepts a page id that cannot be stored, and from then on nothing is saved (LOW)
- **Input:** `store.Move("program:alpha", "")` (likewise `"a/b"`, `"x:y"`), then any `Add`.
- **What happens:** the moved pick is not storable, so `Save` refuses the whole file from then on; `PickBook.Apply`
  ignores `Save`'s answer, so every later add, remove and move is silently lost at the next start. `Add` checks
  `IsStorable`, `Move` does not. (A move to a well-formed page id that does not exist is accepted too: the pick vanishes
  from every page but still blocks re-adding the same thing, because `Add` refuses its id and the + list hides it. The
  island's menu only offers real pages, so I did not count that separately.)
- **Should:** `Move` returns the store unchanged when the moved pick would not be storable (and ideally when the page
  is not a known page); `PickBook` should surface a failed save.
- **Test:** `PickAttackTests.Move_To_An_Empty_Page_Id_Makes_Every_Later_Save_Fail`.

### 7. `PickStore.Save` writes duplicates that `Load` refuses (LOW)
- **Input:** `PickStore.Empty.ReplacePage("browser", [site, site])` with the same `site:example.org` twice, then `Save`,
  then `Load`.
- **What happens:** `Save` returns true; `Load` says Unreadable ("The same pick appears twice."); the app then starts
  with no picks at all (it leaves the file alone). `ReplacePage` keeps duplicates inside the replacement and `Save`/the
  constructor never check ids. Today's caller (`PicksOnIsland.Restore` via `StarterPicks.Build`) does not produce
  duplicates, so this needs a new caller to bite.
- **Should:** `Save` never writes a file its own `Parse` rejects: dedupe by id in `ReplacePage` (and refuse duplicates in
  `Save`).
- **Test:** `PickAttackTests.Save_Writes_A_Pick_Twice_And_The_Next_Start_Cannot_Read_The_File`.

### 8. A snapshot that shows a known tab newly active does not make it the newest (LOW)
- **Input:** `snapshot [1 active, 2 inactive]`, then `snapshot [1 inactive, 2 active]` on the same connection.
- **What happens:** tab 1 stays newest: `TabModel.Snapshot` keeps a known tab's old order, even when it became active.
  PROTOCOL.md: a tab gets the next number "when it is created or becomes active". A click on the site goes to the wrong
  tab. Today the island never sends `resync`, so the add-on snapshots only on welcome (a fresh connection, all tabs
  new); it matters once `resync` is used.
- **Should:** in a snapshot, a known tab that is active now and was not before gets `++_order` (after the others).
- **Test:** `TabAttackTests.Snapshot_That_Shows_A_Known_Tab_Newly_Active_Does_Not_Make_It_Newest`.

### 9. The 256 KB frame limit and the 2000-tab limit contradict each other (MEDIUM)
- **Input:** one `snapshot` of 1000 tabs with 200-character titles on `example.org` (~285 KB).
- **What happens:** the frame is over 256 KB: the island closes the connection (Oversize 1) and shows no tabs. The add-on
  builds the snapshot without any size check and redials into the same frame every 5 s. With real titles (60-200
  characters, emoji and diacritics take 2-4 bytes) the ceiling is somewhere around 700-1500 tabs, far below the 2000 the
  protocol promises. Measured: 2000 tabs with empty titles are already 159 KB.
- **Should:** a protocol decision for the main session: either the add-on sends the snapshot in parts (e.g. a
  `snapshot` with `"part"`/`"last"` fields) and caps a frame under 256 KB, or the island allows a larger frame for a
  snapshot only (2000 × ~1 KB). Island-side, at minimum: never close a connection in a way the add-on will repeat forever.
- **Test:** `TabAttackTests.Snapshot_Of_1000_Tabs_With_Full_Titles_Is_Over_The_Frame_Limit_And_The_Island_Shows_No_Tabs`.

### 10. Deleting a page while settings.json is read-only says "done", keeps its key, and the next page inherits it (MEDIUM)
- **Input:** `CreatePage("Games", "#12AB34", Ctrl+Alt+G)`; make `settings.json` read-only; `DeletePage("page-1")`;
  `CreatePage("Other", "#AB1234")`.
- **What happens:** picks and pages are saved, then `DropPageKey` cannot save settings and ignores it: `DeletePage`
  returns Ok, Windows still holds Ctrl+Alt+G, `Settings` still lists it for `page-1`. `NextId` reuses the free id
  `page-1`, so "Other" silently gets the deleted page's key.
- **Should:** either refuse the delete up front when settings cannot be written (check before touching picks/pages), or
  at least release the key in Windows, drop it from the in-memory settings, and report "settings.json could not be
  saved". Never reuse a page id while a key for it may still be on record (or clear any stale key when creating a page).
- **Test:** `SettingsAttackTests.Deleting_A_Page_While_Settings_Is_Read_Only_Says_Done_But_Keeps_Its_Key_For_The_Next_New_Page`.

### 11. Two page names that differ only by inner spaces are both accepted (LOW)
- **Input:** `Create("Side Work")`, then `Create("Side  Work")` (two spaces). Same with a zero-width space
  (`"Side​Work"`).
- **What happens:** accepted: two pages that look identical on the island. Case and outer spaces are caught.
- **Should:** compare names with runs of white space collapsed and invisible characters removed (EVALS P6, "a name
  already used").
- **Test:** `SettingsAttackTests.Page_Name_That_Differs_Only_By_A_Double_Space_Is_Accepted_As_A_Second_Page`.

## What I could not break

Each was tried with a probe test (removed again, as the brief says not to pad) or read line by line.
- **Frame sizes:** 255 KB and exactly 256 KB are answered; 256 KB + 1 closes the connection (Oversize). A frame header
  claiming 64 MB is never buffered: the island reads at most 256 KB, or the hello deadline ends it.
- **Handshake:** wrong Origin and no Origin get 403 (counted as Refused); the right pretend Origin with `/island/`,
  `/Island`, `/island?x=1`, `/` gets 404; a half-open request line and a byte-every-200-ms slowloris are cut at the 3 s
  hello deadline; a connection closed mid-frame ends quietly.
- **Limits:** eight announced connections at once, the ninth turned away (TurnedAway 1); after they close a new one is
  accepted; a second `hello` with the same profile replaces and closes the older connection; a repeated `hello` on one
  connection is counted as a bad frame; ten bad frames in a row close it.
- **Flood:** 5000 `tab-activated` frames against a 2000-tab snapshot: answered in 3.3 s, all 2000 tabs kept,
  `LastActiveOrder` unique across the model. Deep nesting (MaxDepth 8), wrong types, numbers that are not integers,
  negative ids, NaN-like/huge seconds, `media` before its tab, `tab-removed` twice, `tab-activated`/`icon` for unknown
  ids: all ignored without harm.
- **TabModel invariants:** `LastActiveOrder` unique across connections; icon, media and play order dropped when a tab's
  host changes; nothing written anywhere (no file API in Tabs/ or Bridge/).
- **Hosts:** trailing dots, upper case, a leading `www.` are normalized identically on both sides; `music.youtube.com`
  stays apart from `youtube.com`.
- **PickStore:** `Add` refuses the same id twice; `Remove` of an unknown id is harmless; `ReplacePage` leaves other
  pages and skips starter picks Dan moved to another page.
- **PageStore/SettingsSession:** ten pages refused (nine is the limit, file and editor); case-only duplicates refused;
  colours `#FFF`, `12345G`, `#12345`, empty refused, `abcdef` and `#abcdef` accepted as `#ABCDEF`; built-in pages cannot
  be deleted; a key held inside the app or by "another program" is refused with the old key kept; a refused key during
  `CreatePage` makes no page; unreadable files lock their area (no overwrite).
- **IslandMachine, M3:** a seeded storm (8 seeds × 24 000 steps) with `SetPages` adding/removing a page and
  `ContentsChanged` with 0-11 items mixed into every key: no spring ever moved except by its own step. The only broken
  invariant was the selection (defect 5).

## Doubts about app code I could not reach

`Island.App` classes are internal and WPF; I did not start the app. What I would try:
- **`PickPages.TabIconOf` cache key `"{tabKey}:{png.Length}"`.** A tab that moves to another site whose stored icon has
  the same byte length keeps showing the old site's decoded icon (EVALS I7), and the cache only grows (every tab key and
  length ever seen, decoded BGRA kept for the whole session). I would key by a hash of the bytes and drop entries for
  tabs that are gone.
- **`TabBridge.Changed` on a timer thread.** An exception in any subscriber (`AppWorld.Raise` → `PickPages.Invalidate`
  …) is unhandled on a pool thread and ends the process. I would wrap the invoke.
- **`IslandPanels` while windows open and close.** `JumpTo` uses `_entries` captured when the list was drawn; if the
  list refreshed between hover and click (a window closed), the row index points at another program. `AddEntry` adds to
  `Machine.ContentsPage`, which may already be switching pages (the `Tick` close happens a frame later). I would click a
  row in the same frame as `RefreshList` and as a page key.
- **`IslandPanels.OpenMenu` move targets** are captured when the menu opens; a page deleted from the settings screen
  while a menu is open would be offered still (then defect 6's second form). Probably unreachable if the settings screen
  always dismisses the island first; worth one self-test check.
- **`tab-activated` windowId is ignored** (`TabModel.Activate` keeps the stored window). After a tab is dragged to
  another window, the add-on's `onAttached` upsert arrives through an async `chrome.tabs.get`, so it can come after the
  `tab-activated`; until then `OutsideTabs.Activate` would focus the old window. Using the message's windowId would close
  that gap.
- **Memory a hostile local program can make the island hold:** 8 connections × 2000 tabs × 64 KB icon ≈ 1 GB. Bounded,
  but the comment says the limit exists so memory cannot grow "without bound"; a global cap on icon bytes would be safer.
- **Settings screen (`Island.SettingsUi`)** builds controls that need an STA/WPF dispatcher; I did not lay them out.
  Things to check there: a page deleted while its row has focus, Esc during the closing spring, a second key press while
  the registrar call of the first is still running.
