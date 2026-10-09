# B1 programs: report

Branch `b1-programs`, made from `4c852a1`. Piece: WORK-ORDER-3 section 2 (what is open: programs and windows) plus the real outside actions.
No window was shown, `Island.App` was never started, nothing was launched, closed or brought forward. No NuGet package was added.

## 1. What was built

Core, pure logic (`src/Island.Core/Programs/`):
- `IconChoice.cs`: `IconChoice.Choose(available, drawnPixels, seenOnThisComputer)` returns an `IconPick` (image, `UseLetters`, `Undersized`). Rules: unseen starter gives letters (I9); no usable picture gives letters (I4); otherwise the widest picture, flagged `Undersized` when it is smaller than the drawn size and then used as is (I5). `IconChoice.IsUsable` rejects null, zero size, wrong byte count and all-transparent pictures. `IconChoice.TwoLetterMark(name)`: no helper existed in Core (`Item.Mark` is a hand-typed string in the placeholders), so it was added here: "YouTube Music" gives "YM", "Spotify" gives "Sp", nothing usable gives "?".
- `IconCompare.cs`: mean per-channel difference between two icons of any size (nearest-pixel resample), threshold 12 (chosen by B1). Used to recognise the generic file icon.
- `WindowRules.cs`: `WindowFacts` (plain facts about a window) and `WindowRules.IsListed(facts)`: visible; (app-window style) OR (no owner AND not tool window); not marked deleted from the task list; class is not `Windows.UI.Core.CoreWindow`; not cloaked, or cloaked only by the shell because it sits on another virtual desktop; non-empty title; not tiny (50x30, chosen by B1; window-switcher uses 120x90, which would hide small real tools). UWP: `IsUwpFrameHost`, `PickUwpChild` (first child whose class starts "Windows.UI.Core."), `PackageFamilyOf(aumid)`.

