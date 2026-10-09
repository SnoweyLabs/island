# H1 colour: report (WORK-ORDER-10 section 1, pure logic)

## 1. What I built

New folder `src/Island.Core/RoundIcon/` (namespace `Island.Core`, no WPF, no drawing, no outside call) and `tests/Island.Tests/RoundIcon/` (namespace `Island.Tests`). Nothing outside these two folders changed; `IconFit.cs` is untouched.

Public API (all in `Island.Core`):

```csharp
public enum RoundIconKind { Letters, Plate, FlatShape, NearlyWhiteFlatShape, Other }
public readonly record struct RoundIconColour(byte R, byte G, byte B) { public Rgb ToRgb(); }
public readonly record struct RoundIconPlan(RoundIconKind Kind, RoundIconColour Disc, bool DrawnWhite)
    { public static RoundIconPlan Letters { get; } }          // Disc = dark neutral, meaningless for Letters

public static class RoundIconRule
{
    RoundIconPlan Analyse(IconImage? icon);                                        // null -> Letters
    RoundIconPlan Analyse(int width, int height, ReadOnlySpan<byte> straightBgra); // straight 8-bit BGRA, stride width*4
    byte[] Whiten(ReadOnlySpan<byte> straightBgra);       // copy: every pixel rgb 255, its own alpha kept (the flat shape's drawing)
    bool StaysVisibleOn(RoundIconColour disc, int width, int height, ReadOnlySpan<byte> straightBgra);
    RoundIconColour MakeVisible(RoundIconColour start, int width, int height, ReadOnlySpan<byte> straightBgra);
}

public readonly record struct RoundIconBox(double X, double Y, double Width, double Height);
public static class RoundIconLayout
{
    RoundIconBox Place(int iconWidth, int iconHeight, double tileSize, double pixelsPerDip); // DIP, from the tile's top-left
}

public static class RoundIconSamples   // IconImage (reuses Island.Core's IconImage); the arrays are shared: clone before changing
{
    IconImage PlateWithWhiteSquare, OrangeEllipse, NearlyWhiteEllipse, FourColourRing, SmallPlate, Nothing;
    IReadOnlyList<IconImage> All;       // the six, in the work order's order
}

public static class RoundIconConstants  // every number of the rule, each marked (Claude) in its comment
{   OpaqueAlphaLine=128, GroupBitsPerChannel=4, PlatePercent=70, FlatPercent=90, NearlyWhiteLine=230,
    DarkNeutralRed/Green/Blue=40/42/50 (+ DarkNeutral), VisibleDifference=60, VisiblePercent=90,
    IconBoxFraction=0.66, DiscStepPercent=8, MaxDiscSteps=12, DarkDiscMaxChannel=100, MaxPixelsRead=2048*2048 }
```

