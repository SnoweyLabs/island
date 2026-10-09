# Performance records — Island (WORK-ORDER §8)

Measured by `--selftest` on 2026-10-07, one run, one laptop (12 logical processors, Windows 11, display refresh interval about 11.81 ms), Release build. **These are records, not gates, and one run on one laptop is an anecdote.**

| Measure | Value |
|---|---|
| Window size (two windows, same size, device-independent pixels) | 680 x 268 |
| Memory (working set) with the island hidden, before the first summon | 284.2 MB (private 223.5 MB) |
| Memory (working set) with the island open, Media page | 236.2 MB (private 149.6 MB) |
| Memory (working set) hidden again after the island left | 238.4 MB (private 180.0 MB) |
| Processor time while hidden, over 6.7 s | 15.6 ms = 0.23% of one core |
| Processor time while open with the light moving, over 6.7 s (more than one lap of the light: 5.6 s) | 4078.1 ms = 61.03% of one core, 550 frames drawn |

## How each was measured

- **Memory:** after three garbage collections, `WorkingSet64` and `PrivateMemorySize64` of the self-test process itself (the island, its two windows and the test harness together; the tray icon and keybinds are not in this process during this step). Taken at the three moments named in the table.
- **Processor time:** the difference in `Process.TotalProcessorTime` of the same process between the start and the end of a wait of the stated length, divided by the wall-clock time of the wait (Stopwatch). "Hidden" means the island has left and both windows are on screen, empty, with the app detached from the frame callback. "Open" means the capsule is open and at rest on the Media page, the contents shown, the rim light moving.
- **Not measured:** graphics card load, power draw, other pages, other display scalings, a second monitor, memory growth over hours. The frame intervals during summons are in `selftest.json` under `info.frameIntervals`.

## What it costs while hidden (WORK-ORDER-6 section 6)

Measured by `--selftest --cost` on 2026-10-07, one run, one laptop (12 logical processors), Release build. **A record, not a gate; one run on one laptop is an anecdote.**

| Measure | Value |
|---|---|
| Island hidden for | 60.0 s |
| Processor time of the app in that time | 421.9 ms = 0.70% of one core (the middle of three runs at the end of WORK-ORDER-12: 0.70, 0.49 and 0.81%; at the end of WORK-ORDER-6: 0.52%) |
| Memory at the end (working set / private) | 152.7 MB / 53.9 MB |
| Frames drawn while hidden | 0 |
| Above 1% of one core | no |
| The same app with the small pill up for a minute, its ring showing an invented long item | 0.47% of one core (the middle of three runs at the end of WORK-ORDER-12: 1.20, 0.47 and 0.42%; the first run of the three read above the 1% line, the other two did not; at the end of WORK-ORDER-6: 0.42%) |

- **What was on:** the app as a person runs it with its own names (one copy, settings and picks files in a temporary folder, a stand-in key when the real one is held elsewhere), the window lister, the installed-programs catalogue, the File Explorer reader and Windows' media sessions, and the add-on's listener on a port of its own with no add-on connected. The add-on's real listener is never started by a self-test.
- **How:** the difference in `Process.TotalProcessorTime` of this process over a wait of 60 s after 5 s of settling, divided by the wall-clock time of the wait. The island is hidden from the start of the run; no frame callback runs while it is. Memory is read after three garbage collections.
- **What was learnt (2026-10-06):** the first run read 3.18% of one core. Taking the readers away one at a time in one experiment (windows, Explorer, media, catalogue and icons all running: 3.59%; Explorer reader stopped: 0.55%; media reader stopped: 0.08%; window lister stopped: 0.00%; a bare process: 0.10%) showed the File Explorer reader, which read the shell every 2 seconds even while the island was hidden. It now rests while the island is hidden and reads at once when it is summoned; the run after that read 0.89%. Looked for once, as WORK-ORDER-6 says; not chased further (the media reader is most of what is left).
- **Not measured:** the compositor (dwm) and the graphics card, a second screen, a media player that is playing, a browser with the add-on connected and many tabs, hours of running.

## What the open island costs (WORK-ORDER-11 §4) — before

Measured by `--selftest <folder> --open` on 2026-10-07, one laptop, Release build, the self-test's pretend world, the island held open (idle time longer than the stretch). Each figure is the middle one of three readings of 8 s, as the app's own processor time in percent of one core, with its frame count and the spread of the three readings (highest minus lowest). Nothing else of this run's was going on in another worktree while it ran unless the line says so. **A record, not a gate; one run on one laptop is an anecdote.**

