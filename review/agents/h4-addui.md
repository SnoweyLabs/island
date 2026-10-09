# H4 add-ui: the screen part of WO10 section 3 (report)

State: done (builds; seen only as off-screen renders, never on a real screen, see 4).

## 1. What was built (territory: src/Island.SettingsUi only)
- `AddByHand.cs` (new): the panel and the "Add..." toggle. Public-in-assembly API: `AddByHand.Toggle(host, page)` (the head-row button), `AddByHand.Build(host, page)` (the panel), `AddByHand.PanelOf(pageId)` = `"add:" + pageId`, `AddByHand.PageOf(openPanel)`, `AddByHand.Close(host, pageId)`.
- `PicksSection.cs`: each page card's head row now holds "Add..." then "Restore starter list" (one StackPanel in column 1); when `host.OpenPanel == "add:<pageId>"` the panel is added under the head row.
- `SettingsView.cs`: `HandleEscape()` closes an open Add panel (after the overlay, key capture and EditHooks checks) and `WantsEscape` is true while one is open. `ISectionHost` is unchanged.

Panel (all four parts shown at once, no sub-panels; one `OpenPanel` value per page):
- "A program": a field (focus key `add:filter:<id>`, access name "Type to narrow the list of programs") narrowing a scrollable list (max height 8 rows of 30 + padding) of buttons showing `InstalledProgram.Name` only; a row adds through `session.AddProgramFromList`. At most 100 rows are drawn (`MaxRowsBuilt`), then a line "N more. Type to narrow the list." Below: "Browse..." (`add:browse:<id>`) -> `session.AddProgramByBrowsing`. Down or Enter in the field moves to the first row; Enter with exactly one match adds it. The list is one Tab stop (Tab navigation "Once"); arrows move between rows.
- "A folder": "Choose a folder..." (`add:folder:<id>`) -> `session.AddFolder`.
- "A website": field (`add:site:<id>`), a live line under it, and "Add" (`add:site-add:<id>`, disabled until `UnderstandSite(...).Ok`). Line: hint "Type a site's name, for example example.org." when empty; "Island will keep: <host>" when understood; the refusal's `Why` + `NextAction` in the warn colour otherwise (not the whole `Message`, whose first sentence says "nothing was added"). Enter in the field adds when understood. The typed text is never shown back (the field's help text holds only the line) and is read only to call the session.
- "A file": "Choose a file..." (`add:file:<id>`) -> `session.AddFile`.
- "Close" link (`add:close:<id>`). Esc closes too; focus goes back to the page's "Add..." button.
- After an add: Cancelled -> nothing; Added -> panel closed, `host.Report(new SessionResult(true, null, Message))` (a message of an added pick becomes the warning), section redrawn, keyboard to the new chip (`chip:<pickId>`); not Added -> `host.Report(new SessionResult(false, Message))` (refusal / "already on the island, on the page X" show as the section notice), panel stays open.

How the main session snapshots it: `host.OpenPanel = "add:" + pageId` (for example `"add:" + PageIds.Media`) on `SettingsSection.OnTheIsland`, then `Refresh()`. That is the only value; the panel shows the list with the typed text of the two fields empty. To show the filtered list or the site understood/refused, set `Text` of the TextBox found by `FocusKey` `add:filter:<id>` / `add:site:<id>` (the live line follows `TextChanged`). Nothing else keys off `OpenPanel` in this section. Values of other sections (`<pageId>` in Pages, scene action ids in Scenes) do not start with "add:".

## 2. Commands and results
- `dotnet build src\Island.SettingsUi` : 0 errors, 0 warnings (Build succeeded).
- `dotnet test tests\Island.Tests` : 1378 passed, 1 failed, 1379 total. The failure is `ExtensionGuardTests.The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme`, nothing to do with this piece (I touched no file it reads); not investigated.
- A scratch copy of `src/Island.SettingsUi.Smoke` (in the session scratchpad, outside the worktree, never committed) that drives the panel off screen with `PretendChooser` and invented paths under `Q:\`: 11 of 12 checks passed; the one miss was a wrong expectation in my scratch script. It renders to PNG with RenderTargetBitmap, no window. Confirmed: the panel opens with 8 program rows + scrollbar, the filter narrows ("sp" -> Spotify), "hello" shows the refusal in the warn colour with "Add" disabled, "https://Www.Example.NET/page?x=1" shows "Island will keep: example.net" and the typed text appears nowhere else, "Add" puts one site pick on the page and closes the panel, a pretend folder is added, adding it again shows "Beta is already on the island, on the page Media." and keeps the panel open, a pretend file is added, Esc closes the panel and a second Esc is the host's.
- The real `Island.SettingsUi.Smoke` crashes BEFORE and AFTER my change at `TabStops` (`SettingsView.Rebuild`, IndexOutOfRange, because it sets `SettingsSection.Welcome` on the full view): pre-existing, confirmed with my change stashed. Not in my territory.

## 3. Proven
Build is clean; behaviour above by the scratch run (not a committed test: tests of other folders are outside my territory). Names only: the list uses `InstalledProgram.Name`; no path string is built anywhere in the files I touched.

## 4. Not proven / doubts
- I did look at the off-screen PNGs of the panel (offscreen render of the view, not a real window), but never saw it live: hover, focus ring, the nested wheel hand-over and Tab/arrow order inside the list are written by reading WPF's rules, not tried.
- The notice (refusal, "already on the island") is the section's single notice line at the bottom of the section, as asked; with several pages it can be below the fold after a refusal. A copy of it inside the panel would help but would double the text for any check that counts it.
- Rebuilding after a refused add clears the two fields (the typed address too).
- The 100-row cap is mine (Claude) to keep keystrokes fast; no test pins `MaxRowsBuilt`, `RowsShown`, `RowHeight`.
- The panel is also offered in the first-start setup (same section); there `session.Chooser` may be null, and the session's own message says so.
- "Add..." uses `Look.HintSize` link text like "Restore starter list"; panel height is about 400 px with the list full.

## 5. Requests to the main session
- Set `session.Chooser` (and `HandContextSource`) before the screen opens, or every "Browse...", folder and file button answers that no window is available.
- `src/Island.SettingsUi.Smoke` (pre-existing crash on `Welcome`) and any snapshot/self-test stage for the Add panel are the main session's (outside my territory).

## 6. How to wire it in
Nothing beyond the Chooser: `PicksSection` and `SettingsView` already call the new code.
