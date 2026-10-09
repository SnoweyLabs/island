using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.App.Visuals;
using Island.Core;
using Island.Sources.Front;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 2, from invented sessions only (nothing of the machine is read): the pill comes by itself when a source that counts
/// plays, is 44 high, takes no keyboard and leaves the foreground alone; at 25% played the lit part of its ring covers three quarters of the
/// ring's length (within 2%), by the geometry and by the pixels; with a length of zero the ring is absent and the approved moving light stands in; at
/// rest it lets go of the frame callback; it leaves after the idle time when playing stops. A snapshot goes to review/choices/pill.png.
/// </summary>
internal sealed class PillStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    private const double Scale = 2;
    private static readonly Rgb Backdrop = new(27, 31, 58);

    private static MediaSessionInfo Session(string title, PlaybackState state, double position, double length, DateTimeOffset reported) =>
        new("alpha", "alpha.exe", false, title, "Artist", state, position, length, new ProgressReport(position, length, reported, state == PlaybackState.Playing, 1));

    public async Task RunAsync()
    {
        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var store = new PickStore([]);
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(2, pages); // a short idle time: the pill leaves two seconds after playing stops
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;
        var foregroundBefore = Native.GetForegroundWindow();

        // A source that counts starts playing: the pill comes by itself.
        pretend.Sessions = [Session("Song", PlaybackState.Playing, 25, 100, DateTimeOffset.UtcNow)];
        pretend.Raise();
        var came = await Waiter.UntilAsync(() => m.ShowsPill && m.IsAtRest, "the pill open and at rest", hangLimit, report);
        report.Check("the pill comes by itself when a source that counts starts playing, without the keyboard", came && !m.HasKeyboard && m.Phase == IslandPhase.Open, $"{m.Phase}, keyboard {m.HasKeyboard}");
        report.Check("the pill's drawn size is 44 high", Math.Abs(m.DrawnHeight - PillLayout.Height) < 0.5, $"{m.DrawnHeight:0.0} high");
        report.Check("the foreground window is the one from before, or foregroundGranted: false", Native.GetForegroundWindow() == foregroundBefore, "the pill took nothing");
        report.Check("the pill names what plays, with four buttons and a pause glyph while it plays",
            rt.View.Pill.TitleText == "Song" && rt.View.Pill.ButtonCount == 4 && rt.View.Pill.MiddleGlyph == "pause", $"{rt.View.Pill.TitleText}, {rt.View.Pill.ButtonCount} buttons, {rt.View.Pill.MiddleGlyph}");
        report.Check("at rest with its ring drawn the pill lets go of the frame callback (it may stay up for hours)", c.RingShare is not null && !c.RunsAtFrameRate, $"ring {c.RingShare:0.000}, frame rate {c.RunsAtFrameRate}");

        // 25% played, paused so that the number stands still: the lit part is three quarters of the ring.
        pretend.Sessions = [Session("Song", PlaybackState.Paused, 25, 100, DateTimeOffset.UtcNow)];
        pretend.Raise();
        await Waiter.UntilAsync(() => c.RingShare is { } s && Math.Abs(s - 0.75) < 0.001, "the lit share at 0.75", hangLimit, report);
        var share = c.RingShare ?? -1;
        report.Check("at an invented 25% played the lit share is three quarters", Math.Abs(share - 0.75) <= 0.02, $"{share:0.000}");
        report.Check("a paused item's pill shows the play glyph", rt.View.Pill.MiddleGlyph == "play", rt.View.Pill.MiddleGlyph);

        var perimeter = Perimeter(PillLayout.WidestWidth);
        var walk = perimeter.Walk(0, share);
        var start = perimeter.PointAt(0);
        var end = perimeter.PointAt(share);
        var closeEnough = walk.Count > 0 && Dist(walk[0].From, start) < 0.01 && Dist(walk[^1].To, end) < 0.01;
        report.Check("the lit part is the walk of the outline from the top centre, clockwise, for three quarters of its length", closeEnough, $"starts at the top centre, ends at {share:0.000} of the outline");

        // The same by pixels, on a picture of the ring: points along it that are the page colour against white.
        var scene = new OffscreenScene(Backdrop);
        var content = new PillContent("Song", "So", null, IsPaused: true, CanControl: true);
        var ring = scene.RenderPill(content, 0.75, ModeMark.Look.Approved, Scale);
        var litShare = LitFraction(ring, scene);
        report.Check("the pixels of the ring say the same: three quarters are lit in the Media colour (within 3%)", Math.Abs(litShare - 0.75) <= 0.03, $"{litShare:0.000} of {Samples} points along the ring");

        // No length: no ring. The approved moving light stands in, in the Media colour, and the frame callback runs for it.
        pretend.Sessions = [Session("Live", PlaybackState.Playing, 12, 0, DateTimeOffset.UtcNow)];
        pretend.Raise();
        await Waiter.UntilAsync(() => rt.View.Pill.TitleText == "Live", "the live title", hangLimit, report);
        await Task.Delay(300);
        report.Check("with a length of zero the ring is absent and the moving light stands in (never an invented ring)", c.RingShare is null && c.RunsAtFrameRate, $"ring {(c.RingShare is null ? "absent" : "drawn")}");
        var windowsZero = Session("Zero", PlaybackState.Playing, 12, 100, ProgressReport.WindowsZeroDate);
        pretend.Sessions = [windowsZero];
        pretend.Raise();
        await Waiter.UntilAsync(() => rt.View.Pill.TitleText == "Zero", "the zero-date title", hangLimit, report);
        await Task.Delay(300);
        report.Check("a player that reports Windows' zero date has no ring either", c.RingShare is null, "the zero date is how Windows says there is no timeline");

        // Playing stops: it leaves after the idle time, through the springs.
        pretend.Sessions = [];
        pretend.Raise();
        var left = await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the pill gone after playing stopped", TimeSpan.FromSeconds(Math.Max(20, hangLimit.TotalSeconds)), report);
        report.Check("when playing stops the pill leaves after the idle time", left, "it went through the springs");

        await PillObeysTheFrontAsync();
        await ButtonsAsync();
        Snapshots();
    }

    /// <summary>EVALS X5: the pill that came by itself leaves when a fullscreen program comes to the front in Vibe, and comes back when the front is clear; in Do not disturb it never comes.</summary>
    private async Task PillObeysTheFrontAsync()
    {
        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var pages = new PickPages(() => new PickStore([]), world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages);
        rt.Show();
        await Task.Delay(150);
        var m = rt.Controller.Machine;
        rt.Gate.Mode = Mode.Vibe;
        rt.Gate.Reading = () => new FrontReading(FrontState.Clear, null);
        pretend.Sessions = [Session("Song", PlaybackState.Playing, 25, 100, DateTimeOffset.UtcNow)];
        pretend.Raise();
        var came = await Waiter.UntilAsync(() => m.ShowsPill, "the pill up for the front check", hangLimit, report);

        rt.Gate.Reading = () => new FrontReading(FrontState.FullscreenProgram, null);
        pretend.Raise();
        var gone = await Waiter.UntilAsync(() => !m.ShowsPill, "the pill gone when a fullscreen program is in front in Vibe", hangLimit, report);
        report.Check("a pill that came by itself leaves when a fullscreen program comes to the front in Vibe", came && gone, $"came {came}, left {gone}");

        rt.Gate.Reading = () => new FrontReading(FrontState.Clear, null);
        pretend.Raise();
        var back = await Waiter.UntilAsync(() => m.ShowsPill, "the pill back when the front is clear", hangLimit, report);
        report.Check("the pill comes back when the front is clear again", back, $"shows pill {m.ShowsPill}");

        rt.Gate.Mode = Mode.DND;
        pretend.Sessions = [];
        pretend.Raise();
        await Waiter.UntilAsync(() => !m.ShowsPill && m.Phase == IslandPhase.Hidden, "the pill gone for the Do not disturb check", TimeSpan.FromSeconds(Math.Max(20, hangLimit.TotalSeconds)), report);
        pretend.Sessions = [Session("Song", PlaybackState.Playing, 25, 100, DateTimeOffset.UtcNow)];
        pretend.Raise();
        await Task.Delay(500);
        report.Check("in Do not disturb the pill never comes by itself", !m.ShowsPill, $"shows pill {m.ShowsPill}");
    }

    /// <summary>EVALS N9, four buttons that do something: previous, play or pause and next reach the player (recorded on the pretend world), the title brings the player forward (recorded at the one outside door), the search button grows the pill into the search.</summary>
    private async Task ButtonsAsync()
    {
        var pretend = new PretendWorld { Windows = [new OpenWindow(77, "alpha.exe", null, "t", 0)] };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var book = new PickBook(new PickStore([]), null, canSave: false);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true, plusTile: false);
        using var rt = new IslandRuntime(30, pages, book);
        rt.Show();
        await Task.Delay(150);
        var m = rt.Controller.Machine;
        pretend.Sessions = [Session("Song", PlaybackState.Playing, 25, 100, DateTimeOffset.UtcNow)];
        pretend.Raise();
        var came = await Waiter.UntilAsync(() => m.ShowsPill && m.IsAtRest, "the pill for the button check", hangLimit, report);

        rt.View.Pill.RaiseForSelfTest(0);
        rt.View.Pill.RaiseForSelfTest(1);
        rt.View.Pill.RaiseForSelfTest(2);
        report.Check("the pill's previous, play or pause and next buttons send their commands to the player that is playing, in the order pressed",
            came && pretend.Sent.SequenceEqual(["media:alpha:Previous", "media:alpha:PlayPause", "media:alpha:Next"]), string.Join(", ", pretend.Sent));

        rt.View.Pill.RaiseForSelfTest(4);
        report.Check("a click on the pill's title asks the outside door to bring the player's window forward", recording.Calls.Contains("bring:77"), string.Join(", ", recording.Calls));

        rt.View.Pill.RaiseForSelfTest(3);
        var searching = await Waiter.UntilAsync(() => m.SearchOpen, "the search opened from the pill's button", hangLimit, report);
        report.Check("the pill's search button grows the pill into the search", searching, $"search open {m.SearchOpen}");
    }

    private const int Samples = 600;

    private static RoundedPerimeter Perimeter(double width)
    {
        var frame = new Rect(WindowMetrics.Width / 2 - width / 2 + LookConstants.RimInset, LookConstants.TopGap + LookConstants.RimInset, width - 2 * LookConstants.RimInset, PillLayout.Height - 2 * LookConstants.RimInset);
        return new RoundedPerimeter(frame.X, frame.Y, frame.Width, frame.Height, PillLayout.Radius - LookConstants.RimInset);
    }

    private static double Dist(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The share of points along the ring whose pixel is the Media colour and not the white of the played part.</summary>
    private static double LitFraction(Snapshot snap, OffscreenScene scene)
    {
        var perimeter = Perimeter(PillLayout.Width(scene.Pill.TitleWidth));
        var media = Rgb.FromHex(LookConstants.MediaColor);
        var lit = 0;
        for (var i = 0; i < Samples; i++)
        {
            var p = perimeter.PointAt((i + 0.5) / Samples);
            var (r, g, b, _) = snap.At((int)(p.X * Scale), (int)(p.Y * Scale));
            if (r - Math.Max(g, b) > 80 && Math.Abs(r - media.R) < 90) lit++;
        }

        return lit / (double)Samples;
    }

    private void Snapshots()
    {
        var content = new PillContent("Lo-fi beats mix", "YM", null, IsPaused: false, CanControl: true);
        var cells = new (string Label, double? Ring)[] { ("75% left", 0.75), ("25% left", 0.25), ("no length: the moving light", null) };
        var shots = cells.Select(c => (c.Label, Snap: new OffscreenScene(Backdrop).RenderPill(content, c.Ring, ModeMark.Look.Approved, Scale))).ToList();

        var width = (int)Math.Round(360 * Scale);
        var height = (int)Math.Round(90 * Scale);
        var x = (int)Math.Round((WindowMetrics.Width / 2 - 180) * Scale);
        var grid = new Grid { Background = Paint.Brush(Backdrop), Width = 3 * 360 + 4 * 16, Height = 90 + 40 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < shots.Count; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(16, 8, 0, 0) };
            cell.Children.Add(new TextBlock { Text = shots[i].Label, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            cell.Children.Add(new Image
            {
                Source = new System.Windows.Media.Imaging.CroppedBitmap(shots[i].Snap.Bitmap, new Int32Rect(Math.Max(0, x), 0, Math.Min(width, shots[i].Snap.Width - Math.Max(0, x)), Math.Min(height, shots[i].Snap.Height))),
                Stretch = Stretch.None,
                Width = 360,
                Height = 90,
            });
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        grid.UpdateLayout();
        Snapshot.Of(grid, (int)Math.Round(grid.Width * Scale), (int)Math.Round(grid.Height * Scale), 96 * Scale).SavePng(Path.Combine(folder, "choices", "pill.png"));
        report.Check("the pill with its ring and with the moving light was drawn into review/choices/pill.png", File.Exists(Path.Combine(folder, "choices", "pill.png")), "a snapshot proves it draws, not that it looks right");
    }
}
