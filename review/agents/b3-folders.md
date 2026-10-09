# B3 folders — report

State: done for the reading side (WORK-ORDER-3 section 6, reading only). Branch `b3-folders`, made from `4c852a1`.

## 1. What was built

Island.Core (pure, `src/Island.Core/Folders/`):
- `FolderMatch.cs` — `new FolderMatch(IEnumerable<(string Name, string Path)>)`, `Resolve(path)` returns one of `Pick.KnownFolders` or null. Exact folder only (a subfolder is not it), case, `/` vs `\` and trailing slashes ignored, `::{GUID}` parsing names accepted when listed for that name. Names outside `Pick.KnownFolders` are ignored.
- `FolderReading.cs` — `RawFolderEntry(FrameHandle, TabHandle, Path, FrameZOrder)` and `FolderReading.Build(raw, tabsByFrame, match)` giving `FolderWindow` records; `FolderReading.ActiveTab(tabsInZOrder)`.
- `FolderLocation.cs` — `FromUrl("file:///X:/...")` to a path, UNC path, or `::{GUID}`; null otherwise.

Decision on `FolderWindow` (documented in code): **one entry per tab**. `Handle` is the **frame (top-level Explorer window) handle**, shared by all tabs of that frame, because bringing a frame forward is all that is possible (selecting a tab needs undocumented messages). So "how many" = number of entries, and "bring forward" = `BringForward(entry.Handle)`. `ZOrder` is the frame's rank among File Explorer frames (0 = top-most Explorer frame), NOT among all windows; it stays steady while other programs are moved about and is only comparable with other folder entries. The list is sorted top-most frame first; inside a frame the active tab (first `ShellTabWindowClass` child in z-order) comes first. Entries with no path or no frame are dropped; a folder that is not a known folder is kept with `KnownFolder = null`.

Island.Sources.Folders (`net10.0-windows10.0.19041.0`, no NuGet, references Island.Core):
- `ExplorerWindowReader.cs` — public `IFolderSource, IDisposable`; owns the STA thread and the poll loop.
- `ExplorerRawRead.cs` (internal) — the actual reading: `FindWindowEx` cheap check for a `CabinetWClass` frame; `EnumWindows` for frame order; the shell's window collection (`CLSID_ShellWindows`, late-bound) for each tab's address (`LocationURL`, with `Document.Folder.Self.Path` as fallback) and `IServiceProvider.QueryService(SID_STopLevelBrowser)` then `IShellBrowser.GetWindow` for the tab's own window; `FindWindowEx` over `ShellTabWindowClass` children for tab z-order.
- `ShellNative.cs`, `KnownFolderPaths.cs` (real known-folder paths via `SHGetKnownFolderPath`, read once at thread start, plus their `::{GUID}` forms), `AssemblyInfo.cs` (internals visible to the smoke project only).

Island.Sources.Folders.Smoke (console, no window, not in the solution): prints counts only.

Study sources re-opened tonight (ExplorerExtras, MIT; ExplorerTabUtility, MIT): confirmed IShellWindows on an STA thread, one entry per tab sharing the frame HWND, `IShellBrowser::GetWindow` as the per-tab identity. The Microsoft Learn IShellWindows page URL I tried returned 404; the technique rests on the research file and the two projects. Nothing was copied; the code is a re-implementation.

## 2. Commands and results

- `dotnet test .worktrees\b3-folders\tests\Island.Tests` — Passed 265, Failed 0 (whole suite incl. `OutsideGuardTests` and `GuardTests`; baseline was 248, plus 17 new in `Folders/`).
- `dotnet build src\Island.Sources.Folders` — 0 warnings, 0 errors.
- `dotnet build src\Island.Sources.Folders.Smoke` — 0 warnings, 0 errors.
- Smoke run on this laptop (counts only; one Explorer window happened to be open, I opened none):
  - firstReadFinished True; explorerFrames 1; entries 1; withKnownFolder 0; firstReadMs about 500 to 630 (three runs; includes thread start and COM start-up)
  - withPath 1; withShellParsingName 0; knownFolderPathsRead 6 of 6
  - raw: entries 1, entries with a tab handle 1, frames with tab windows 1, tab windows 1

## 3. Proven

- `FolderMatchTests`: `Exact_Path_Matches_Its_Known_Folder`, `Subfolder_Is_Not_The_Known_Folder`, `Case_And_Trailing_Slash_Do_Not_Matter`, `Unknown_Path_Has_No_Known_Folder`, `Shell_Parsing_Name_Maps_Only_When_It_Is_The_Known_Folder`, `Empty_Lookup_Matches_Nothing`.
- `FolderReadingTests`: `Each_Tab_Is_One_Entry_And_Shares_Its_Frame_Handle`, `Active_Tab_Is_The_First_Tab_Window_In_Z_Order`, `Active_Tab_Comes_First_Inside_Its_Frame_Whatever_The_Shell_Order`, `Top_Most_Frame_Is_Listed_First`, `Entries_Without_A_Path_Or_Frame_Are_Dropped`, `The_Same_Tab_Listed_Twice_Is_One_Entry_But_Unknown_Tabs_Are_Not_Merged`, `A_Folder_That_Is_Not_Known_Is_Still_Listed_Without_A_Known_Folder`.
- `FolderLocationTests`: file address, UNC, `::{GUID}`, non-file addresses.
- By the smoke run: the real reader starts, reads through STA Shell COM, gets a non-zero per-tab window handle and a path, the six known-folder paths are read, and it stops cleanly (process exits).
- By the guard tests: no outside call, no forced foreground, no HttpClient in the new code. The reader contains no call that opens, closes, activates, navigates or brings forward any window (grep for the guard's word list is clean).

## 4. Not proven

- Matching against a REAL known folder: the one open window on this laptop was not a known folder, and I may not open Explorer windows. So `withKnownFolder > 0` was never seen live. NEEDS-HUMAN-VERIFY: open Downloads, the reader should report `KnownFolder == "Downloads"` for it. Risks: OneDrive-redirected Documents/Desktop (the real path from `SHGetKnownFolderPath` should be what Explorer shows, untested), and whether Explorer reports Downloads as a path or as a `::{GUID}` (both forms are in the lookup).
- Several tabs in one window were not seen live (only one window with one tab was open): the shared frame handle and per-tab handles are proven only by the logic tests and the documented research (ExplorerExtras measured it on build 26200).
- `Changed` firing was proven only by logic (list inequality), not by opening and closing a window live.
- A reading that fails half-way (one tab unreadable) skips that tab for that poll, so the list can flicker for one poll and raise `Changed` twice; acceptable, not measured.
- Elevated (admin) Explorer windows: not tested.
- Windows 10 (no tabs): not tested; the code falls back to one entry per window.

## 5. Out of scope and requests

- **Tab activation is NOT implemented** (it needs undocumented `WM_COMMAND`s that can break on any Windows update). Clicking an open folder brings the whole frame forward; with several tabs it will not select the clicked tab. Say so on the page if it ever matters.
- Nothing is asked of the joints. Observation only: `FolderWindow` has no "active tab" flag; the order (active tab first inside a frame) is the only carrier. If the page wants one tile per folder rather than per tab, it should group by `KnownFolder`.
- B1's `BringForward(long)` must accept a frame handle that is a `CabinetWClass` window; it is not in B3's territory.

## 6. How the main session wires it

```csharp
// Island.App references src/Island.Sources.Folders (add it to Island.sln when merging).
var folders = new Island.Sources.Folders.ExplorerWindowReader();   // optional: new(TimeSpan) for another poll interval
folders.Changed += () => dispatcher.BeginInvoke(RebuildFolderPage); // raised on the reader's own STA thread: marshal to the UI thread
folders.Start();                                                    // starts the thread; no-op if called twice
// read at any time, from any thread:  IReadOnlyList<FolderWindow> list = folders.Windows;
// on shutdown:
folders.Dispose();                                                  // stops the thread, waits up to 2 s; safe twice
```

- Typed as `IFolderSource` it fits the joint; `Windows` is empty until the first read finishes (about half a second after `Start`), and `Changed` fires for that first non-empty list too.
- Poll: every 2 s. While no File Explorer frame exists only a cheap `FindWindowEx` runs; the Shell COM read happens only when a frame exists. On any failure the last good list stays; the reader never throws into the caller (handlers that throw are swallowed).
- `FolderWindow.PathInMemory` holds the real path: memory only, never log it.
- Pick state for a folder pick: open = `list.Any(w => w.KnownFolder == pick.KnownFolder)`, count = how many such entries, click = `BringForward(first such entry's Handle)` (the first is the top-most frame's active tab).
