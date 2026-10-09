# What goes public, and what was left out (WORK-ORDER-14)

Written 2026-10-09 by the session that ran WORK-ORDER-14. Every number below was measured in that run, with the command named.

## The public copy (`dist/public/`, section 1)

Made by `ship/public/make-public.ps1`: `git archive HEAD` of this repository (its files, not its history), and at the top the licence, the README and the third-party notices. Its `.gitignore` loses the two lines that keep the workshop's notes out of git (`/*.md`, `/Research/`), so the README and the notices can be committed there.

957 files: the 954 that git holds at the commit it was made from, plus `LICENSE`, `README.md` and `THIRD-PARTY-NOTICES.md`.

| Folder | Files | What it is |
|---|---|---|
| `src/` | 363 | the program: Island.App and its libraries, Island.Notify, the small smoke programs |
| `tests/` | 323 | the test projects (`dotnet test Island.sln`) |
| `review/` | 136 | the self-test's pictures and the review notes of the work orders (paths made relative) |
| `ship/` | 84 | the installer script and its build script, the public texts, the site's page, the third-party licence files, the earlier Store preparation (unused) |
| `extension/` | 29 | the optional Chrome add-on and its tests |
| `tools/` | 14 | BlurProbe, the probe that decided the blur |
| `reference/` | 1 | `island-previews.html`, the approved look |
| top | 7 | `Island.sln`, `Directory.Build.props`, `.gitignore`, `.ignore`, `LICENSE`, `README.md`, `THIRD-PARTY-NOTICES.md` |

## Left out

- **This folder's history**: the public repository starts with one commit of its own.
- **The workshop's notes**, every `.md` at the top of this folder (work orders, briefs, STATE.md, LOG.md, DECISIONS.md, EVALS.md, HOUSE-RULES.md, PROPUNERI.md, CLAUDE.md) and `Research/`: git never held them.
- **Built and local things**: `dist/`, `.worktrees/`, `ship/app/` (the build), `ship/installer/out/` (the installers), every `bin/` and `obj/`.
- **The workshop's scripts for its own copies under `dist/`**: `run.cmd`, `stop.cmd`, `copy-next.cmd`, taken out of git in section 1 (they stay on disk).
- **Files git never held**: `explainer-video/`, `reference/island-choices.html`, `reference/island-setup-previews.html`, `reference/round-icons.png`, `reference/terminal-states.png`, `review/setup/practice.png`.

## The sweep (section 1, `PublishGuardTests`)

Over every file git holds:
- secrets (private key blocks, GitHub tokens, secret API keys, cloud access keys, chat-service tokens, a password with a value, `.env` files): none found; the add-on manifest's public `key` is allowed by name;
- this computer: the account's name, the profile folder's path and name, the computer's name, the working folder's path, the vault's name: found in 25 files of `review/` (the vault's path in the notes of earlier work orders) and once in `reference/island-previews.html` (the vault's name as a demo folder); all fixed in commit `58f9229`;
- the workshop's leftovers: `run.cmd`, `stop.cmd`, `copy-next.cmd`; taken out of git in `58f9229`.
- The words of the account's full name are not looked for in the public copy (Dan's decision, WORK-ORDER-14 "WHAT CHANGES"). Measured all the same: the first name appears in 33 files of `review/` and `reference/`, the family name in none.

## The build people get (section 2, `ship/app/`)

`powershell -File ship/installer/build.ps1` (the publish command of `ship/size.md`, then the add-on, the licence and the notices copied in):

- 506 files, 207,121,316 bytes (197.5 MB), counted by `Get-ChildItem -Recurse -File | Measure-Object Length -Sum`. (`ship/size.md`, WORK-ORDER-8: 488 files, 206,350,274 bytes; the difference is the code of WORK-ORDER-9 to 14, the add-on folder, the licence and the notices.)
- .NET runtime packs inside: `Microsoft.NETCore.App.Runtime.win-x64` and `Microsoft.WindowsDesktop.App.Runtime.win-x64`, both 10.0.12 (`Island.App.deps.json`), as `THIRD-PARTY-NOTICES.md` says.
- Version: `Island.App.exe` file and product version 1.0.0.0, company SnoweyLabs, product Island. The settings screen shows no version.
- The add-on: `ship/app/extension/` (13 files: the add-on without its tests and without `PROTOCOL.md`), where Settings → General → "Open the add-on folder" looks for it (`AddonFolder.Find`, a folder named `extension` beside the program).
- Its own self-test, from its own folder around `.screen-lock`: `ship/app/Island.App.exe --selftest dist/test-ship` → exit 0, 462 of 462 checks, NEEDS-HUMAN-VERIFY only.
- The name guard of WORK-ORDER-8 (`ShipGuardTests.No_Trace_Of_This_Laptop_In_The_Ship_Folder`) over `ship/` with the build in it: 598 files scanned (archives counted by their entries), 2 words of the account's full name available to look for, no hit.

## The installer (section 3, `ship/installer/out/`, not in git)

`powershell -File ship/installer/build.ps1 -SkipBuild -Installer -TestCopy` (Inno Setup 6, `ISCC.exe` from `%LOCALAPPDATA%\Programs\Inno Setup 6`), 183 seconds:

- `Island-Setup.exe`: 56,479,156 bytes (53.9 MB), SHA-256 `78ff15bc7029262ba0151bce13e8ddc43d641e36265f79945c0d64ddf4b21e7b`.
- `Island-Setup-testcopy.exe`: 56,478,925 bytes, the quiet test copy (no uninstaller, no uninstall entry, no shortcut, no "Start Island", no clean-up, no check for a running Island).
- The test copy, run as `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=dist\install-test`: exit 0; 506 files, 207,121,316 bytes, every file's SHA-256 equal to `ship/app/`'s (0 differences); no `%LOCALAPPDATA%\Programs\Island`, no uninstall key, no Start menu or desktop shortcut afterwards.
- Its self-test, `dist\install-test\Island.App.exe --selftest dist/test-install` around `.screen-lock`: exit 0, 462 of 462 checks.
- `dist\install-test\Island.App.exe --selftest <folder> --uninstall-cleanup`: exit 6 (the gate refused it, nothing done).
- The test folder was deleted afterwards. The real installer was not run on this computer.
