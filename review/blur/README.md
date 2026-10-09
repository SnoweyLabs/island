# Blur test (WORK-ORDER §7) — result

**Verdict: YES, with the plain backdrop brush (`Compositor.CreateBackdropBrush`). The host backdrop brush gives solid black on this laptop and is NO.** On this laptop (Windows 11, build 10.0.26200, one 1920x1080 display at 100 % scaling, transparency effects reported enabled) a topmost, no-activate, no-redirection-bitmap tool window holding one visual whose brush is the plain backdrop brush run through a Gaussian blur (standard deviation 8 px, a hand-made D2D1 effect description, no Win2D, no Windows App SDK) and clipped to a rounded rectangle showed, while the pattern window (not the probe) was the foreground window with focus: the stripes blurred inside the capsule (grey 52–203 where the pattern is pure 0/255; no pure pixels inside), still pure black and white everywhere else in the probe window (0 non-pure pixels in 109 176), live after the stripes moved by half a stripe (the blurred picture moved with them: RMS error 1.0 grey level when compared as "moved left by half a stripe", against 74 if it had not moved), and the same for the 30 px ball, with the blur confined to it. The probe window was never activated (0 activation messages, 0 focus messages). Three runs in a row gave the same verdicts. Not measured: transparency effects switched off, battery saver on, any other Windows version or scaling, the shape morphing as an animation, real mouse clicks through the empty part of the probe window, CPU or GPU cost. Whether the curved edge is smooth enough to film is NEEDS-HUMAN-VERIFY: look at `edge-4x.png`.

## What the numbers say (all in `blur.json`)

| Criterion (WORK-ORDER §7) | plain backdrop brush | host backdrop brush (attribute first) | host backdrop brush (attribute after show) |
|---|---|---|---|
| Blurred inside the capsule, away from its edge | yes | no: flat black (grey 0 to 0) | no: flat black |
| Sharp outside the capsule, inside the probe window | yes | yes | yes |
| Live after the stripes shift | yes | no (a black fill does not change) | no |
| All of it while the pattern window has focus | yes | (held, but nothing to hold) | (same) |
| Ball: blur confined to the ball | yes | no (black, not blurred) | no |
| Verdict | **YES** | NO | NO |

`UISettings.AdvancedEffectsEnabled` read `true`; `PowerManager.EnergySaverStatus` read `Disabled`. Both were only read.

## What was done

`tools/BlurProbe` is a console-started program (C#, .NET 10, target `net10.0-windows10.0.19041.0`, no NuGet package; the Windows SDK projection comes with the target framework). It opens:

- a **pattern window**: an ordinary captioned window, 1100x420 client pixels, painted with GDI as vertical stripes 20 px wide, pure black and pure white, which can shift left by 10 px (half a stripe);
- a **probe window** on top of it, 700x220 px: `WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP`, answering `WM_MOUSEACTIVATE` with `MA_NOACTIVATE` and `WM_NCHITTEST` with `HTTRANSPARENT`. A `DesktopWindowTarget` from `ICompositorDesktopInterop` holds one `SpriteVisual` whose brush is the effect brush (blur of a "backdrop" source parameter fed by the backdrop brush), clipped with a `CompositionGeometricClip` over a `CompositionRoundedRectangleGeometry`. The shape is a 500x76 capsule (radius 38) or a 30 px ball; switching sets the geometry only.

Methods tried, each in a fresh probe window: (1) host backdrop brush, `DWMWA_USE_HOSTBACKDROPBRUSH` set on the window before the tree is built; (2) plain backdrop brush; (3) host backdrop brush, attribute set only after the window is shown. For each: capsule at rest, stripes shifted, then ball; each picture is retaken until two in a row are identical (the compositor has settled).

The one permitted capture: before every photograph the probe checks that the pattern window is the foreground window and has focus, is visible and not minimised, and that the window found at a grid of 105 points inside the photographed rectangle is one of its own two windows; it checks the foreground and the overlap again after the copy and throws the picture away if anything changed. Only the pattern window's client rectangle, inset by 12 px on every side (Windows 11 rounds the window's corners and the outermost pixels could show what is behind it), is copied; the frame in the saved pictures is flat grey (128) and was never read from the screen. A first photograph of the bare stripes ("control") must come out as pure black and white (it did: 0 non-pure pixels), otherwise the probe reports NOT-MEASURED and says why (screen locked, display off, focus refused). No synthetic input of any kind is used: the pattern window is brought to the front by attaching to the foreground thread's input queue and calling `SetForegroundWindow`.

