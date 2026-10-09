# B6 "blur" — report (WORK-ORDER-3 section 8, as a library)

Branch `b6-blur`, worktree `.worktrees/b6-blur`, started from `4c852a1`. Verdict for the piece: **YES for the library on its own, measured on this laptop** (details and pictures in `review/glass/`). Not yet proven: the layer under the real island, a real mouse click, the lag against WPF's capsule, the compositor's (dwm/GPU) cost.

## 1. What I built

`src/Island.Glass/` (new project, `net10.0-windows10.0.19041.0`, references Island.Core, `AllowUnsafeBlocks`, no NuGet):

- `GlassLayer.cs` — `public sealed class GlassLayer : IGlassLayer, IDisposable`. A window of its own, the same rectangle as the island's window and directly beneath it in z-order, holding one `SpriteVisual` whose brush is the plain backdrop brush run through Gaussian blur (sigma 18 DIP) and one colour matrix (saturate 1.9, brightness 1.08: the reference's `.isl-d .body` recipe), clipped by a `CompositionGeometricClip` over a `CompositionRoundedRectangleGeometry` that `Follow()` moves. Never throws from the constructor; `IsAvailable`, `UnavailableReason` (code), `UnavailableHResult` (number). `Follow()` while hidden only stores five doubles; while shown it sets only the properties that changed; 0 bytes allocated (measured).
- `GlassWindow.cs` — the Win32 window: `WS_POPUP`, `WS_EX_TOPMOST | TOOLWINDOW | NOACTIVATE | NOREDIRECTIONBITMAP | TRANSPARENT | LAYERED` (alpha 255), `WM_NCHITTEST` → `HTTRANSPARENT`, `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`, counters of activation/focus messages. Internal enum `GlassClickThrough` keeps the two weaker styles only so the probe can show why they fail.
- `GlassCompositor.cs` — one `Windows.UI.Composition.Compositor` per UI thread (creates a DispatcherQueue only if the thread has none), `CreateDesktopWindowTarget`, the effect brush; `SaturateBrighten` builds the CSS saturate/brightness matrix.
- `NativeEffect.cs` — moved from `tools/BlurProbe` (git rename), extended with effect chaining (`AsSource`), bool and float-array properties, and the colour-matrix CLSID.

`tools/BlurProbe/` (kept its old behaviour without flags; `Glass.cs` now takes `NativeEffect` from the library):

- `GlassMode.cs`, `GlassClicks.cs`, `GlassPictures.cs`, `GlassTiming.cs` — `BlurProbe.exe --glass`: click checks, pictures, timing, verdict, `review/glass/glass.json`.
- `ClickTarget.cs` — `--click-target x y w h`: the second copy of the probe that owns a plain grey window under the layer and answers `WindowFromPoint` questions from its own process.
- `OutsideProbeClickTarget.cs` — starts that copy (`Process.Start`, after `OutsideGate.Current.Allow(OutsideKind.StartOwnCopy)`).
- `Win.cs` — a click-through stand-in for the island window (`CreateAnchor`), `DwmFlush`; the capture check also accepts that stand-in. `Program.cs` — mode switch, helpers made internal, output folder parameter.

`review/glass/` — `glass.json`, `README.md`, 10 pictures. `review/agents/b6-blur.md` — this file.

## 2. Commands run and results

- `dotnet build src/Island.Glass -c Release` → Build succeeded, 0 warnings, 0 errors.
- `dotnet build tools/BlurProbe -c Release` → Build succeeded, 0 warnings, 0 errors.
- `dotnet test tests/Island.Tests` → **Passed 248, Failed 0, Skipped 0** (guards included: Outside_Actions_Are_Gated, The_Foreground_Is_Never_Forced, App_Has_No_Internet_Client, No_Keyboard_Hook_Or_Fake_Input_In_Source, No_Account_Name_In_Source).
- `BlurProbe.exe --glass`, each time with `.screen-lock` = `B6` created before and deleted right after (checked absent afterwards): three runs to a scratch folder while building (YES each; the first exposed a measurement bug, below), then two into `review/glass`: the first PARTLY because the foreground was taken away half-way (ball and mid-expansion photographs refused by the capture rules), the second **YES** — the one committed.
- Regression of the old mode, `BlurProbe.exe --out <scratch>` twice: compositor tree created for all three methods (so the moved `NativeEffect` works in the old path), but the photographs were refused: "pattern window is not the foreground window". The **base commit's own probe** (built from `git archive 4c852a1` into scratch) did the same or worse (NOT-MEASURED at the very first photograph). So this is Windows refusing a plainly-asked foreground since phase A removed the forced one, not a change of mine. `review/blur/` was not touched.

## 3. What is proven, and by what (all in `review/glass/glass.json`)

| Claim | Evidence |
|---|---|
| Blur inside, sharp outside, open capsule 500x76 r38 | `pictures_check.LayeredTransparent.capsule`: inside grey 71–204, 0 pure pixels; outside 0 non-pure of 109 176 |
| Live | `pictures_check.live`: RMS 13.78 if moved by half a stripe vs 64.53 if not |
| Ball 30 across | `pictures_check.ball`: inside 90–185, 0 pure; outside 0 non-pure |
| Middle of the expansion, island's `Spring` (230/20, 1/120 s steps), frozen | `pictures_check.middle_of_expansion`: frozen at 274.5x53.9 r27.0 after 7 frames; blurred inside, sharp outside |
| Clicks reach another program | `click_through.LayeredTransparent`: all 6 points "other_program", asked from both processes |
| The weaker styles do not | `click_through.Transparent` / `.HitTestOnly`: "glass_layer" when asked from the other program |
| Never takes focus | `glass_activation_messages` 0, `glass_focus_messages` 0 |
| Refuses quietly | `refusal_without_anchor`: available false, reason `ANCHOR_INVALID`, no exception |
| Cost | `timing`: Follow() median 44.9 µs while moving (p99 370, max 912), 1.4 µs still, 0.4 µs hidden; 0 bytes allocated; probe process CPU ≤ 2.3 % of one core in every run; Show() 1.7 ms, Hide() 1.4 ms |
| Shape reaches the screen | `pictures_check.lag`: seen at the first photograph after one compositor frame, 60.7 ms upper bound (mostly the photograph itself) |
| `foregroundGranted` | true in the committed run |

