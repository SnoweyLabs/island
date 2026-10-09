; Island's installer (WORK-ORDER-14 section 3), compiled by build.ps1 with Inno Setup 6's own compiler (ISCC).
; Every directive is from the Inno Setup help; what was read, where and when is in Research/publish-facts.md, section A (not in the public copy).
; Compiled with /DTestCopy it makes the quiet test installer of section 3: no uninstall entry and no uninstaller, no Start menu or desktop item,
; no "Start Island", no clean-up, and no check for a running Island (the island on this computer is the owner's and is never asked to close).

#define AppName "Island"
; The version shown in Windows Settings and on the release; InstallerScriptTests checks it against Directory.Build.props.
#define AppVersion "1.0.0"
#define AppFileVersion "1.0.0.0"
#define AppPublisher "SnoweyLabs"
#define AppExe "Island.App.exe"

[Setup]
; The key of the uninstall entry; never shown.
AppId=SnoweyLabs.Island
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://snoweylabs.com
VersionInfoVersion={#AppFileVersion}
; For the current user only, never asking for administrator rights: the uninstall entry goes under HKEY_CURRENT_USER, and
; {userpf} is the user's own programs folder, %LOCALAPPDATA%\Programs. Leaving PrivilegesRequiredOverridesAllowed unset offers no other mode.
PrivilegesRequired=lowest
DefaultDirName={userpf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\app\LICENSE.txt
SetupIconFile=..\package\Assets\Island.ico
WizardStyle=modern
OutputDir=out
#ifdef TestCopy
OutputBaseFilename=Island-Setup-testcopy
#else
OutputBaseFilename=Island-Setup
#endif
; As tight as the help allows for this build: the strongest preset, one solid stream, one block, and a dictionary (256 MB) larger than
; everything that is packed (about 200 MB), which needs the compressor in its own process. A larger dictionary cannot find more.
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=262144
LZMANumBlockThreads=1
; The build is for x64 Windows; x64compatible also lets an ARM computer that runs x64 programs install it (never tried there).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Never closes a program by itself (Restart Manager): a running Island is found by its own one-copy lock, and the person is asked.
CloseApplications=no
#ifdef TestCopy
Uninstallable=no
#else
AppMutex=Local\Island.Snowey.Running
#endif

[Tasks]
#ifndef TestCopy
Name: "desktopicon"; Description: "Put Island on the desktop too"; Flags: unchecked
#endif

[Files]
; The build of section 2 (ship\app), file for file, with the licence and the third-party notices it carries.
Source: "..\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifndef TestCopy
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
#endif

[Run]
#ifndef TestCopy
Filename: "{app}\{#AppExe}"; Description: "Start Island"; Flags: postinstall nowait skipifsilent
#endif

#ifndef TestCopy
[UninstallRun]
; First, before any file goes: what Disconnect does for every connected helper (Claude Code, Codex), and Start with Windows off.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "IslandCleanup"

[UninstallDelete]
; The program's own folder: its log and the copy of Island.Notify that helpers ran. The person's settings and picks (roaming AppData) stay.
Type: filesandordirs; Name: "{localappdata}\Island"

[Messages]
UninstalledAll=%1 was removed from your computer.%n%nYour own settings and picks were kept, in the folder AppData\Roaming\Island inside your user folder, so that installing Island again brings everything back. Delete that folder if you do not want them.
#endif
