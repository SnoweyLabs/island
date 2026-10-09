using System.Globalization;
using System.Windows.Media;
using Island.Core;

namespace Island.App;

/// <summary>
/// Section 5 checks: the whole sequence run through the same handlers the keybinds will call
/// (summon, each page, dismiss), with the real windows. No key is pressed and no input is faked.
/// </summary>
internal sealed class MotionStage(SelfTestReport report, TimeSpan hangLimit)
{
    private const int Summons = 5;
    private const int ArcPoints = 400;

    private IslandRuntime _rt = null!;
    private double _scale;

    public async Task RunAsync(IntPtr foregroundBefore)
    {
        _rt = new IslandRuntime();
        try
        {
            _rt.Show();
            using var watch = new FocusWatch(_rt.Host.Capsule.Handle, _rt.Host.Shadow.Handle);
            _scale = VisualTreeHelper.GetDpi(_rt.Host.Capsule).DpiScaleX;
            await Task.Delay(200);

            CheckHiddenBlocksNothing("before the first summon");
            await RunSequenceAsync();
            await RepeatedSummonsAsync();
            await DismissAndCheckQuietAsync();
            CheckHiddenBlocksNothing("after the island left");

            var after = Native.GetForegroundWindow();
            report.Info["focusAfterMotion"] = after == foregroundBefore ? "same" : "different";
            report.Info["foregroundChangesDuringMotion"] = watch.Changes;
            report.Check("no island window was ever the foreground window during the whole sequence", !watch.OurWindowWasForeground,
                $"foreground changed {watch.Changes} times (other windows, if any, are not the island's doing); at the end it is {(after == foregroundBefore ? "same" : "different")} as before");
        }
        finally
        {
            _rt.Dispose();
        }
    }

