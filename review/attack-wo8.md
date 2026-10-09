# Attack on WORK-ORDER-8 (ship/): find the owner

Reviewer: adversarial, read-only. Date of the run: 2026-10-07. Nothing in this file names a person, an account or a computer. Findings are given by file and kind only. "The owner's first name" stands for the first word of the full name Windows holds for the running account; it is never written here.

Totals: 1 HIGH, 4 MEDIUM, 17 LOW, 4 LATENT.

## 1. What was searched, and how (routes)

The needles were read from the running system and used only inside throw-away scripts kept outside the project: the account name, the profile folder's path and name, the computer's name, the working directory, each word of the account's full name (read twice: through the Windows display-name call the repo's scanner uses, and through the local user-account record), the local part of the owner's real mailbox, the vault folder's name in four spellings, and drive-letter prefixes.

Files covered: all 562 files under `ship/`, among them the 488 files of `ship/app/` (git-ignored, listed by hand, all subfolders opened), the 74 pictures, the add-on zip (entry names, bytes, comment, extra fields, dates, attributes) and every `.md`.

Routes beyond the repo scanner (`TraceScanner.cs` + `ShipGuardTests.cs`):

1. Raw bytes, two-byte text (both byte orders), reversed text, Base64 at three alignments, hex, URL-encoded, JSON-escaped, `\` / `/` / `\\` forms, UTF-7, and names split by `.`, `-`, `_`, space or a NUL (the two-byte form seen byte by byte), case folded. Short needles (3 to 5 letters) were matched as whole words inside extracted strings, because raw matching drowns in Microsoft binaries.
2. Every printable string (ASCII and two-byte, 5+ characters) of the 12 `Island.*` programs and libraries was extracted and read in full (2,929 literals), not only grepped. The same grep patterns were run for drive letters, `Users`, `@`, `http`, `.pdb`, `.cs`, `.xaml`, project and vault names, git, commit ids, mailbox providers.
3. PE headers of every `Island.*` file: debug directory entries (CodeView, embedded PDB, PDB checksum, reproducible-build marker), link timestamps, version resources (Company, Product, Copyright, Comments, original and internal file names), assembly attributes by name, manifest-resource list (none), `.deps.json` and `.runtimeconfig.json` read.
4. PNG chunk lists of all 74 pictures and of the 13 PNGs embedded in `Island.ico`, `Island.App.dll` and `Island.App.exe` (text, time, EXIF, colour-profile chunks): looked at chunk by chunk, not only by name. Every picture was opened and looked at (store screenshots, add-on screenshots, promo tile, app tile).
5. Add-on zip: entry timestamps, extra fields, comment, create-system, attributes, byte-for-byte comparison with `extension/`, manifest `key`; the `extension/` scripts read in full.
6. Alternate data streams and hidden or system files under `ship/` (none).
7. Emails, URLs and capitalised two-word names across all text files (only vendor pages and third-party notices turned up).
8. Git history of the repo (author and committer values, remote), `Directory.Build.props`, search of the repo for private keys or `.pem`/`.pfx`/`.crx` files.
9. Claims: every sentence of the six public texts was compared with `src/`, `extension/` and the guard tests; `CE-FACI-TU.md` against `SOURCES.md` and `Research/shipping.md`.

## 2. Findings

Format: id, severity, file, kind; why it matters; suggested fix (and how the scanner can catch it next time).

### A. Traces of the person or the computer

**A1. HIGH. `ship/app/Island.App.dll` (a two-byte string literal; source: the self-test cost report in `src/Island.App/CostStage.cs`, line 139; the same word is in a comment of `src/Island.App/AppHost.cs`). Kind: a word of the full name (the owner's first name).**
The sentence "the app as <first name> runs it ..." is compiled into the program that goes to the Store. Anybody who runs a strings tool on the package finds the first name next to the brand "SnoweyLabs". This is exactly what the owner's rule forbids ("in files he is the owner"). Only this one literal of the 2,929 carries it.
Why the scanner missed it: `TraceScanner` looks for the words of the full name only as whole words in files with a text extension; in programs it looks only for the account name, paths and the computer's name (the code comment says three letters are in every program).
Fix: replace by "the owner" (and in the `AppHost.cs` comment); rebuild `ship/app/`. Scanner: for files named `Island.*` (not Microsoft's), extract the user-string heap (System.Reflection.Metadata `GetUserString`) and the ASCII/two-byte runs and apply the whole-word regex of the full-name words there. A planted-word test in a tiny PE or a byte array with a two-byte word would pin it. Also add the local part of the owner's mailbox and the vault spellings as needles (they were absent; no hit today).

**A2. MEDIUM. `ship/app/Island.Core.dll` (two-byte literals; source: `src/Island.Core/Page.cs` lines 57-95, the placeholder rows; `IslandMachine` falls back to them when no item source is given). Kind: personal habits, a local path, a project name.**
The sample rows were copied from the owner's own desktop mock-up, not invented: a folder on the `D:` drive, the names of his video and coding programs, a webmail host (the same provider as his real mailbox), a short-video creator-studio site, an earlier name of this project used as a repository title, a project folder name as a subtitle. Together with the nickname and the brand this is a profile of one person that can be searched. The Store screenshots use invented names, so only the binary leaks it.
Fix: replace the whole table by invented neutral samples (the self-test already uses "Alpha", "Beta"). Scanner: a guard on `Pages.AllPlaceholders` (no drive letter, no host from a list of real mail and social providers), and the same list as needles for strings in `Island.*`.

**A3. LOW. `ship/app/Island.App.dll` (many literals) and the class names `ShipAssets`, `ShipScreenshots`. Kind: the way the app was built.**
The shipped build carries the self-test, the cost and performance reports, the asset renderer and their text: work-order numbers, evaluation ids, a date of a measurement, "one laptop", "Tell Claude.", relative paths `review/...` and `ship/...`, switches `--render-assets`, `--cost`. Not identity, but it says how and with what the program was made. Fix: compile these under a symbol (or a separate assembly) that the shipping publish does not include; scanner: a denylist of these words in `Island.*` strings.

**A4. LOW. `ship/CE-FACI-TU.md`, step 6 (hosting the policy). Kind: a URL that may carry a name.**
The step says "brand account, not personal" but not that the address itself (a sub-domain or path made from the account name by most page hosts) must contain no personal name. Add one sentence; the owner should check the final URL before pasting it into either store.

**A5. LOW. `ship/store/privacy.md`. Kind: internal notes in a public text.**
The file starts with an HTML comment naming `ship/SOURCES.md`, contains guard-test names and `extension/PROTOCOL.md` in brackets, a closing section "What the owner must decide" and the placeholder contact. Step 6 of the owner's page says to host "the text" without saying that those parts must be cut. Not identity (no name), but "the owner" and internal file names would be public. Fix: a second, publishable file (no comment, no brackets, no decision section) or a sentence on the owner's page.

**A6. LOW. `ship/store/full-rights-justification.md`. Kind: internal references in text meant to be pasted into Partner Center.**
It points to `ship/CHECKS-FOR-OWNER.md` and says "the owner checks" the add-on connection first. Remove before pasting.

**A7. LOW. `extension/lib/media.js` and `extension/content/media-main.js` (inside the add-on zip). Kind: working notes public in the add-on's source.**
Comments say the five sites were never opened and that button selectors are "UNVERIFIED guesses". Everyone can read an add-on's source in the store. The owner's page lists which files hold `UNVERIFIED`, but not these two, because `Every_Placeholder_Is_Listed_On_The_Owners_Page` skips `.js` and zips.

### B. Gaps in the instructions for the owner (`ship/CE-FACI-TU.md`)

**B1. MEDIUM. Step 9 (Chrome Web Store). Kind: a step that can expose the real name without a warning.**
Warned: the contact mailbox, the trader/merchant answer. Not mentioned: (a) the name shown as the publisher on the listing and where it comes from (the page that would say whether a brand name can be used is not recorded in `SOURCES.md`; it should be listed as `UNVERIFIED` like the Microsoft one in section 8 of that file); (b) the first and last name typed when the Google account is created, which is the likeliest default; (c) the payment profile (name and address) used for the registration fee. Add a warning and the instruction "before submitting, open the listing preview and read the name shown; if it is the real one, stop and tell Claude".

**B2. LOW. Claims on the owner's page that no page recorded in `ship/SOURCES.md` backs.**
They exist in `Research/shipping.md` ([S8], [S9], [S10], [S20], [S22], [S46], [S47]) but `SOURCES.md` is the record the owner's page names. Not recorded in `SOURCES.md`: phone and address optional and "not displayed" for individuals (step 3); trader details public on the Chrome page and contact mailbox displayed (step 9); support mailbox may be shown publicly (step 2/6); the packing tool's default publisher is the account name (step 1 warning); "Windows updates the app by itself" and the Windows SmartScreen remark (item 1 of "Before anything"); "good light, original documents" for the selfie (step 3). Also: "nesemnat (Store îl semnează singur)" is stated plainly in step 5, while `SOURCES.md` sections 4 and 6 mark an entirely unsigned upload as `UNVERIFIED`. Fix: copy the cited pages into `SOURCES.md`; a test that every `[S..]` cited on the owner's page is in `SOURCES.md`.

**B3. LOW. Commands.** The only command is `winget install Microsoft.winappcli --source winget`, taken from the page recorded in `SOURCES.md` section 6. Safe to run. The text correctly tells him not to run the tool itself. No destructive command appears anywhere. First-run check 6 ("Connect") edits the owner's Claude Code settings file: the text says what the lines look like but not that this is a file outside Island's folder (a backup copy is made by the code).

**B4. Placeholders.** `__FROM_PARTNER_CENTER__` (3 places in the manifest), `__OWNER__` (privacy.md, data-use.md, addon/listing-en.md twice) and `UNVERIFIED` (SOURCES, age-and-category, data-use, CHECKS, plus the two add-on scripts of A7) were all counted. The page lists every file but the two scripts. `Manifest_Has_Only_Placeholders_For_Identity` holds today.

### C. Sentences that are false, overstated, or not backed by code

**C1. MEDIUM. `ship/addon/data-use.md`, "Nothing else: no personally identifiable information ... no personal communications".**
The add-on sends the title of every open tab (any site; private-window tabs too when the owner of the browser allows the add-on there). Tab titles routinely hold names, mailbox addresses and message subjects. The boxes the owner ticks in the dashboard follow this sentence. Fix: say that titles can contain such data and tick the boxes accordingly, or send less.

**C2. MEDIUM. `ship/addon/listing-en.md` (detailed description) and `ship/addon/single-purpose.md`: "show and control what a tab on <five named sites> is playing".**
Nothing was tried on any of the five sites (comments of A7; the owner's page admits first runs are not done). Only the blanket "Not yet tried on other computers" is said. A reviewer or a user reads this as a tested feature. Fix: say "meant to work with ...; not yet tested on these sites" or leave the control claim out until it has been tried.

**C3. LOW. "sends it only to the Island program on the same computer" (`privacy.md`, `data-use.md`, `addon/listing-en.md`, the manifest description).**
True of the address (always 127.0.0.1). The add-on dials the first of five ports (47653-47657) that accepts and, after any `welcome` frame, sends the whole tab snapshot, icons and media reports and obeys switch and close commands. There is no shared secret (`extension/PROTOCOL.md` records this as an owner decision); another local program holding one of the ports would be served. Say "to a program on the same computer that answers on one of five local ports (Island)". Related: `data-use.md` says a guard fails "if any other address appears in the add-on's code"; the guard (`No_Remote_Fetch_And_No_Icon_Service`) catches only literal `http`/`https`/`ws`/`wss`/`ftp` text, not an address built in code.

**C4. LOW. `addon/single-purpose.md`, `addon/listing-en.md`, `data-use.md`.** They list switching to a tab and media buttons; the code also closes tabs on the island's request (`chrome.tabs.remove` in `background.js`; `permissions.md` and the Store full-rights text do say it). Make the four texts agree.

**C5. LOW. `addon/permissions.md`, "it only reads and presses buttons".** `media-main.js` runs in the page's own world and replaces two page functions (`navigator.mediaSession.setActionHandler`, `HTMLMediaElement.prototype.play`), and calls the page's own media-session handlers and the media element's `play`/`pause`. The wrappers pass calls through, but the sentence is stronger than the code.

**C6. LOW. `addon/permissions.md`, host permission `http://127.0.0.1/*` "to connect to Island".** No code fetches that origin; a WebSocket from a service worker does not need a host permission (not verifiable here without running Chrome). `The_Manifest_Asks_For_Only_The_Permissions...` checks only the four API permissions (a substring in any code, comments included), so "None was unused" is not shown for the host permission.