Windows readers (`src/Island.Sources.Programs/`, `net10.0-windows10.0.19041.0`, references Island.Core only):
- `WindowLister : IOpenWindowSource, IDisposable`: one background thread with a message loop. Five `SetWinEventHook` hooks (foreground, show, hide, destroy, name change; whole windows only). An event starts one 150 ms throttle timer (not a restarting debounce, so a stream of events cannot postpone the read for ever); when it fires there is one full `EnumWindows` pass; a 5 s reconcile timer re-reads anyway. `Changed` is raised on that thread, only when the list differs. `WindowReader.cs` holds the pass itself (exe file NAME only, package family from the process, UWP resolution through the child window, or through the window's app id when the child is missing, i.e. minimised). `HooksInstalled` and `WaitForFirstRead` exist for the console check.
- `InstalledProgramCatalog : IProgramCatalog`: one STA background thread, `Refresh()`, `Loaded` event, `WaitForLoad`. Source: `shell:AppsFolder` (IShellItem enumeration; covers Start-menu programs and Store apps), then executables registered under App Paths (HKLM and HKCU) that the first source did not already give. Exe name of a desktop entry comes from its shortcut target (System.Link.TargetParsingPath) or the "{known folder id}\path" form of its id; a shortcut to a shared `Update.exe` (Squirrel installers) is resolved to the program in the newest `app-*` folder, or to null (never "Update.exe"). Packaged entries carry the package family (from the "Family!App" id) and no exe name. Launch target: `shell:AppsFolder\<id>` for AppsFolder entries, the exe path for App Paths entries; memory only.
- `ProgramIcons : IIconSource, IDisposable`: all reading on one STA worker thread; callers wait for it (8 s limit), so the app must call from a background thread. Order: catalog entry by exe name or package family (else the exe looked up under the Windows and System32 folders) then the shell image factory on the launch target (`IShellItemImageFactory`, icon only) at 256, 128, 64, 48, 32, 16 until one comes; then `SHGetFileInfo` (retried 3 times) for plain files. `FolderIcon(knownFolder)` does the same on the folder's path. `FileIcon(path)` is for checks. `IsGenericFileIcon(image)` compares with the system image list's icon for a made-up extension and for a bare `.exe`, at 256, 48 and 32 pixels. Results are cached in memory (a miss is not cached, because the catalog may still be loading). Bitmaps come back as straight (not premultiplied) BGRA, top row first.
- `OutsideActions : IOutsideActions` in `OutsideActions.cs`, constructor argument `IProgramCatalog`. Every method asks `OutsideGate.Current.Allow(...)` first (kinds: `StartProgram`, `OpenFolder`, `OpenAddress`, `BringForward` with the owning process id) and returns false without acting when refused. `StartProgram` resolves the pick in the catalog (exe name, then package family) and hands the launch target to the shell; `OpenFolder` resolves the known folder and opens it; `OpenSite` hands `SiteAddress.For(host)` to the default browser; `BringForward` restores if minimised and then calls `SetForegroundWindow` synchronously in the caller: no thread-input attach, no synthetic input, false if Windows declines. Nothing is logged.
- `Native.cs`, `ShellInterop.cs`, `IconReader.cs`, `KnownFolders.cs`: Win32 and shell declarations written by hand.

Smoke console (`src/Island.Sources.Programs.Smoke/`, not in the solution, no window): see section 2.

Source study (re-opened tonight, read only, nothing pasted): PowerToys Window Walker `OpenWindows.cs` (nine-step filter, matches the research file section 1, the "cloaked on another desktop" exception confirmed), `Window.cs` (individual property checks, five cloak states), sigoden/window-switcher `window.rs` (empty title, 120x90 minimum, tool windows, frame host handling). Re-implemented from the description of the rules; no GPL/AGPL code was consulted for code.

## 2. Commands run and results

- Baseline before my changes: `dotnet test ...\tests\Island.Tests` gave 248 passed.
- `dotnet test ".worktrees\b1-programs\tests\Island.Tests"` (whole suite, guards included): **288 passed, 0 failed, 0 skipped** (248 old + 40 new in `tests/Island.Tests/Programs/`).
- `dotnet build ...\src\Island.Sources.Programs` and `...\src\Island.Sources.Programs.Smoke`: 0 errors, 0 warnings.
- `dotnet run --no-build --project ...\src\Island.Sources.Programs.Smoke` with `OutsideGate.Current = new OutsideGate(selfTest: true)`. Counts and yes/no only (one run on this laptop, 6 Oct 2026 night; window counts vary with what is open):

```
windowsListerFirstReadDone: True        windowsListerFirstReadMs: 67
windowEventHooksInstalled: 5            windowsListed: 8
windowsWithExeName: 8                   windowsWithPackageFamily: 3
windowsZOrderIsSequence: True
catalogReadDone: True                   catalogReadMs: 1140
installedPrograms: 166                  installedWithExeName: 98     installedPackaged: 45
starterProgramsInList: 16               starterProgramsFound: 7      starterPicksBuilt: 7
explorerIconNonEmpty: True              explorerIconNotGeneric: True explorerIconLargestSize: 256
explorerIconAlphaLooksPremultiplied: False   (so: straight alpha)
madeUpExtensionIconRead: True           madeUpExtensionIconIsGeneric: True
folderIconsRead: 6                      folderIconLargestSize: 256
starterIconsRead: 7                     starterIconsGeneric: 0       starterIconsLargestSize: 256
starterProgramsNotFound: 9              notFoundButNameContainsHint: 0   notFoundButExeStemContainsCandidate: 0
starterFoundButExeIsNotACandidate: 0    runningStarterExeButNotFound: 0
sampleOfInstalledChecked: 60            sampleIconsRead: 60          sampleIconsGeneric: 2
startProgramReturned / openFolderReturned / openSiteReturned / bringForwardReturned: all False
gateAllowed: 0   gateRefusedTotal: 4   (start 1, folder 1, address 1, bringForward 1)
```

Starter programs: **7 of the 16 were found** in this laptop's list of installed programs. The other 9: no installed entry has a name that merely contains a starter's name hint, no installed exe name contains a candidate's stem, and no running window uses a candidate exe name, so on this laptop they look not installed (or not listed by Windows' all-apps folder) rather than mis-named. No candidate additions are requested from this evidence. A general request is in section 5 (the Squirrel and versioned-name cases), which this laptop could not exercise.