Behaviour notes:
- Group = top 4 bits of R, G, B (4096 groups, fixed tables). Winner = most pixels; tie = group whose first pixel comes first reading row by row. Main colour = integer mean (rounded) of the winner's pixels. One pass over the pixels, O(pixels).
- Order of decisions: no opaque pixel -> Letters; plate; flat (>= 90% in the winning group; nearly white -> dark neutral disc, shape as it is, else white drawing); else Other with `MakeVisible`.
- Disc colour after `MakeVisible` is the colour of the first step that holds. Step k moves the disc 8*k percent toward black (uniform scale, hue kept), or toward white (uniform mix, hue kept) if the brightest channel of the start is below 100. Steps 0..12. If none holds, the step with the most visible pixels wins (earliest on a tie). Always ends: at most 13 passes over the pixels.
- Fail soft: width/height <= 0, buffer length != width*height*4, or more than 2048*2048 pixels -> `Letters`, nothing read. The pixel count is a `long` compared to the cap before multiplying by 4, so int.MaxValue x int.MaxValue cannot overflow.
- `RoundIconLayout.Place`: box = tile * 0.66 (26.4 DIP on the 40 tile). One formula: factor = min(1, box*scale / longerSidePixels), so a picture that fits is drawn at its own size (never stretched) and a larger one is fitted to the box keeping its aspect; centred. Scale 0/negative/NaN/infinite counts as 1 (as `IconFit.Place`); a tile size that is not positive and finite counts as 0; 1e300 / 1e-300 / 1e-310 / int extremes give finite boxes inside the tile.
- Sample edges: every pixel is alpha 255 or fully transparent (0,0,0,0); a pixel is inside a shape when its centre (x+0.5, y+0.5) is. No antialiasing, so all counts are exact. Ring quarters, clockwise from top left: red, yellow, green, blue (so the 4-way tie goes to red, as the work order's "darker than rgb(220,60,60)" needs).

## 2. Commands and results

`export PATH="$PATH:/c/Program Files/dotnet"; dotnet test ".worktrees\h1-colour\tests\Island.Tests"`
- Whole project: Total 1228, Passed 1227, Failed 1. The one failure is `ExtensionGuardTests.The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme` (see 5: it is unrelated to this piece).
- `--filter FullyQualifiedName~RoundIcon`: Total 45, Passed 45, Failed 0.
- Guard tests that scan `src/` (no hook, no account name): green.

## 3. Proven, by test

- The eight named `RoundIconTests`: `A_Plate_Icon_Gets_A_Disc_In_Its_Plates_Colour`, `A_Flat_Shape_Is_Drawn_White_On_Its_Own_Colour`, `A_Nearly_White_Flat_Shape_Gets_A_Dark_Disc`, `A_Many_Coloured_Logo_Stays_Visible_On_A_Darker_Disc`, `A_Small_Icon_Is_Never_Stretched`, `No_Opaque_Pixels_Means_Letters`, `Transparency_Is_Kept_When_A_Shape_Turns_White`, `The_Same_Icon_Always_Gets_The_Same_Colour` (also pins both tie directions).
- `RoundIconRuleTests`: the six samples' exact plans (ring disc = rgb(150,41,41)); sizes, hard edges and equal ring quarters; one-pixel icons (alpha 255, 128, 127, 0); every pixel alpha 127 / 128 / 255; one-colour square vs L shape; the 70% line (70 vs 69 of 100 pixels) and the 90% line (90 vs 89 of 100); nearly white at 230 (229 is not); a nearly white plate keeps its own disc; mean of the winning group; a dark logo gets a lighter disc of the same hue; a disc that already works is not moved; the visibility loop for a sweep of 4096 start colours on two icons (stays within the steps, ends); an unwinnable logo ends with the best step; wrong-length buffers, bad sizes, huge claims (10000x10000, int.Max, 2049x2048 with a real buffer), a 512x512 icon is read.
- `RoundIconLayoutTests`: own size / fitted values, exact-fit box, never larger than own size over 9 scalings x 11 sizes, nonsense scales equal scale 1, a sweep of odd scales/sizes/tiles gives finite boxes inside the tile.
- `RoundIconConstantsTests`: every constant pinned, the set of public constants pinned, and each (Claude) marker present in the source comment.

## 4. Not proven / doubts

- **Plate rule vs the ellipse (a conflict in the text).** Read literally, "at least 70% of the square that bounds them" with the bounding rectangle would make the 44x30 ellipse (78.5% of its 44x30 rectangle) a plate, but the work order says it is a flat shape. I read "square" as a square whose side is the longer side of the opaque bounding box: circle 78%, rounded square ~96%, ellipse 54%, ring 62%. Consequence: a plain wide bar (say 2x8 solid) is a flat shape, not a plate. Please confirm this is Dan's meaning.
- A nearly white plate (a white rounded square with a coloured glyph) gets a white disc: the text says plates are drawn as they are on a disc of their main colour and the nearly-white exception is for flat shapes only. Nothing keeps a plate visible against its own disc. Not changed; worth Dan's eye.
- Direction of the disc shift is one-way as the text says; I did not try the other direction when one fails. The "already dark" line (brightest channel below 100) and the 8% step / 12 steps are my choices, all marked (Claude) for OWNER DECISIONS REQUIRED.
- Colours are compared in raw sRGB values, per the text; no judgment of looks (no picture was drawn or looked at). Whether the six discs look right is for the self-test snapshot and Dan.
- Pixels with alpha 128..254 count fully as opaque in every count (the text says alpha of 128 or more).

## 5. Requests to the main session

- No existing assertion needs changing for this piece. `IconFitTests` and the old self-test icon check are replaced by the main session as the work order says; `IconFit.cs` still exists and untouched.
- The red test `ExtensionGuardTests.The_Zip_Holds_The_Addon...` fails in this worktree for a reason outside my territory: the worktree has `core.autocrlf=true`, so the add-on files on disk are CRLF while the committed zip holds LF (the failing byte comparison shows 13,10 expected versus 10 in the zip). It does not involve my files; expect the same in any worktree with autocrlf. The main folder was green at `a31afb5` presumably because its working files differ; I did not investigate further or touch it.
- If the work order should list the constants under OWNER DECISIONS REQUIRED, take them from `RoundIconConstants` (every one marked (Claude) except the ones the text states).

## 6. How to wire it in

1. Off the drawing thread, once per icon, when the icon arrives: `var plan = RoundIconRule.Analyse(icon)` (the `IconImage` Windows gave, straight BGRA). Cache the plan next to the icon (as `GreyIcons.Of` caches the grey copy).
2. `plan.Kind == Letters` -> the two-letter tile. Otherwise the tile face is a full disc filled with `plan.Disc` (`plan.Disc.ToRgb()` if you want the double `Rgb`).
3. The picture drawn on it: if `plan.DrawnWhite`, use `RoundIconRule.Whiten(icon.Bgra)` as the pixels (same width/height, once, cached); else the icon as it is.
4. Placement: `RoundIconLayout.Place(icon.Width, icon.Height, LookConstants.ItemSize, pixelsPerDip)` replaces `IconFit.Place`; it returns the DIP rectangle from the tile's top-left; no round clip is needed any more (the disc is the round shape; a plate icon on its disc is inside the 66% box).
5. Closed picks: grey the whole tile, disc and icon (the plan's disc colour through the same grey step).
6. Self-test and picture: `RoundIconSamples.All` are the six icons in the work order's order; expected plans are in `RoundIconRuleTests.The_Six_Sample_Icons_Get_Exactly_These_Plans`.
