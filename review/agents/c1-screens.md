# C1 screens — report (WORK-ORDER-6.md section 1, EVALS W1, W2 choosing half, M8 mixed-scaling half)

Branch `c1-screens`, started from `6033be4`. Nothing outside the territory was touched; no existing file was changed.

## 1. What was built

Island.Core, no Windows call (`src/Island.Core/Screens/`, namespace `Island.Core` like its neighbours):
- `ScreenTypes.cs` — `ScreenPoint`, `PixelRect` (edges as Windows gives them, half-open, long width/height), `ScreenInfo` (full rectangle, work area, scale 1.0 = 100%, `IsPrimary`; `SafeScale`, `SafeWork`, `WorkWidthDip`, `Fallback`), `ScreenPlacement` (index, screen, window rectangle, `IsFallback`).
- `ScreenChooser.cs` — `Choose(pointer, screens, windowWidthDip, windowHeightDip)`. The screen holding the pointer, else the nearest, else (null pointer) the primary or first usable. The window is DIP x that screen's scale, centred on the work area's width, top = work area's top. Never throws.
- `ScreenFit.cs` — `Tiles(workWidthDip | ScreenInfo, wantedTiles, reservedDip = 0)`: the narrow-screen rule, on `PageFit.MaxTiles` (min of the media and non-media capsule, as `AppHost.MaxTiles` does today).
- `IScreenSource.cs` — `IScreenSource` (`Screens`, `Pointer`, `Changed`) and `PretendScreens` (settable lists, `Raise()`).

Windows (`src/Island.Sources.Screens/`, `net10.0-windows10.0.19041.0`, `UseWPF` true, see section 5):
- `Native.cs` — read-only P/Invokes: `GetCursorPos`, `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor` (MDT_EFFECTIVE_DPI = 0).
- `ScreenReader.cs` — implements `IScreenSource`, `IDisposable`. Reads on a pool thread, keeps the last good list, debounces 300 ms, raises `Changed` only when the list really differs. No window, no outside action, no `Outside*` file needed.

Smoke (`src/Island.Sources.Screens.Smoke/`): console, per-monitor-V2 manifest, prints counts and booleans only.

Tests (`tests/Island.Tests/Screens/`): `ScreenChoiceTests.cs` (the six named tests plus side bar, scaling in units, reserved room, nothing wanted, absurd widths), `ScreenAbsurdInputTests.cs` (garbage and a seeded 20,000-round fuzz).

### Decisions and documented fallbacks
- Change detection: `SystemEvents.DisplaySettingsChanged` plus `SystemEvents.UserPreferenceChanged` (any category), handlers detached in `Dispose` (Learn: static events leak otherwise). Microsoft Learn does not say which `UserPreferenceCategory` carries a work-area change (SPI_SETWORKAREA), so every preference change wakes the reader; the re-read is a few microseconds and only a real difference is reported. Plus a public `NotifyPossibleChange()` for the app to forward WM_DISPLAYCHANGE / WM_DPICHANGED / WM_SETTINGCHANGE from its own window procedure.
- No usable screen: `Choose` returns the 1920 x 1080 / 100% stand-in with `IsFallback = true` and index -1 (the app may skip the move). Zero-size full rectangle = unusable. Empty work area = full rectangle. NaN, infinite, zero or negative scale = 1.0. Non-finite or non-positive window size = 1 px (never zero). More than 64 screens: only the first 64 are looked at (`MaxScreens`). Overlapping screens: first in the list. Pointer ties between two screens: first. Window wider than the work area stays centred and overhangs both sides equally. Coordinates are clamped to int, no overflow.
- Tiles: NaN, zero, negative width = 1 tile; nothing wanted = 0; infinite width = what was wanted. At least one tile is always shown when something is wanted (the capsule is clipped rather than absent).

## 2. Commands run and results