Judging: inside = pixels at least 10 px (ball: 4 px) inside the outline, measured with a signed distance to the rounded rectangle; outside = pixels at least 4 px outside it and inside the probe window. A pixel is "pure" when its channels agree within 8 and it is within 8 of 0 or 255. Blurred = grey range at least 6 and at most 5 % pure pixels. Sharp outside = at most 0.2 % non-pure pixels and both 0 and 255 present. Live = the inside changed by at least 15 % of its grey range and the new picture equals the old one moved by exactly half a stripe better than half as badly as it equals the old one unmoved. These thresholds were chosen by the author of the probe (not measured) and are written into `blur.json`.

## Files

- `blur.json` — every measurement above, the verdict, the failed criteria (empty for YES), the list of pictures.
- `control-pattern.png` — the bare stripes, the proof that the capture is exact.
- `<method>-capsule-a.png`, `<method>-capsule-b-shifted.png`, `<method>-ball.png` — the pictures the numbers come from, for each of the three methods.
- `edge-4x.png` — 64x96 px around the capsule's right-hand curved end, from the plain backdrop brush, enlarged 4x with nearest-neighbour (no smoothing). In the host-brush pictures the clip edge also showed anti-aliased pixels (255, 220, 98, 0 along a row at the corner), so the clip itself is anti-aliased; whether it is smooth enough for a camera is for a person to judge.

## Rerun

1. Close anything that should not lose focus: the probe takes the foreground for under a minute and draws over the middle of the screen. Do not touch the mouse or keyboard meanwhile. Create `.screen-lock` in the project root with the word `blurprobe` if the island self-test might run at the same time, and delete it afterwards.
2. `dotnet build tools/BlurProbe -c Release`
3. Run `tools\BlurProbe\bin\Release\net10.0-windows10.0.19041.0\BlurProbe.exe` (run the `.exe`, not `dotnet BlurProbe.dll`: the DPI-awareness manifest belongs to the `.exe`). `--out <folder>` writes somewhere other than `review/blur`. Exit code 0 = measured, 2 = not measured (the reason is in `blur.json`).

## Not verified and caveats

- One machine, one display, 100 % scaling. At higher scaling the sizes in the file are physical pixels; not tried.
- Transparency effects off and battery saver on were not tried: switching them would change system settings, which the work order forbids. Microsoft's pages say the backdrop turns into a solid colour in those cases; this probe does not test that. The island needs a fallback that checks `AdvancedEffectsEnabled`.
- Why the host backdrop brush gives black here is not known; two orders of setting the window attribute made no difference. Research notes mention that it may be tied to the window being inactive; not isolated.
- Windows 10 is untested (the plain brush is what Avalonia uses there; the host-brush attribute is Windows 11 only).
- The morph from ball to capsule was not animated; the clip geometry was switched. Animating it (and whether the blur follows frame for frame) is untested.
- Real clicks through the empty part of the probe window were not tried (no synthetic input is allowed). The window answers `HTTRANSPARENT`; whether that is enough for a window with no redirection bitmap is UNVERIFIED.
- The interface id of `ICompositorDesktopInterop` and the method order of `IGraphicsEffectD2D1Interop` were taken from the Avalonia source (MIT) because the pages on Microsoft Learn that were opened do not give them; the compositor accepting the objects proves them in practice. No Avalonia code was copied.
- CPU and GPU cost of the blur, and any flicker while the shape moves, were not measured.
