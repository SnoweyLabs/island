using System.Runtime.InteropServices;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>
/// WORK-ORDER-12 section 2, the graphics-card light, on the self-test's pretend world and an island of the stage's own (every other island of the self-test draws the light as before):
/// the compositor's objects hold the old light's numbers and the light is set going where the clock says; at rest in Focus on a page of picks, for one lap of the light, the app draws no
/// frame and renders no layer; the light's two windows are this program's, let every click through, take no keyboard, are not tool-less (no Alt+Tab entry), lie directly above the island's
/// window and directly above its shadow, and are gone when the island is; a lifted tile gets the old light and the light comes back; and an island that cannot make the light draws the old one.
/// Nothing is captured from the screen: how the light looks is for a person (NEEDS-HUMAN-VERIFY).
/// </summary>
internal sealed class LightStage(SelfTestReport report, TimeSpan hangLimit)
{
    private const uint GwHwndPrev = 3;
    private const long WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000, WsExTopmost = 0x8;

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    private static readonly string[] Everything = ["shadow", "bloom", "fill", "bottom-glow", "category-glow", "edge", "rim"];

    public async Task RunAsync()
    {
        Direct2DStartsWhereTheLightAssumes();
        await FallsBackWithoutTheCompositorAsync();
        await GraphicsCardLightAsync();
    }

    /// <summary>
    /// Evidence for the one fact the graphics-card light rests on that Microsoft Learn does not state: where a rounded rectangle path starts and which way it runs. Direct2D is asked about its own
    /// rounded rectangle (the compositor draws with it); the answer is compared with <see cref="CompositorPath"/>'s assumption (the left end of the top edge, clockwise).
    /// </summary>
    private void Direct2DStartsWhereTheLightAssumes()
    {
        var r = Island.Glass.GeometryProbe.Probe(600, 120, 40);
        report.Info["direct2dRoundedRectangle"] = new { worked = r.Worked, length = Math.Round(r.Length, 2), expected = Math.Round(r.ExpectedLength, 2), start = new[] { Math.Round(r.StartX, 2), Math.Round(r.StartY, 2) }, next = new[] { Math.Round(r.NextX, 2), Math.Round(r.NextY, 2) }, tangent = new[] { Math.Round(r.TangentX, 3), Math.Round(r.TangentY, 3) } };
        if (!r.SlotsProven)
        {
            report.NeedsHumanVerify.Add("Direct2D could not be asked where its rounded rectangle starts, so the graphics-card light's assumption about where its path starts and which way it runs rests on nothing here: look at which way round the new light goes.");
            return;
        }

        var startsOnTheLeftEdge = Math.Abs(r.StartX) < 0.01 && Math.Abs(r.StartY - 40) < 0.01; // x = left, y = top + radius
        var runsClockwise = r.TangentY < -0.99 && Math.Abs(r.TangentX) < 0.01; // upwards on the left edge, y growing downwards: clockwise on the screen
        report.Check("Direct2D's rounded rectangle starts where the straight part of its left edge begins and runs clockwise, as the graphics-card light assumes (evidence about the compositor's own path, not proof)",
            startsOnTheLeftEdge && runsClockwise == CompositorPath.IsClockwise, $"starts at ({r.StartX:0.##}, {r.StartY:0.##}), next point ({r.NextX:0.##}, {r.NextY:0.##}), tangent ({r.TangentX:0.##}, {r.TangentY:0.##})");
    }

    private static PretendWorld World() => new() { Windows = [new OpenWindow(9001, "alpha.exe", null, "invented", 0)] };

