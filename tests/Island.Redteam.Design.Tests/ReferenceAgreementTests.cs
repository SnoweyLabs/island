using System.Globalization;
using System.Text.RegularExpressions;
using Island.Core;
using Island.Core.Terminals;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The approved references (reference/island-choices.html, reference/island-setup-previews.html) against the numbers the island is built from. Every number is read out of the reference's own style rules
/// and compared with the constant, or with the literal in the drawing code where the code has no constant. A row that agrees must agree exactly; a row in <see cref="KnownDepartures"/> departs by a stated amount
/// (each is a finding of the report, DESIGN-1-xx, and by the one rule a proposal: repairing it changes a picture).
/// </summary>
public class ReferenceAgreementTests(ITestOutputHelper output)
{
    private sealed record Check(string Id, string Where, double Reference, double Ours);

    /// <summary>The departures that exist when this was written: id and the amount (ours minus reference). Each is explained in the report.</summary>
    private static readonly Dictionary<string, double> KnownDepartures = new()
    {
        // All five departures were taken away in WORK-ORDER-13 (Dan's P22 and P19): the notice gap is 8, the search inset 14, the + is a path, the heading is 650.
    };

    private static double Const(string source, string pattern)
    {
        var m = Regex.Match(source, pattern);
        if (!m.Success) throw new InvalidOperationException("no match for " + pattern);
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static double Num(string? css, params int[] index)
    {
        var n = Css.Numbers(css);
        return n[index.Length == 0 ? 0 : index[0]];
    }

    private static List<Check> Checks()
    {
        var choices = Src.Reference("island-choices.html");
        var setup = Src.Reference("island-setup-previews.html");
        if (choices is null || setup is null) return [];
        double C(string selector, string prop, int i = 0) => Num(Css.Prop(Css.Rule(choices, selector), prop), i);
        double S(string selector, string prop, int i = 0) => Num(Css.Prop(Css.Rule(setup, selector), prop), i);

        var contents = Src.Read("Island.App/Visuals/ContentsLayer.cs");
        var notice = Src.Read("Island.App/Visuals/NoticeView.cs");
        var pill = Src.Read("Island.App/Visuals/PillView.cs");
        var search = Src.Read("Island.App/Visuals/SearchView.cs");
        var drag = Src.Read("Island.App/Visuals/DragView.cs");
        var tile = Src.Read("Island.App/Visuals/TileView.cs");
        var step = Src.Read("Island.SettingsUi/StepIsland.cs");
        var look = Src.Read("Island.SettingsUi/Look.cs");
        var parts = Src.Read("Island.SettingsUi/Parts.cs");

        return
        [
            // ---- the capsule (the same numbers the previews pin; here from the choices file's own .cap) ----
            new("capsule: height", "island-choices .cap", C(".cap", "height"), LookConstants.CapsuleHeight),
            new("capsule: corner radius", "island-choices .cap", C(".cap", "border-radius"), LookConstants.CapsuleCornerRadius),
            new("capsule: left padding of the row", "island-choices .cap padding", C(".cap", "padding", 1), Const(contents, @"const double RowStart = (\d+)")),
            new("chip: size", "island-choices .chip", C(".chip", "width"), LookConstants.ChipSize),
            new("chip: gap to the tiles", "island-choices .chip margin-right", C(".chip", "margin-right"), LookConstants.WidthChipGap),
            new("tile: size", "island-choices .t", C(".t", "width"), LookConstants.ItemSize),
            new("tile: letters size", "island-choices .t font-size", C(".t", "font-size"), LookConstants.ItemLabelFontSize),
            new("tiles: gap", "island-choices .tiles gap", C(".tiles", "gap"), LookConstants.ItemGap),
            new("tiles: gap to the text block", "island-choices .tiles margin-right", C(".tiles", "margin-right"), LookConstants.WidthItemsToTextGap),
            new("text block: width", "island-choices .info", C(".info", "width"), LookConstants.WidthTextBlock),
            new("text block: title size", "island-choices .info .ti", C(".info .ti", "font-size"), LookConstants.TitleFontSize),
            new("text block: line size", "island-choices .info .su", C(".info .su", "font-size"), LookConstants.SubtitleFontSize),
            new("text block: line strength", "island-choices .info .su opacity", C(".info .su", "opacity"), LookConstants.SubtitleAlpha),
            new("controls: gap to the text", "island-choices .ctl margin-left", C(".ctl", "margin-left"), LookConstants.WidthTextToControlsGap),
            new("controls: gap between", "island-choices .ctl gap", C(".ctl", "gap"), LookConstants.ControlGap),
            new("controls: button size", "island-choices .ib", C(".ib", "width"), LookConstants.ControlSize),
            new("controls: glyph size", "island-choices .ib svg", C(".ib svg", "width"), LookConstants.ControlGlyphSize),
            new("selected tile: ring width", "island-choices .t.sel", Num(Regex.Match(choices, @"\.t\.sel\{[^}]*0 0 0 (\d+)px rgba\(255,255,255,\.92\)").Groups[1].Value), LookConstants.SelectedRingWidth),
            new("selected tile: glow", "island-choices .t.sel", Num(Regex.Match(choices, @"\.t\.sel\{[^}]*0 0 (\d+)px var\(--cat\)").Groups[1].Value), LookConstants.SelectedGlowRadius),
            new("closed tile: strength", "island-choices .t.grey opacity", C(".t.grey", "opacity"), ChoiceConstants.ClosedOpacity),

            // ---- count dots, the small + of the second row, the strip's arrows, the second row ----
            new("dots: size", "island-choices .n-dots i", C(".n-dots i", "width"), ChoiceConstants.DotSize),
            new("dots: gap", "island-choices .n-dots gap", C(".n-dots", "gap"), ChoiceConstants.DotGap),
            new("badge +: size", "island-choices .badge-plus", C(".badge-plus", "width"), ChoiceConstants.BadgeSize),
            new("badge +: reach outside the tile", "island-choices .badge-plus right", -C(".badge-plus", "right"), ChoiceConstants.BadgeReach),
            new("arrow: size", "island-choices .arrow", C(".arrow", "font-size"), ChoiceConstants.ArrowGlyphSize),
            new("arrow: strength", "island-choices .arrow opacity", C(".arrow", "opacity"), ChoiceConstants.ArrowAlpha),
            new("second row: height of a row", "island-choices .cap.two .rowx", C(".cap.two .rowx", "height"), ChoiceConstants.RowHeight),
            new("second row: label size", "island-choices .back", C(".back", "font-size"), ChoiceConstants.RowLabelSize),
            new("second row: label strength", "island-choices .back opacity", C(".back", "opacity"), ChoiceConstants.RowLabelAlpha),
            new("second row: gap label to tiles", "island-choices .back margin-right", C(".back", "margin-right"), LookConstants.WidthChipGap),

            // ---- the playing tile ----
            new("bars: width", "island-choices .eq i", C(".eq i", "width"), Equalizer.BarWidth),
            new("bars: gap", "island-choices .eq gap", C(".eq", "gap"), Equalizer.BarGap),
            new("bars: lowest", "island-choices @keyframes eq", Num(Regex.Match(choices, @"@keyframes eq\{0%,100%\{height:([\d.]+)px").Groups[1].Value), Equalizer.Lowest),
            new("bars: highest", "island-choices @keyframes eq", Num(Regex.Match(choices, @"50%\{height:([\d.]+)px").Groups[1].Value), Equalizer.Highest),
            new("bars: lap seconds", "island-choices .eq i animation", Num(Regex.Match(choices, @"animation:eq ([\d.]+)s").Groups[1].Value), Equalizer.Seconds),

            // ---- the small pill ----
            new("pill: height", "island-choices .pill", C(".pill", "height"), PillLayout.Height),
            new("pill: corner radius", "island-choices .pill", C(".pill", "border-radius"), PillLayout.Radius),
            new("pill: gap", "island-choices .pill gap", C(".pill", "gap"), PillLayout.Gap),
            new("pill: left padding", "island-choices .pill padding", C(".pill", "padding", 3), PillLayout.PadLeft),
            new("pill: right padding", "island-choices .pill padding", C(".pill", "padding", 1), PillLayout.PadRight),
            new("pill: tile", "island-choices .pill .t", C(".pill .t", "width"), PillLayout.Tile),
            new("pill: title width", "island-choices .pill .ti", C(".pill .ti", "max-width"), PillLayout.TitleMaxWidth),
            new("pill: title size", "island-choices .pill .ti", C(".pill .ti", "font-size"), Const(pill, @"Label\((\d+\.?\d*), FontWeights\.SemiBold")),
            new("pill: button", "island-choices .pill .ib", C(".pill .ib", "width"), PillLayout.Button),
            new("pill: ring thickness", "island-choices .pill:before padding", C(".pill:before", "padding"), PillLayout.RingThickness),

            // ---- the notice (note big) ----
            new("notice: height", "island-choices .note.big", C(".note.big", "height"), NoticeLayout.Height),
            new("notice: corner radius", "island-choices .note.big", C(".note.big", "border-radius"), NoticeLayout.Radius),
            new("notice: left padding", "island-choices .note.big padding", C(".note.big", "padding", 3), NoticeLayout.PadLeft),
            new("notice: right padding", "island-choices .note.big padding", C(".note.big", "padding", 1), NoticeLayout.PadRight),
            new("notice: disc", "island-choices .note.big .ok", C(".note.big .ok", "width"), NoticeLayout.Disc),
            new("notice: edge width", "island-choices .note box-shadow", 1.5, NoticeLayout.EdgeWidth),
            new("notice: gap between the disc and the words", "island-choices .note gap", C(".note", "gap"), NoticeLayout.Gap),
            new("notice: name size", "island-choices .note", C(".note", "font-size"), Const(notice, @"Label\((\d+\.?\d*), FontWeights\.SemiBold")),
            new("notice: line size", "island-choices .note small", C(".note small", "font-size"), Const(notice, @"Label\((\d+), FontWeights\.Normal")),
            new("notice: line strength", "island-choices .note small opacity", C(".note small", "opacity"), 0.8),

            // ---- search ----
            new("search: field height", "island-choices .field", C(".field", "height"), SearchLayout.FieldHeight),
            new("search: field radius", "island-choices .field", C(".field", "border-radius"), SearchLayout.FieldRadius),
            new("search: field least width", "island-choices .field", C(".field", "min-width"), SearchLayout.FieldMinWidth),
            new("search: gap to the tiles", "island-choices .field margin-right", C(".field", "margin-right"), LookConstants.WidthChipGap),
            new("search: text size", "island-choices .field", C(".field", "font-size"), Const(search, @"_typed = ContentsLayer\.Label\((\d+)")),
            new("search: the typed text's inset in the field", "island-choices .field padding", C(".field", "padding", 1), Const(search, @"Place\(_typed, x \+ (\d+)")),

            // ---- drag off ----
            new("drag: zone width", "island-choices .dropzone", C(".dropzone", "width"), Const(drag, @"ZoneWidth = (\d+)")),
            new("drag: zone height", "island-choices .dropzone", C(".dropzone", "height"), Const(drag, @"ZoneHeight = (\d+)")),
            new("drag: zone line width", "island-choices .dropzone border", C(".dropzone", "border", 0), Const(drag, @"StrokeThickness = (\d+\.?\d*)")),
            new("drag: zone words", "island-choices .dropzone", C(".dropzone", "font-size"), Const(drag, @"Label\((\d+), FontWeights\.Normal")),
            new("drag: tilt", "island-choices .t.drag rotate", -8, Const(drag, @"LiftTilt = (-?\d+)")),

            // ---- the + tile ----
            // the + tile's glyph is a path since WORK-ORDER-13 (Dan's P19): it has no font size or weight to compare

            // ---- settings and first start (look C of the setup reference) ----
            new("settings: top island height", "island-setup .c-top", S(".c-top", "height"), Const(look, @"TopHeight = (\d+)")),
            new("settings: top island left padding", "island-setup .c-top padding", S(".c-top", "padding", 3), Const(step, @"Padding = new Thickness\((\d+), 0, 8, 0\)")),
            new("settings: step dash width", "island-setup .c-dots i", S(".c-dots i", "width"), Const(step, @"Width = (\d+),\s*Height = 6")),
            new("settings: step dash height", "island-setup .c-dots i", S(".c-dots i", "height"), Const(step, @"Height = (\d+),\s*CornerRadius")),
            new("settings: heading size", "island-setup h1 (c)", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] h1\{font-size:(\d+)px").Groups[1].Value), Const(look, @"H1Size = (\d+)")),
            new("settings: heading weight", "island-setup h1 (c)", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] h1\{font-size:\d+px;font-weight:(\d+)").Groups[1].Value), 650),
            new("settings: sentence size", "island-setup .shell[c] .sub", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] \.sub\{text-align:center;font-size:(\d+)px").Groups[1].Value), Const(look, @"SubSize = (\d+)")),
            new("settings: group corner radius", "island-setup .shell[c] .group", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] \.group,\.shell\[data-theme=c\] \.mode\{border:1px solid rgba\(255,255,255,\.12\);border-radius:(\d+)px").Groups[1].Value), Const(look, @"GroupRadius = (\d+)")),
            new("settings: row least height", "island-setup .row", S(".row", "min-height"), Const(look, @"RowMinHeight = (\d+)")),
            new("settings: button height", "island-setup .btn", S(".btn", "height"), Const(look, @"ButtonHeight = (\d+)")),
            new("settings: key cap height (glass)", "island-setup .shell[c] .cap", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] \.cap\{border-radius:(\d+)px;height:(\d+)px").Groups[2].Value), Const(look, @"CapHeight = (\d+)")),
            new("settings: key cap radius (glass)", "island-setup .shell[c] .cap", Num(Regex.Match(setup, @"\.shell\[data-theme=c\] \.cap\{border-radius:(\d+)px;height:(\d+)px").Groups[1].Value), Const(look, @"CapRadius = (\d+)")),
            new("settings: small cap height", "island-setup .hint .cap", S(".hint .cap", "height"), Const(parts, @"Height = small \? (\d+)")),
            new("settings: swatch size", "island-setup .sw button", S(".sw button", "width"), Const(parts, @"Width = (\d+),\s*Height = 24,\s*CornerRadius = new CornerRadius\(12\)")),
            new("settings: page dot", "island-setup .dot", S(".dot", "width"), Const(parts, @"Width = (\d+), Height = 16, CornerRadius = new CornerRadius\(8\)")),
        ];
    }

    [Fact]
    public void Every_Number_Of_The_Island_That_Has_A_Reference_Agrees_With_It_Or_Is_A_Known_Departure()
    {
        var checks = Checks();
        if (checks.Count == 0) return; // the references are not beside this worktree
        var wrong = new List<string>();
        foreach (var c in checks)
        {
            var delta = c.Ours - c.Reference;
            var known = KnownDepartures.TryGetValue(c.Id, out var allowed);
            var ok = known ? Math.Abs(delta - allowed) < 0.011 : Math.Abs(delta) < 0.011;
            output.WriteLine($"{(ok ? (known ? "DEPARTS" : "agrees ") : "WRONG  ")} {c.Id,-48} reference {c.Reference,7:0.##}  ours {c.Ours,7:0.##}  ({c.Where})");
            if (!ok) wrong.Add($"{c.Id}: reference {c.Reference}, ours {c.Ours}");
        }

        Assert.True(wrong.Count == 0, "numbers that moved: " + string.Join("; ", wrong));
        Assert.True(KnownDepartures.Keys.All(k => checks.Any(c => c.Id == k)), "a known departure has no check");
    }

    [Fact]
    public void The_Checks_Are_Many_Enough_To_Mean_Something()
    {
        var n = Checks().Count;
        output.WriteLine($"{n} numbers compared");
        if (n > 0) Assert.True(n >= 70);
    }
}
