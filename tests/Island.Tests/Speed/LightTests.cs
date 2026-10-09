using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.Speed;

/// <summary>WORK-ORDER-12 section 2: the kinds of moving light.</summary>
public class LightTests
{
    [Theory]
    [InlineData(60.0, 3.0)]
    [InlineData(85.0, 1.0)]
    [InlineData(165.0, 2.2)]
    public void Fixed_Rate_Moves_The_Light_At_Most_Forty_Times_A_Second_And_Nothing_Else_Slower(double hz, double callsPerRefresh)
    {
        var clock = new LightClock();
        var interval = 1000.0 / hz;
        var gap = interval / callsPerRefresh;
        var changes = new List<double>();
        var last = double.NaN;
        // Only the edge moves for the first stretch: the light's place changes at most 40 times a second.
        for (var t = 0.0; t < 5000; t += gap)
        {
            var head = clock.Head(t, LightKind.FixedRate, hz, onlyEdgeMoves: true);
            if (head != last) changes.Add(t);
            last = head;
        }

        for (var i = 1; i < changes.Count; i++) Assert.True(changes[i] - changes[i - 1] >= LightClock.FixedRateIntervalMs - 1e-6, $"{changes[i] - changes[i - 1]:0.0} ms between two places at {hz} Hz");
        Assert.True(changes.Count >= 5000 / LightClock.FixedRateIntervalMs * 0.5, $"{changes.Count} changes: the light must not stand still either");

        // Something else moves (a spring, a page change): the light has its exact place on every call, so nothing else is made slower.
        var calls = 0;
        var exact = 0;
        for (var t = 5000.0; t < 6000; t += gap)
        {
            calls++;
            if (clock.Head(t, LightKind.FixedRate, hz, onlyEdgeMoves: false) == ArcClock.Head(t / 1000.0)) exact++;
        }

        Assert.Equal(calls, exact);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(40.0)]
    [InlineData(44.0)]
    public void A_Screen_That_Refreshes_No_Faster_Than_The_Fixed_Rate_Gets_The_Exact_Place(double hz)
    {
        var clock = new LightClock();
        for (var t = 0.0; t < 2000; t += 1000.0 / hz) Assert.Equal(ArcClock.Head(t / 1000.0), clock.Head(t, LightKind.FixedRate, hz, onlyEdgeMoves: true));
    }

    [Fact]
    public void The_Held_Place_Is_A_Place_On_The_Clock_And_The_Light_Never_Falls_Behind_By_More_Than_One_Fixed_Interval()
    {
        var clock = new LightClock();
        const double Hz = 60;
        for (var t = 0.0; t < 20_000; t += 3.7)
        {
            var head = clock.Head(t, LightKind.FixedRate, Hz, onlyEdgeMoves: true);
            var behind = ArcClock.Head(t / 1000.0) - head;
            behind -= Math.Floor(behind + 0.5); // the shortest way round
            Assert.InRange(behind, -1e-9, LightClock.FixedRateIntervalMs / 1000.0 * LookConstants.ArcSpeedPerSecond + 1e-9);
        }
    }

