# Probe: console windows (WORK-ORDER-11 section 2)

Run on 2026-10-07, from the shell this session was started in (`Island.Sources.Programs.Smoke probe-console`). It started nothing and attached to nothing. Only yes-or-no answers and counts are written: no title, no program name, no process id.

- a console window (class PseudoConsoleWindow or ConsoleWindowClass) owned by this process or one of its ancestors was found: **no**
- that window's owner (for a ConsoleWindowClass window: itself) is a window the island lists: **no**
- that window counts as a terminal by section 1: **no**
- steps up the chain from this process to the owning process: **none**
- for the record: 8 PseudoConsoleWindow and 2 ConsoleWindowClass windows exist on the computer now; the chain of this process is 12 processes long.

**UNVERIFIED**: the fact (a helper's console window leads to its terminal's window) was not shown on this laptop by this run. The rule falls back by itself (the window whose title holds the project's name, else the one used last); the page is built all the same.