| Page | Glass | Mode | % of one core | Frames | Spread | Layers rendered (counts per stretch) |
|---|---|---|---|---|---|---|
| picks | approved | Vibe | 80.08 | 645 | 34.55 | shadow 645, bloom 645, fill 645, bottom-glow 645, category-glow 645, edge 645, rim 645 |
| media | approved | Vibe | 67.97 | 659 | 1.18 | shadow 659, bloom 659, fill 659, bottom-glow 659, category-glow 659, edge 659, rim 659 |
| terminals | approved | Vibe | 60.73 | 659 | 2.74 | shadow 659, bloom 659, fill 659, bottom-glow 659, category-glow 659, edge 659, rim 659 |
| picks | approved | Focus | 75.97 | 639 | 3.52 | shadow 639, bloom 639, fill 639, bottom-glow 639, category-glow 639, edge 639, rim 639 |
| media | approved | Focus | 69.33 | 660 | 6.25 | shadow 660, bloom 660, fill 660, bottom-glow 660, category-glow 660, edge 660, rim 660 |
| terminals | approved | Focus | 68.94 | 661 | 14.07 | shadow 661, bloom 661, fill 661, bottom-glow 661, category-glow 661, edge 661, rim 661 |
| picks | approved | DND | 81.83 | 637 | 3.70 | shadow 637, bloom 0, fill 637, bottom-glow 637, category-glow 637, edge 637, rim 637 |
| media | approved | DND | 69.53 | 661 | 3.13 | shadow 661, bloom 0, fill 661, bottom-glow 661, category-glow 661, edge 661, rim 661 |
| terminals | approved | DND | 61.13 | 660 | 2.35 | shadow 660, bloom 0, fill 660, bottom-glow 660, category-glow 660, edge 660, rim 660 |
| picks | darker | Vibe | 78.13 | 642 | 6.43 | shadow 642, bloom 642, fill 642, bottom-glow 642, category-glow 642, edge 642, rim 642 |
| picks | blur | Vibe | 81.64 | 643 | 3.13 | shadow 643, bloom 643, fill 643, bottom-glow 643, category-glow 643, edge 643, rim 643 |

## What the open island costs (WORK-ORDER-11 §4) — where it goes

Made once, on 2026-10-07, from one experiment at a time on the open island at rest (`--selftest <folder> --open --open-case picks,approved,Focus` or `…,Vibe` with `--open-experiment <what is taken away>`; the experiments were temporary and are removed with this list). Each figure is the middle one of three readings of 8 s, as the app's own processor time in percent of one core, with the frames drawn in the stretch. **The three readings of one figure differ by 15 to 45 points here (Dan's own copy of the island was running and so was this work's other tooling), so only differences of 15 points or more mean anything.** A record, not a gate.

| What was taken away | Vibe | Focus | Frames (Focus) |
|---|---|---|---|
| nothing (the island as it is) | 82.2 | 78.9 | 651 |
| the light standing still (its position held) | 84.2 | | |
| each layer of the capsule not drawn again, one at a time: shadow 81.6, bloom 76.2, fill 80.9, bottom glow 78.9, category glow 82.0, edge 79.1, rim 81.8 | see left | | |
| every layer not drawn again | 80.7 | | |
| the clips and transforms put again only when the shape moved | | 77.5 | 652 |
| the tiles' and text's per-frame updates left out | | 75.6 | 642 |
| the shadow window not shown | 74.6 | | |
| the capsule's contents not shown | 84.8 | | |
| every layer not drawn again **and** the clips put once | | 41.8 | 1221 |
| … and the light standing still | | 45.9 | 1216 |
| … and the tiles' per-frame updates left out | | 4.7 | 503 |
| every layer but the two of the moving light (rim, bloom) not drawn again, the clips once | | 76.9 | 642 |
| … and the bloom not drawn again | | 67.0 | 662 |
| … and the rim not drawn again | | 36.5 | 662 |
| the whole of the island's drawing left out (the callback joined, doing nothing) | 2.9 | 1.6 | 0 |
| the callback left altogether (window shown, nothing redrawn) | 0.0 | | 0 |
| every second frame not drawn | | 36.7 | 246 |
| … and everything else quiet as above | | 2.5 | 252 |
| the layers that never move kept as bitmaps (BitmapCache) | | 79.3 | 622 |
| … with the other fixes | | 69.7 | 657 |