    private static PickPages Pages(AppWorld world)
    {
        var store = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), Pick.ForProgram("Gamma", PageIds.Apps, "gamma.exe", null)]);
        return new PickPages(() => store, world, synchronousIcons: true);
    }

    /// <summary>An island that asks for the graphics-card light and cannot make it draws the old light, with no error: the rim and the bloom are drawn again as before.</summary>
    private async Task FallsBackWithoutTheCompositorAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        using var rt = new IslandRuntime(3600, Pages(world), null, null, takeKeyboard: false);
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        report.Check("an island made by a stage draws the light as before unless the stage asks otherwise (the pictures and checks were written for it)", c.Light == LightKind.AsBefore && !c.GraphicsLightActive, $"kind {c.Light}, graphics-card light active {c.GraphicsLightActive}");
        c.Light = LightKind.GraphicsCard; // no light was made for this island
        c.SetMode(Mode.Focus, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => c.Machine.IsAtRest, "the island open without a graphics-card light", hangLimit, report);
        var before = rt.View.LayerRenders();
        await Task.Delay(1500);
        var after = rt.View.LayerRenders();
        var light = after.Where((a, i) => a.Name is "rim" or "bloom" && a.Renders > before[i].Renders).Count();
        report.Check("an island that asks for the graphics-card light and cannot make it draws the light the old way, by itself", !c.GraphicsLightActive && light == 2 && !rt.MovingLightAvailable,
            $"graphics-card light active {c.GraphicsLightActive}, rim and bloom drawn again: {light} of 2, unavailable reason {rt.MovingLightUnavailableReason}");
    }

    private async Task GraphicsCardLightAsync()
    {
        var world = AppWorld.Pretend(World(), new OutsideSelfTestActions());
        using var rt = new IslandRuntime(3600, Pages(world), null, null, takeKeyboard: false);
        rt.Show();
        rt.EnableMovingLight();
        await Task.Delay(300);
        report.Info["graphicsLightReason"] = rt.MovingLightUnavailableReason ?? "available";
        report.Info["graphicsLightHResult"] = rt.MovingLight?.UnavailableHResult ?? 0;
        if (!rt.MovingLightAvailable)
        {
            report.NeedsHumanVerify.Add($"The graphics-card light could not be made on this computer ({rt.MovingLightUnavailableReason}), so its checks were not run: the island draws the light the lighter way here.");
            return;
        }

        var c = rt.Controller;
        var m = c.Machine;
        var light = rt.MovingLight!;
        c.Light = LightKind.GraphicsCard;
        c.SetMode(Mode.Focus, instant: true);
        c.MainKey();
        await Waiter.UntilAsync(() => m.IsAtRest && c.GraphicsLightActive, "the island open with the graphics-card light", hangLimit, report);
        await Task.Delay(400);

        // The compositor's objects hold the old light's numbers, and the light was set going.
        var applied = light.Applied;
        var drawn = RimOutline.Of(rt.Host.WidthDip / 2 - m.DrawnWidth / 2, m.DrawnY, m.DrawnWidth, m.DrawnHeight, m.DrawnRadius);
        report.Check("the light is drawn by the compositor: its windows are shown, the old rim and bloom are not drawn, and the compositor was set going", c.GraphicsLightActive && light.IsShown && rt.View.GpuLightOn && applied is { Animating: true },
            $"active {c.GraphicsLightActive}, shown {light.IsShown}, animating {applied?.Animating}");
        var spec = LightSpec.For(Rgb.FromHex(m.Page.Color), ModeMark.Look.Approved);
        report.Check("the compositor's objects were given the old light's numbers (the rim, the arcs, the glow, the speed) and the outline the old rim is drawn on",
            applied is not null && spec is not null && SameNumbers(applied.Spec, spec) && Near(applied.Outline, drawn), $"{(applied is null ? "nothing applied" : "numbers equal " + (spec is not null && SameNumbers(applied.Spec, spec)))}");

        // The gate: at rest in Focus on a page of picks, for one lap of the light, no frame and no layer drawn.
        var quiet = await Waiter.UntilAsync(() => c.IsQuiet, "the capsule at rest leaving the frame callback", hangLimit, report);
        var frames = c.FramesDrawn;
        var renders = rt.View.LayerRenders();
        await Task.Delay(TimeSpan.FromSeconds(1 / LookConstants.ArcSpeedPerSecond + 0.2)); // one lap of the light is 5.6 seconds
        var same = rt.View.LayerRenders().Select((r, i) => r.Renders == renders[i].Renders).All(x => x);
        report.Check("with the graphics-card light, at rest in Focus on a page of picks, for one lap of the light the app draws no frame and renders no layer",
            quiet && !c.RunsAtFrameRate && c.FramesDrawn == frames && same && c.GraphicsLightActive, $"{c.FramesDrawn - frames} frames, layers {(same ? "all" : "not all")} unchanged over a lap");

        // The windows are the island's own, in their places, and let every click through.
        var capsule = rt.Host.Capsule.Handle;
        var shadow = rt.Host.Shadow.Handle;
        var rim = light.RimWindowHandle;
        var glow = light.GlowWindowHandle;
        bool Own(IntPtr h) => Native.ProcessIdOf(h) == (uint)Environment.ProcessId;
        bool Styled(IntPtr h) => (Native.GetExStyle(h) & (WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate | WsExTopmost)) == (WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate | WsExTopmost);
        report.Check("the light's two windows are this program's own, topmost, never activated, out of Alt+Tab and let every click through",
            Own(rim) && Own(glow) && Styled(rim) && Styled(glow), $"own {Own(rim) && Own(glow)}, styles {Styled(rim) && Styled(glow)}");
        report.Check("the lit rim lies directly above the island's window and the glow directly above its shadow window", GetWindow(capsule, GwHwndPrev) == rim && GetWindow(shadow, GwHwndPrev) == glow,
            $"above the island's window: {(GetWindow(capsule, GwHwndPrev) == rim ? "the rim" : "something else")}; above the shadow: {(GetWindow(shadow, GwHwndPrev) == glow ? "the glow" : "something else")}");
        Native.GetWindowRect(capsule, out var rect);
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(rt.Host.Capsule).DpiScaleX;
        var centre = new Native.Point(rect.Left + (rect.Right - rect.Left) / 2, rect.Top + (int)Math.Round((LookConstants.TopGap + m.DrawnHeight / 2) * scale));
        var hit = Native.WindowFromPoint(centre);
        report.Check("a click at the middle of the capsule reaches the capsule, not the light's windows", hit == capsule || Native.ProcessIdOf(hit) != (uint)Environment.ProcessId, $"the window under the pointer is {(hit == capsule ? "the capsule" : hit == rim || hit == glow ? "a light window" : "another")}");

        // The glow is cut out of the island's inside, as the old bloom is (it is drawn only outside the capsule's outline).
        Native.GetWindowRect(glow, out var glowRect);
        var topEdge = rect.Top + (int)Math.Round(LookConstants.TopGap * scale);
        bool GlowAt(int x, int y) => light.GlowShownAt(x - glowRect.Left, y - glowRect.Top);
        var outsideShown = GlowAt(centre.X, topEdge - 6);
        var middleShown = GlowAt(centre.X, centre.Y);
        var insideShown = GlowAt(centre.X, topEdge + 6);
        report.Check("the glow is cut out of the island's inside as the old bloom is: shown just outside the outline, not at its middle and not just inside it", outsideShown && !middleShown && !insideShown,
            $"just outside {outsideShown}, middle {middleShown}, just inside {insideShown}");

        // A lifted tile gets the old light, and the light comes back when it is let go.
        var beforeLift = rt.View.LayerRenders();
        c.IsLifted = () => true;
        c.HandleKey(0x27); // a key that does nothing here, so that a frame is drawn at once
        await Waiter.UntilAsync(() => !c.GraphicsLightActive, "the old light while a tile is lifted", hangLimit, report);
        await Task.Delay(300);
        var duringLift = rt.View.LayerRenders();
        var oldDrawn = duringLift.Where((a, i) => a.Name is "rim" or "bloom" && a.Renders > beforeLift[i].Renders).Count();
        report.Check("a lifted tile gets the old light: the compositor's windows go and the rim and the bloom are drawn again", !c.GraphicsLightActive && !light.IsShown && !rt.View.GpuLightOn && oldDrawn == 2,
            $"active {c.GraphicsLightActive}, shown {light.IsShown}, old layers drawn {oldDrawn} of 2");
        c.IsLifted = () => false;
        c.HandleKey(0x25);
        await Waiter.UntilAsync(() => c.GraphicsLightActive && light.Applied is { Animating: true }, "the graphics-card light back after the lift", hangLimit, report);
        report.Check("when the tile is let go the graphics-card light is back and goes round again", c.GraphicsLightActive && light.IsShown && light.Applied is { Animating: true }, $"active {c.GraphicsLightActive}");
        report.Check("after the light was shown again its rim lies directly above the island's window and its glow directly above the shadow window",
            GetWindow(capsule, GwHwndPrev) == rim && GetWindow(shadow, GwHwndPrev) == glow, $"above the island's window: {(GetWindow(capsule, GwHwndPrev) == rim ? "the rim" : "something else")}");

        // Something raised the island's window (a click that gave it the keyboard does): the next beat puts the light back directly above it.
        Native.SetWindowPos(capsule, IntPtr.Zero, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate); // HWND_TOP: the top of its band, above the rim
        var raised = GetWindow(capsule, GwHwndPrev) != rim;
        c.HandleKey(0x27);
        await Waiter.UntilAsync(() => GetWindow(capsule, GwHwndPrev) == rim, "the light's rim back above the island's window", hangLimit, report);
        report.Check("when the island's window is raised above the light the next frame puts the light back directly above it", GetWindow(capsule, GwHwndPrev) == rim && GetWindow(shadow, GwHwndPrev) == glow,
            $"raised above the rim: {raised}; the rim is back: {GetWindow(capsule, GwHwndPrev) == rim}");

        // Where the compositor's animation was started from: the place the clock says at the moment of the hand-over (after the lift above, and after each of the two below).
        bool StartedWhereTheClockSays(out string detail)
        {
            var a = light.Applied;
            if (a is null || !a.Animating || a.Spec.FirstArc is not { } first)
            {
                detail = "no animation was started";
                return false;
            }

            var expected = (float)CompositorPath.TrimOffset(ArcClock.Head(a.OffsetsSetAtMs / 1000.0), first, a.Outline);
            var age = c.NowMs - a.OffsetsSetAtMs;
            detail = $"offset {a.FirstArcOffset:0.0000}, the clock's place {expected:0.0000}, started {age:0} ms ago";
            return Math.Abs(a.FirstArcOffset - expected) < 1e-4 && age >= 0 && age < 4000;
        }

        report.Check("after the lift the compositor's animation was started where the clock says", StartedWhereTheClockSays(out var liftDetail), liftDetail);

        // Gone whenever the island is.
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden", hangLimit, report);
        report.Check("when the island is hidden the light's windows are gone too", !c.GraphicsLightActive && !light.IsShown && !IsWindowVisible(rim) && !IsWindowVisible(glow),
            $"active {c.GraphicsLightActive}, shown {light.IsShown}, windows visible {IsWindowVisible(rim) || IsWindowVisible(glow)}");
        // The pill and the notice stand in the place of the capsule and are drawn with the old light: the compositor's windows stay away while either is up, and the light that comes back with the
        // capsule is started where the clock says.
        c.SetPill(true, true);
        await Waiter.UntilAsync(() => m.ShowsPill, "the pill up", hangLimit, report);
        await Task.Delay(300);
        report.Check("while the pill is up the compositor's windows are not shown", m.ShowsPill && !c.GraphicsLightActive && !light.IsShown && !rt.View.GpuLightOn, $"pill {m.ShowsPill}, active {c.GraphicsLightActive}, shown {light.IsShown}");
        c.SetPill(false, true);
        await Task.Delay(200);
        c.NoticeText = new Island.App.Visuals.NoticeContent("island", "Agent finished — waiting for you");
        c.SetNotice(true);
        await Waiter.UntilAsync(() => m.ShowsNotice, "the notice up", hangLimit, report);
        await Task.Delay(300);
        report.Check("while the notice is up the compositor's windows are not shown", m.ShowsNotice && !c.GraphicsLightActive && !light.IsShown && !rt.View.GpuLightOn, $"notice {m.ShowsNotice}, active {c.GraphicsLightActive}, shown {light.IsShown}");
        c.SetNotice(false);
        await Task.Delay(500);
        c.MainKey();
        await Waiter.UntilAsync(() => c.GraphicsLightActive && light.Applied is { Animating: true }, "the graphics-card light back with the capsule", hangLimit, report);
        report.Check("when the capsule comes back after the pill and the notice the graphics-card light is back and was started where the clock says", c.GraphicsLightActive && StartedWhereTheClockSays(out var backDetail), "the numbers are in the check of the lift");

        report.NeedsHumanVerify.Add("The graphics-card light was never compared with the old one by eye (no screen is captured): open the island and watch its edge: does the light run round it smoothly, with a soft rim, and do the corners and the ball's first moment look right?");
    }

    // The colour that comes through the page's colour change is the same to a fraction of a step of 255.
    private static bool SameStroke(StrokeSpec? a, StrokeSpec? b) =>
        a is null && b is null
        || a is { } x && b is { } y && Math.Abs(x.Colour.R - y.Colour.R) < 0.5 && Math.Abs(x.Colour.G - y.Colour.G) < 0.5 && Math.Abs(x.Colour.B - y.Colour.B) < 0.5
        && Math.Abs(x.Alpha - y.Alpha) < 1e-6 && Math.Abs(x.Width - y.Width) < 1e-9 && x.RoundCaps == y.RoundCaps;

    private static bool SameArc(ArcSpec? a, ArcSpec? b) =>
        a is null && b is null || a is { } x && b is { } y && SameStroke(x.Stroke, y.Stroke) && Math.Abs(x.Fraction - y.Fraction) < 1e-9 && Math.Abs(x.EndAheadOfHead - y.EndAheadOfHead) < 1e-9;

    private static bool SameNumbers(LightSpec a, LightSpec b) =>
        a.Inset == b.Inset && SameStroke(a.BaseRim, b.BaseRim) && SameArc(a.FirstArc, b.FirstArc) && SameArc(a.SecondArc, b.SecondArc) && SameStroke(a.BloomRing, b.BloomRing)
        && SameArc(a.BloomArc, b.BloomArc) && a.BloomBlurSigma == b.BloomBlurSigma && Math.Abs(a.GlowOpacity - b.GlowOpacity) < 1e-9 && a.SpeedPerSecond == b.SpeedPerSecond;

    private static bool Near(RimOutline a, RimOutline b) =>
        Math.Abs(a.X - b.X) < 0.02 && Math.Abs(a.Y - b.Y) < 0.02 && Math.Abs(a.Width - b.Width) < 0.02 && Math.Abs(a.Height - b.Height) < 0.02 && Math.Abs(a.Radius - b.Radius) < 0.02;
}