- Microsoft Learn pages opened 6 Oct 2026 and matched against the code: GetCursorPos (BOOL, POINT out, screen coordinates), EnumDisplayMonitors (hdc NULL, clip NULL, callback, dwData; nonzero on success), MONITORENUMPROC (4 params, TRUE continues), GetMonitorInfoW and MONITORINFO (cbSize DWORD, rcMonitor, rcWork, dwFlags; cbSize must be set; MONITORINFOF_PRIMARY; negative values allowed), GetDpiForMonitor (Shcore.dll, HRESULT, S_OK / E_INVALIDARG; values accurate only for a per-monitor-aware caller), MONITOR_DPI_TYPE (MDT_EFFECTIVE_DPI = 0), MonitorFromPoint (read, not used: the chooser is pure), WM_DISPLAYCHANGE (sent to top-level windows), WM_DPICHANGED (0x02E0), WM_SETTINGCHANGE (0x001A), SystemEvents.DisplaySettingsChanged and UserPreferenceChanged (static, detach).
- `dotnet test tests\Island.Tests` (worktree): **Passed 545, Failed 0** (502 existing + 43 new, incl. the guard tests that scan `src/`).
- `dotnet build src\Island.Sources.Screens.Smoke` (builds `Island.Sources.Screens` and `Island.Core`): **0 warnings, 0 errors**.
- `dotnet run --no-build --project src\Island.Sources.Screens.Smoke` on this laptop, exit 0:
  `readFinished=True`, `changedEventsFirstRead=1`, `changedEventsAfterIdenticalReread=0` (five wake-ups, debounced, no event), `screens=1`, `primaryScreens=1`, `pointerRead=True`, `pointerOnAScreen=True`, `workAreaSmallerThanScreen=True`, `scalingAboveOneHundred=False`, `mixedScaling=False`, `placementIsFallback=False`, `placementAtTopOfWorkArea=True`, `tilesOnChosenScreen=8`.
- Island.App was not built or run; `Island.sln` untouched.

## 3. What is proven, and by what

- Pointer's screen chosen: `ScreenChoiceTests.Pointer_Screen_Is_Chosen` (incl. the first pixel of the second screen).
- Negative coordinates, screens left of and above the main one: `Negative_Coordinates_Work`.
- Same size in units at 100/125/150/200%, and centring on the target screen's work area: `Mixed_Scaling_Gives_The_Same_Size_In_Units` (4 cases).
- Top taskbar not covered: `Top_Taskbar_Is_Not_Covered`; side bar shifts the centre: `A_Side_Bar_Moves_The_Centre_With_The_Work_Area`.
- Nearest screen when the pointer is on none, ties to the first: `No_Screen_Under_The_Pointer_Takes_The_Nearest`.
- Narrow screen lowers the tile count until the capsule fits, one tile minimum, scaling counted in units: `A_Narrow_Screen_Shows_Fewer_Picks`, `A_Narrow_Screen_Counts_Its_Scaling_In_Units`, `Reserved_Room_Lowers_The_Count`, `An_Absurd_Width_Gives_One_Tile`.
- Robustness: no screens, null list, zero-size, empty work area, NaN/infinite/negative scaling, absurd window size, int-extreme coordinates, 40 screens, 100,000 screens (capped), overlap, unreadable pointer, wider-than-screen window, determinism, and `Random_Garbage_Never_Throws_And_Keeps_Its_Promises` (20,000 seeded rounds: never throws; a fallback only when nothing is usable; a screen that holds the pointer always wins).
- The real reader works here: Smoke counts above. The debounce and "no event for an identical read" are shown by the Smoke counters.

## 4. Not proven

- Anything with a real second screen: mixed scaling on real hardware, a screen left of or above the main one, hot-plug. This laptop has one screen at 100%, so `GetDpiForMonitor` returning a value above 96 for a per-monitor-aware process was never observed (the registry says 100%, consistent with the reading). NEEDS-HUMAN-VERIFY stays as WORK-ORDER-6.md says.
- That `SystemEvents` (or any wake-up) fires for a work-area change such as a taskbar moving or auto-hiding: Learn does not say; the reader survives that either way only if the app forwards WM_SETTINGCHANGE through `NotifyPossibleChange()` (see section 6). SPI_SETWORKAREA = 0x002F is from the research file only: UNVERIFIED on Learn, nothing is built on it.
- `Window.DpiChanged` / WM_DPICHANGED overwriting the app's own SetWindowPos, and the read-back-and-repeat (at most three times) loop: app wiring, not in this piece.
- That the reader's own `Changed` carries no work-area change without a fresh `ScreenReader` read: it re-reads every time it is woken, so it does, but no test can fake a Windows work-area change.
- `IslandMachineTests.Screen_Never_Changes_While_Visible` is not mine (it changes an existing class); its shape is in section 5.

