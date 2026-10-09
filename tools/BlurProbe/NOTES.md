# BlurProbe — hand-over notes

Status: complete. Verdict YES (plain backdrop brush), measured three times; details in `review/blur/blur.json` and `review/blur/README.md`.

## What exists

- `BlurProbe.csproj`, `app.manifest` (per-monitor-v2 DPI, same as `src/Island.App/app.manifest`). Target `net10.0-windows10.0.19041.0`, `AllowUnsafeBlocks`. No NuGet package. The first restore downloads the `Microsoft.Windows.SDK.NET.Ref` targeting pack that the target framework itself requires; it is not a package reference.
- `Program.cs` — orchestration, thresholds, verdict, `blur.json`.
- `Win.cs` — the two windows, message pump, foreground handling, the single permitted capture (pattern window client rectangle only, inset 12 px).
- `Glass.cs` — Windows.UI.Composition tree: dispatcher queue, compositor, `DesktopWindowTarget`, blur effect brush, rounded clip.
- `NativeEffect.cs` — hand-made COM object (IGraphicsEffect, IGraphicsEffectSource, IGraphicsEffectD2D1Interop) describing a Direct2D Gaussian blur, because nothing in the SDK projection implements those interfaces and Win2D is a NuGet package.
- `Imaging.cs` — pixel measurements and a hand-written PNG writer.

## Glass mode (WORK-ORDER-3 section 8, added 6 Oct 2026)

- `BlurProbe.exe --glass` measures `src/Island.Glass` (the layer the island uses) instead of the probe's own visual and writes `review/glass/`. Files: `GlassMode.cs` (orchestration, verdict), `GlassClicks.cs` (WindowFromPoint from this process and from a second copy started with `--click-target`, see `ClickTarget.cs` and `OutsideProbeClickTarget.cs`), `GlassPictures.cs` (captures, unchanged rules), `GlassTiming.cs` (Follow() cost, CPU).
- `NativeEffect.cs` moved to `src/Island.Glass/` (one copy); `Glass.cs` uses it from there. Without `--glass` the probe behaves as before.
- Composition changes are committed only when the thread pumps messages: a loop that calls Follow() without pumping shows nothing new on screen.

## Verified

- Builds with `dotnet build tools/BlurProbe -c Release` (0 warnings, 0 errors).
- Ran three times against the real screen; same verdicts each time.
- No source file contains the four words the guard test scans for, or the account name (grepped).

## For the main session

- Add to the solution: `dotnet sln Island.sln add tools/BlurProbe/BlurProbe.csproj`, then the verify commands. (Not done here: this territory excludes `Island.sln`.)
- Nothing here changes the island. Putting blur into the island is a later work order. What the result means for it: use `CreateBackdropBrush`, not the host brush; a fixed-size no-redirection-bitmap backdrop window under the existing transparent WPF window; a fallback when `UISettings.AdvancedEffectsEnabled` is false. Mouse pass-through of that window and its cost are untested.
- Source credit: the structure of the effect description and the interface ids come from studying Avalonia (MIT, `WinUiCompositionUtils.cs`, `WinUIEffectBase.cs`, `winrt.idl`). Nothing was copied, so there is no `THIRD-PARTY-NOTICES.md`.
