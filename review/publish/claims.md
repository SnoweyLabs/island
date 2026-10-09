# The claims of the public texts, and where each was checked (WORK-ORDER-14 section 4)

Written 2026-10-09. The texts: `ship/public/README.md` (the README at the top of the public copy), `ship/public/release-notes.md`, `ship/web/island/index.md`, `ship/CE-FACI-TU.md`. "Measured" means in this run; `review/publish/contents.md` has the commands.

| Claim | Where checked |
|---|---|
| One key brings it; Ctrl+Q by default; another key in Settings → Your key | `Settings.Defaults` (`src/Island.Core/Settings.cs`, `HotkeyCombo.Parse("Ctrl+Q")`); section names in `src/Island.SettingsUi/SettingsView.cs` (`FullSections`) |
| Pages Media, Folders, Apps, Vibe coding, Browser, Terminals | `src/Island.Core/Page.cs` built-in pages; the sixth page of WORK-ORDER-11 |
| Tab / Shift+Tab, Left/Right, Enter, Down, Shift+Enter, Up, Space on Media, Delete twice, Esc | `SettingsText.IslandKeys` (`src/Island.Core/SettingsEdit/SettingsText.cs`), the list the Key section shows; `IslandKeys` |
| A page can have a key of its own | KeySection "A key straight to one page" (`src/Island.SettingsUi/KeySection.cs`) |
| The + on the island; Settings → What goes on the island → Add… for a program, folder, website or file | `SettingsView.cs` (section title), `AddByHand.cs` ("Add…", "a program, a folder, a website or a file") |
| Settings opens from the tray icon | `TrayIcon.cs` (menu item "Settings") |
| The first start has a setup with a practice on the real island; "Run the setup again" in General | `GeneralSection.cs` ("Run the setup again"), `Tutorial.cs`, `FirstStart.cs`; WORK-ORDER-13 G |
| Terminals page fills itself; blue = working, orange = waiting, green = finished; a notice when finished | WORK-ORDER-11 close-out (STATE.md), `TerminalsPage` tests; the notice: WORK-ORDER-7 §4 |
| Connect shows the lines and the file first and writes nothing until confirmed | `OutsideClaudeSettings.cs` / `OutsideCodexSettings.cs` (Connect only from the confirming button), `AgentsSection.cs` |
| Claude Code: `%USERPROFILE%\.claude\settings.json`; Codex: `%USERPROFILE%\.codex\hooks.json` | `OutsideAgentConnector.Location`, `CodexHooks.PlaceText` |
| A copy of the file is saved before the first change; Island.Notify copied into `%LOCALAPPDATA%\Island\notify` | `OutsideClaudeSettings.cs` (summary and `NotifyFolder`) |
| Disconnect removes exactly those entries | `IAgentConnector.Disconnect` ("Removes exactly the entries that run Island.Notify and nothing else"), `HookInstaller.Disconnect` tests |
| The add-on: optional; tabs, which plays, switch to a tab, close it; loaded by hand from "Open the add-on folder", chrome://extensions, Developer mode, Load unpacked | `GeneralSection` button (WORK-ORDER-13 P11), `AddonFolder.Find`; `ship/app/extension/` exists (measured); `extension/PROTOCOL.md`; the Chrome steps are the ones `extension/README.md` gives |
| The add-on talks only to 127.0.0.1, fetches nothing, sends to no server | `ExtensionGuardTests.No_Remote_Fetch_And_No_Icon_Service`, manifest host list (WORK-ORDER-9 prohibitions) |
| Island never contacts the internet; no account, analytics, ads or update check | `OutsideGuardTests.App_Has_No_Internet_Client`; no updater exists in `src/` |
| What it reads stays in memory; search text never written | WORK-ORDER-3 and -11 prohibitions; `GuardTests.Typed_Text_Is_Never_Logged`; `ship/store/privacy.md` (checked against the code in WORK-ORDER-8) |
| Writes only settings/pages/picks/scenes in `%APPDATA%\Island` and a log in `%LOCALAPPDATA%\Island` | `AppFiles.cs` (the four paths and `LogPath`) |
| Writes outside those only on a button: Start with Windows (one value in the user's start-up list), Connect | `StartupSwitch` (never at launch), `StartupKey` (HKCU Run, value "Island"); the connectors |
| Installs for you only, no administrator rights, Start menu, can start at the end | `ship/installer/island.iss` (`PrivilegesRequired=lowest`, `{userpf}`, `{userprograms}`, `[Run] postinstall`); `InstallerScriptTests.It_Installs_Per_User_Without_Administrator_Rights`; Inno Setup help (`Research/publish-facts.md` A1, A3, A9). **Not seen on a screen: the real installer was not run here** (NEEDS-HUMAN-VERIFY) |
| Windows shows "Windows protected your PC"; More info, Run anyway | Not checked on this computer (the real installer was not run, and no screen is captured). What Windows shows for an unsigned download is from the work order and common knowledge, **not from a page read in this run** |
| Uninstall from Settings → Apps; it asks to close a running Island; disconnects helpers; Start with Windows off; removes the program and `%LOCALAPPDATA%\Island`; keeps `%APPDATA%\Island` | `island.iss` (`AppMutex` = the app's own lock name, `[UninstallRun] --uninstall-cleanup`, `[UninstallDelete] {localappdata}\Island`, `UninstalledAll`); `UninstallCleanupTests` (three named tests); `InstallerScriptTests`; Inno help A6, A9, A11. Whether Settings → Apps lists a per-user install is from Inno's help (HKCU uninstall key); Microsoft's page was not found (`Research/publish-facts.md` B2, UNVERIFIED there) |
| Windows 11 x64; tried on one laptop; never on Windows 10, ARM or another computer; the installer refuses neither | `MinVersion=10.0`, `ArchitecturesAllowed=x64compatible` in `island.iss`; the close-outs |
| About 54 MB | measured: 56,479,156 bytes |
| SHA-256 `78ff15bc…4b21e7b` | measured (`sha256sum`), and again in section 5 from the release |
| The link `…/releases/latest/download/Island-Setup.exe` always gives the newest | docs.github.com, "Linking to releases" (`Research/publish-facts.md` C4); the file's name carries no version |
| No automatic updates | no updater in `src/`; `OutsideGuardTests.App_Has_No_Internet_Client` |
| Build it yourself: `dotnet build`, `dotnet test`, `dotnet run`, `build.ps1 -Installer` | run in this session: `dotnet build`/`dotnet test` in `dist/public` (section 1); `build.ps1` made the installer (section 3) |
| The pictures are drawn by the self-test with invented names | `review/apps-dark.png` and `review/media-playing.png` come from the self-test (WORK-ORDER-3 rule: nothing from the real desktop under `--selftest`); looked at in this run: "Editor", "clip one", "Alpha track", "Alpha-player" |
| Built with Claude Code from written work orders, by SnoweyLabs | the work orders and git history of this folder |
| MIT licence; the .NET runtime is Microsoft's under MIT | `ship/public/LICENSE` (choosealicense.com text, `Research/publish-facts.md` D); `ship/THIRD-PARTY-NOTICES.md` and `ship/third-party/` |
| (CE-FACI-TU) the settings in `%APPDATA%\Island` carry over to the installed island | `AppFiles.cs`: the same paths whatever folder the program runs from |
| (CE-FACI-TU) Start with Windows shows off in the installed island when it was on for `dist\Island` | `StartupSwitch.IsOn`: on only when the value names this program; turning it on overwrites the one value "Island" |
| (CE-FACI-TU) Claude Code and Codex keep working without anything to do | the hooks name the copy in `%LOCALAPPDATA%\Island\notify`, not the program's folder (`OutsideClaudeSettings.NotifyExe`) |
