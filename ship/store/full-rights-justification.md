<!-- Source page: https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/manage-submission-options (page date 2022-10-30, updated 2026-08-17), "Restricted capabilities": for each capability, say why the app needs it and how it is used; and https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations (2026-10-02) for runFullTrust. Details in ship/SOURCES.md, sections 3 and 4. -->

# Why Island must run as an ordinary desktop program (the restricted capability `runFullTrust`)

Text for the Partner Center box "Restricted capabilities" of the submission options: paste only what is between `PASTE FROM HERE` and `PASTE UNTIL HERE`. It is written from the code: Island is a classic desktop program (a WPF program with a tray icon) that is packaged, not rewritten as a sandboxed app.

PASTE FROM HERE

In one paragraph: Island is a small always-on-top window, with a global key, a tray icon and a reader of which windows are open, that starts and brings forward the person's own programs. None of that can be done from inside an app sandbox. It asks for nothing else of the system, it never contacts the internet, and every action that reaches outside the program passes through one place in its code that names what kind of action it is.

## The three things the whole program rests on

- **The global key.** Island registers one key of the person's choosing (Ctrl+Q at first) with Windows so that it can be called from any program; it also registers keys for pages, for single things and for scenes if the person sets them. Without full trust it cannot register a key that works while another program is in front.
- **The tray icon.** Island lives in the notification area (an icon near the clock) with a menu to show or hide it, open settings, choose the glass, the mode and a scene, and quit. It starts quietly there when Windows starts it.
- **Reading which windows are open.** Island shows which of the person's chosen programs, folders and sites are open, one dot for each window, and which window is in front so that it stays away from games and other fullscreen programs. It reads window handles, titles and the names of the programs' files in memory; it writes none of that anywhere.

## One sentence for each kind of action that reaches outside the program

Island's code allows an action on the outside world only through one door that asks for the kind of action first (`OutsideGate`). These are all the kinds there are.

### StartProgram
Starts a program the person chose, when they click its tile or press its key (or run a scene); it is the same as the person double-clicking that program.

### OpenFolder
Opens one of the person's chosen known folders (Downloads, Documents and so on) in File Explorer when they click it.

### OpenAddress
Hands the address of a site the person chose, or the results page of a search the person typed, to their default browser, which then does the rest.

### BringForward
Brings a window the person already has to the front when they click its tile or press its key; it never forces a window forward against Windows' own rules.

### MediaCommand
Sends play, pause, next or previous to the media session Windows reports (what is playing), when the person presses the matching button on the island's Media page or on its small pill.

### TabCommand
Tells the Island browser add-on, over a connection on the same computer only, to switch to a tab or press play, pause, next or previous in a tab's page, or to close a tab, when the person asks for that on the island.

### OpenFile
Opens Island's own settings file in the person's text editor when they choose "Open settings file" in the tray menu (when packaged the settings screen opens instead), and opens a file, a folder or a program that the person added to the island by hand, with Windows' own handling, when they click it.

### StartOwnCopy
Starts a second copy of Island itself; only the program's own self-test uses this, to check that a second copy is turned away, and a person never triggers it.

### WriteStartupValue
Switches "Start with Windows" on or off, and only when the person flips that switch in the settings; in a package this is the package's start-up task, switched off until the person asks.

### PlaySound
Plays one short system sound when a notice that a coding agent has finished has to wait behind a fullscreen program in Focus mode; Vibe and Do not disturb play none.

### EditAgentSettings
Adds small hook entries to the settings file of a coding helper (Claude Code or Codex; the entries are shown to the person first, line by line), only when the person presses "Connect" in the settings screen after reading exactly what will be added, and removes only those entries when they press "Disconnect"; a copy of the file is saved beside it first.

### CloseWindow
Asks the window the person just brought forward to close, as a click on its own close button would, when they press the island's close button; it never closes a program that runs with administrator rights.

### ChoosePlace
Opens Windows' own window for choosing a file or a folder, in front of Island's settings screen, only when the person presses "Browse…", "A folder" or "A file" in "Add…" to put something of their own on the island; Island sees only what they chose.

## Nothing else

Island declares one other capability: `globalMediaControl`, a general-use capability, so that it can read which media is playing (`Windows.Media.Control`). It asks for no internet capability, no file-system capability and no device capability.

PASTE UNTIL HERE

## Notes for the owner (not pasted)

The first thing to check in a package is that the add-on connection, the global key and the tray icon work (`ship/CHECKS-FOR-OWNER.md`); none of the three has been seen in a package.