## 3. Proven, and by what

Named tests (all green in the 288):
- EVALS I4: `IconChoiceTests.Missing_Icon_Falls_Back_To_Letters` (null, empty list, null entry, empty picture, wrong byte count, blank picture).
- EVALS I5: `IconChoiceTests.Largest_Available_Size_Is_Chosen`; also `Only_Smaller_Picture_Is_Used_As_It_Is_And_Flagged` (same object back, `Undersized` true) and `A_Picture_Exactly_As_Large_As_Drawn_Is_Not_Flagged`.
- EVALS I9: `IconChoiceTests.Unseen_Starter_Pick_Uses_Letters`.
- `IconChoiceTests.Two_Letter_Mark_Follows_The_Tile_Rule` (9 cases).
- `WindowRulesTests`: one named test per rule (invisible, owned, tool window, app-window style wins, removed from task list, core window class, cloaked app/inherited/other, shell-cloaked on another desktop kept, shell-cloaked on this desktop dropped, empty title, tiny size with the boundary, frame host recognised, UWP child picked, minimised UWP has no child, package family from app id).
- `IconCompareTests` (identical at different sizes, very different, unusable).
- Guards stay green: `OutsideGuardTests.Outside_Actions_Are_Gated` (my outside calls are only in `OutsideActions.cs`, which names `OutsideGate`), `The_Foreground_Is_Never_Forced`, `App_Has_No_Internet_Client`, `GuardTests.*`.

Counted results on the real machine (smoke, section 2):
- EVALS I1 (reading part): the icon of `%WINDIR%\explorer.exe` is non-empty (256 px), and is not the generic icon. The generic check itself was verified against a real file with a made-up extension (`madeUpExtensionIconIsGeneric: True`), so `IsGenericFileIcon` can say yes as well as no.
- EVALS I6 (reading part): the windows read took 67 ms, the catalog 1.1 s, both on their own threads; the caller only reads a volatile list.
- The gate: under self-test all four outside actions on a made-up pick or a real window returned false and were counted as refused (4 refusals, 0 allowed). The real window handle used for `BringForward` belongs to another process, so the gate refused it before anything happened.
- Five window-event hooks installed.

## 4. Not proven

