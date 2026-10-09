# C3 startup report (c3-startup)

Branch `c3-startup`, from 6033be4. Section: WORK-ORDER-6.md section 3, "Start with Windows". Pure logic, no registry call anywhere.

## 1. What I built

`src/Island.Core/Startup/`
- `StartupRegistry.cs`:
  - `IStartupRegistry` has `Read()`, `Write(commandLine)` and `Remove()`. The methods are deliberately not named `SetValue` or `DeleteValue`, so the future guard that hunts registry writes never flags this file. Please keep that in mind when writing the guard's word list.
  - `StartupKey` holds the constants: `KeyPath`, `ValueName = "Island"` and `MaxCommandLineLength = 260`.
  - `MemoryStartupRegistry` is the in-memory stand-in. It counts `Writes` and `Removes`, and has a `RefuseChanges` flag.
- `StartupSwitch.cs`:
  - `StartupSwitch(IStartupRegistry, string programPath)` with `CommandLine`, `IsOn()`, `TurnOn()`, `TurnOff()` and the static `IsAutostartLaunch(args)`.
  - `StartupResult(Ok, Refusal?)`.
- `StartupRefusals.cs`: `StartupRefusals.PathTooLong`, code `STARTUP_PATH_TOO_LONG`. Its `.Message` is word for word the register text.
- `StartupSettingJson.cs`: `Read(JsonElement root, out bool value, out string? problem)` for the key `startWithWindows`.

`tests/Island.Tests/Startup/`: `StartupSwitchTests.cs` and `StartupLaunchTests.cs`.