    private async Task RunSequenceAsync()
    {
        var c = _rt.Controller;
        var m = c.Machine;

        report.Check("before the first summon the controller is detached from the frame callback and the island is hidden",
            !c.Attached && m.Phase == IslandPhase.Hidden, $"attached {c.Attached}, phase {m.Phase}");

        c.ShowHide();
        report.Check("the summon key handler starts the fly-in", m.Phase == IslandPhase.FlyingIn, $"phase {m.Phase}");

        var open = await Waiter.UntilAsync(() => m.Phase == IslandPhase.Open, "the machine reaching Open", hangLimit, report);
        report.Check("the machine reached Open", open, $"phase {m.Phase}");
        await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), "the capsule open and at rest", hangLimit, report);

        // Frames advance while open.
        var framesBefore = c.FramesDrawn;
        await Task.Delay(500);
        report.Check("a frame counter advanced while the island was open", c.FramesDrawn > framesBefore + 5,
            $"{c.FramesDrawn - framesBefore} frames in 500 ms");

        CheckClickThrough("while open");
        await CheckArcMovesAsync();
        await CheckEachPageAsync();
    }

    /// <summary>
    /// Two snapshots of the live island, far enough apart for the arc to have moved, show the arc in
    /// different places, and each place is where the arc clock says it is (the arc covers the 28% of
    /// the outline behind its head).
    /// </summary>
    private async Task CheckArcMovesAsync()
    {
        var c = _rt.Controller;
        var first = SnapshotFront();
        var head0 = c.LastArcHead;
        var profile0 = RimProfile(first);

        await Task.Delay(1200);
        var second = SnapshotFront();
        var head1 = c.LastArcHead;
        var profile1 = RimProfile(second);
        var centre0 = BrightRunCentre(profile0);
        var centre1 = BrightRunCentre(profile1);

        var expected0 = Wrap(head0 - LookConstants.ArcFraction / 2);
        var expected1 = Wrap(head1 - LookConstants.ArcFraction / 2);
        var off0 = Math.Abs(WrapSigned(centre0 - expected0));
        var off1 = Math.Abs(WrapSigned(centre1 - expected1));
        var moved = Wrap(centre1 - centre0);

        report.Info["arcMove"] = $"arc centre {Fmt(centre0)} -> {Fmt(centre1)} of the outline (clock says {Fmt(expected0)} -> {Fmt(expected1)}); moved {Fmt(moved)}";
        report.Check("two snapshots taken while open show the arc in different places", !first.SameAs(second) && Math.Abs(WrapSigned(moved)) > 0.05,
            $"moved {Fmt(moved)} of the outline");
        report.Check("in each snapshot the arc is where its clock says (18% of the outline per second)", off0 < 0.06 && off1 < 0.06,
            $"differences {Fmt(off0)} and {Fmt(off1)} of the outline");
    }

    private async Task CheckEachPageAsync()
    {
        var m = _rt.Controller.Machine;
        var neverLeftOpen = true;
        foreach (var page in Pages.BuiltIn)
        {
            _rt.Controller.PageKey(page.Id);
            var shown = await Waiter.UntilAsync(
                () => m.ContentsPageId == page.Id && m.ContentsVisible && m.IsAtRest,
                $"the {page.Id} page open and at rest", hangLimit, report);
            neverLeftOpen &= m.Phase == IslandPhase.Open;
            report.Check($"page key {page.Id}: the island shows that page", shown && m.PageId == page.Id,
                $"phase {m.Phase}, contents of {m.ContentsPageId}");
            await Task.Delay(120);
        }

        report.Check("changing page while open never closed the island", neverLeftOpen, "phase stayed Open");
    }

    /// <summary>Repeated real summons: the time to open and at rest differs by no more than one refresh interval (EVALS.md M4); every frame interval is recorded (M6).</summary>
    private async Task RepeatedSummonsAsync()
    {
        var c = _rt.Controller;
        var m = c.Machine;
        var intervals = new List<double>();

        // Start from hidden.
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden before the repeated summons", hangLimit, report);
        await Task.Delay(200);

        var stamps = new List<double>();
        c.FrameIntervals = intervals;
        c.FrameStamps = stamps;
        var durations = new List<double>();
        var seen = new List<double>();
        for (var i = 0; i < Summons; i++)
        {
            c.PageKey(PageIds.Media);
            var ok = await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), $"summon {i + 1}: capsule open and at rest", hangLimit, report);
            if (ok)
            {
                durations.Add(c.RestDurationMs);
                seen.Add(c.AtRestMs - c.SummonedMs);
            }

            await Task.Delay(300);
            c.ShowHide();
            await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, $"summon {i + 1}: the island hidden again", hangLimit, report);
            await Task.Delay(200);
        }

        c.FrameIntervals = null;
        c.FrameStamps = null;

        // The refresh interval is read from the frames themselves: their median.
        var sorted = intervals.OrderBy(x => x).ToList();
        var refresh = sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        var longest = sorted.Count == 0 ? 0 : sorted[^1];
        var late = sorted.Count(x => x > 2 * refresh);

        report.Info["refreshIntervalMs"] = Math.Round(refresh, 2);
        report.Info["summonDurationsMs"] = durations.Select(d => Math.Round(d, 1)).ToList();
        report.Info["summonSeenOnFirstFrameMs"] = seen.Select(d => Math.Round(d, 1)).ToList();
        report.Info["frameIntervals"] = new
        {
            frames = sorted.Count,
            medianMs = Math.Round(refresh, 2),
            longestMs = Math.Round(longest, 2),
            lateFrames = late,
            lateMeans = "later than twice the median frame interval",
            lateAtMsAfterSummon = intervals.Select((v, i) => (v, at: stamps[i])).Where(x => x.v > 2 * refresh)
                .Select(x => $"{Math.Round(x.at)}:{Math.Round(x.v)}").ToList(),
        };

        report.Check("every frame interval of the repeated summons was recorded, with its median, its longest and the count of late frames (EVALS M6: a record for a person, never a gate)",
            sorted.Count > 0 && report.Info.ContainsKey("frameIntervals") && report.Info.ContainsKey("refreshIntervalMs"), $"{sorted.Count} intervals, median {refresh:0.0} ms");

        // The gate is the machine's own time to rest (what the app controls). What the screen showed is
        // that time rounded up to a frame, plus any late frame; it is recorded and judged by a person.
        var spread = durations.Count == 0 ? double.PositiveInfinity : durations.Max() - durations.Min();
        report.Check($"{Summons} summons took the same time to open and be at rest, within one refresh interval", durations.Count == Summons && spread <= refresh + 1,
            $"spread {Fmt(spread)} ms, refresh interval {Fmt(refresh)} ms, durations {string.Join(", ", durations.Select(Fmt))}");
        var seenSpread = seen.Count == 0 ? double.PositiveInfinity : seen.Max() - seen.Min();
        report.Info["summonSeenSpreadMs"] = Math.Round(seenSpread, 2);
        if (seenSpread > refresh + 1)
        {
            report.NeedsHumanVerify.Add(
                $"As seen on the first frame at rest, the summon times differed by {Fmt(seenSpread)} ms (one refresh interval is {Fmt(refresh)} ms), because of late frames. Watch ten summons: does the ball come in the same way every time?");
        }

        if (late > 0)
        {
            report.NeedsHumanVerify.Add(
                $"{late} of {sorted.Count} frames during the repeated summons were later than twice the refresh interval ({Fmt(refresh)} ms); longest {Fmt(longest)} ms. A busy laptop can cause that. Watch ten summons: does the ball come in smoothly?");
        }
    }

    private async Task DismissAndCheckQuietAsync()
    {
        var c = _rt.Controller;
        var m = c.Machine;
        c.PageKey(PageIds.Media);
        await Waiter.UntilAsync(() => !double.IsNaN(c.AtRestMs), "the capsule open before the final dismiss", hangLimit, report);

        var started = c.NowMs;
        c.ShowHide();
        report.Check("the dismiss key handler starts the fly-out", m.Phase == IslandPhase.Closing, $"phase {m.Phase}");
        var hidden = await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the machine reaching Hidden", hangLimit, report);
        var took = c.NowMs - started;
        report.Check("the machine reached Hidden after the dismiss", hidden, $"{Fmt(took)} ms after the key");

        await Task.Delay(100);
        var before = c.FramesDrawn;
        var wait = Math.Max(1500, (int)(took * 1.5));
        await Task.Delay(wait);
        var after = c.FramesDrawn;
        report.Check("the frame counter did not advance while hidden, over a wait longer than the whole dismiss animation",
            before == after && !c.Attached, $"counter {before} -> {after} over {wait} ms (dismiss took {Fmt(took)} ms), attached {c.Attached}");
    }

    private void CheckHiddenBlocksNothing(string when)
    {
        var (centre, _, _, _) = Points();
        var ours = Native.ProcessIdOf(Native.WindowFromPoint(centre)) == (uint)Environment.ProcessId;
        report.Check($"hidden ({when}): no pixel at the capsule's resting place catches the mouse", !ours, "WindowFromPoint at the rest centre");
    }

    private void CheckClickThrough(string when)
    {
        var (centre, below, beside, corner) = Points();
        bool Ours(Native.Point p) => Native.ProcessIdOf(Native.WindowFromPoint(p)) == (uint)Environment.ProcessId;
        report.Check($"{when}: the centre of the capsule belongs to this process", Ours(centre), "WindowFromPoint");
        report.Check($"{when}: the shadow just below the capsule belongs to another window", !Ours(below), "WindowFromPoint");
        report.Check($"{when}: the shadow just beside the capsule belongs to another window", !Ours(beside), "WindowFromPoint");
        report.Check($"{when}: a corner of the window belongs to another window", !Ours(corner), "WindowFromPoint");
    }

    private (Native.Point Centre, Native.Point Below, Native.Point Beside, Native.Point Corner) Points()
    {
        Native.GetWindowRect(_rt.Host.Capsule.Handle, out var rect);
        int Px(double dip) => (int)Math.Round(dip * _scale);
        var cx = rect.Left + (rect.Right - rect.Left) / 2;
        var open = CapsuleLayout.SizeFor(Pages.Placeholder(PageIds.Media));
        var top = LookConstants.TopGap;
        return (
            new Native.Point(cx, rect.Top + Px(top + open.Height / 2)),
            new Native.Point(cx, rect.Top + Px(top + open.Height + 10)),
            new Native.Point(cx - Px(open.Width / 2 + 10), rect.Top + Px(top + open.Height / 2)),
            new Native.Point(rect.Left + 1, rect.Top + 1));
    }

    private Snapshot SnapshotFront() =>
        Snapshot.Of(_rt.Host.Capsule.Root,
            (int)Math.Round(_rt.Host.WidthDip * _scale), (int)Math.Round(_rt.Host.HeightDip * _scale), 96 * _scale);

    /// <summary>Brightness of the rim sampled at evenly spaced fractions of the outline.</summary>
    private double[] RimProfile(Snapshot snap)
    {
        var m = _rt.Controller.Machine;
        var size = CapsuleLayout.SizeFor(m.Contents);
        var left = _rt.Host.WidthDip / 2 - size.Width / 2 + LookConstants.RimInset;
        var top = m.DrawnY + LookConstants.RimInset;
        var path = new RoundedPerimeter(left, top, size.Width - 2 * LookConstants.RimInset, size.Height - 2 * LookConstants.RimInset,
            size.Radius - LookConstants.RimInset);

        var profile = new double[ArcPoints];
        for (var i = 0; i < ArcPoints; i++)
        {
            var p = path.PointAt((double)i / ArcPoints);
            var (r, g, b, _) = snap.At((int)Math.Round(p.X * _scale), (int)Math.Round(p.Y * _scale));
            profile[i] = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        return profile;
    }

    /// <summary>
    /// Middle of the longest circular run of rim samples that are close to the brightest 10% of the
    /// rim. The first arc covers 28% of the outline, so the top tenth of the samples lie inside it;
    /// the second arc is fainter and the rest of the rim is dimmer still.
    /// </summary>
    private static double BrightRunCentre(double[] profile)
    {
        var sorted = profile.OrderBy(x => x).ToArray();
        var typical = sorted[sorted.Length / 2];
        var arcLevel = sorted[(int)(sorted.Length * 0.9)];
        var threshold = typical + 0.7 * (arcLevel - typical);

        var bestStart = 0;
        var bestLength = 0;
        for (var start = 0; start < profile.Length; start++)
        {
            if (profile[start] < threshold || profile[(start - 1 + profile.Length) % profile.Length] >= threshold) continue;
            var length = 0;
            while (length < profile.Length && profile[(start + length) % profile.Length] >= threshold) length++;
            if (length > bestLength) { bestLength = length; bestStart = start; }
        }

        return Wrap((bestStart + bestLength / 2.0) / profile.Length);
    }

    private static double Wrap(double f) => f - Math.Floor(f);

    private static double WrapSigned(double f)
    {
        var w = Wrap(f);
        return w > 0.5 ? w - 1 : w;
    }

    private static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