    [Theory]
    [InlineData(LightKind.AsBefore)]
    [InlineData(LightKind.GraphicsCard)]
    public void The_Other_Kinds_And_An_Unknown_Rate_Give_The_Exact_Place(LightKind kind)
    {
        var clock = new LightClock();
        for (var t = 0.0; t < 2000; t += 5) Assert.Equal(ArcClock.Head(t / 1000.0), clock.Head(t, kind, 60, onlyEdgeMoves: true));
        Assert.Equal(ArcClock.Head(1.0), new LightClock().Head(1000, LightKind.FixedRate, 0, onlyEdgeMoves: true));
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "island-light-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void An_Old_Settings_File_Loads_With_The_Default_Light()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, """{ "schema": 1, "hotkeys": { "showHide": "Ctrl+Q" }, "idleSeconds": 5, "glass": "approved" }""");
            var load = Settings.Load(path);
            Assert.Equal(SettingsStatus.Loaded, load.Status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"movingLight\": \"graphicscard\"")]
    [InlineData("\"movingLight\": \"HalfRate\"")]
    [InlineData("\"movingLight\": 3")]
    [InlineData("\"movingLight\": null")]
    public void The_Old_Moving_Light_Field_Is_Ignored_Whatever_It_Holds_And_Is_Not_Written_Again(string field)
    {
        // WORK-ORDER-13 (Dan's Q1): the light is not a setting any more. A file that still has the field loads, and saving it does not write the field.
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ " + field + " }");
            var load = Settings.Load(path);
            Assert.Equal(SettingsStatus.Loaded, load.Status);
            Assert.True(load.Settings.Save(path));
            Assert.DoesNotContain("movingLight", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_Island_Picks_The_Light_Itself_The_Card_Where_It_Can_Be_Had_And_The_Fixed_Rate_Where_It_Cannot()
    {
        var host = File.ReadAllText(RepoPaths.File("src", "Island.App", "AppHost.cs"));
        Assert.Contains("OutsideGate.Current.SelfTest ? LightKind.AsBefore : GraphicsLightAvailable ? LightKind.GraphicsCard : LightKind.FixedRate", host);
        Assert.False(File.Exists(RepoPaths.File("src", "Island.Core", "SettingsEdit", "LightChoice.cs")), "the Moving light setting is gone");
    }

    private static readonly Rgb Page = Rgb.FromHex("#1F6FFF");

    [Fact]
    public void The_Graphics_Card_Light_Is_Given_The_Old_Lights_Numbers()
    {
        var focus = LightSpec.For(Page, ModeMark.Look.Approved)!;
        Assert.Equal(LookConstants.RimInset, focus.Inset);
        Assert.Equal(new StrokeSpec(Page, LookConstants.BaseRimAlpha, LookConstants.BaseRimWidth, false), focus.BaseRim);
        var hot = ColorMath.ArcColor(Page);
        Assert.Equal(new ArcSpec(new StrokeSpec(hot, 1, LookConstants.ArcWidth, true), LookConstants.ArcFraction, 0), focus.FirstArc);
        Assert.Equal(new ArcSpec(new StrokeSpec(hot, LookConstants.SecondArcAlpha, LookConstants.SecondArcWidth, true), LookConstants.SecondArcFraction, LookConstants.SecondArcPhase), focus.SecondArc);
        Assert.Equal(new StrokeSpec(Page, LookConstants.BloomBaseAlpha, LookConstants.BloomBaseWidth, false), focus.BloomRing);
        Assert.Equal(new ArcSpec(new StrokeSpec(Page, 1, LookConstants.BloomArcWidth, true), LookConstants.ArcFraction, 0), focus.BloomArc);
        Assert.Equal(LookConstants.BloomBlurCss, focus.BloomBlurSigma);
        Assert.Equal(LookConstants.BloomLayerAlpha, focus.GlowOpacity);
        Assert.Equal(LookConstants.ArcSpeedPerSecond, focus.SpeedPerSecond);
        Assert.Equal(1 / LookConstants.ArcSpeedPerSecond, focus.LapSeconds, 9);

        // Vibe: the glow is the approved one times the breath, nothing else differs.
        for (var t = 0.0; t < 5; t += 0.37)
        {
            var vibe = LightSpec.For(Page, ModeMark.LookOf(Mode.Vibe, t))!;
            Assert.Equal(LookConstants.BloomLayerAlpha * ModeMark.Breath(t), vibe.GlowOpacity, 12);
            Assert.Equal(focus.FirstArc, vibe.FirstArc);
            Assert.Equal(focus.BaseRim, vibe.BaseRim);
        }

        // Do not disturb draws a dashed rim and no light: the old drawing keeps it.
        Assert.Null(LightSpec.For(Page, ModeMark.LookOf(Mode.DND, 0)));
        // Part way through a cross-fade the numbers scale with the look.
        var half = LightSpec.For(Page, new ModeMark.Look(1, 0.5, 0.5, 0))!;
        Assert.Equal(0.5, half.FirstArc!.Value.Stroke.Alpha, 12);
        Assert.Equal(LookConstants.SecondArcAlpha * 0.5, half.SecondArc!.Value.Stroke.Alpha, 12);
        Assert.Equal(LookConstants.BaseRimAlpha * 0.5, half.BaseRim!.Value.Alpha, 12);
    }

    [Theory]
    [InlineData(600.0, 120.0, 40.0)]
    [InlineData(360.0, 64.0, 32.0)]
    [InlineData(64.0, 64.0, 32.0)]
    [InlineData(900.0, 200.0, 18.0)]
    public void The_Compositors_Path_Starts_Where_It_Is_Assumed_To_And_Runs_Clockwise(double width, double height, double radius)
    {
        var o = RimOutline.Of(100, 20, width, height, radius);
        var old = new RoundedPerimeter(o.X, o.Y, o.Width, o.Height, o.Radius);
        Assert.Equal(old.Length, o.Length, 9);
        var start = old.PointAt(o.CompositorStart);
        Assert.Equal(o.X, start.X, 6); // where the straight part of the left edge begins (what Direct2D's own rounded rectangle does: the self-test measures it)...
        Assert.Equal(o.Y + o.Radius, start.Y, 6);
        var next = old.PointAt(o.CompositorStart + 1.0 / o.Length); // ...and a pixel further along is higher up on the left edge: clockwise on the screen
        Assert.True(next.Y < start.Y || o.Height <= 2 * o.Radius);
        Assert.True(CompositorPath.IsClockwise);
    }

    [Theory]
    [InlineData(0.0, 600.0, 120.0, 40.0)]
    [InlineData(1.234, 360.0, 64.0, 32.0)]
    [InlineData(777.7, 900.0, 200.0, 18.0)]
    public void The_Light_Is_Started_Where_The_Clock_Says(double seconds, double width, double height, double radius)
    {
        var o = RimOutline.Of(100, 20, width, height, radius);
        var old = new RoundedPerimeter(o.X, o.Y, o.Width, o.Height, o.Radius);
        var spec = LightSpec.For(Page, ModeMark.Look.Approved)!;
        var head = ArcClock.Head(seconds);
        foreach (var arc in new[] { spec.FirstArc!.Value, spec.SecondArc!.Value })
        {
            var offset = CompositorPath.TrimOffset(head, arc, o);
            // The compositor's arc starts at compositor-path fraction `offset`, which is old-path fraction `offset + CompositorStart`: where the old arc starts.
            var a = old.PointAt(offset + o.CompositorStart);
            var b = old.PointAt(head + arc.EndAheadOfHead - arc.Fraction);
            Assert.True(Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6, $"{a} against {b}");
            Assert.InRange(offset, 0, 1);
        }

        // The place moves by the old speed: the offset a second later is a clock second further along.
        var later = CompositorPath.TrimOffset(ArcClock.Head(seconds + 1), spec.FirstArc!.Value, o);
        var moved = later - CompositorPath.TrimOffset(head, spec.FirstArc!.Value, o);
        moved -= Math.Floor(moved + 0.5);
        Assert.Equal(LookConstants.ArcSpeedPerSecond, moved, 9);
    }

    [Fact]
    public void Follow_Gives_The_Compositor_Its_Strokes_Only_When_The_Spec_Changed()
    {
        // The compositor cannot be built in a test; what the source does is pinned (the self-test's LightStage checks the numbers the compositor was given).
        var text = File.ReadAllText(RepoPaths.File("src", "Island.Glass", "MovingLight.cs"));
        var follow = text[text.IndexOf("private void FollowCore", StringComparison.Ordinal)..text.IndexOf("private void KeepOrder", StringComparison.Ordinal)];
        Assert.Contains("if (!spec.Equals(_appliedSpec))", follow);
        Assert.Contains("_appliedSpec = spec;", follow);
        Assert.Contains("_appliedSpec = null;", text[text.IndexOf("private void ShowCore", StringComparison.Ordinal)..text.IndexOf("public void Hide()", StringComparison.Ordinal)]);
    }

    [Fact]
    public void A_Self_Test_Island_Draws_The_Light_As_Before()
    {
        // WORK-ORDER-12 section 2: the self-test's own pictures and checks were written for the old light. An island made by a stage is "as before" unless the stage asks otherwise, and under the
        // self-test the app host picks "as before" itself. (The self-test's LightStage checks the same on a made island; the compositor cannot be built in a test.)
        var controller = File.ReadAllText(RepoPaths.File("src", "Island.App", "IslandController.cs"));
        Assert.Contains("public LightKind Light { get; set; } = LightKind.AsBefore;", controller);
    }
}