## 4. What is not proven

- The layer under the real island window: nothing here touched Island.App (not allowed for me). The stand-in island window is a click-through layered window with nothing drawn.
- A real mouse click through the layer: only `WindowFromPoint` was asked (no synthetic input). NEEDS-HUMAN-VERIFY.
- Lag between the blur and WPF's capsule: WPF draws on its own render thread; I could only measure my own side. NEEDS-HUMAN-VERIFY in motion.
- Edge smoothness for a camera (`glass-edge-4x.png` shows a 1–2 px anti-aliased edge): NEEDS-HUMAN-VERIFY.
- The compositor's own cost (dwm.exe CPU was not readable from this process; GPU not measured). Process CPU readings are coarse (15.6 ms ticks).
- Transparency effects off, battery saver, another scaling, a second monitor, Windows 10: not tried (changing settings is forbidden). `IsAvailable` reads `UISettings.AdvancedEffectsEnabled` live but the "off" path has never run.
- The `CLSID_D2D1ColorMatrix` value and the method order of `IGraphicsEffectD2D1Interop` are from memory / the Avalonia IDL, UNVERIFIED on Learn, proven only by the compositor accepting them (and the saturation itself is invisible on grey stripes: the colour matrix is proven to run, not proven to look right in colour).
- Whether "transparency effects off" turns the backdrop brush into a solid colour while the layer is shown (the Learn acrylic page says so for acrylic) — untested.

## 5. Requests to the main session / joints

1. **`src/Island.App/Island.App.csproj` must target `net10.0-windows10.0.19041.0`** (keep `UseWPF`; add `<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>`) to reference Island.Glass: a `net10.0-windows` project cannot reference a `net10.0-windows10.0.19041.0` one. Not tried by me (outside my territory).
2. Add `src/Island.Glass/Island.Glass.csproj` to `Island.sln` (BlurProbe, already in it, now references Island.Glass).
3. `IGlassLayer` has no reason code; the BLUR_UNAVAILABLE refusal needs one. Either hold the concrete `GlassLayer` (it has `UnavailableReason`), or add `string? UnavailableReason { get; }` to `IGlassLayer` (and to any pretend version).
4. **Click checks with Blur on must not be asked from inside Island.App**: from the glass's own process, `WindowFromPoint` said "passes" even for the two window styles that fail for every other program. Ask from a second process (e.g. start `BlurProbe.exe --click-target x y w h` the way `OutsideProbeClickTarget.cs` does and read its "at X Y" answers), or reuse that pattern in the self-test.
5. Look decision for Dan (OWNER DECISIONS REQUIRED, not mine to make): in the reference, the blurred variant's body fill is lighter (`rgba(16,18,28,.20)` under the white gradient) than the approved no-blur fill (`.6`). With the approved fill drawn over the blur, little blur will show through.

## 6. How to wire it into the app

Constructor (WPF UI thread, after the island window's `SourceInitialized`):

```csharp
var hwnd = new System.Windows.Interop.WindowInteropHelper(islandWindow).Handle;
_glass = new Island.Glass.GlassLayer(hwnd);   // never throws
if (settings.Glass == GlassKind.Blur && !_glass.IsAvailable)
    // fall back to Approved; refusal BLUR_UNAVAILABLE with _glass.UnavailableReason (a code; never a message)
```

Threading: create it and call every member on that one UI thread. Composition changes are committed when the thread goes back to its message loop; WPF's dispatcher does this after each `CompositionTarget.Rendering` callback, so nothing extra is needed. (A loop that calls `Follow()` without pumping shows nothing new: found and fixed in the probe.)

Call sequence, in `IslandController` terms:

1. When the island appears (summon), Blur chosen and `IsAvailable`: `_glass.Follow(<first shape>)` then `_glass.Show()`. `Show()` reads the island window's rectangle and DPI and puts the layer directly beneath it. Call `Show()` again if the island window was moved, changed monitor, or was re-raised in the topmost band.
2. Every frame, in `Draw(now)`, right where `ShapeFrame` is built, with the same values: `_glass.Follow(left, top, width, height, radius)` in DIPs from the island window's top-left, i.e. `left = _centreX - drawnWidth/2`, `top = _machine.DrawnY`, `width = _machine.DrawnWidth`, `height = _machine.DrawnHeight`, `radius = _machine.DrawnRadius` — adjusted for `StretchX/StretchY` exactly as `_view.Apply` stretches the drawn capsule (I did not read how; the layer only takes a rounded rectangle, no transform). Allocation-free.
3. When the machine reaches `Hidden` (`Detach`), or the glass setting leaves Blur: `_glass.Hide()`. While hidden it costs nothing per frame (Follow stores five numbers).
4. On exit: `_glass.Dispose()`.

The WPF tint, rim and contents stay as they are and draw over the blur (the island window is above the layer).

## Three caveats

1. Island.App's target framework must change before it can reference the library (request 1).
2. A same-process `WindowFromPoint` check is not proof of click-through (request 4); only the layered + transparent style passed from another program.
3. Lag against WPF's capsule, the real click, the edge on camera and the compositor's cost are NEEDS-HUMAN-VERIFY.