### Behaviour decisions
- **What is written:** `"<fullpath>" --autostart`.
- **Writable path:** the path must be fully qualified. It must not be empty, hold a `"` or a control character (NUL and newline included), or end in `\` or `/`.
  - A trailing backslash would escape the closing quote when Windows splits the line.
  - An unusable path gives `Ok=false` with no Refusal, and nothing is written.
- **Length:** the check is on the whole command line, including quotes, space and `--autostart` (path + 14 characters). More than 260 is refused with `STARTUP_PATH_TOO_LONG` and nothing is written. Exactly 260 is allowed.
- **IsOn():** reads the registry now. It is on only if the stored text equals this program's command line, ignoring case. A value naming another program is "off". A longer path that starts the same is "off".
- **TurnOn:** writes only if the value is not already ours. Turning it on twice writes once.
- **TurnOff:** removes only a value that is ours. A value naming another copy of Island is left alone.
  - A stale value from a moved folder shows "off" and is cleared by switching on, then off. This is a deliberate cautious choice. The brief says "Off deletes that value". If you prefer to always delete, change `TurnOff` to call `Remove()` unconditionally.
- **Failures:** a registry that refuses `Write` or `Remove` gives `Ok=false` and the displayed state does not change.
- **Never at launch:** nothing writes at launch. Only `TurnOn` and `TurnOff` touch the registry.

## 2. Commands and results

All run in the worktree.
- `"C:\Program Files\dotnet\dotnet.exe" test tests\Island.Tests`: **Passed 546, Failed 0, Skipped 0, Total 546**. This is the existing tests plus mine. A warning-free build.
- Mutation checks, run once and reverted (`git status` shows only my two new folders):
  - `>` changed to `>=` in the length check: `A_Too_Long_Path_Is_Refused` failed.
  - The "already on" early return removed: `Turning_On_Twice_Writes_Once` failed.
- No Smoke program. Island.App and the real registry were never run or opened.

## 3. Proven, and by which test

- **One quoted value with `--autostart`:** `On_Writes_One_Quoted_Value_With_Autostart` (one write, no removal, exact text).
- **Off deletes it:** `Off_Deletes_It`.
- **Nothing written at launch or to repair:** `Launch_Never_Writes` (missing value, foreign value, stale value: zero writes, zero removes, value untouched).
- **Shows what the registry holds:** `The_Switch_Shows_What_The_Registry_Holds`. A value removed behind its back shows "off".
- **Too long is refused:** `A_Too_Long_Path_Is_Refused`.
  - The 261-character line is refused with the right code, nothing is written, and the 260-character line is written.
  - Boundary arithmetic: 14 characters beyond the path.
- **My extra cases:**
  - path with spaces: `A_Path_With_Spaces_Is_Quoted_As_One_Piece`
  - empty, whitespace, relative, quote, trailing backslash, NUL, CR/LF: theory `A_Path_That_Cannot_Be_Quoted_Safely_Writes_Nothing`
  - null path: `A_Null_Path_Writes_Nothing`
  - registry that refuses writes or removal: `A_Registry_That_Refuses_Writes_Leaves_The_Switch_Off`, `A_Registry_That_Refuses_Removal_Leaves_The_Switch_On`
  - another program's value: `A_Value_That_Names_Another_Program_Shows_Off_And_Is_Not_Deleted`
  - prefix path, case, replace foreign value on turn-on, idempotence: the remaining `StartupSwitchTests`
- **`--autostart` recogniser:** exact, case-sensitive match, as in `CommandLine.Parse`. Tested in `The_Autostart_Argument_Is_Recognised_Exactly`. The line the switch writes carries the argument: `The_Line_The_Switch_Writes_Carries_The_Recognised_Argument`.
- **Refusal text word for word:** `The_Refusal_Has_The_Register_Text_Word_For_Word`.
- **Settings key:** `StartupSettingJson` reads yes, no, absent (off), non-boolean (problem, stays off), and a non-object root (absent).

### Confirmed on Microsoft Learn
Page: Run and RunOnce Registry Keys, `learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys`, `updated_at 2026-02-21`.
- **Key path:** `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` is listed on the page.
- **Length:** "The data value for a key is a command line no longer than 260 characters." Entries have the form description-string=commandline.
- **Order:** with several programs under a key, the run order is indeterminate.
- **Safe Mode:** the keys are ignored by default.
- **Timing:** the system may delay the programs started from `Run`.
- **REG_SZ is UNVERIFIED on that page.** It does not state the value type. Only the separate RunOnce driver page names REG_SZ or REG_EXPAND_SZ, and only for RunOnce. The research file says Chromium and Electron write REG_SZ. The `Outside` writer must use REG_SZ, which is also what I recommend, since the path must not be expanded. Treat "REG_SZ" as community practice, not a Learn statement for `Run`.
- **Quoting:** the Run page says nothing. The quoted form comes from the Chromium and Electron sources cited in the research file. It was not re-opened.
- **Counting:** whether the 260 includes a terminating NUL is not stated. I took the page literally (at most 260 characters). If you want a margin, lower `MaxCommandLineLength` to 259. One line in `StartupKey`; the boundary test is computed from the constant.

## 4. Not proven

- **Real registry:** whether Windows actually starts the island after sign-in, with the icon near the clock, and whether the key works. This is Dan's NEEDS-HUMAN-VERIFY.
- **Value type and quoting:** the real `Outside` registry class has not been written, so REG_SZ and the quoting are not tested against a real hive.
- **Task Manager disable:** `StartupApproved\Run` is not read, as the work order says. If Dan disables the entry there, the switch still shows "on".
- **Write failures:** what the real registry class does on failure (`Write`/`Remove` returning false, `Read` returning null) is only simulated by `RefuseChanges`.

## 5. Requests to the main session and the joints

I changed no existing file.

1. **`src/Island.Core/Refusals.cs`:** add `StartupRefusals.PathTooLong` to `Refusals.All`, or move it into `Refusals`. Check that `RefusalTests` still holds for the new code.
2. **`src/Island.App/CommandLine.cs`:**
   - Add `bool Autostart = false` to the record and `case "--autostart": autostart = true; break;` to the `switch`.
   - A case inside the switch is not read as the value of `--data-dir` or `--selftest`, because `++i` skips values.
   - `StartupSwitch.IsAutostartLaunch(args)` is the same test for code that has only the raw argument list.
3. **`src/Island.Core/Settings.cs`:**
   - Add a `bool StartWithWindows = false` parameter to the positional record, after `Glass`.
   - In `Parse`, add `if (StartupSettingJson.Read(doc.RootElement, out var sw, out var why)) { if (why is not null) return Unreadable(why); result = result with { StartWithWindows = sw }; }`.
   - In `ToJson`, add `["startWithWindows"] = StartWithWindows` (JSON bool, not text).
   - Add it to `Equals` and `GetHashCode`.
   - `Defaults` stays off.
   - **The file value is only a memory of the last choice.** The screen shows `StartupSwitch.IsOn()`. Nothing may compare the file value with the registry at launch and write to reconcile them.
4. **`IslandMachineTests.Autostart_Launch_Shows_Nothing`** (not mine):
   - The machine must take "started with `--autostart`" as an input (a flag on construction or an argument to the start method).
   - With it set, the start-up reveal and any auto-show must not happen. The tray icon and the key still work.
   - The test should assert no reveal state and no window shown after the start sequence, and the same with the flag off as the control.
   - `CommandLine.Autostart` from request 2 is the source of the flag.
5. **`GuardTests.Registry_Is_Written_In_One_Outside_File`** (not mine):
   - Strip comments, as `OutsideGuardTests` does.
   - Search all `src` and `tools` `.cs` files for the framework's writing members: `CreateSubKey`, `SetValue`, `DeleteValue`, `DeleteSubKey`, `DeleteSubKeyTree`, `RegSetValueEx` and `RegDeleteValue`. Confirm the list on the `Microsoft.Win32.RegistryKey` page.
   - Require that every hit is in exactly one file whose name begins `Outside` and which contains `OutsideGate`.
   - My folder contains none of those words.
6. **`OutsideKind.WriteStartupValue`** already exists in `OutsideGate.cs`. Nothing to add.

## 6. How to wire it in

- **Registry class:** write `OutsideStartupRegistry : IStartupRegistry` in `src/Island.App/`, the only file that touches the real registry.
  - Open the current user's `StartupKey.KeyPath`.
  - `Read()` returns the string under `StartupKey.ValueName`, or null for missing, not a string, or any error.
  - `Write(text)`: `if (!OutsideGate.Current.Allow(OutsideKind.WriteStartupValue)) return false;`, then write a REG_SZ. Catch exceptions and return false.
  - `Remove()`: ask the gate with the same kind, then delete the value. Return true if the value is gone or was never there. Catch exceptions and return false.
  - Reads need no gate.
- **Switch:** `new StartupSwitch(new OutsideStartupRegistry(), Environment.ProcessPath ?? "")`, one per process. `ProcessPath` is `dist\Island\Island.App.exe` when run for real.
  - It is not thread-safe. Call it from the UI thread on a click, or one at a time.
  - `IsOn()` reads the registry every time. Call it when the settings screen opens and after each flip, not on every frame.
  - A `Refusal` (the too-long path) is shown with `result.Refusal.Message`. `Ok=false` with no `Refusal` is shown as "Windows would not take the change".
- **Self-test:** flip through the same code the screen calls, using a switch over `OutsideStartupRegistry` under `OutsideGate(selfTest: true)`. `TurnOn` and `TurnOff` return `Ok=false`, and `OutsideGate.Refused(WriteStartupValue)` counts the refusals. Put that count in `selftest.json`.
  - A pure stage over `MemoryStartupRegistry` can test the logic without the gate. Never over the real hive.
- **At launch:** build the switch only to show its state in the settings screen. Never call `TurnOn` or `TurnOff` at launch.
- **Autostart launch:** when `CommandLine.Autostart` is set, start silently (see request 4).
