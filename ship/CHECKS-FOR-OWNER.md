# Checks for the owner, in a packaged build

Written for the first real run of the package, which the owner does with Claude beside him (`ship/store/CE-FACI-TU-store.md` has the same list in Romanian, one action and one yes/no question each). Nothing below has been tried: the package has never run (WORK-ORDER-8.md, section 0), and a package file was not even made here (no packing tool on this computer).

## Try first

1. **The listener for the add-on.** In a packaged build, does the Island add-on in Chrome connect to the island (the tile of a media site lights up when a tab of that site is open)? Microsoft's pages do not say whether a packaged full-trust program may listen on 127.0.0.1 for a program outside the package; nothing in the code was changed for it. If it does not connect, nothing else about tabs will work.
2. **The global key and the tray icon.** Does the main key bring the island while another program is in front? Is the island's icon near the clock? No page confirms or denies either for a full-trust package (UNVERIFIED).

## Then

3. **Start with Windows.** Settings, General: switch it on, sign out and in again: does the island start by itself and stay silent (icon and key only, nothing on the screen)? Then, in Windows' own Settings, Apps, Startup, switch Island off: does the switch in Island say so and refuse to switch it back on over that "no"?
4. **Connecting Claude Code, packaged.** (This edits Claude Code's own settings file, which is outside Island's folder; Island shows the lines first, asks, and saves a copy beside it.) Settings, Coding agents: the lines it shows name `Island.Notify.exe` (the package's own command name) and not a path. After connecting, does a finished task in Claude Code bring the notice? Whether a program outside the package (Claude Code's launcher) finds a command by that name is stated only as "users and other processes can use an alias" (UNVERIFIED in practice).
5. **The settings file.** The tray's "Open settings file" opens the settings screen instead, in a package (the file lives in the package's private place). Does it?
6. **Settings survive an update.** After the Store updates the app, are the keys, pages, picks and scenes still there? (EVALS U2 is proven only between the file shapes of today; U3, that an update is applied at the next start, is the Store's own doing.)
7. **The first start.** On a computer that never had Island: does the setup of five steps open by itself, and does "Done" leave the island open?
8. **The icon.** Which of the three candidates (`review/choices/app-icon.png`): A the ball with its lit rim in the blue, B the capsule with the light at the top in the red, C the ball with the five page colours? A is what is in the package now.
9. **The package's behaviour that nobody has seen:** files under the private location, the registry writes (a package writes none: the start-up task replaces the Run value), the update at the next start.
