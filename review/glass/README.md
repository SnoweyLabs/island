# Blur as a third glass (WORK-ORDER-3 section 8) — the layer, measured

**Verdict: YES, for the library on its own, on this laptop.** `Island.Glass.GlassLayer` (the plain backdrop brush, blur sigma 18 DIP, saturate 1.9, brightness 1.08: the reference's `blur(18px) saturate(190%) brightness(1.08)`) sitting beneath a stand-in island window showed, while the probe's own pattern window had the foreground and focus: the stripes blurred inside the shape (grey 71–204, no pure pixels) and pure black/white everywhere else in the layer's window (0 non-pure pixels of 109 176) for the open capsule (500 x 76, radius 38), the ball (30 across: grey 90–185 inside, 0 non-pure outside) and one still moment in the middle of a spring-driven expansion (274.5 x 53.9, radius 27.0, frozen after 7 frames of the island's own `Spring`); live (the blurred picture moved with the stripes: RMS 13.8 against 64.5 unmoved); the glass window received 0 activation and 0 focus messages. Clicks: with `WS_EX_LAYERED | WS_EX_TRANSPARENT` every `WindowFromPoint` answer at six points (inside the capsule, inside the ball, in the window outside each shape, two corners) was the window of ANOTHER program under the layer, asked from both processes. Not proven here: the layer under the real island (the main session wires it), a real mouse click (no synthetic input is allowed), the lag between the blur and WPF's own capsule, the GPU/compositor cost, transparency effects off, other scalings and monitors.

## The click-through finding (the important one)

| Window styles of the glass window | `WindowFromPoint` asked from the glass's own process | asked from another program | Clicks reach the other program |
|---|---|---|---|
| `HTTRANSPARENT` only (the first probe's way) | the other program | **the glass layer** | no |
| + `WS_EX_TRANSPARENT` | the other program | **the glass layer** | no |
| + `WS_EX_TRANSPARENT` + `WS_EX_LAYERED` (alpha 255 via `SetLayeredWindowAttributes`) | the other program | the other program | **yes** |

So asking from inside the same program is misleading: it said "passes" for all three. Only the layered + transparent window lets the other program be found, and only that one is used (`GlassLayer`'s public constructor). It still renders the blur (all three capsule pictures are identical: `*-capsule.png`). Avoid `WS_DISABLED` as a way out: the `WindowFromPoint` page on Microsoft Learn (updated 2025-07-01) says it "does not retrieve a handle to a hidden or disabled window", so that check would pass while a real click would still land on the window (inferred, not tried). Why the answer depends on who asks is not proven; inferred: the glass window's own `WM_NCHITTEST` answer (`HTTRANSPARENT`) is honoured when the question comes from the glass window's own thread and not from another program, while a layered + transparent window is skipped by Windows itself whoever asks. Consequence for the main session: a `WindowFromPoint` check made from inside `Island.App` cannot prove click-through of the glass; ask from another process, as this probe does.

## What the pictures show (looked at by the agent, not by a person)

- `glass-capsule.png`: a blurred capsule of soft grey bands inside, sharp stripes all around it; no blurred rectangle, no halo outside the outline.
- `glass-capsule-shifted.png`: the stripes moved by half a stripe and the bands inside moved with them.
- `glass-ball.png`: a small blurred ball on a black/white edge, sharp stripes right up to it.
- `glass-mid-expansion.png`: the half-grown capsule, blurred inside, sharp outside.
- `glass-edge-4x.png` (4x, nearest neighbour): the curved end steps through intermediate greys over about one to two pixels, so the clip is anti-aliased; inside, the grey lightens towards the right end because the blur reaches the white stripe beyond the shape. Whether it is smooth enough to film is NEEDS-HUMAN-VERIFY.
- Lag: after `Follow(capsule)` the new shape was on screen at the first photograph after one compositor frame (60.7 ms upper bound, most of it the photograph itself). The lag against WPF's capsule, drawn by a different part of Windows, was not measurable here: NEEDS-HUMAN-VERIFY in the island.

## Cost (glass.json, "timing"; one run, one laptop: an anecdote)

| Run (Follow once per compositor frame, ~65 frames/s) | Follow() median / p99 / max | Bytes allocated by Follow | Probe process CPU, % of one core |
|---|---|---|---|
| hidden, Follow every frame | 0.4 / 2.8 / 18.8 µs | 0 | 2.3 |
| shown, shape still | 1.4 / 9.8 / 13.9 µs | 0 | 0 |
| shown, shape moving ball↔capsule | 44.9 / 370 / 912 µs | 0 | 2.0 |

`Show()` took 1.7 ms and `Hide()` 1.4 ms (one window move each). Process CPU is read from `TotalProcessorTime`, which moves in 15.6 ms ticks, so these are coarse (one tick is 0.8 % over 2 s). The compositor's own cost (dwm.exe, the GPU) could not be read by this process (`dwm_cpu_percent_of_one_core: null`): NOT MEASURED, and it is where most of the blur's cost lives.

## How it was run

`dotnet build tools/BlurProbe -c Release`, then `tools\BlurProbe\bin\Release\net10.0-windows10.0.19041.0\BlurProbe.exe --glass` with `.screen-lock` held (it writes here by default; `--out <folder>` elsewhere). Without `--glass` the probe does exactly what it did in WO1 section 7. The pattern window asks Windows plainly for the foreground (`Win.EnsurePatternForeground`); nothing forces it. The capture rules are the first probe's, unchanged (`Win.CapturePatternRect`): only the pattern window's client rectangle inset by 12 px, only while it is the foreground window with focus and nothing else is found over it, checked again after the copy. Stripes are 40 px here (the first probe's 20 px are blurred almost flat by sigma 18). The click check starts a second copy of the probe (`--click-target`, through the outside gate) whose plain grey window lies under the layer; no click is ever sent.

Runs, all on 6 Oct 2026: three to a scratch folder while building (YES each; the first had a broken lag measurement, fixed: the compositor only commits when the thread pumps messages), then two into this folder: the first lost the foreground half-way (something else came to the front; the ball and the middle of the expansion were refused by the capture rules, recorded then as failures, which was wrong and was changed to "not measured"), the second is the one saved here.