**C7. LOW. `store/privacy.md`, "What Island reads, in memory only".** It leaves out the folder locations of open File Explorer windows (`Island.Sources.Folders`), the icons read out of programs' files and the registry entries read to list installed programs. "It does not read your files" is true for documents but not for program files. Add them.

**C8. LOW. `store/privacy.md`, the sentences on a Store package ("Windows keeps these files in a private place ... removes them when the app is uninstalled"; "asks Windows to enable the package's start-up task") and the header "Every sentence below was checked against the code and the guards".** The package has never been built or run (`CHECKS-FOR-OWNER.md` item 9, first lines of the owner's page). These are statements about Windows behaviour no test saw; several other sentences have no guard.

**C9. LOW. `store/privacy.md`, log line: "Never ... anything you typed".** On an unreadable settings file the refusal logs the framework's JSON error text (`Settings.cs` line 278 to `AppHost.Refuse`), which can include a property path and line position from the user's file. The sentence "the wording of a refusal can name a page, a scene or a thing you chose" is already there; add "or a fragment of the settings file".

**C10. LOW. `store/listing-en.md`, policy notes: "Another company's product name appears once".** "Claude Code" occurs three times in the text proper (twice in the description, once in the features) and a fourth time in the note. The search term "taskbar" is not a feature of this program (policy 10.1.3 asks for relevant terms).

**C11. LOW. `THIRD-PARTY-NOTICES.md`, `size.md`, `SOURCES.md` section 0.** They say the runtime is what is copied into the folder. `Island.App.deps.json` lists a third runtime pack, `Microsoft.Windows.SDK.NET.Ref`, and the folder holds `Microsoft.Windows.SDK.NET.dll` (24 MB) and `WinRT.Runtime.dll`. Their notices and licences are not in `ship/third-party/`. Not about identity.

**C12. LOW. `addon/listing-en.md`, "What it reads".** It lists title, host and sound only; `privacy.md` and `data-use.md` also list active, pinned and private-window flags and the site icon. The three texts must say the same.

Limits checked and found true: Store short description 171 characters, description 2,158, 14 features of at most 90, "What's new" 18, seven search terms; add-on name 11, manifest description 125 (limit 132), detailed description 793; no HTML or URL in the Store description; the default main key is Ctrl+Q; the pipe is open to the current account only (network denied); the app contains no web client (guard confirmed in code); no keyboard hook; no clipboard or screen capture API; 488 files in `ship/app` as `size.md` says.

### D. Latent (not wrong today, can become wrong)

**D1. LATENT. The repo's text.** The owner's first name occurs about 437 times in 75 files of the repository (docs, work orders, 20 source files as comments). It is not in `ship/`, the zip or the add-on source today. It would be in a published repository, in an XML documentation file (`GenerateDocumentationFile` is off) or in a symbol file (none made). Keep the repo private; never publish the source for the policy page.

**D2. LATENT. Packing (`winapp pack`) when it runs.** Per `Research/shipping.md` [S22] its default publisher is the account name; a signature made with a generated certificate carries it; entry dates inside an `.msix` are the build time (local clock, time-zone hint); a resources index (`resources.pri`) may carry build-machine data (not checked, no file exists). `Manifest_Has_Only_Placeholders_For_Identity` and `No_Trace_Of_This_Laptop_In_The_Ship_Folder` do read `Island.msix` when present, but the dates and the PRI contents are not looked at. Suggest: run the scanner and a listing of entry dates on the first package, and pass the publisher and a fixed date explicitly.

**D3. LATENT. Scanner coverage (`TraceScanner.cs`, `ShipGuardTests.cs`).** (1) Words of the full name never searched in programs (A1). (2) Needles come only from the display name; add the user-principal name, the account record's full name and Windows' own first and last name for the signed-in Microsoft account (the mailbox local part was not a needle). (3) No folding of Romanian diacritics: a name typed with `ă â î ș ț` does not match an ASCII needle (a diacritic-aware search found nothing today). (4) `Png_Pictures_Carry_No_Text_Date_Or_Other_Hidden_Data` opens only `*.png` files, not pictures inside `.ico`, zips or programs (checked by hand: clean). (5) Word search covers only listed text extensions (`.ps1` is missing; `.cs`, `.js` are in). (6) No search for Base64, hex, reversed or split names (done by hand: none).

**D4. LATENT. Manifest `key` of the add-on.** It is a 2048-bit public RSA key. It identifies only the add-on (its fixed id is derived from it), nothing about a person or this computer, and appears in every installed copy. No private key, `.pem` or `.pfx` exists in the repo or in `ship/`. If one exists elsewhere on this computer, its folder path is the only thing to keep out of the zip.

## 3. What held

- No hit for the account name, the profile folder's path or name, the computer's name, the working directory, the vault folder's name, the surname, or the mailbox local part, in any form listed in section 1 route 1, in any of the 562 files, inside the add-on zip (names, bytes, comment, extra fields).
- All 74 pictures and 13 embedded pictures carry only colour-space, gamma and resolution chunks. The pictures show invented names (letters, "Tunes", "Chat", "Sunset set", "island"); no clock, no taskbar, no user picture.
- Island's own programs: reproducible-build markers, no CodeView, no embedded PDB, no source paths, no commit ids; Company, Product, Copyright and Authors are the brand only; the only drive path in the two launcher stubs is the vendor's own build-agent path.
- Add-on zip: fixed 1980 date on every entry, no extras, no comment, identical to `extension/` (byte for byte), no tests or protocol document inside.
- Git history: every commit by the nickname with a reserved `.invalid` mailbox; no remote.
- No mailbox or URL anywhere in `ship/` except vendor documentation pages and third-party notices; no personal name except third-party authors in the copied notices (one shares the owner's first name; the scanner correctly skips that folder).
- No alternate data streams, no hidden files, no logs or settings files in `ship/app`.
- The three placeholders are all named on the owner's page; the commands are safe.

## 4. What could not be checked

- The `.msix` (does not exist); its manifest values, signature, entry dates, resource index.
- What the two stores' dashboards show as the publisher name, whether the add-on's `key` is accepted, what Partner Center auto-fills (no web access, nothing signed in).
- Behaviour of the packaged app (private storage, start-up task, add-on connection, alias): never run.
- Whether a WebSocket from the add-on needs the `127.0.0.1` host permission, and whether Chrome's favicon endpoint ever asks the network (needs a browser run).
- Managed code constants held only in IL data blobs other than strings (no managed resources exist; all user strings and metadata names were read). No decompilation was done.
- The Windows-side meaning of Windows profile data beyond the two name sources named in section 1.