## 5. Requests to the joints / main session

1. **Add the projects to `Island.sln`** (`Island.Sources.Screens`, `Island.Sources.Screens.Smoke`) and a project reference from `Island.App` to `Island.Sources.Screens`.
2. **`UseWPF` in `Island.Sources.Screens`.** `Microsoft.Win32.SystemEvents` is not in `Microsoft.NETCore.App`; without `UseWPF` (a framework reference, no NuGet package, and Island.App already has it) the project does not compile (CS1069, tried). If you prefer a console-clean library, drop the two `SystemEvents` subscriptions and the `UseWPF` line and call `NotifyPossibleChange()` from the app window's message hook: nothing else changes.
3. **`IslandMachineTests.Screen_Never_Changes_While_Visible`** (not mine) needs no new state in the machine if the rule is "call `ScreenChooser.Choose` only when `!machine.NeedsFrames` (Phase is Hidden), just before `ShowHideKey` / `MainKey`". For the test to exist inside the machine's own tests, the cheapest shape is a public `bool IsVisible => Phase != IslandPhase.Hidden` (or reuse `NeedsFrames`) and a small app-layer helper `ScreenPicker { ScreenPlacement? Current; Update(pointer, screens, isVisible) }` that keeps the last placement while `isVisible` and recomputes only when not. The test then drives the machine through FlyingIn, Open and Closing with the pointer moved to another pretend screen (`PretendScreens`) and asserts the placement index stays; after Hidden the next summon takes the new one. I did not write that helper: it lives next to the app wiring.
4. A `WindowMetrics.Width`-based DIP width is what to pass as `windowWidthDip` (the window includes shadow reach); pass the same window height. Pass `reservedDip` to `ScreenFit.Tiles` as the peak-overshoot margin of the capsule (`WindowMetrics.PeakCapsuleWidth` minus the widest capsule) if the capsule must fit with its overshoot; the shadow may overhang the edge.
5. The self-test: `selftest.json` can take `screens = reader.Screens.Count` (a count); the check "window real rectangle equals `ScreenChooser.Choose(...)`" compares two `PixelRect` values and records same/different only. With one screen record the two-screen checks as `NO-VERIFIER — one screen`.
6. Possibly useful for the settings screen (WORK-ORDER-4 section 3, "same code"): `ScreenChooser.Choose(reader.Pointer, reader.Screens, w, h).Screen.SafeWork` is the rectangle to centre the settings window in.

## 6. How to wire it

```csharp
var screens = new ScreenReader();   // keep one for the app's life
screens.Start();                    // schedules the first read on a pool thread; returns at once
screens.Changed += () => { /* optional: mark "screens dirty"; do not touch WPF objects here, it is a pool thread */ };
// forward from the island window's HwndSource hook (WM_DISPLAYCHANGE, WM_DPICHANGED 0x02E0, WM_SETTINGCHANGE 0x001A):
//   screens.NotifyPossibleChange();
// at a summon, only when nothing of the island is visible (machine.Phase == Hidden), on the UI thread:
var placement = ScreenChooser.Choose(screens.Pointer, screens.Screens, WindowMetrics.Width, WindowMetrics.Height);
if (!placement.IsFallback) { /* SetWindowPos(hwnd, 0, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_NOZORDER) for the capsule window, the shadow window and the blur layer */ }
var tiles = ScreenFit.Tiles(placement.Screen, wantedTilesFromPicks /*, reservedDip */);
// after moving: read each window's real rectangle back; if it differs from placement.Window, apply again, at most three times.
// at shutdown: screens.Dispose();   // detaches the static SystemEvents handlers
```

- Threading: `Screens` and `Pointer` are safe to read from any thread (`Pointer` is a direct `GetCursorPos`, microseconds, fine on the UI thread at summon). `Changed` is raised on a pool thread. `Choose` and `Tiles` are pure and thread-safe.
- Do not call `Start()` before the process is per-monitor-V2 aware (the app manifest already says so); Smoke carries its own manifest for the same reason.
- Flags for `SetWindowPos` and the DPI re-application are the main session's: they were not part of this piece and were not confirmed here.
