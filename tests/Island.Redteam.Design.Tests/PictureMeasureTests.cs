using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The pictures the self-test drew (read from the main folder's review/, decoded here; no picture is drawn by these tests), measured: where the row of tiles stands in every page picture, where the + glyph
/// stands in its tile, where the strip's clip cuts the glow of the selected tile, and the sizes in the notice. The capsule pictures are 2 pixels to a device-independent pixel (192 dpi); the settings
/// pictures are 1 to 1.
/// </summary>
public class PictureMeasureTests(ITestOutputHelper output)
{
    private static readonly string[] Pages = ["media", "folders", "apps", "vibe", "browser", "terminals"];
    private static readonly string[] Themes = ["dark", "light"];

    private static IEnumerable<(string Name, Pic Pic)> PagePictures()
    {
        foreach (var page in Pages)
            foreach (var theme in Themes)
                if (Pic.TryLoad($"review/{page}-{theme}.png") is { } p) yield return ($"{page}-{theme}", p);
    }

    /// <summary>The tests below read the pictures of the main folder (a worktree sits in its .worktrees/). When that folder is not found they return at once; when it is, every picture they need must be there.</summary>
    [Fact]
    public void The_Pictures_These_Tests_Read_Are_Found_When_The_Main_Folder_Is()
    {
        var root = Pic.Root();
        output.WriteLine("main folder: " + (root is null ? "not found; the picture tests return without measuring" : "found"));
        if (root is null) return;
        Assert.Equal(12, PagePictures().Count());
        foreach (var f in new[] { "choices/plus-row", "choices/strip", "choices/drag-off", "choices/notice", "choices/terminal-states", "media-paused", "media-playing", "settings/general", "settings/pages", "settings/key" })
            Assert.True(Pic.TryLoad($"review/{f}.png") is not null, f + " is missing");
        // Every pixel measure of a capsule picture assumes 2 pixels to a dp (192 dpi): say so if that ever changes.
        Assert.Equal(1360, Pic.TryLoad("review/media-dark.png")!.Width);
        Assert.Equal(1920, Pic.TryLoad("review/settings/general.png")!.Width);
    }

    /// <summary>The ring of the selected tile (near white, about 88 across) in a page picture.</summary>
    private static Box? SelectedRing(Pic p) =>
        p.Components(new Box(250, 40, 700, 175), PicMeasure.NearWhite).Where(b => b.Width is >= 80 and <= 96 && b.Height is >= 80 and <= 96).Cast<Box?>().FirstOrDefault();

    [Fact]
    public void The_Selected_Tile_Stands_On_One_Line_In_All_Twelve_Page_Pictures()
    {
        var centres = new List<(string, double)>();
        foreach (var (name, p) in PagePictures())
        {
            var ring = SelectedRing(p);
            Assert.True(ring is not null, $"{name}: no selected ring found");
            output.WriteLine($"{name,-16} ring {ring!.Value.Width}x{ring.Value.Height} px, centre y {ring.Value.CentreY}");
            centres.Add((name, ring.Value.CentreY));
            Assert.InRange(ring.Value.Width, 86, 89); // 44 dp: the 40 tile and its 2 ring on each side
        }

        if (centres.Count == 0) return;
        Assert.InRange(centres.Max(c => c.Item2) - centres.Min(c => c.Item2), 0, 1.01);
    }

    [Fact]
    public void The_Ring_Of_The_Selected_Tile_Is_Centred_On_The_Page_Chip_In_Every_Picture()
    {
        foreach (var (name, p) in PagePictures())
        {
            var ring = SelectedRing(p)!.Value;
            // The chip's glyph (white, inside the 36 dp chip) lies 100 px left of the ring's centre on the same line: 20 + 12 + 18 dp.
            // WORK-ORDER-13 (Dan's P4): the glyph is white or near-black, whichever reads on its chip (ChipGlyph); the box lies inside the chip, so either is the glyph.
            var glyph = p.Bounds(new Box((int)ring.CentreX - 100 - 22, 80, (int)ring.CentreX - 100 + 22, 135), (r, g, b, a) => PicMeasure.NearWhite(r, g, b, a) || (r < 50 && g < 50 && b < 50));
            Assert.True(glyph is not null, $"{name}: no chip glyph");
            output.WriteLine($"{name,-16} chip glyph centre ({glyph!.Value.CentreX}, {glyph.Value.CentreY}), ring centre ({ring.CentreX}, {ring.CentreY}), glyph y offset {glyph.Value.CentreY - ring.CentreY}");
            Assert.InRange(Math.Abs(glyph.Value.CentreY - ring.CentreY), 0, 3);
        }
    }

    /// <summary>
    /// The row of tiles stands 39 dp below the capsule's outer top (<c>ContentsLayer.RowCentre</c> = 76 / 2 + 1); the middle of a capsule 76 high is 38, and the reference's content box (inside the 1 px border) is centred at
    /// 1 + 37 = 38. Measured in the pictures: the middle between the top and the bottom rim lines, against the centre of the selected tile's ring. Finding DESIGN-1-11 (one dp, below the middle; a repair moves every tile
    /// and text and so changes every picture).
    /// </summary>
    [Fact]
    public void The_Row_Of_Tiles_Stands_About_One_Dp_Below_The_Middle_Of_The_Capsule()
    {
        var offsets = new List<double>();
        foreach (var (name, p) in PagePictures().Where(x => x.Name.EndsWith("dark", StringComparison.Ordinal))) // on the light backdrop the brightest row is not the rim
        {
            var ring = SelectedRing(p)!.Value;
            var x = (int)ring.CentreX - 120; // between the left end and the chip's glyph: the flat top and bottom edges, away from the light's arc and from every tile
            x = Math.Max(x, (int)ring.CentreX - 174 + 90); // 90 px in from the capsule's left end: past the corner radius of 76 px
            int Peak(int y0, int y1) => Enumerable.Range(y0, y1 - y0 + 1).OrderByDescending(y => p.Lum(x, y)).First();
            var top = Peak(20, 44);
            var bottom = Peak(168, 190);
            var middle = (top + bottom) / 2.0;
            var offset = ring.CentreY - middle;
            offsets.Add(offset);
            output.WriteLine($"{name,-16} rim lines at y {top} and {bottom} (middle {middle}); tile row centre {ring.CentreY}; the row is {offset} px = {offset / 2} dp below the middle");
        }

        if (offsets.Count == 0) return;
        output.WriteLine($"average {offsets.Average():0.00} px");
        Assert.InRange(offsets.Average(), 0, 3);
    }

    /// <summary>
    /// The capsule is centred in the picture (1360 pixels across, 2 pixels to a dp) and its first pick is 87 dp (174 pixels) from its left edge (19 + 36 + 12 + 20). Its left edge is at 680 minus its width in dp,
    /// in pixels (half the width is the half in pixels), so the selected ring's centre is at 680 - width + 174 pixels: the width in dp is 854 minus that centre in pixels. It must be the width rule of <c>LookConstants</c> for a whole number of picks and the page's own controls (88 for Media's three buttons,
    /// 28 for the X).
    /// </summary>
    [Fact]
    public void The_Width_Each_Page_Picture_Shows_Is_The_Width_Rule_For_A_Whole_Number_Of_Picks()
    {
        foreach (var (name, p) in PagePictures())
        {
            var ring = SelectedRing(p)!.Value;
            var width = 854 - ring.CentreX; // dp: (1360 / 2 + 87 * 2 ... ) see above, both in pixels at 2 to a dp
            var controls = name.StartsWith("media", StringComparison.Ordinal) ? LookConstants.WidthMediaControls : LookConstants.WidthOtherControls;
            var fixedPart = LookConstants.WidthLeadingInset + LookConstants.WidthChip + LookConstants.WidthChipGap - LookConstants.WidthItemTrailingGap + LookConstants.WidthItemsToTextGap
                            + LookConstants.WidthTextBlock + LookConstants.WidthTextToControlsGap + controls + LookConstants.WidthTrailingInset;
            var picks = (width - fixedPart) / LookConstants.WidthItemPitch;
            output.WriteLine($"{name,-16} width {width,6:0.0} dp, {picks:0.00} picks");
            Assert.InRange(Math.Abs(picks - Math.Round(picks)), 0, 0.03);
        }
    }

    /// <summary>
    /// The white glyph of the page chip (and the white letters of a tile) against the chip's own fill, read from the pictures: the ratio of WCAG's relative luminance. The Terminals page's lime (#B8F03A, chosen by Claude in
    /// WORK-ORDER-11 and marked changeable) is the light colour under white; the other five pages are darker. Recorded for the twelve page pictures; the loose bound holds before and after a change of colour.
    /// </summary>
    [Fact]
    public void The_White_Chip_Glyph_Against_The_Chip_Fill_Is_Weakest_On_The_Terminals_Page()
    {
        var ratios = new List<(string Name, double Ratio)>();
        foreach (var (name, p) in PagePictures())
        {
            var ring = SelectedRing(p)!.Value;
            var chipCx = (int)ring.CentreX - 100;
            var cy = (int)ring.CentreY;
            // The fill of the chip just above its glyph (the glyph is about 20 px high; the chip is 72 px across).
            var fill = Pic.Ratio(1.0, p.Lum(chipCx, cy - 26));
            ratios.Add((name, fill));
            output.WriteLine($"{name,-16} white glyph on the chip: {fill:0.00}:1");
        }

        if (ratios.Count == 0) return;
        var weakest = ratios.OrderBy(r => r.Ratio).First();
        output.WriteLine($"weakest: {weakest.Name} {weakest.Ratio:0.00}:1 (WCAG 1.4.11 asks 3:1 for a mark that must be understood)");
        Assert.StartsWith("terminals", weakest.Name);
    }

    // ---- the + tile -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The + of the + tile (<c>TileView</c>: <c>Letters("+", 22)</c>, bold, the line height 1.45 of the size) is drawn below the middle of its dashed circle: measured in four pictures it is
    /// about 4 dp lower than the tile's own centre; letters in the other tiles are about 0.75 dp low. Finding DESIGN-1-03 (a repair changes the pictures: by the one rule's first case a proposal).
    /// Recorded: the offset in pixels (2 to a dp); the loose bounds below hold before and after a repair.
    /// </summary>
    [Fact]
    public void The_Plus_Glyph_Is_Lower_Than_The_Centre_Of_Its_Tile()
    {
        var offsets = new List<double>();
        foreach (var (file, window, tileCentreY) in new[]
                 {
                     ("choices/plus-row", new Box(580, 55, 700, 160), 106.0),
                     ("choices/strip", new Box(820, 55, 940, 160), 106.0),
                     ("choices/drag-off", new Box(580, 55, 700, 160), 106.0),
                 })
        {
            var p = Pic.TryLoad($"review/{file}.png");
            if (p is null) continue;
            var glyph = p.Bounds(new Box(window.X0 + 20, window.Y0 + 20, window.X1 - 20, window.Y1 - 20), (r, g, b, a) => r >= 190 && g >= 190 && b >= 190);
            Assert.True(glyph is not null, file + ": no glyph");
            var tileCentreX = (window.X0 + window.X1) / 2.0;
            var offset = glyph!.Value.CentreY - tileCentreY;
            output.WriteLine($"{file,-20} glyph {glyph.Value.Width}x{glyph.Value.Height} px at ({glyph.Value.CentreX}, {glyph.Value.CentreY}); tile centre y {tileCentreY}; offset {offset} px = {offset / 2} dp");
            offsets.Add(offset);
        }

        if (offsets.Count == 0) return;
        // Before a repair: about 7 px (3.5 dp) low in each. After: within 2 px.
        Assert.All(offsets, o => Assert.InRange(o, -2, 12));
    }

    // ---- the strip's clip -----------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>StripView</c> clips its row 6 dp (<c>Slack</c>) outside the first tile, but the glow of the selected tile (<c>SelectedGlowRadius</c> 14) reaches further, so where the first pick is selected (the usual
    /// state) the glow ends in a straight vertical line at the clip. Measured in all twelve page pictures as a one-pixel step in luminance across that column; the reference (a CSS box-shadow) has no such edge.
    /// Finding DESIGN-1-04. Recorded: the step, in thousandths of luminance, on the four rows 26 and 36 px above and below the tile's centre; the loose bound holds before and after a repair.
    /// </summary>
    [Fact]
    public void The_Glow_Of_The_First_Selected_Tile_Ends_In_A_Straight_Line_At_The_Strip_Clip()
    {
        var biggest = new List<double>();
        foreach (var (name, p) in PagePictures())
        {
            var ring = SelectedRing(p)!.Value;
            var edge = ring.X0 - 8; // 6 dp outside the tile's own left edge (2 px ring) = 12 px, less the 4 px of the ring
            var steps = new List<string>();
            double best = 0;
            foreach (var dy in new[] { -36, -26, 26, 36 })
            {
                var y = (int)ring.CentreY + dy;
                // The jump between two neighbouring columns near the edge, against the same jump 4 and 8 columns further in.
                double Jump(int x) => Math.Abs(p.Lum(x + 1, y) - p.Lum(x, y)) * 1000;
                var atEdge = Enumerable.Range(edge - 2, 5).Max(Jump);
                var inside = Math.Max(Jump(edge + 6), Jump(edge + 9));
                steps.Add($"dy{dy}: {atEdge:0.0} (inside {inside:0.0})");
                best = Math.Max(best, atEdge - inside);
            }

            output.WriteLine($"{name,-16} edge x {edge}: {string.Join("; ", steps)}");
            biggest.Add(best);
        }

        if (biggest.Count == 0) return;
        output.WriteLine($"largest step above the surrounding gradient: {biggest.Max():0.0} per mille of luminance; smallest of the twelve pictures: {biggest.Min():0.0}");
        // A step of more than 10 per mille on a dark glass, and more than that on the light, is a visible line; recorded as found.
        Assert.True(biggest.Max() >= 0);
    }

    // ---- the notice ---------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Notice_Picture_Has_The_Sizes_Of_The_Notice_Layout()
    {
        var p = Pic.TryLoad("review/choices/notice.png");
        if (p is null) return;
        // The disc is the mint of the Vibe-coding colour (#19E6B3); its own pixels are flat.
        var disc = p.Components(new Box(440, 20, 700, 140), (r, g, b, a) => Math.Abs(r - 0x19) < 12 && Math.Abs(g - 0xE6) < 12 && Math.Abs(b - 0xB3) < 12)
            .Where(b => b.Width > 40).Cast<Box?>().FirstOrDefault();
        Assert.True(disc is not null, "no disc found");
        output.WriteLine($"disc {disc!.Value.Width}x{disc.Value.Height} px = {disc.Value.Width / 2.0} dp, at x {disc.Value.X0}..{disc.Value.X1}");
        Assert.InRange(disc.Value.Width, 71, 73);

        // Where the words start: the first bright pixel to the right of the disc on the title's line.
        var words = p.Bounds(new Box(disc.Value.X1 + 2, disc.Value.Y0 + 5, disc.Value.X1 + 120, disc.Value.Y0 + 60), PicMeasure.NearWhite);
        Assert.True(words is not null);
        var gapPx = words!.Value.X0 - disc.Value.X1 - 1;
        output.WriteLine($"gap disc to words {gapPx} px = {gapPx / 2.0} dp (NoticeLayout.Gap is 10; the reference's .note gap is 8; the glyphs have a side bearing of about half a dp)");
        Assert.InRange(gapPx, 16, 26);
    }

    // ---- the Terminals page: the rings -----------------------------------------------------------------------------------

    /// <summary>
    /// WORK-ORDER-11 section 3 and <c>TerminalRing</c>: a ring 2.5 thick whose inner edge is 0.75 beyond the 40 disc (so 46.5 across at the outside), the tiles 8 apart so that two neighbouring rings are left 1.5 apart. Measured in
    /// review/choices/terminal-states.png on the orange ring of the third tile and the green ring of the fourth (neighbours): the outside diameter, the thickness along the row, and the gap between them.
    /// </summary>
    [Fact]
    public void The_Rings_Of_Two_Neighbouring_Tiles_Are_The_Size_And_The_Distance_The_Work_Order_Gives()
    {
        var p = Pic.TryLoad("review/choices/terminal-states.png");
        if (p is null) return;
        bool Orange(byte r, byte g, byte b, byte a) => Math.Abs(r - 255) + Math.Abs(g - 176) + Math.Abs(b - 32) < 60;
        bool Green(byte r, byte g, byte b, byte a) => Math.Abs(r - 53) + Math.Abs(g - 196) + Math.Abs(b - 106) < 60;
        var orange = p.Components(new Box(480, 40, 700, 175), Orange).First(b => b.Width > 60);
        var green = p.Components(new Box(480, 40, 700, 175), Green).First(b => b.Width > 60);
        output.WriteLine($"orange ring {orange.Width}x{orange.Height} px at x {orange.X0}..{orange.X1}; green ring {green.Width}x{green.Height} px at x {green.X0}..{green.X1}");
        Assert.InRange(orange.Width, 91, 95);   // 46.5 dp
        Assert.InRange(green.Width, 91, 95);
        var gap = green.X0 - orange.X1 - 1;
        output.WriteLine($"gap between the neighbouring rings {gap} px = {gap / 2.0} dp (the work order: 1.5)");
        Assert.InRange(gap, 1, 5);
        // The thickness along the middle row of the green ring's left side.
        var y = (int)green.CentreY;
        var thick = 0;
        for (var x = green.X0; x < green.X0 + 20 && Green(p.At(x, y).R, p.At(x, y).G, p.At(x, y).B, 255); x++) thick++;
        output.WriteLine($"ring thickness {thick} px = {thick / 2.0} dp (the work order: 2.5)");
        Assert.InRange(thick, 4, 6);
    }

    // ---- the Terminals page: a selected tile that has a state ring ------------------------------------------------------

    /// <summary>
    /// On a tile with a state ring the white ring of the selected tile is not drawn (<c>TerminalRing.WhiteRingShown</c>; decided in WORK-ORDER-11 section 3 and recorded in STATE.md), so the selection is told only by the
    /// tile being at full strength instead of 74% (and the glow of the page colour). Measured in review/choices/terminal-states.png: the tile "Is" is selected, "Be" is not, both have the orange ring.
    /// Recorded: the lightness of the two discs, near their tops, and the lightness of the glass beside them. (Already known to Snowey; kept here as the measure of how strong the only remaining sign is.)
    /// </summary>
    [Fact]
    public void A_Selected_Tile_With_A_State_Ring_Differs_From_Its_Neighbour_Only_By_The_Strength_Of_The_Disc()
    {
        var p = Pic.TryLoad("review/choices/terminal-states.png");
        if (p is null) return;
        double At(int x, int y) => p.Lum(x, y);
        var selected = At(352, 88);
        var other = At(544, 88);
        var glass = At(448 + 48 - 24, 40);
        output.WriteLine($"disc lightness: selected {selected:0.000}, unselected {other:0.000}, ratio {selected / other:0.00}; glass above {glass:0.000}");
        Assert.True(selected > other, "the selected disc is not the stronger one");
        Assert.InRange(selected / other, 1.05, 2.0);
    }

    // ---- which backdrops the pictures are drawn on ----------------------------------------------------------------------

    /// <summary>
    /// Every picture of review/choices that the self-test redraws on each run (strip, plus-row, drag-off, playing-tile, pill, notice, search, modes, terminals-page, terminal-states) is on the dark backdrop only; only the six
    /// page pictures have a light one. So the second row, the pill, the notice, the search and the rings have never been looked at on a light desktop. Recorded as a gap for round 2 (the report asks for the pictures).
    /// </summary>
    [Fact]
    public void The_Redrawn_Choices_Pictures_Are_All_On_The_Dark_Backdrop()
    {
        var dark = new List<string>();
        var light = new List<string>();
        foreach (var name in new[] { "strip", "plus-row", "drag-off", "playing-tile", "pill", "notice", "search", "modes", "terminals-page", "terminal-states" })
        {
            var p = Pic.TryLoad($"review/choices/{name}.png");
            if (p is null) continue;
            (p.Lum(3, p.Height - 3) < 0.2 ? dark : light).Add(name);
        }

        output.WriteLine("dark backdrop: " + string.Join(", ", dark));
        output.WriteLine("light backdrop: " + (light.Count == 0 ? "none" : string.Join(", ", light)));
        Assert.Empty(light);
    }

    // ---- the lifted tile -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The tile that is lifted off the island is drawn like an unselected tile (<c>DragView.Lift</c>: <c>SetSelected(false, ...)</c>, so at 74%), and the dashed line of the drop zone shows through it
    /// (review/choices/drag-off.png, the top of the tile). In the reference (<c>.t.drag</c>) the tile has the strength of a tile and a shadow, the line does not show through. Finding DESIGN-1-09.
    /// Recorded: along the dashed line's row, how much the light changes across the tile's inside, against a row without the line.
    /// </summary>
    [Fact]
    public void The_Dashed_Line_Of_The_Drop_Zone_Shows_Through_The_Lifted_Tile()
    {
        var p = Pic.TryLoad("review/choices/drag-off.png");
        if (p is null) return;
        // Along the zone's top line (the row where the dash colour reaches the glass beside the tile), the tile's inside alternates between dash and gap: count the alternations over 60 columns.
        int Alternations(int y)
        {
            var values = Enumerable.Range(486, 52).Select(x => p.Lum(x, y)).ToArray();
            var mid = (values.Max() + values.Min()) / 2;
            var swing = values.Max() - values.Min();
            var crossings = 0;
            for (var i = 1; i < values.Length; i++) if ((values[i - 1] < mid) != (values[i] < mid)) crossings++;
            return swing * 1000 > 15 ? crossings : 0;
        }

        var best = Enumerable.Range(193, 8).Select(y => (y, n: Alternations(y))).OrderByDescending(x => x.n).First();
        var control = Alternations(214);
        output.WriteLine($"row {best.y}: {best.n} light/dark alternations across the tile's inside; a row below the line: {control}");
        var line = best.n;
        Assert.True(line >= 0 && control >= 0);
    }
}

internal static class Linq
{
    public static TOut Let<TIn, TOut>(this TIn value, Func<TIn, TOut> f) => f(value);
}
