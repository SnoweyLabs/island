# c7-scenes report (WORK-ORDER-7.md section 5, logic only)

Branch `c7-scenes`, from 6033be4. No existing file was changed. No outside call, no window, no Smoke program.

## 1. What was built

All in namespace `Island.Core` (the folders of Island.Core do not change namespaces).

- `src/Island.Core/Scenes/Scene.cs`
  - `Scene(Id, Name, Things, Key)`: things are `Pick` copies (page set to `Scenes.ThingPage` = "scene"), so a scene owns its identities. `Key` is a `HotkeyCombo?` that is only carried.
  - `SceneEdit(Store, Scene, Refusal)`, `Scenes` (limits: `MaxScenes` 100, `MaxThingsPerScene` 200, `MaxNameLength` 24, `ThingFrom(pick)`), `SceneText` (refusal sentences for the editor).
- `src/Island.Core/Scenes/SceneStore.cs`: immutable-style store. `Create`, `Rename`, `Delete`, `SetKey`, `SetThings`, `AddThing`, `RemoveThing`, `MoveThing`, `ById`, `WithKeys`, `Empty`. Names: trimmed, whitespace runs made one space (same rule as `PageStore.Tidy`, whose method is private: the one-line regex is repeated, not copied code), non-empty, at most 24 characters, no duplicate ignoring case, no control, format (RTL override, zero-width, BOM), private-use or lone-surrogate characters.
- `src/Island.Core/Scenes/SceneStoreFile.cs`: `Load(path)`, `Parse(json)`, `Save(path)`, `ToJson()`, `SceneStoreLoad`, `SceneStoreStatus`. Version field (1). Never throws. Things are written with `PickStore.ToJson` and read with `PickStore.Parse` (one reader and one definition of "storable", so a path can never be kept).
- `src/Island.Core/Scenes/ScenePlan.cs`
  - `ScenePlans.For(scene, OpenSnapshot, Func<Pick,bool> isInstalled)` gives a `ScenePlan` of `SceneStep(Thing, Action, Click)`. `Action` is `Open`, `BringForward` or `Skip`; there is no member that closes or places. `Click` is exactly `PickStates.Plan(...)` (with a fresh `ClickCycler`, so always the newest window or tab): the app carries it out the way `PickPages.Carry` does.
  - `ScenePlans.InstalledIn(IReadOnlyList<InstalledProgram>)` builds the predicate (exe name or package family, ignoring case; folders and sites are always "installed").
  - `ScenePlan.OpenedCount`, `OpenedText` ("<n> opened"), `SkippedNames`, `PartMissing`.
  - `SceneRefusals.PartMissing(sceneName, skippedNames)` returns the existing `Refusal` type with code `SCENE_PART_MISSING` (null when none skipped). `.Message` is the register text word for word. Capped to 250 characters: names are listed while they fit, then "and N more"; a single too-long name is cut with an ellipsis; hidden/control characters are removed from names; the pieces are inserted, never placeholder-replaced, so a name that looks like a placeholder is safe.
- `src/Island.Core/Scenes/HeldKeyGuard.cs`: `Accept(key, nowMs)` is true for a new press and false for a repeat (a message within 1100 ms of the previous one for the same key; a hold keeps itself swallowed). `Released(key)` makes the next message a new press. The clock is a value handed in. The app already registers with MOD_NOREPEAT, so this is the second line of defence and the only one that can be tested.
- `tests/Island.Tests/Scenes/SceneTests.cs`: 80 tests.

## 2. Commands and results

- `"C:\Program Files\dotnet\dotnet.exe" test .worktrees\c7-scenes\tests\Island.Tests` : Passed 582, Failed 0 (502 existing + 80 mine; the guard tests that scan `src/` stay green).
- `... test --no-build --filter FullyQualifiedName~Island.Tests.SceneTests` : Passed 80, Failed 0.
- Only Island.Core and Island.Tests were built. Island.App was never built or run.

## 3. What is proven, by test

- `SceneTests.Closed_Things_Open_And_Open_Things_Come_Forward`: mixed program/folder/site scene; order kept; open things give BringForward to the newest window or tab; closed give Open; `OpenedCount` / "2 opened".
- `SceneTests.Nothing_Is_Ever_Closed`: no `SceneAction`, `ClickKind` or `IOutsideActions` member contains "Close"; every step over several snapshots (also with nothing installed) is one of Open / BringForward / Skip with a Start / OpenFolder / OpenSite / BringForward click.
- `SceneTests.A_Missing_Thing_Is_Skipped_And_Named`: skipped names in order, the rest still opens, refusal message equals the register text exactly. Also `A_Program_Is_Installed_By_Exe_Or_By_Package_Family`.
- `SceneTests.Scenes_Round_Trip`: names, order, kinds, exe, package family, folder, host, key, empty scene, no `.tmp` left.
- `SceneTests.A_Removed_Pick_Stays_In_Its_Scene`: removed from a `PickStore`, the scene keeps its copy, still plans Open, still survives save and load.
- `SceneTests.A_Held_Key_Runs_It_Once`: a 10-second hold with repeats gives exactly one run; release then press runs again; a pause longer than the gap runs again.
- Adversarial: open and uninstalled; 200 things; 201 refused; the same thing twice (store and a hand-built scene); a scene built by hand with 400+ things; no add-on for a site; a click-cycled window never changes the plan; names with null, escape, RTL override and mark, zero-width space, BOM, lone surrogate, private use (all refused), 10,000 characters (refused, not cut), whitespace-only, duplicate ignoring case and spacing; message under the balloon limit with 200 names, 200-character names, a 5000-character name, surrogate pairs, placeholder-looking names and RTL characters in names; hostile JSON (garbage, `[]`, `null`, empty, wrong shapes, path in a thing, URL as host, repeated id, repeated name, hidden character in a name, version 0 / "one" / 2 / 99, 5000-deep nesting, a file over the size limit): all `Unreadable`, file bytes and write time untouched; 10,000 scenes cut to 100 and 100,000 things cut to 200 with a count-only note; an invalid or doubly used key is dropped and the scene kept; a folder where the file should be; an unwritable save path; `Save` refuses a path-like thing; no scene shipped (a missing file gives an empty list and creates nothing).