- The window-change path: that `Changed` really fires when a window opens, closes, is renamed or changes foreground. Only the first read and the hook installation were observed. Needs a real run with the app or a person opening a window (the first real start, or a self-test with its own test windows).
- `BringForward` really bringing a window forward, restoring a minimised one, or Windows declining: never exercised (nothing may be brought forward tonight). The self-test with the island's own windows should cover the "own process" door.
- `StartProgram`, `OpenFolder`, `OpenSite` really starting things: the gate refused them in the smoke. Launching `shell:AppsFolder\<id>` through `Process.Start` with the shell flag is the documented way but was not run.
- UWP handling (frame host child; the minimised case through the window's app id): only the pure rules are tested; the real path ran on whatever windows were open (3 of 8 had a package family) but a minimised UWP window was not present. Windows on another virtual desktop (cloak value 2): rule tested, real COM check `IVirtualDesktopManager` untested with a second desktop. UNVERIFIED: whether `SetForegroundWindow` on such a window switches desktop.
- Squirrel `Update.exe` resolution: written for the layout I know (`app-*` folder next to `Update.exe`, exe named after the app) but untested here: this laptop had no such shortcut among the starters.
- Icon "as large as the program offers": Windows scales a program's largest picture to the size asked, so 256 is returned even when the program only has a 48 picture, and the reader cannot tell. The picture is never stretched by our code, but the "flag the undersized one" rule (I5) can therefore not fire for shell-read icons; it works on whatever `IconImage` list is given to `IconChoice.Choose`. A PE-resource reader (icon group directory) would make the true size known; not built (YAGNI until Dan sees a blurry icon).
- Packaged-app exe names: the catalog gives no exe name for Store apps (only the package family). Matching a running window to a Store pick therefore goes through the package family, which the lister gives from the window's process. Reading each package's manifest would add exe names; not built.
- Elevated windows: listed, but their exe name is null when the process cannot be opened; bringing them forward may be refused by Windows.
- Not proven by eye: how the icons look (alpha is straight BGRA, WPF `Bgra32`; premultiplied was tested for and found false).
- Self-test file `selftest.json` and its `starterProgramsFound` count: that belongs to the app; the number is `StarterPicks.CountFound(catalog.Installed)`.

## 5. Requests to the joints and the main session

- Add the two projects to `Island.sln` (`Island.Sources.Programs`; the smoke project is deliberately outside it). The app project must target `net10.0-windows10.0.19041.0` or newer to reference it; if `Island.App` has a different target, the build output folder moves (see WORK-ORDER-3 section 2: fix `run.cmd`, `stop.cmd`).
- `StarterPicks.Find`: name hints match by exact equality. Windows lists versioned names for some programs (for example Adobe products carry a year); request: also accept `StartsWith` (or `Contains`) of the hint on `InstalledProgram.Name`. This laptop showed no case of it (`notFoundButNameContainsHint: 0`), so it is a precaution only.
- `StarterPicks.Build` / `Pick.IsStorable`: a found program with neither an exe name nor a package family is silently dropped (a desktop program whose shortcut target could not be resolved). The catalog does its best, but the Core could fall back to the first exe candidate of the starter when the program was found by name. Not done, since it would write a guess into a pick.
- `OpenWindow` carries no process id; `BringForward(handle)` reads the owner itself. If the app wants to hide its own windows from the list (the island is a tool window and is filtered anyway), it filters in the app.
- Candidate lists in `StarterPicks.Programs`: no addition requested from evidence (see section 2). Unverified names remain as the research file marks them.
- Possible cleanup in Core: `Item.Mark` placeholders could use `IconChoice.TwoLetterMark` when the page shows picks (section 3 of the work order).

## 6. How to wire it into the app

Construct once at start, on any thread (all of them start their own background threads and return at once):

```
var catalog = new InstalledProgramCatalog();      // reads in the background; catalog.Loaded fires when ready (raised on its thread)
var windows = new WindowLister();                 // windows.Changed fires on its own thread: marshal to the UI thread before touching the UI
var icons   = new ProgramIcons(catalog);          // call ProgramIcon / FolderIcon only from a background thread (it waits for its STA worker, 8 s limit)
var outside = new OutsideActions(catalog);        // call from the click or key handler itself, synchronously
```

- `OpenSnapshot.Windows` = `windows.Windows` (a volatile snapshot, never null; empty until the first read, about 70 ms).
- After `catalog.Loaded`, apply the starter picks (`StarterPicks.Build(catalog.Installed)`) only when no picks file exists and never under the self-test, as the work order says; before that the catalog is empty and `Build` would give only sites and folders.
- Icons: show `IconChoice.TwoLetterMark` tiles at first, then ask `icons.ProgramIcon(pick.ExeName, pick.PackageFamily)` / `icons.FolderIcon(pick.KnownFolder)` on a worker, put the result through `IconChoice.Choose(...)` and swap it in on the UI thread. Use `icons.IsGenericFileIcon(image)` to treat a generic icon as missing. A miss returns null and is retried on the next ask.
- Self-test: set `OutsideGate.Current = new OutsideGate(selfTest: true)` before creating `OutsideActions`; every refusal is counted in the gate (`gate.RefusedCounts()`), numbers only.
- Dispose `WindowLister` and `ProgramIcons` at shutdown (they use background threads, so a forgotten dispose does not keep the process alive).
- Nothing in this project writes a file, a log or a console line.
