<!-- Source pages for why this text exists: https://learn.microsoft.com/en-us/windows/apps/publish/store-policies (version 7.20, 2026-09-14), policy 10.5.1: Desktop Bridge and Win32 products must always have a privacy policy; https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info (2022-10-30, updated 2026-04-21): the privacy policy is a link or text entered directly. Details in ship/SOURCES.md, section 4. -->

# Island — privacy policy (working copy)

**What to publish:** only the text between the two lines `PUBLISH FROM HERE` and `PUBLISH UNTIL HERE`. Everything outside them is a note for the owner and must not be published.

Written from the program's code, not from a description of it; the notes after the published text say which checks stand behind each sentence. Where a store's form asks a question whose answer is a legal judgement, the notes say so and leave it to the owner.

PUBLISH FROM HERE

# Island — privacy policy

Contact: __OWNER__

## In one line

Island does not connect to the internet, has no account, and sends nothing anywhere. It keeps a few small files of your choices on your own computer and reads, in memory only, which windows, media and browser tabs are open while it runs.

## What Island writes to your computer

Island is the only program that writes these. Each is a small text file; none holds a password, a payment detail or a path to one of your files.

- **settings.json** — your main key and the keys you gave to pages, things and scenes; how long the island waits before it leaves; the glass; which kind of moving light runs on the island's edge; the mode; whether the small pill is shown; how long the notice stays; the list of programs it never appears over (a name you gave and the program's file name, never a path); and whether Start with Windows is on. A number says which version of the file it is.
- **pages.json** — the pages: an id, a name and a colour for each.
- **picks.json** — the things you put on the island. For each: a name, the page it is on, and what it is. A program is kept by its name and its executable's file name or its package family name; a folder by the name of a known folder (Downloads, Documents and so on); a site by its host (for example the part of an address before the first slash). The one exception is what you add by hand in the settings ("Add…"): a program you browse to, a folder that is not one of those known folders, or a file is kept by the location you chose, in one field of this file, written with `%USERPROFILE%` in place of your user folder so that your account name is not in it. A full web address is never kept, only its host.
- **scenes.json** — the scenes: a name, and the things in it, kept as picks are.
- **island.log** — one line for each event of the program's own life, with the time: that it started and stopped, that a key was accepted or refused by Windows, that a refusal was shown (the wording of a refusal can name a page, a scene or a thing you chose, and a refusal about a damaged settings file can carry a short fragment of what the reader objected to), that the browser add-on's listener started, and that an add-on connected, left or was refused (the kind of event and, for a refusal, a running number: never a browser's name, a tab or a site), and that the island stayed away because of what was in front. It never holds a window title, a list of programs, your user name, or anything you typed.
- **Where they are:** outside a package, settings, pages, picks and scenes are in your account's application data folder under Island, and the log in the local application data folder under Island. In a Store package, Microsoft's documentation says Windows keeps these files in a private place that belongs to the package and removes them when the app is uninstalled.

Two things Island writes only when you press a button, and only after showing you what:
- **Start with Windows.** Switched on, it adds one value to your own start-up list (outside a package) or asks Windows to enable the package's start-up task (in a package). Switched off, it removes it. Island never writes this at launch and never to repair it.
- **Connect (a coding helper: Claude Code or Codex).** After showing you the exact lines and the place of the file, and after you confirm, it saves a copy of that helper's own settings file beside it, then adds small entries to it, and (outside a package) copies the small program that the helper's hook runs into Island's own folder. **Disconnect** removes only those entries. Island does this for no other program and never without that press.

## What Island reads, in memory only

While it runs, Island reads: which windows are open and which is in front, with their titles and the names of their programs' files (to show open things, to count windows, and to stay away from games and other fullscreen programs); which folders are open in File Explorer (only which of the known folders you chose are open); the icons of programs; the programs installed on your computer, from Windows' own lists in the registry and the Start menu (to offer them when you add a thing and to start them); which media is playing and, where the player reports it, its title, artist and position (for the small pill and the Media page); and, if you use the add-on, the title and host of your browser tabs. None of this is written to a file. What you type into Island's search is used only to find matches on the screen and is never written anywhere: not to the log, not to a file. While the Terminals page is on the screen, Island also reads the list of running programs (the names of their files and which one started which) and the titles of terminal windows, to show one round tile for each terminal window and to tell which helper runs in which; it reads nothing of what is typed or shown inside them, and it does not read that list while the page is not on the screen. Nothing a coding agent's hook sends (a folder, a session's id, a session name, the name of a project) and none of those names or titles is written anywhere either; it stays in memory while the notice or the page is on the screen.

## What the browser add-on reads and where it goes

The add-on (a separate download) reads, for each tab: its title (cut to 200 characters; **a title can contain personal information, because websites put names and mailbox addresses in titles**), the host of its address (never the full address, never a query, never the page's contents), whether it makes sound, whether it is the active tab, a pinned tab or a tab of a private window (where the browser lets the add-on run there), and the small picture the browser has stored for the site, taken from the browser's own cache and never asked of the website; on five named media sites it also reads what is playing (title, artist, position and length, state). It can also switch to a tab, close a tab and press the page player's buttons, only when Island asks. It sends what it reads to a program listening on this same computer's own address (127.0.0.1) on one of five ports it tries in turn, which should be Island; the add-on cannot check which program answers, and the connection is not protected by a secret. It never sends anything to a server. Island keeps none of it in a file: what it keeps is only the things you choose to put on the island yourself. The add-on stores one invented identifier in the browser's own storage so that several browsers can be told apart; it contains nothing about you.

## What Island does not do

- It contacts nothing on the internet and has no account, no analytics, no advertising and no update check of its own: Windows updates a Store package.
- It does hand an address to your browser when you click a site you put on the island, or when you use "Search on" a service: your browser then contacts that site, as if you had typed the address yourself.
- It listens on your own computer only: a connection on 127.0.0.1 for the add-on, and a pipe that only your own Windows account can open for the small program that tells Island a coding agent has finished.
- It does not read your files, your clipboard, what you type in other programs, your screen, or your browsing history. It installs no keyboard hook: it registers keys with Windows the ordinary way.
- It does not sell, share or send anything to anyone.

## Removing it

Uninstalling Island removes its files in a package. Outside a package, delete the Island folders named above, and switch Start with Windows off first. "Disconnect" in the settings screen removes the entries it added to Claude Code's settings file.

PUBLISH UNTIL HERE

## Notes for the owner (not published)

Checks behind the sentences above (the guards run with every verify):
- No internet client: `OutsideGuardTests.App_Has_No_Internet_Client`. No keyboard hook: `GuardTests.No_Keyboard_Hook_Or_Fake_Input_In_Source`. Search text never logged: `GuardTests.Typed_Text_Is_Never_Logged`. The settings location of Claude Code lives in one gated file: `GuardTests.Claude_Settings_Location_Is_In_One_Outside_File`. Files: the places that write them are `Settings.Save`, `PageStore.Save`, `PickStore.Save`, `SceneStore.Save`, `AppFiles.Log` and the one file `OutsideClaudeSettings.cs`.
- The add-on's side: `extension/PROTOCOL.md` ("Transport" and "Messages from the add-on") and `ExtensionGuardTests`.
- The sentences about a Store package come from Microsoft's documentation (`ship/SOURCES.md`, section 2); a package has never run here.

What the owner must decide (a legal judgement, not this file's):
- Whether Island, or its publisher, counts as a "controller" of personal data under any law, and in which country: on the facts above nothing leaves the computer, which is what a store's privacy questions mostly ask, but the answer to the legal question is the owner's.
- Whether to answer the stores' "does this app collect personal information" question yes or no: the Store's own page says that capabilities which could allow personal information to be accessed make it mark the question Yes; the owner answers.
- The contact in the published text, which the stores may show publicly, should be a mailbox that belongs to the brand and not to a person.