## 4. What is not proven

- That a real run opens or raises anything: this piece has no outside call. The self-test stage of the work order (own program plus a missing one, gate asked once, one skipped) is for the main session.
- Whether Windows lets more than one window through to the foreground (the work order says it may flash instead).
- The 1100 ms repeat gap against a real keyboard: it is derived from Windows' documented repeat settings, not measured. Cost: a real double press within 1.1 s without a release notice is read as one press.
- No eyes on any text. The settings editor and tray menu are not here.

## 5. Requests to the main session (I did not change existing files)

1. `Refusals.cs`: register `SceneRefusals.PartMissingTemplate` in `Refusals.All` (the `RefusalTests` may check three-part shape; the template has all three parts and the code `SCENE_PART_MISSING`). A filled message comes from `SceneRefusals.PartMissing(name, skipped)`.
2. Settings editor (existing files, not mine) needs from this piece:
   - `KeybindEditorTests.A_Scene_Key_Obeys_The_Same_Rules`: the editor validates with `HotkeyCombo.TryParse` and the K5 duplicate check across pages, picks and `store.WithKeys`; then calls `store.SetKey(sceneId, combo)` (it carries any value, it never judges) and shows `SceneEdit.Refusal`. Rows for the keybind list: `SceneStore.Items` (Id, Name, Key).
   - `KeybindEditorTests.Deleting_A_Scene_Releases_Its_Key`: `var after = store.Delete(id)`; the keys to release are those of `store.WithKeys` that are not in `after.WithKeys` (compare by scene id); `HeldKeyGuard.Released(id)` is optional. Scene ids are reused when free (first free `scene-N`, as pages do), so the key must be released on delete.
   - Yes/no question text for delete is not written here (the editor owns screen text).
   - Tick-the-things screen: `SetThings(sceneId, picks)` takes the ticked picks in the order wanted (whole list replaced, duplicates collapsed, all-or-nothing refusal); `MoveThing`, `RemoveThing`, `AddThing` for finer edits.
3. A pick removed from the island needs nothing from the scenes: they hold copies.
4. `PageStore.Tidy` is private; I repeated its one-line regex. If `Tidy` is ever made shared, both can use it.

## 6. How to wire it

- Own one `SceneStore` (immutable: replace the field on every edit, then `Save(path)`). Path: next to picks.json, for example `%APPDATA%\Island\scenes.json`. At start: `var load = SceneStore.Load(path)`. If `Status` is `Unreadable`, run on `load.Store` (empty) and do NOT `Save` over the file (the same discipline as picks and pages) and show `load.Detail`. If `Loaded` with a non-null `Detail`, tell the user once (counts only).
- Own one `HeldKeyGuard`, used on the UI thread. On a scene key message: `if (!guard.Accept(scene.Id, Environment.TickCount64)) return;` (a monotonic clock; `Stopwatch` ms also works).
- Run a scene (UI thread, never blocking): `var plan = ScenePlans.For(scene, world.Snapshot(), ScenePlans.InstalledIn(catalog.Installed));` then, for each step in order, skip `Skip` and carry `step.Click` out exactly as `PickPages.Carry` does (site BringForward: find the tab with `LastActiveOrder == Target` and `TabControl.Activate`; otherwise `Outside.BringForward`, `Outside.StartProgram(step.Thing)`, `OpenFolder(step.Thing.KnownFolder!)`, `OpenSite(step.Thing.Host!)`). The island's text block: scene name and `plan.OpenedText`. If `plan.PartMissing is { } refusal` show `refusal.Message` as the tray notification, once. Pass in a snapshot taken at the moment of the press; the plan does not look at the machine itself.
- The things carry pick ids, so the existing icon code can draw them if a screen wants to.

## Doubts written down (cautious reading taken)

- Open but not installed: brought forward (it is plainly there). The brief listed Skip for "no longer installed or known"; skipping something that is on screen seemed worse.
- A site with no add-on connected cannot be told open or closed, so it is opened, as a click would. A scene run twice with no add-on opens the site twice.
- A file with more than 100 scenes or 200 things per scene loads with the excess dropped (as the brief asked), so the next save loses them; structural faults make the file unreadable instead (as PickStore does), including repeated things in one scene.
- Name limit of 24 matches the page-name limit; the brief only said "bounded".