**The list, largest first:**

1. **The moving light is redrawn on every frame, and in software.** With everything else quiet, drawing the rim again costs about 40 points and the bloom about 10; the glass layers under them are composed again with them, because Windows draws a transparent window without the graphics card and re-composes whatever the changed region touches. Drawing the light every second frame would take 79 to 37. *Stopped at* (the movement is the approved one; half the rate is recorded below under OWNER DECISIONS, not applied).
2. **The island's own work on every frame when nothing changed** — every layer invalidated, new clips and transforms put, the colour of every tile put again — about 35 points more than the callback itself (the whole drawing left out leaves 1.6 to 2.9). *Fixed* in three checkpoints: a layer is drawn again only when what it draws changed (not the light's: the rim and the bloom still follow it); the clips and transforms are put again only when the shape moved; a tile's colour is put again only when it changed. On their own they do not move the figure while the light moves (item 1 hides them: 78.9 to 77.5 to 75.6 inside the spread), but together with a still light they take it from 41.8 to 4.7; they are what lets the island rest.
3. **The frame callback runs at the display's rate even when nothing at all moves** (Do not disturb at rest: no light, no breathing, no playing tile). *Fixed*: in that state the callback is left, a timer keeps the island alive (it still leaves after its idle time, still notices the keyboard going elsewhere, still refreshes the playing line and the reading of the process list), and anything that changes wakes it.
4. **A bitmap cache on the layers that never move** — *tried once*: 79.3 against 78.9, 69.7 with the other fixes against 73.6: inside the spread; reverted, DEAD ENDS.
5. **The shadow window, the glass layers, the contents** — each alone is inside the spread (74.6, 76 to 82, 84.8): none is a cause by itself.

Not measured: the graphics card's share and Windows' own drawing outside this process.

## What the open island costs (WORK-ORDER-11 §4) — after

Measured by `--selftest <folder> --open` on 2026-10-07, one laptop, Release build, the self-test's pretend world, the island held open (idle time longer than the stretch). Each figure is the middle one of three readings of 8 s, as the app's own processor time in percent of one core, with its frame count and the spread of the three readings (highest minus lowest). Nothing else of this run's was going on in another worktree while it ran unless the line says so. **A record, not a gate; one run on one laptop is an anecdote.**

| Page | Glass | Mode | % of one core | Frames | Spread | Layers rendered (counts per stretch) |
|---|---|---|---|---|---|---|
| picks | approved | Vibe | 74.02 | 653 | 26.36 | shadow 0, bloom 653, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 653 |
| media | approved | Vibe | 61.72 | 656 | 1.95 | shadow 0, bloom 656, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 656 |
| terminals | approved | Vibe | 51.95 | 662 | 1.94 | shadow 0, bloom 662, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 662 |
| picks | approved | Focus | 72.07 | 648 | 3.52 | shadow 0, bloom 648, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 648 |
| media | approved | Focus | 61.91 | 657 | 2.14 | shadow 0, bloom 657, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 657 |
| terminals | approved | Focus | 52.34 | 661 | 2.35 | shadow 0, bloom 661, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 661 |
| picks | approved | DND | 0.00 | 0 | 0.59 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | DND | 41.40 | 1178 | 6.84 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | DND | 34.96 | 962 | 3.31 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | darker | Vibe | 71.87 | 649 | 4.49 | shadow 0, bloom 649, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 649 |
| picks | blur | Vibe | 72.46 | 652 | 7.02 | shadow 0, bloom 652, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 652 |

## How quickly it answers (WORK-ORDER-12) — before

Measured by `--selftest <folder> --speed` on 2026-10-07, one laptop, Release build. Each figure is the middle one of 5 readings, in milliseconds, with the spread (highest minus lowest) and every reading, in the order they were taken. No real key was pressed: the handlers a key reaches were called. Rows 2 to 6 and 9 are on the self-test's pretend world with the island in Focus; rows 1, 7 and 10 go through the app's real start; row 8 uses the real icon reader on two files only. Other programs were running (Dan's own copy of the island among them), so only differences larger than the spread mean anything. **A record, not a gate; one run on one laptop is an anecdote.**

| # | What | Middle of the readings | Spread | The readings | Note |
|---|---|---|---|---|---|
| 2a | main key: the key handler returns | 2.7 ms | 97.5 | 99.7, 9.6, 2.7, 2.3, 2.5 | the first drawing is done inside it |
| 2 | main key to the first frame drawn after it | 7.2 ms | 224.9 | 229.8, 15.5, 7.2, 5.2, 4.9 |  |
| 3 | main key to the capsule at rest | 1109.1 ms | 12.9 | 1119.1, 1109.1, 1106.2, 1111.2, 1107.6 | a record only: the length of the movement is approved |
| 4 | Enter on an open pick to the moment the outside door is asked | 0.2 ms | 10.4 | 10.6, 0.4, 0.2, 0.2, 0.2 |  |
| 4b | a click on a closed pick to the moment the outside door is asked | 0.2 ms | 0.4 | 0.6, 0.4, 0.2, 0.2, 0.2 |  |
| 5 | a typed letter to the search results laid out | 3.0 ms | 44.9 | 47.2, 2.5, 3.0, 3.3, 2.3 |  |
| 6 | a page's digit to the new page's row laid out | 135.3 ms | 26.9 | 153.7, 135.3, 136.9, 126.8, 133.7 |  |
| 6b | Tab to the new page's row laid out | 128.7 ms | 13.5 | 139.9, 126.4, 134.1, 128.7, 127.3 |  |
| 6c | the + tile to the second row built and at rest | 696.1 ms | 8.9 | 698.7, 696.1, 689.7, 696.6, 690.8 |  |
| 1 | the app's own start to ready for the main key (AppHost.Start returns, keys registered) | 357.9 ms | 208.7 | 538.6, 359.2, 329.9, 357.9, 332.7 | the first reading is the cold one: it includes first-time code; the process's own start-up (the runtime loading) is not in it |
| 7 | the settings asked for to their first frame | 102.4 ms | 158.8 | 259.7, 104.6, 101.7, 100.9, 102.4 | the screen is built on the drawing thread; the first reading of the five is the cold one |
| 10a | memory held: working set, hidden before the first summon | 198.4 MB | 0.0 | 198.4 | private 277.4 MB; one reading, after three garbage collections |
| 10b | memory held: working set, open | 200.1 MB | 0.0 | 200.1 | private 215.0 MB |
| 10c | memory held: working set, hidden again | 195.0 MB | 0.0 | 195.0 | private 209.6 MB |
| 8 | the first icons asked for to read and made into their discs (real reader, Windows' own explorer.exe and the app's own file) | 25.7 ms | 84.6 | 108.0, 25.7, 24.3, 37.2, 23.4 | off the drawing thread; a new reader each time (Windows' own icon cache is warm after the first reading); no icon is written anywhere |
| 9 2 | the longest time the drawing thread did not answer during the main key | 12.5 ms | 138.6 | 140.6, 8.5, 20.4, 12.5, 2.0 | late frames 25 of 352 (later than twice the median interval of 13.8 ms); the longest frame interval 54.7 ms |
| 9 5 | the longest time the drawing thread did not answer during a typed letter | 4.3 ms | 42.5 | 45.9, 4.3, 4.1, 5.3, 3.4 | late frames 19 of 239 (later than twice the median interval of 13.8 ms); the longest frame interval 60.5 ms |
| 9 6 | the longest time the drawing thread did not answer during a page change | 4.1 ms | 3.7 | 5.7, 3.7, 3.4, 4.4, 5.4, 2.7, 5.2, 3.4, 2.0, 4.5 | late frames 24 of 518 (later than twice the median interval of 13.8 ms); the longest frame interval 44.0 ms |
| 9 7 | the longest time the drawing thread did not answer during the settings opening | 87.4 ms | 161.5 | 247.8, 91.6, 86.9, 86.3, 87.4 | late frames 0 of 0 (later than twice the median interval of 13.8 ms); the longest frame interval 0.0 ms |
| 9 2row | the longest time the drawing thread did not answer during the second row opening | 2.4 ms | 2.5 | 3.2, 1.3, 3.8, 2.4, 1.8 | late frames 14 of 191 (later than twice the median interval of 13.8 ms); the longest frame interval 47.1 ms |

## What the open island costs (WORK-ORDER-12) — after

Measured by `--selftest <folder> --open` on 2026-10-07, one laptop, Release build, the self-test's pretend world, the island held open (idle time longer than the stretch). Each figure is the middle one of three readings of 8 s, as the app's own processor time in percent of one core, with its frame count and the spread of the three readings (highest minus lowest). Nothing else of this run's was going on in another worktree while it ran unless the line says so. **A record, not a gate; one run on one laptop is an anecdote.**

| Page | Glass | Mode | Light | % of one core | Frames | Spread | Layers rendered (counts per stretch) |
|---|---|---|---|---|---|---|---|
| picks | approved | Vibe | As before | 74.61 | 515 | 34.16 | shadow 0, bloom 515, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 515 |
| media | approved | Vibe | As before | 69.14 | 636 | 7.02 | shadow 0, bloom 636, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 636 |
| terminals | approved | Vibe | As before | 59.37 | 660 | 5.85 | shadow 0, bloom 660, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 660 |
| picks | approved | Focus | As before | 68.94 | 492 | 10.54 | shadow 0, bloom 492, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 492 |
| media | approved | Focus | As before | 70.70 | 564 | 5.28 | shadow 0, bloom 564, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 564 |
| terminals | approved | Focus | As before | 51.75 | 661 | 3.91 | shadow 0, bloom 661, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 661 |
| picks | approved | DND | As before | 0.20 | 0 | 0.20 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | DND | As before | 39.45 | 923 | 4.29 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | DND | As before | 39.06 | 666 | 7.61 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | darker | Vibe | As before | 72.46 | 652 | 5.27 | shadow 0, bloom 652, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 652 |
| picks | blur | Vibe | As before | 70.90 | 610 | 3.90 | shadow 0, bloom 610, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 610 |
| picks | approved | Vibe | Half rate | 73.24 | 632 | 6.45 | shadow 0, bloom 462, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 632 |
| media | approved | Vibe | Half rate | 64.25 | 657 | 4.30 | shadow 0, bloom 657, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 657 |
| terminals | approved | Vibe | Half rate | 52.92 | 657 | 2.14 | shadow 0, bloom 657, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 657 |
| picks | approved | Focus | Half rate | 58.20 | 578 | 5.46 | shadow 0, bloom 485, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 485 |
| media | approved | Focus | Half rate | 61.52 | 655 | 5.07 | shadow 0, bloom 655, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 655 |
| terminals | approved | Focus | Half rate | 52.73 | 659 | 4.50 | shadow 0, bloom 659, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 659 |
| picks | approved | DND | Half rate | 0.00 | 0 | 0.59 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | DND | Half rate | 41.01 | 1167 | 1.76 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | DND | Half rate | 37.50 | 944 | 2.93 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | darker | Vibe | Half rate | 71.48 | 636 | 4.10 | shadow 0, bloom 441, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 636 |
| picks | blur | Vibe | Half rate | 70.90 | 636 | 7.23 | shadow 0, bloom 452, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 636 |
| picks | approved | Vibe | Graphics card | 0.00 | 0 | 0.00 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | Vibe | Graphics card | 27.34 | 858 | 7.03 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | Vibe | Graphics card | 26.56 | 683 | 3.12 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | approved | Focus | Graphics card | 0.00 | 0 | 0.39 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | Focus | Graphics card | 28.71 | 664 | 2.14 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | Focus | Graphics card | 28.90 | 660 | 4.10 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | approved | DND | Graphics card | 0.20 | 0 | 0.39 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| media | approved | DND | Graphics card | 40.03 | 1104 | 5.47 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| terminals | approved | DND | Graphics card | 37.30 | 922 | 8.78 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | darker | Vibe | Graphics card | 0.20 | 0 | 0.19 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |
| picks | blur | Vibe | Graphics card | 0.20 | 0 | 1.17 | shadow 0, bloom 0, fill 0, bottom-glow 0, category-glow 0, edge 0, rim 0 |

## Glasses — processor time with the capsule open and the light moving (WO3 section 8)

Measured by the self-test over 2.5 s, this process only, as a percentage of one core; the compositor's own cost (dwm, the GPU) is not included and was not measured. One run, one laptop, while other programs were working: an anecdote, not a gate.

| Glass | Process CPU while open, % of one core |
|---|---|
| Approved | 58.1 |
| Blur | 53.7 |

## How quickly it answers (WORK-ORDER-12) — after

Measured by `--selftest <folder> --speed` on 2026-10-07, one laptop, Release build. Each figure is the middle one of 5 readings, in milliseconds, with the spread (highest minus lowest) and every reading, in the order they were taken. No real key was pressed: the handlers a key reaches were called. Rows 2 to 6 and 9 are on the self-test's pretend world with the island in Focus; rows 1, 7 and 10 go through the app's real start; row 8 uses the real icon reader on two files only. Other programs were running (Dan's own copy of the island among them), so only differences larger than the spread mean anything. **A record, not a gate; one run on one laptop is an anecdote.**

| # | What | Middle of the readings | Spread | The readings | Note |
|---|---|---|---|---|---|
| 2a | main key: the key handler returns | 2.7 ms | 13.8 | 15.4, 4.8, 2.7, 1.8, 1.5 | the first drawing is done inside it |
| 2 | main key to the first frame drawn after it | 7.1 ms | 33.2 | 36.9, 8.4, 7.1, 5.4, 3.7 |  |
| 3 | main key to the capsule at rest | 1104.7 ms | 12.6 | 1116.4, 1111.8, 1104.7, 1103.8, 1104.5 | a record only: the length of the movement is approved |
| 4 | Enter on an open pick to the moment the outside door is asked | 0.2 ms | 5.1 | 5.3, 0.3, 0.2, 0.2, 0.2 |  |
| 4b | a click on a closed pick to the moment the outside door is asked | 0.2 ms | 0.5 | 0.6, 0.1, 0.1, 0.2, 0.2 |  |
| 5 | a typed letter to the search results laid out | 2.6 ms | 6.2 | 8.1, 2.6, 2.6, 1.9, 2.4 |  |
| 6 | a page's digit to the new page's row laid out | 124.5 ms | 43.3 | 138.6, 124.5, 122.3, 122.5, 165.6 |  |
| 6b | Tab to the new page's row laid out | 124.1 ms | 23.1 | 144.9, 139.3, 122.6, 124.1, 121.9 |  |
| 6c | the + tile to the second row built and at rest | 703.0 ms | 9.3 | 700.0, 694.7, 703.0, 703.9, 703.1 |  |
| 1 | the app's own start to ready for the main key (AppHost.Start returns, keys registered) | 323.1 ms | 257.6 | 572.5, 332.4, 314.8, 323.1, 318.9 | the first reading is the cold one: it includes first-time code; the process's own start-up (the runtime loading) is not in it |
| 7 | the settings asked for to their first frame | 89.0 ms | 16.5 | 100.7, 84.2, 87.7, 91.1, 89.0 | the screen is built on the drawing thread; the first reading of the five is the cold one |
| 10a | memory held: working set, hidden before the first summon | 196.4 MB | 0.0 | 196.4 | private 268.8 MB; one reading, after three garbage collections |
| 10b | memory held: working set, open | 200.2 MB | 0.0 | 200.2 | private 240.7 MB |
| 10c | memory held: working set, hidden again | 200.5 MB | 0.0 | 200.5 | private 241.0 MB |
| 8 | the first icons asked for to read and made into their discs (real reader, Windows' own explorer.exe and the app's own file) | 27.1 ms | 113.4 | 136.0, 22.6, 24.2, 34.8, 27.1 | off the drawing thread; a new reader each time (Windows' own icon cache is warm after the first reading); no icon is written anywhere |
| 9 2 | the longest time the drawing thread did not answer during the main key | 2.1 ms | 13.8 | 15.4, 1.9, 2.5, 2.1, 1.5 | late frames 18 of 362 (later than twice the median interval of 13.5 ms); the longest frame interval 46.6 ms |
| 9 5 | the longest time the drawing thread did not answer during a typed letter | 3.6 ms | 6.4 | 6.9, 3.6, 2.2, 1.6, 8.0 | late frames 23 of 244 (later than twice the median interval of 13.5 ms); the longest frame interval 49.5 ms |
| 9 6 | the longest time the drawing thread did not answer during a page change | 5.4 ms | 8.6 | 6.8, 6.3, 3.4, 7.8, 10.4, 4.9, 3.5, 1.8, 2.6, 6.0 | late frames 32 of 510 (later than twice the median interval of 13.5 ms); the longest frame interval 63.9 ms |
| 9 7 | the longest time the drawing thread did not answer during the settings opening | 74.3 ms | 17.9 | 88.7, 70.8, 73.8, 76.6, 74.3 | late frames 0 of 0 (later than twice the median interval of 13.5 ms); the longest frame interval 0.0 ms |
| 9 2row | the longest time the drawing thread did not answer during the second row opening | 2.9 ms | 12.9 | 2.9, 1.5, 1.3, 14.3, 5.2 | late frames 14 of 198 (later than twice the median interval of 13.5 ms); the longest frame interval 46.7 ms |
| 2 cold | main key to the first frame, the first time | 36.9 ms | 0.0 | 36.9 | the first of the five readings |
| 5 cold | a typed letter to the search results, the first time | 8.1 ms | 0.0 | 8.1 | the first of the five readings |
| 7 cold | the settings asked for to their first frame, the first time | 100.7 ms | 0.0 | 100.7 | the first of the five readings |
| 9 2 cold | the longest silence of the drawing thread at the first main key | 15.4 ms | 0.0 | 15.4 | the first of the five readings |
| 9 7 cold | the longest silence of the drawing thread at the first opening of the settings | 88.7 ms | 0.0 | 88.7 | the first of the five readings |

## How quickly it answers (WORK-ORDER-12) — where it goes

Made once, 2026-10-07, from the table "— before" (the line to aim at: rows 2, 4, 5 and 6 within three frames of the screen, 18 ms at 165 Hz; the drawing thread never silent for more than two frames, 12 ms). Largest first; each cause is *fixed*, *stopped at* or *tried*.

1. **The first summon after a silent start found its code cold** (the first Ctrl+Q after Windows' start-up list started the app): main key to the first frame 158 ms, the drawing thread silent for 92 ms; the first typed letter 43 ms. *Fixed* (checkpoint 1b1382d): once, five seconds after a silent start, at the idle priority, in small steps, an island of invented pages is built over canvases that are in no window, summoned, moved to two other pages, searched, drawn into a bitmap and thrown away (`IslandWarmUp`); a self-test check proves it shows nothing and ends. After: 39 ms and 15 ms; typed letter 8 ms. The rows "cold" in the table are the first reading of five (the middle of five hides it).
2. **The first opening of Settings:** 257 ms cold, silent 247 ms; warm 87 to 100 ms, silent 74 to 98 ms. The cold part is *fixed* by the same warm-up (the settings view is built once in no window): 101 ms and 89 ms. What is left is the window: of the 90 ms, creating the layered full-screen window takes 46 to 110 ms (`Show`), 11 ms its handle and 28 ms setting it to the size of the screen. *Stopped at*: it is WPF's own window creation and the full-screen transparent window of the approved opening; changing it would rebuild the screen's window.
3. **A page change (digit or Tab) to the new row laid out:** 124 to 141 ms. *Stopped at*: the contents go out and come back through the approved swap delay of 120 ms (`LookConstants`), so the row cannot be there sooner without changing the movement.
4. **The app's own start to ready for the main key:** 323 ms (572 ms cold). *Stopped at*: nobody presses a key while the app is starting, and what it does (the readers, the windows, the tray icon, the key) is needed; listed because it is over the line, not chased.
5. **The second row (the + tile) to built and at rest:** 703 ms: the approved movement (a record; the key handler returns in 2 ms).
6. **Enter and a click:** 0.2 to 0.3 ms to the moment the outside door is asked; the first time 5 to 9 ms. Inside the line.
7. **The first icons** read with the real reader (Windows' own `explorer.exe` and the app's own file): 25 ms, 100 to 136 ms the first time; off the drawing thread, so the tile shows its letters until the picture arrives. Inside the line for the person.
8. **Memory:** 195 to 201 MB held, hidden or open (private 241 to 269 MB). Not over any line; recorded.
9. The hidden cost, the run of WORK-ORDER-6 §6 after these changes: 0.52% of one core (the highest it has had on unchanged code is 1.12%).

## What the open island costs (WORK-ORDER-12) — where it goes

Made once, 2026-10-07, after the table "— after" of this work order (`--open --open-light asbefore,halfrate,graphicscard`, three readings of 8 s each; the readings differ among themselves by 2 to 34 points, so only larger differences mean anything; Dan's own copy and other tools were running). A record, not a gate.

1. **The moving light, drawn by the system's compositor.** *Fixed* (checkpoint aef637e). On a page of picks, open and at rest: Vibe 74.6% → 0.0%, Focus 68.9% → 0.0%, Vibe with the darker glass 72.5% → 0.2%, with the Blur glass 70.9% → 0.2% of one core; 0 frames and 0 layers rendered over a lap of the light (the gate, a self-test check). The old light costs about 40 points for the rim and 10 for the bloom; the compositor draws it without the app.
2. **Half rate** (the reserve Dan chose, kept as a kind): *no saving on this laptop*. The screen refreshes 165 times a second, so half its rate is 82 a second, which is more than the 75 the island manages to draw while the light moves: Vibe 74.6% → 73.2%, Focus 68.9% → 58.2% (the second is inside the spread). It would save something only on a screen that refreshes at 120 or less. Recorded as a proposal: a lower fixed rate for the light (30 to 40 a second) as a fourth kind.
3. **The playing tile (Media page) and a working ring (Terminals page).** *Stopped at.* With the compositor light: Media Focus 28.7%, Terminals Focus 28.9% (of 71 and 52 as before); in Do not disturb, where no light moves, Media 40.0% and Terminals 37.3%. Every frame that changes anything costs about 3 ms, whatever changes: the window the capsule is drawn in is a layered transparent window that Windows draws without the graphics card, and it is presented as a whole (`review/perf.md`, WORK-ORDER-11 §4, "stopped at": how the window is put on the screen; the same item). *Tried once:* the frames of the island drawn at half the screen's rate while a playing tile or a working ring is the only thing that moves (a temporary switch, removed): Media Focus 31.8% → 24.2%, Terminals Focus 30.9% → 25.8%, Media Do not disturb 29.7% → 23.6%, Terminals Do not disturb 40.6% → 30.1%: inside the spread of the readings (13 to 31 points) and it would change how the bars and the ring move, so **not applied and a proposal**, with these figures.
4. **Vibe's breathing glow.** *Fixed* with the light: the compositor breathes the glow with an animation of 44 keyframes taken from `ModeMark.Breath` itself; nothing is drawn by the app at rest.
5. **More frames than the screen refreshes** (the lead in the order's ground truth). *Not found here* (the screen is 165 Hz; the island drew about 130 to 147 frames a second); a cap that draws no more than the refreshes (`FrameBudget`) was added all the same, as a safe improvement for screens that refresh more slowly: it loses no frame where the island already runs at the screen's rate (a test pins it).
6. **The 0.6 px softening of the rim** (the old rim layer's blur of 0.6 px): left to the compositor's own anti-aliasing; it is below one pixel. Recorded as a difference the picture checks cannot see (no picture of the compositor's light can be taken).

Not measured: the graphics card's share and Windows' own drawing outside this process.

## WORK-ORDER-13 — the hidden cost after the decided changes (2026-10-08)

Three runs of `--selftest <folder> --cost` from `dist/Island-build` (the same method as above: 5 s of settling, then 60 s hidden): **1.22%, 0.91% and 1.07% of one core**, working set 150 to 154 MB, 0 frames drawn. WORK-ORDER-12's three runs read 0.49, 0.70 and 0.81%; WORK-ORDER-6's 0.52%; the highest earlier reading on unchanged code was 1.12%. WO13 adds a listener for Windows' text size and animation settings and a per-user pipe, none of which should wake while hidden, so this looks like the run-to-run noise of this laptop (other programs were running) and not a measured cost of any change; it was not isolated. Not over any line the work orders set (the open island's costs are in the tables above, and the half rate while only a tile or ring moves is in section A of WORK-ORDER-13). The pill's own run read 0.86, 0.96 and 0.60%.

## WORK-ORDER-14 — the hidden cost at the hand-over (2026-10-09)

Three runs of `--selftest <folder> --cost` from `dist/Island-build`, the same method: **1.56%, 1.35% and 1.46% of one core** hidden, working set 153 to 155 MB, 0 frames drawn; the pill's own run 1.12, 1.15 and 1.09%. WORK-ORDER-13's runs read 1.22, 0.91 and 1.07%. The only change of WORK-ORDER-14 to the program is the command line `--uninstall-cleanup`, a branch at start that this run never takes, so the difference is not a cost of this work order; it was measured right after the full test suite and three self-tests, with the owner's own copy running, and it was not isolated. It is above the 1% that WORK-ORDER-6 aimed at for the hidden island, as WO13's first run already was: written for the owner, not chased here.
