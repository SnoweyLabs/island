using System.Diagnostics;
using System.Text;
using Island.Core;

namespace Island.Tests;

/// <summary>
/// Adversarial tests (work order section 8, item 2). The brief was "break it". Tests that demonstrate a
/// real defect are marked DEFECT in a comment: they FAIL until the defect is fixed, and then they stay as
/// the regression test. Everything else here is an attack that did not break the code and stays as a guard.
/// The details, with the exact inputs, are in review/attack.md.
/// </summary>
public class AttackTests
{
    private const double Frame = 1000.0 / 60;

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private readonly record struct Snap(IslandPhase Phase, double Y, double W, double H, double R, string PageId, int Selected, bool Contents);

    private static Snap Take(IslandMachine m) =>
        new(m.Phase, m.Y.Drawn, m.Width.Drawn, m.Height.Drawn, m.Radius.Drawn, m.PageId, m.SelectedItem, m.ContentsVisible);

    private static string Describe(Snap s) =>
        $"{s.Phase} y={s.Y:F6} w={s.W:F6} h={s.H:F6} r={s.R:F6} page={s.PageId} sel={s.Selected} contents={s.Contents}";

    private static void AssertSane(IslandMachine m, string what)
    {
        var drawn = new[] { m.Y.Drawn, m.Width.Drawn, m.Height.Drawn, m.Radius.Drawn, m.DrawnWidth, m.DrawnHeight, m.DrawnRadius, m.DrawnY, m.StretchX, m.StretchY };
        Assert.True(drawn.All(double.IsFinite), $"{what}: a drawn value is not finite");
        var count = m.ContentsItems.Count;
        Assert.True(m.SelectedItem >= 0 && m.SelectedItem <= Math.Max(0, count - 1), $"{what}: selection {m.SelectedItem} out of range for {count} items");
        Assert.True(m.DrawnWidth >= LookConstants.BallSize * LookConstants.MinSizeFraction - 1e-9, $"{what}: drawn width below 94% of the ball");
        Assert.True(m.DrawnHeight >= LookConstants.BallSize * LookConstants.MinSizeFraction - 1e-9, $"{what}: drawn height below 94% of the ball");
        Assert.True(m.DrawnRadius <= Math.Min(m.DrawnWidth, m.DrawnHeight) / 2 + 1e-9, $"{what}: radius larger than half the shape");
        Assert.Equal(m.Phase != IslandPhase.Hidden, m.NeedsFrames);
        if (m.Phase == IslandPhase.Hidden)
        {
            Assert.False(m.ContentsVisible, $"{what}: contents shown while hidden");
            Assert.True(double.IsPositiveInfinity(m.IdleDeadlineMs), $"{what}: hidden but an idle deadline is armed");
        }
        else
        {
            Assert.True(double.IsFinite(m.IdleDeadlineMs) || m.Phase == IslandPhase.Closing, $"{what}: shown with no idle deadline");
        }
    }

    /// <summary>Runs <paramref name="work"/> on its own thread so that a hang fails the test instead of freezing the run.</summary>
    private static bool CompletesWithin(Action work, TimeSpan limit, out Exception? error)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception e) { caught = e; }
        }) { IsBackground = true };
        thread.Start();
        var done = thread.Join(limit);
        error = caught;
        return done;
    }

    private static string Join(IEnumerable<string> lines, int max = 12)
    {
        var all = lines.ToList();
        return all.Count == 0 ? string.Empty : $"{all.Count} problem(s):\n" + string.Join("\n", all.Take(max)) + (all.Count > max ? "\n..." : string.Empty);
    }

    // =====================================================================================
    // 1. A key at every instant of every phase
    // =====================================================================================

    private delegate void Step(IslandMachine m, double at);

    private sealed record Scenario(string Name, (double At, Step Do)[] Events, double Reference, double[] Offsets);

    private static readonly double[] SharedOffsets =
    [
        0, 1e-9, 1e-6, 0.5, Frame, 89.999999, 90, 90.000001, 109.999999, 110, 110.000001, 119.999999, 120, 120.000001,
        209.999999, 210, 210.000001, 319.999999, 320, 320.000001, 409.999999, 410, 410.000001, 559.999999, 560, 560.000001,
        700, 1000, 2000, 4000,
    ];

    private static readonly double[] IdleOffsets = [59_999.999999, 60_000, 60_000.000001, 60_109.999999, 60_110, 60_559.999999, 60_560, 60_561, 62_000];

    private static Step Show => (m, t) => m.ShowHideKey(t);

    private static Step Page(string id) => (m, t) => m.PageKey(id, t);

    private static Step Active => (m, t) => m.Activity(t);

    private static Scenario[] Scenarios() =>
    [
        new("summon", [(0, Show)], 0, [.. SharedOffsets, .. IdleOffsets]),
        new("open then dismiss", [(0, Show), (3000, Show)], 3000, SharedOffsets),
        new("open then switch page", [(0, Show), (3000, Page(PageIds.Folders))], 3000, SharedOffsets),
        new("dismiss during the pre-expand fly-in", [(0, Show), (100, Show)], 100, SharedOffsets),
        new("dismiss between expand and contents", [(0, Show), (350, Show)], 350, SharedOffsets),
        new("page key restarts the fly-in", [(0, Show), (200, Page(PageIds.Browser))], 200, SharedOffsets),
        new("switch restarted before its swap", [(0, Show), (3000, Page(PageIds.Folders)), (3050, Page(PageIds.Apps))], 3050, SharedOffsets),
        new("summon reverses a closing island", [(0, Show), (3000, Show), (3300, Show)], 3300, SharedOffsets),
        new("idle clock poked once", [(0, Show), (30_000, Active)], 30_000, [.. SharedOffsets, .. IdleOffsets]),
        new("idle fires during a switch", [(0, Show), (59_950, Page(PageIds.Vibe))], 59_950, SharedOffsets),
    ];

    private static IslandMachine Replay(Scenario s, double untilMs, double frameMs)
    {
        var m = new IslandMachine();
        var next = 0;
        for (var k = 1; ; k++)
        {
            var frameAt = k * frameMs;
            if (frameAt > untilMs) break;
            while (next < s.Events.Length && s.Events[next].At <= frameAt)
            {
                s.Events[next].Do(m, s.Events[next].At);
                next++;
            }

            m.Tick(frameAt);
        }

        while (next < s.Events.Length && s.Events[next].At <= untilMs)
        {
            s.Events[next].Do(m, s.Events[next].At);
            next++;
        }

        return m;
    }

    private static readonly (string Name, Action<IslandMachine, double> Do)[] Probes =
    [
        ("ShowHide", (m, t) => m.ShowHideKey(t)),
        ("PageKey media", (m, t) => m.PageKey(PageIds.Media, t)),
        ("PageKey folders", (m, t) => m.PageKey(PageIds.Folders, t)),
        ("PageKey browser", (m, t) => m.PageKey(PageIds.Browser, t)),
        ("PageKey unknown", (m, t) => m.PageKey("nope", t)),
        ("PageKey null", (m, t) => m.PageKey(null!, t)),
        ("PageKey empty", (m, t) => m.PageKey(string.Empty, t)),
        ("Activity", (m, t) => m.Activity(t)),
        ("ItemClick -1", (m, t) => m.ItemClick(-1, t)),
        ("ItemClick 1", (m, t) => m.ItemClick(1, t)),
        ("ItemClick int.Max", (m, t) => m.ItemClick(int.MaxValue, t)),
        ("ItemClick int.Min", (m, t) => m.ItemClick(int.MinValue, t)),
        ("Tick", (m, t) => m.Tick(t)),
    ];

    [Fact]
    public void Key_At_Every_Phase_Boundary_Never_Jumps_And_Keeps_Invariants()
    {
        // For every scenario and every instant around its events (just before, exactly at, just after),
        // every input is applied twice-built machine against a reference that only ticked: the input
        // must not move the drawn shape at its own instant, must leave the machine sane, and an island
        // left alone afterwards must always end up hidden.
        var problems = new List<string>();
        foreach (var scenario in Scenarios())
        {
            foreach (var frameMs in new[] { Frame, double.MaxValue })
            {
                foreach (var offset in scenario.Offsets)
                {
                    var t = scenario.Reference + offset;
                    var reference = Replay(scenario, t, frameMs);
                    reference.Tick(t);
                    var before = Take(reference);

                    foreach (var (name, apply) in Probes)
                    {
                        var label = $"[{scenario.Name} | frames {(frameMs < 1e9 ? "60Hz" : "none")} | t={t} | {name}]";
                        try
                        {
                            var m = Replay(scenario, t, frameMs);
                            apply(m, t);
                            AssertSane(m, label);
                            var after = Take(m);
                            CheckInput(name, before, after, label, problems);
                            CheckEventuallyHidden(m, t, label, problems);
                        }
                        catch (Exception e) when (e is not OutOfMemoryException)
                        {
                            problems.Add($"{label} threw {e.GetType().Name}: {e.Message}");
                        }

                        if (problems.Count > 40) goto done;
                    }
                }
            }
        }

    done:
        Assert.True(problems.Count == 0, Join(problems));
    }

    private static void CheckInput(string input, Snap before, Snap after, string label, List<string> problems)
    {
        if (before.Phase != IslandPhase.Hidden)
        {
            // A visible island never teleports: drawn values at the instant of the key are unchanged.
            var moved = Math.Abs(before.Y - after.Y) > 1e-9 || Math.Abs(before.W - after.W) > 1e-9
                        || Math.Abs(before.H - after.H) > 1e-9 || Math.Abs(before.R - after.R) > 1e-9;
            if (moved) problems.Add($"{label} the shape jumped: {Describe(before)}  ->  {Describe(after)}");
        }

        switch (input)
        {
            case "Tick":
                if (before != after) problems.Add($"{label} a bare tick at the same time changed the machine");
                break;
            case "Activity" or "ItemClick -1" or "ItemClick 1" or "ItemClick int.Max" or "ItemClick int.Min":
                if (before.Phase != after.Phase) problems.Add($"{label} {input} changed the phase {before.Phase} -> {after.Phase}");
                break;
            case "PageKey unknown" or "PageKey null" or "PageKey empty":
                if (before != after) problems.Add($"{label} an unknown page id changed the machine");
                break;
            case "ShowHide":
                var wanted = before.Phase is IslandPhase.FlyingIn or IslandPhase.Open ? IslandPhase.Closing : IslandPhase.FlyingIn;
                if (after.Phase != wanted) problems.Add($"{label} ShowHide from {before.Phase} gave {after.Phase}, wanted {wanted}");
                break;
        }
    }

    private static void CheckEventuallyHidden(IslandMachine m, double from, string label, List<string> problems)
    {
        var now = from;
        var limit = from + 60_000 + 20_000;
        while (m.Phase != IslandPhase.Hidden && now < limit)
        {
            now += 50;
            m.Tick(now);
        }

        if (m.Phase != IslandPhase.Hidden)
            problems.Add($"{label} left alone for 80 s it is still {m.Phase}");
        else AssertSane(m, label + " (after closing)");
    }

    [Fact]
    public void Hundreds_Of_Keys_With_Zero_Backwards_And_Repeated_Times_Keep_Invariants()
    {
        // Random keys, hundreds in a row per seed, with gaps of exactly 0, sub-millisecond, ordinary, and
        // a minute; times that repeat and times that go backwards. Springs only move by their step: a
        // backwards or repeated time must not move the drawn shape.
        var problems = new List<string>();
        for (var seed = 1; seed <= 300 && problems.Count < 10; seed++)
        {
            var rng = new Random(seed);
            var m = new IslandMachine(idleSeconds: rng.Next(3) == 0 ? 0.25 : 60);
            double now = 1000, highest = now;
            var before = Take(m);
            for (var i = 0; i < 600; i++)
            {
                var previous = now;
                now += rng.Next(12) switch
                {
                    0 or 1 or 2 => 0,
                    3 => 1e-9,
                    4 => 1e-4,
                    5 => 0.3,
                    6 => rng.NextDouble() * 20,
                    7 => rng.NextDouble() * 120,
                    8 => rng.NextDouble() * 700,
                    9 => -rng.NextDouble() * 400,
                    10 => rng.Next(40) == 0 ? 65_000 : rng.NextDouble() * 3000,
                    _ => -1e-6,
                };
                highest = Math.Max(highest, now);
                var op = rng.Next(7);
                var label = $"[seed {seed} op {i} t={now:F6}]";
                try
                {
                    switch (op)
                    {
                        case 0: m.ShowHideKey(now); break;
                        case 1: m.PageKey(Pages.BuiltIn[rng.Next(5)].Id, now); break;
                        case 2: m.PageKey(rng.Next(2) == 0 ? "nope" : null!, now); break;
                        case 3: m.Activity(now); break;
                        case 4: m.ItemClick(rng.Next(-2, 8), now); break;
                        case 5: m.ItemClick(rng.Next(2) == 0 ? int.MaxValue : int.MinValue, now); break;
                        default: m.Tick(now); break;
                    }

                    AssertSane(m, label);
                    var after = Take(m);
                    var dt = now - previous;
                    if (before.Phase != IslandPhase.Hidden && after.Phase != IslandPhase.Hidden)
                    {
                        // 6000 px/s is above the fastest a spring here ever moves.
                        var bound = (dt > 0 ? 6000 * dt / 1000 : 0) + 1e-6;
                        var jump = new[] { before.Y - after.Y, before.W - after.W, before.H - after.H, before.R - after.R }.Max(Math.Abs);
                        if (jump > bound) problems.Add($"{label} jumped {jump:F4} px in {dt:F6} ms: {Describe(before)} -> {Describe(after)}");
                    }

                    before = after;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    problems.Add($"{label} {e.GetType().Name}: {e.Message}");
                    break;
                }
            }

            // Whatever happened, an island left alone always ends hidden.
            CheckEventuallyHidden(m, highest, $"[seed {seed} after the storm]", problems);
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Same_Input_Sequence_Gives_The_Same_Machine_Whatever_The_Frame_Pattern()
    {
        // Frames are only observers: the same keys at the same times must give the same state, however
        // many ticks come between them and whatever their spacing.
        var failures = new List<string>();
        for (var seed = 1; seed <= 60; seed++)
        {
            var rng = new Random(seed);
            var keys = new List<(double At, int Kind)>();
            double t = 0;
            for (var i = 0; i < 40; i++)
            {
                t += rng.Next(3) == 0 ? rng.NextDouble() * 5 : rng.NextDouble() * 900;
                keys.Add((t, rng.Next(4)));
            }

            var end = t + 3000;
            Snap Run(Func<double, double> nextFrame)
            {
                var m = new IslandMachine(idleSeconds: 4);
                var i = 0;
                double now = 0;
                while (now < end)
                {
                    now = Math.Min(end, nextFrame(now));
                    while (i < keys.Count && keys[i].At <= now)
                    {
                        var (at, kind) = keys[i++];
                        switch (kind)
                        {
                            case 0: m.ShowHideKey(at); break;
                            case 1: m.PageKey(Pages.BuiltIn[(int)(at * 7) % 5].Id, at); break;
                            case 2: m.Activity(at); break;
                            default: m.ItemClick((int)(at * 3) % 5, at); break;
                        }
                    }

                    m.Tick(now);
                }

                return Take(m);
            }

            var steady = Run(n => n + Frame);
            var coarse = Run(n => n + 700);
            var tiny = Run(n => n + 3.1);
            foreach (var other in new[] { coarse, tiny })
            {
                // Out of sight (Hidden) the springs are not drawn, and where the last one stopped depends on
                // which tick noticed it was settled: only the state that matters is compared then.
                var hidden = other.Phase == IslandPhase.Hidden && steady.Phase == IslandPhase.Hidden;
                var same = other.Phase == steady.Phase && other.PageId == steady.PageId && other.Selected == steady.Selected
                           && other.Contents == steady.Contents
                           && (hidden || (Math.Abs(other.Y - steady.Y) < 1e-6 && Math.Abs(other.W - steady.W) < 1e-6
                                          && Math.Abs(other.H - steady.H) < 1e-6 && Math.Abs(other.R - steady.R) < 1e-6));
                if (!same) failures.Add($"seed {seed}: {Describe(steady)}  vs  {Describe(other)}");
            }
        }

        Assert.True(failures.Count == 0, Join(failures));
    }

    [Fact]
    public void Key_Exactly_At_The_Idle_Deadline_Follows_One_Consistent_Rule()
    {
        // The island is dismissed at the deadline instant, before an input at that same instant is handled.
        foreach (var offset in new[] { -1e-6, 0, 1e-6 })
        {
            foreach (var input in new[] { "activity", "click", "page-same", "showhide" })
            {
                var m = new IslandMachine(idleSeconds: 10);
                m.ShowHideKey(0);
                for (double t = Frame; t < 9_990; t += Frame) m.Tick(t);
                var deadline = m.IdleDeadlineMs;
                Assert.Equal(10_000, deadline, 1e-9);
                var at = deadline + offset;
                switch (input)
                {
                    case "activity": m.Activity(at); break;
                    case "click": m.ItemClick(1, at); break;
                    case "page-same": m.PageKey(PageIds.Media, at); break;
                    default: m.ShowHideKey(at); break;
                }

                AssertSane(m, $"{input} at deadline{offset:+0.######;-0.######;+0}");
                if (offset < 0)
                {
                    // Before the deadline the island is open and the clock restarted (or, for ShowHide, it is closing by choice).
                    Assert.Equal(input == "showhide" ? IslandPhase.Closing : IslandPhase.Open, m.Phase);
                    if (input != "showhide") Assert.Equal(at + 10_000, m.IdleDeadlineMs, 1e-9);
                }
                else
                {
                    // At or after the deadline the island is already closing: only a summon turns it round.
                    Assert.Equal(input == "showhide" || input == "page-same" ? IslandPhase.FlyingIn : IslandPhase.Closing, m.Phase);
                }
            }
        }
    }

    [Fact]
    public void Input_After_A_Long_Silence_Closes_First_Then_Handles_The_Input()
    {
        // No frames for ten minutes (laptop asleep with the island open), then a click.
        var m = new IslandMachine();
        m.ShowHideKey(0);
        for (double t = Frame; t < 3000; t += Frame) m.Tick(t);
        m.ItemClick(2, 600_000);
        AssertSane(m, "click after ten minutes");
        Assert.Equal(IslandPhase.Hidden, m.Phase); // closed at 60 s, hidden long before the click; the click is ignored
        Assert.False(m.ContentsVisible);
    }

    // =====================================================================================
    // 2. Time that is not a normal number
    // =====================================================================================

    private static IslandMachine OpenMachine(double idleSeconds = 60)
    {
        var m = new IslandMachine(idleSeconds);
        m.ShowHideKey(0);
        for (double t = Frame; t <= 2000; t += Frame) m.Tick(t);
        Assert.Equal(IslandPhase.Open, m.Phase);
        return m;
    }

    [Fact]
    public void Machine_Tick_With_NaN_Recovers_On_The_Next_Tick()
    {
        // Not a defect, a guard: Tick(NaN) skips that stretch of time (the clock is overwritten by the
        // next valid time), but the machine does not freeze, and a closing island still reaches Hidden.
        var m = OpenMachine();
        m.Tick(double.NaN);
        m.ShowHideKey(2000 + Frame);
        double now = 2000 + Frame;
        for (var i = 0; i < 600 && m.Phase != IslandPhase.Hidden; i++)
        {
            now += Frame;
            m.Tick(now);
        }

        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    [Fact]
    public void Machine_Key_With_NaN_Time_Does_Not_Wedge_The_Machine()
    {
        // DEFECT (fails until fixed): ShowHideKey(NaN) from Hidden arms the idle deadline and the expand
        // timer with NaN, which never compares as due, so the island flies in and then never opens and
        // never closes by itself: it can only be reversed by another key.
        var m = new IslandMachine(idleSeconds: 2);
        m.ShowHideKey(double.NaN);
        double now = 0;
        for (var i = 0; i < 60 * 30; i++)
        {
            now += Frame;
            m.Tick(now);
        }

        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    [Fact]
    public void Machine_Tick_With_Infinity_Does_Not_Hang()
    {
        // DEFECT (fails until fixed): StepSprings loops dt / (1/120 s) times with no cap, so an infinite
        // (or merely enormous) elapsed time never returns. Spring.Frame has a 5 s cap, but the machine
        // calls it one step at a time, so the cap never applies.
        var finished = CompletesWithin(() =>
        {
            var m = OpenMachine();
            m.Tick(double.PositiveInfinity);
        }, TimeSpan.FromSeconds(5), out var error);
        Assert.Null(error);
        Assert.True(finished, "Tick(+infinity) on an open island did not return within 5 s");
    }

    [Fact]
    public void Machine_Tick_After_A_Day_Of_Sleep_Is_Fast()
    {
        // DEFECT (fails until fixed, if slower than the limit on this machine; see attack.md for the
        // measured time): after the idle deadline passes inside one huge elapsed time, the machine still
        // steps every spring 120 times per second of the whole gap, even though the island has long since
        // settled out of sight. An island left open as the lid closes pays this on wake-up, on the UI
        // thread: here 24 hours is over 10 million steps of four springs.
        var m = OpenMachine();
        var clock = Stopwatch.StartNew();
        m.Tick(2000 + 24.0 * 3600 * 1000);
        clock.Stop();
        Assert.Equal(IslandPhase.Hidden, m.Phase);
        Assert.True(clock.ElapsedMilliseconds < 250, $"one Tick after 24 h of silence took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Machine_Tick_After_Eight_Hours_Of_Sleep_Hides_It_And_Stays_Sane()
    {
        var m = OpenMachine();
        m.Tick(2000 + 8.0 * 3600 * 1000);
        AssertSane(m, "after 8 h");
        Assert.Equal(IslandPhase.Hidden, m.Phase);
        m.ShowHideKey(2000 + 8.0 * 3600 * 1000 + 1);
        Assert.Equal(IslandPhase.FlyingIn, m.Phase);
        Assert.Equal(-90, m.Y.Value);
    }

    // =====================================================================================
    // 3. The springs
    // =====================================================================================

    [Fact]
    public void Spring_Frame_With_NaN_Elapsed_Does_Not_Poison_Drawn()
    {
        // DEFECT (fails until fixed): Frame(NaN) stores NaN as the leftover time, because Math.Max(0, NaN)
        // and Math.Min(NaN, 5) are both NaN. Drawn is then NaN for ever, whatever is passed afterwards.
        var s = new Spring(-90, 0, 14).Frame(0.01).Frame(double.NaN);
        s = s.Frame(0.5);
        Assert.True(double.IsFinite(s.Drawn), $"Drawn is {s.Drawn} after a NaN elapsed time");
        Assert.True(double.IsFinite(s.Value));
    }

    [Fact]
    public void Spring_Negative_Infinite_And_Enormous_Elapsed_Stay_Finite_And_Never_Run_Backwards()
    {
        foreach (var elapsed in new[] { -1.0, -1e300, double.NegativeInfinity, double.PositiveInfinity, double.MaxValue, 1e300, 5, 5.0000001, 1e-300, double.Epsilon, 0 })
        {
            var s = new Spring(-90, 0, 14).Frame(0.1);
            var after = s.Frame(elapsed);
            Assert.True(double.IsFinite(after.Value) && double.IsFinite(after.Velocity) && double.IsFinite(after.Drawn), $"elapsed {elapsed}");
            Assert.True(after.Steps >= s.Steps, $"elapsed {elapsed}: steps went backwards");
            Assert.InRange(after.Leftover, 0, Spring.StepSeconds);
            if (elapsed <= 0) Assert.Equal(s.Steps, after.Steps);
        }
    }

    [Fact]
    public void Spring_Target_Change_Every_Step_Never_Jumps_Drawn()
    {
        var rng = new Random(5);
        var s = new Spring(-90, 0, 14);
        for (var i = 0; i < 20_000; i++)
        {
            var drawn = s.Drawn;
            s = s.WithTarget(rng.Next(3) == 0 ? -90 : rng.NextDouble() * 600 - 100);
            Assert.Equal(drawn, s.Drawn, 1e-12);
            var elapsed = rng.Next(4) switch { 0 => 0, 1 => 1e-7, 2 => rng.NextDouble() * 0.01, _ => rng.NextDouble() * 0.05 };
            var next = s.Frame(elapsed);
            Assert.True(double.IsFinite(next.Drawn), $"step {i}");
            Assert.True(Math.Abs(next.Drawn - drawn) <= 8000 * elapsed + 1e-6, $"step {i}: drawn moved {next.Drawn - drawn} in {elapsed} s");
            s = next;
        }
    }

    [Fact]
    public void Spring_Random_Splits_Give_The_Same_Path_As_One_Advance()
    {
        var rng = new Random(77);
        for (var round = 0; round < 400; round++)
        {
            var split = new Spring(-90, 0, 14);
            var whole = split;
            double total = 0;
            var pieces = rng.Next(1, 800);
            for (var i = 0; i < pieces; i++)
            {
                var piece = rng.Next(6) switch { 0 => 0.0, 1 => 1e-9, 2 => 1.0 / 120, 3 => 1.0 / 60, _ => rng.NextDouble() * 0.02 };
                split = split.Frame(piece);
                total += piece;
            }

            whole = whole.Frame(Math.Min(total, 4.99));
            if (total > 4.99) continue;
            Assert.True(Math.Abs(split.Steps - whole.Steps) <= 0, $"round {round}: {split.Steps} steps vs {whole.Steps}");
            Assert.Equal(whole.Value, split.Value, 1e-7);
            Assert.Equal(whole.Drawn, split.Drawn, 1e-6);
        }
    }

    // =====================================================================================
    // 4. Pages that are data
    // =====================================================================================

    private static readonly Page ZeroPage = new("zero", "Zero", "#112233", "x", null, false);
    private static readonly Page ThreePage = new("three", "Three", "#445566", "x", null, false);
    private static readonly Page HugePage = new("huge", "Huge", "#778899", "x", null, false);

    private static IReadOnlyList<Item> CustomItems(Page p) => p.Id switch
    {
        "zero" => [],
        "three" => [new("a", "a", "A", 10), new("b", "b", "B", 20), new("c", "c", "C", 30)],
        "huge" => [.. Enumerable.Range(0, 1000).Select(i => new Item($"i{i}", "s", "X", i % 360))],
        _ => Pages.PlaceholderItems(p),
    };

    [Fact]
    public void Custom_Pages_With_Zero_And_Huge_Item_Counts_Keep_Invariants()
    {
        // Selection stays in range (0 when there are no rows), widths stay finite, every page can be
        // summoned, switched to and closed. A page with no rows is laid out 270 wide (the item term is 0 for no items, WORK-ORDER-3 section 3), with 1000 rows 48 thousand.
        List<Page> pages = [ZeroPage, ThreePage, .. Pages.BuiltIn, HugePage];
        var problems = new List<string>();
        for (var seed = 1; seed <= 40 && problems.Count < 6; seed++)
        {
            var rng = new Random(seed);
            var m = new IslandMachine(idleSeconds: 5, pages, CustomItems);
            double now = 0;
            for (var i = 0; i < 500; i++)
            {
                now += rng.Next(4) == 0 ? 0 : rng.NextDouble() * 300;
                var label = $"[seed {seed} op {i}]";
                try
                {
                    switch (rng.Next(5))
                    {
                        case 0: m.ShowHideKey(now); break;
                        case 1: m.PageKey(pages[rng.Next(pages.Count)].Id, now); break;
                        case 2: m.ItemClick(rng.Next(-1, 1200), now); break;
                        case 3: m.Activity(now); break;
                        default: m.Tick(now); break;
                    }

                    AssertSane(m, label);
                    Assert.True(double.IsFinite(m.CapsuleTargetWidth) && m.CapsuleTargetWidth > 0, $"{label} capsule target width {m.CapsuleTargetWidth}");
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    problems.Add($"{label} {e.GetType().Name}: {e.Message}");
                    break;
                }
            }

            CheckEventuallyHidden(m, now, $"[seed {seed} after]", problems);
        }

        Assert.True(problems.Count == 0, Join(problems));
        Assert.Equal(270, CapsuleLayout.Width(0, isMedia: false));
    }

    [Fact]
    public void Zero_Item_Page_Selection_Is_Zero_And_Clicks_Select_Nothing()
    {
        var m = new IslandMachine(60, [ZeroPage, ThreePage], CustomItems);
        m.ShowHideKey(0);
        for (double t = Frame; t < 2000; t += Frame) m.Tick(t);
        Assert.Equal(IslandPhase.Open, m.Phase);
        Assert.Empty(m.ContentsItems);
        foreach (var index in new[] { -1, 0, 1, int.MaxValue }) m.ItemClick(index, 2000);
        Assert.Equal(0, m.SelectedItem);
        m.PageKey("three", 2000);
        for (double t = 2000 + Frame; t < 3000; t += Frame) m.Tick(t);
        m.ItemClick(2, 3000);
        Assert.Equal(2, m.SelectedItem);
        m.PageKey("zero", 3000);
        for (double t = 3000 + Frame; t < 4000; t += Frame) m.Tick(t);
        Assert.Equal(0, m.SelectedItem);
    }

    [Fact]
    public void Duplicate_Page_Ids_And_Lookalike_Ids_Do_Not_Crash()
    {
        var a = new Page("dup", "A", "#111111", "x", null, false);
        var b = new Page("dup", "B", "#222222", "x", null, false);
        var m = new IslandMachine(60, [a, b, ThreePage], CustomItems);
        foreach (var id in new[] { "dup", "DUP", "dup ", " dup", "three\0", "Three", "THREE", "three" })
        {
            m.PageKey(id, 0);
            m.Tick(500);
            AssertSane(m, id);
        }

        Assert.Equal("three", m.PageId);
    }

    // =====================================================================================
    // 5. Settings
    // =====================================================================================

    private static string DeepArrays(int depth) => new string('[', depth) + new string(']', depth);

    private static string DeepObjects(int depth) =>
        string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] WithBom(Encoding encoding, string text) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static byte[] RandomBytes(int seed, int length)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private enum Want { Unreadable, Loaded }

    private static void CheckSettingsFile(string name, byte[] content, Want want, List<string> problems, Action<SettingsLoad>? extra = null)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllBytes(path, content);
        var written = File.GetLastWriteTimeUtc(path);
        try
        {
            var load = Settings.Load(path);
            if (want == Want.Unreadable)
            {
                if (load.Status != SettingsStatus.Unreadable) problems.Add($"{name}: status {load.Status}, wanted Unreadable");
                else if (load.Settings != Settings.Defaults) problems.Add($"{name}: not the defaults");
                else if (string.IsNullOrEmpty(load.Detail)) problems.Add($"{name}: no detail to show");
            }
            else if (load.Status != SettingsStatus.Loaded)
            {
                problems.Add($"{name}: status {load.Status} ({load.Detail}), wanted Loaded");
            }

            extra?.Invoke(load);
        }
        catch (Exception e)
        {
            problems.Add($"{name}: Load threw {e.GetType().Name}: {e.Message}");
        }

        if (!File.ReadAllBytes(path).SequenceEqual(content)) problems.Add($"{name}: the file was changed");
        if (File.GetLastWriteTimeUtc(path) != written) problems.Add($"{name}: the file's write time changed");
        var files = Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).ToList();
        if (files.Count != 1) problems.Add($"{name}: loading left extra files: {string.Join(",", files)}");
    }

    [Fact]
    public void Settings_Broken_Empty_Huge_Binary_Nested_And_Wrongly_Typed_Files_Give_Defaults_And_Are_Left_Alone()
    {
        var problems = new List<string>();
        var bad = new Dictionary<string, byte[]>
        {
            ["empty"] = [],
            ["one space"] = Bytes(" "),
            ["only newlines"] = Bytes("\r\n\r\n"),
            ["null"] = Bytes("null"),
            ["true"] = Bytes("true"),
            ["number"] = Bytes("123"),
            ["string"] = Bytes("\"str\""),
            ["array"] = Bytes("[]"),
            ["open brace"] = Bytes("{"),
            ["close brace"] = Bytes("}"),
            ["cut off"] = Bytes("{\"hotkeys\":"),
            ["two objects"] = Bytes("{}{}"),
            ["trailing garbage"] = Bytes("{} x"),
            ["single quotes"] = Bytes("{'idleSeconds': 5}"),
            ["hotkeys null"] = Bytes("{\"hotkeys\":null}"),
            ["hotkeys array"] = Bytes("{\"hotkeys\":[]}"),
            ["hotkeys string"] = Bytes("{\"hotkeys\":\"x\"}"),
            ["key null"] = Bytes("{\"hotkeys\":{\"media\":null}}"),
            ["key array"] = Bytes("{\"hotkeys\":{\"media\":[]}}"),
            ["key object"] = Bytes("{\"hotkeys\":{\"media\":{}}}"),
            ["key true"] = Bytes("{\"hotkeys\":{\"media\":true}}"),
            ["key number"] = Bytes("{\"hotkeys\":{\"media\":49}}"),
            ["showHide empty"] = Bytes("{\"hotkeys\":{\"showHide\":\"\"}}"),
            ["showHide null"] = Bytes("{\"hotkeys\":{\"showHide\":null}}"),
            ["showHide only whitespace"] = Bytes("{\"hotkeys\":{\"showHide\":\"   \"}}"),
            ["key with newline in text"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Alt+M\n\"}}"),
            ["key with NUL escape"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Alt+\\u0000\"}}"),
            ["key with Windows key"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Win+M\"}}"),
            ["key is only modifiers"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Alt+Shift\"}}"),
            ["two keys the same, other case"] = Bytes("{\"hotkeys\":{\"media\":\"ctrl+alt+m\",\"folders\":\"CTRL+ALT+M\"}}"),
            ["media equals showHide default"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Q\"}}"),
            ["swap into a clash"] = Bytes("{\"hotkeys\":{\"folders\":\"Ctrl+Alt+2\",\"media\":\"Ctrl+Alt+2\"}}"),
            ["idle null"] = Bytes("{\"idleSeconds\":null}"),
            ["idle true"] = Bytes("{\"idleSeconds\":true}"),
            ["idle array"] = Bytes("{\"idleSeconds\":[60]}"),
            ["idle object"] = Bytes("{\"idleSeconds\":{}}"),
            ["idle text"] = Bytes("{\"idleSeconds\":\"60\"}"),
            ["idle negative"] = Bytes("{\"idleSeconds\":-1}"),
            ["idle zero"] = Bytes("{\"idleSeconds\":0}"),
            ["idle negative zero"] = Bytes("{\"idleSeconds\":-0}"),
            ["idle 86401"] = Bytes("{\"idleSeconds\":86401}"),
            ["idle 1e999"] = Bytes("{\"idleSeconds\":1e999}"),
            ["idle -1e999"] = Bytes("{\"idleSeconds\":-1e999}"),
            ["idle 1e-999"] = Bytes("{\"idleSeconds\":1e-999}"),
            ["idle 400 digits"] = Bytes("{\"idleSeconds\":1" + new string('0', 400) + "}"),
            ["idle NaN literal"] = Bytes("{\"idleSeconds\":NaN}"),
            ["idle Infinity literal"] = Bytes("{\"idleSeconds\":Infinity}"),
            ["idle leading plus"] = Bytes("{\"idleSeconds\":+5}"),
            ["idle hex"] = Bytes("{\"idleSeconds\":0x10}"),
            ["deep arrays 100000"] = Bytes(DeepArrays(100_000)),
            ["deep objects 100000"] = Bytes(DeepObjects(100_000)),
            ["deep arrays 65"] = Bytes(DeepArrays(65)),
            ["valid hotkeys, 70-deep extra key"] = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Alt+M\"},\"extra\":" + DeepArrays(70) + "}"),
            ["invalid utf-8 bytes"] = [0xFF, 0xFE, 0xFD, 0x7B, 0x7D, 0x80],
            ["png header"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D],
            ["random 1 MB"] = RandomBytes(1, 1_000_000),
            ["random 16 bytes"] = RandomBytes(2, 16),
            ["all zero 4096"] = new byte[4096],
            ["utf-16 without a BOM"] = Encoding.Unicode.GetBytes("{\"idleSeconds\":5}"),
            ["utf-32 with BOM, truncated"] = [.. Encoding.UTF32.GetPreamble(), 0x7B],
            ["NUL inside valid json"] = Bytes("{\"idleSeconds\":5}\0"),
            ["comment never closed"] = Bytes("{ /* oops \"idleSeconds\":5}"),
            ["10 MB of spaces then garbage"] = Bytes(new string(' ', 10_000_000) + "}"),
        };

        foreach (var (name, content) in bad) CheckSettingsFile(name, content, Want.Unreadable, problems);

        Assert.True(problems.Count == 0, Join(problems, 40));
    }

    [Theory]
    [InlineData("{\"hotkeys\":{\"media\":\"\\ud800\"}}")]
    [InlineData("{\"hotkeys\":{\"media\":\"\\udc00\"}}")]
    [InlineData("{\"hotkeys\":{\"media\":\"Ctrl+Alt+\\ud83d\"}}")]
    [InlineData("{\"junk\":\"\\ud800\"}")]
    public void Settings_Lone_Surrogate_Escape_In_A_Text_Value_Does_Not_Throw(string json)
    {
        // DEFECT (fails until fixed): a JSON escape for half a surrogate pair parses as valid JSON, but
        // JsonElement.GetString then throws InvalidOperationException, which Parse does not catch (it
        // catches JsonException only) and Load does not either (IOException and UnauthorizedAccess only).
        // The "unreadable file gives defaults" promise breaks: the exception reaches AppHost.Start and
        // the app dies at launch. Only reachable by typing such an escape into the file, but the
        // promise in the work order is "never throw".
        // The last case is an ignored key, so GetString is never called on it and it passes today.
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, json);
        SettingsLoad? load = null;
        var thrown = Record.Exception(() => load = Settings.Load(path));
        Assert.Null(thrown);
        Assert.True(load!.Status is SettingsStatus.Unreadable or SettingsStatus.Loaded);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Settings_Parse_Of_A_String_With_A_Lone_Surrogate_Character_Does_Not_Throw()
    {
        // DEFECT (low; fails until fixed): Parse(string) throws ArgumentException ("Cannot transcode
        // invalid UTF-16 string") for a string holding half a surrogate pair. Load can never hand Parse such
        // a string (ReadAllText replaces bad data), so only a direct caller can trigger it; it still breaks
        // "Settings.Parse never throws".
        var thrown = Record.Exception(() => Settings.Parse("{ \"idleSeconds\": 5 }\ud800"));
        Assert.Null(thrown);
    }

    [Fact]
    public void Settings_Files_That_Are_Valid_In_Odd_Ways_Load()
    {
        var problems = new List<string>();
        void Idle5(SettingsLoad l)
        {
            if (l.Status == SettingsStatus.Loaded && l.Settings.IdleSeconds != 5) problems.Add($"idle was {l.Settings.IdleSeconds}");
        }

        var json = "{\"idleSeconds\": 5}";
        CheckSettingsFile("utf-8 BOM", WithBom(new UTF8Encoding(true), json), Want.Loaded, problems, Idle5);
        CheckSettingsFile("utf-16 LE BOM", WithBom(Encoding.Unicode, json), Want.Loaded, problems, Idle5);
        CheckSettingsFile("utf-16 BE BOM", WithBom(Encoding.BigEndianUnicode, json), Want.Loaded, problems, Idle5);
        CheckSettingsFile("utf-32 BOM", WithBom(Encoding.UTF32, json), Want.Loaded, problems, Idle5);
        CheckSettingsFile("CRLF and tabs", Bytes("{\r\n\t\"idleSeconds\":\t5\r\n}\r\n"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("comments and trailing commas", Bytes("{ // c\n \"idleSeconds\": 5, /* x */ }"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("exponent", Bytes("{\"idleSeconds\": 5e0}"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("empty object", Bytes("{}"), Want.Loaded, problems, l =>
        {
            if (l.Settings != Settings.Defaults) problems.Add("empty object is not the defaults");
        });
        CheckSettingsFile("unknown keys and 60-deep extra", Bytes("{\"junk\":" + DeepArrays(60) + ",\"idleSeconds\":5}"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("a 10 MB string value that is ignored", Bytes("{\"junk\":\"" + new string('a', 10_000_000) + "\",\"idleSeconds\":5}"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("200 thousand ignored keys", Bytes("{" + string.Concat(Enumerable.Range(0, 200_000).Select(i => $"\"k{i}\":{i},")) + "\"idleSeconds\":5}"), Want.Loaded, problems, Idle5);
        CheckSettingsFile("86400 exactly", Bytes("{\"idleSeconds\":86400}"), Want.Loaded, problems);
        CheckSettingsFile("tiny but above zero", Bytes("{\"idleSeconds\":1e-300}"), Want.Loaded, problems);
        CheckSettingsFile("every key emptied except showHide", Bytes("{\"hotkeys\":{\"media\":\"\",\"folders\":\"\",\"apps\":\"\",\"vibe\":\"\",\"browser\":\"\"}}"), Want.Loaded, problems, l =>
        {
            if (l.Settings.PageKeys.Any(k => k.Combo is not null)) problems.Add("an emptied key survived");
        });
        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Settings_Duplicate_Keys_Never_Crash_And_Always_Give_A_Valid_Result()
    {
        // Which duplicate wins is recorded in attack.md; the guard here is only that the result is valid.
        foreach (var json in new[]
        {
            "{\"idleSeconds\":5,\"idleSeconds\":6}",
            "{\"idleSeconds\":5,\"idleSeconds\":-1}",
            "{\"idleSeconds\":-1,\"idleSeconds\":5}",
            "{\"hotkeys\":{\"media\":\"Ctrl+Alt+M\",\"media\":\"Ctrl+Alt+N\"}}",
            "{\"hotkeys\":{\"media\":\"Ctrl+Alt+M\"},\"hotkeys\":{\"media\":\"Win+M\"}}",
            "{\"hotkeys\":{\"media\":\"Win+M\"},\"hotkeys\":{\"media\":\"Ctrl+Alt+M\"}}",
            "{\"hotkeys\":{\"showHide\":\"Ctrl+Alt+N\",\"showHide\":\"\"}}",
        })
        {
            var load = Settings.Parse(json);
            AssertValidSettings(load, json);
        }
    }

    private static void AssertValidSettings(SettingsLoad load, string json)
    {
        Assert.True(load.Status is SettingsStatus.Loaded or SettingsStatus.Unreadable, json);
        var s = load.Settings;
        Assert.True(s.IdleSeconds > 0 && s.IdleSeconds <= 86400, $"idle {s.IdleSeconds} for {json}");
        var combos = s.PageKeys.Select(k => k.Combo).Append(s.ShowHide).Where(c => c is not null).Select(c => c!.Value).ToList();
        Assert.Equal(combos.Count, combos.Distinct().Count());
        Assert.All(combos, c =>
        {
            Assert.NotEqual(HotkeyModifiers.None, c.Modifiers);
            Assert.NotEqual(0, c.VirtualKey);
            Assert.True(HotkeyCombo.TryParse(c.ToString(), out var again, out _) && again == c, $"{c} does not round-trip");
        });
        if (load.Status == SettingsStatus.Unreadable) Assert.Equal(Settings.Defaults, s);
    }

    [Fact]
    public void Settings_Mutation_Fuzz_Never_Throws_And_Never_Returns_A_Bad_Result()
    {
        var seedText = "{ // keys\n \"hotkeys\": { \"showHide\": \"Ctrl+Alt+Shift+Space\", \"media\": \"Ctrl+Alt+M\", \"folders\": \"\", \"apps\": \"Alt+F7\" },\n \"idleSeconds\": 90.5, \"extra\": [1, {\"a\": null}] }";
        var alphabet = "{}[]\",:+ \n\t\\/*0123456789eE.-truefalsnlCtrlAltShiftWinF\u00e9\u212A\u017F\u0131\u200B\0";
        var rng = new Random(2026);
        var problems = new List<string>();
        for (var i = 0; i < 40_000 && problems.Count < 8; i++)
        {
            var sb = new StringBuilder(seedText);
            for (var edits = rng.Next(1, 5); edits > 0; edits--)
            {
                if (sb.Length == 0) { sb.Append(alphabet[rng.Next(alphabet.Length)]); continue; }
                var at = rng.Next(sb.Length);
                switch (rng.Next(5))
                {
                    case 0: sb[at] = alphabet[rng.Next(alphabet.Length)]; break;
                    case 1: sb.Remove(at, Math.Min(sb.Length - at, rng.Next(1, 12))); break;
                    case 2: sb.Insert(at, alphabet[rng.Next(alphabet.Length)]); break;
                    case 3: sb.Insert(at, sb.ToString(at, Math.Min(sb.Length - at, rng.Next(1, 30)))); break;
                    default: sb.Length = at; break;
                }
            }

            var json = sb.ToString();
            try
            {
                AssertValidSettings(Settings.Parse(json), json);
            }
            catch (Exception e)
            {
                problems.Add($"{e.GetType().Name}: {e.Message.Split('\n')[0]}  input: {json.Replace("\n", "\\n").Replace("\0", "\\0")}");
            }
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Settings_Random_Bytes_Through_Load_Never_Throw()
    {
        var problems = new List<string>();
        var valid = Bytes("{\"hotkeys\":{\"media\":\"Ctrl+Alt+M\"},\"idleSeconds\":5}");
        var rng = new Random(11);
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        for (var i = 0; i < 1500 && problems.Count < 8; i++)
        {
            var data = i % 2 == 0 ? RandomBytes(i, rng.Next(0, 300)) : (byte[])valid.Clone();
            if (i % 2 == 1) for (var k = rng.Next(1, 4); k > 0; k--) data[rng.Next(data.Length)] = (byte)rng.Next(256);
            File.WriteAllBytes(path, data);
            try
            {
                var load = Settings.Load(path);
                AssertValidSettings(load, $"bytes #{i}");
            }
            catch (Exception e)
            {
                problems.Add($"#{i}: {e.GetType().Name}: {e.Message.Split('\n')[0]}  bytes: {Convert.ToHexString(data)}");
            }
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Settings_Locked_Or_Unwritable_Or_Odd_Paths_Do_Not_Throw_On_Load()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{\"idleSeconds\":5}");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var locked = Settings.Load(path);
            Assert.Equal(SettingsStatus.Unreadable, locked.Status);
            Assert.Equal(Settings.Defaults, locked.Settings);
        }

        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.Equal(SettingsStatus.Loaded, Settings.Load(path).Status);
        File.SetAttributes(path, FileAttributes.Normal);

        Assert.Equal(SettingsStatus.Missing, Settings.Load(dir.Path).Status); // a folder where a file should be
        Assert.Equal(SettingsStatus.Missing, Settings.Load(string.Empty).Status);
        Assert.Equal(SettingsStatus.Missing, Settings.Load(dir.File("a\0b.json")).Status);
        Assert.Equal(SettingsStatus.Missing, Settings.Load(dir.File("nope.json")).Status);
        Assert.Equal(SettingsStatus.Missing, Settings.Load(new string('x', 5000)).Status);
    }

    [Fact]
    public void Settings_Load_Detail_Does_Not_Echo_The_File_Path()
    {
        // DEFECT (fails until fixed): when the file cannot be opened (another program holds it, or access
        // is denied) the Detail is the raw IOException message, which contains the full path. AppHost
        // writes that Detail into island.log, so the log under %LOCALAPPDATA% would hold a path under the
        // user's profile folder, with the account name in it. The work order forbids writing such a path
        // into any file, and the app is meant to be filmed.
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "{}");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var load = Settings.Load(path);
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.DoesNotContain(dir.Path, load.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", load.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Settings_EnsureExists_Does_Not_Throw_When_The_Path_Cannot_Be_Written()
    {
        // DEFECT (fails until fixed): EnsureExists has no error handling, and AppHost.Start calls it
        // before anything else. A folder named settings.json, a file where the data folder should be, or
        // a read-only profile makes the very first line of start-up throw. The exception is unhandled, so
        // Island crashes at launch with no tray icon, no log line and no message to Dan.
        using var dir = new TempDir();

        var folderInTheWay = dir.File("settings.json");
        Directory.CreateDirectory(folderInTheWay);
        Assert.Null(Record.Exception(() => Settings.EnsureExists(folderInTheWay)));

        var fileInTheWay = dir.File("data");
        File.WriteAllText(fileInTheWay, "x");
        Assert.Null(Record.Exception(() => Settings.EnsureExists(Path.Combine(fileInTheWay, "settings.json"))));
    }

    [Fact]
    public void Settings_Tiny_Idle_Seconds_Is_Accepted_And_The_Machine_Survives_It()
    {
        // Recorded as an observation, not a defect: "above 0" is the stated rule, so 1e-9 s is accepted,
        // and the island then closes on its first frame. The machine itself must stay sane.
        var load = Settings.Parse("{\"idleSeconds\":1e-9}");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        var m = new IslandMachine(load.Settings.IdleSeconds);
        double now = 0;
        for (var round = 0; round < 5; round++)
        {
            m.PageKey(PageIds.Apps, now);
            for (var i = 0; i < 600; i++)
            {
                now += Frame;
                m.Tick(now);
                AssertSane(m, "tiny idle");
            }
        }

        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    // =====================================================================================
    // 6. Hotkey text
    // =====================================================================================

    [Fact]
    public void Hotkey_Fuzz_Never_Throws_And_Every_Accepted_Combo_Round_Trips()
    {
        string[] atoms =
        [
            "Ctrl", "ctrl", "CONTROL", "Alt", "alt", "Shift", "SHIFT", "Win", "win", "Windows", "Super", "Meta", "Cmd", "LWin", "RWin",
            "Space", "space", "Tab", "Enter", "Esc", "Escape", "Delete", "Home", "F1", "F12", "F24", "F25", "F0", "f5", "F", "a", "Z", "0", "9",
            "1", "\u00df", "\u017f", "\u212a", "\u0131", "\u0130", "\u0663", "\uff21", "\u200b", "\u00a0", "\ud800", "\ud83d\ude00", "\0", "\t", " ",
            "", "+", "++", "Eſc", "ESc", "F 1", "F+1", "F-1", "F٣", "Ctrl\u200b", "ﬁ", "Ǆ", "Spaçe",
        ];
        var rng = new Random(99);
        var problems = new List<string>();
        for (var i = 0; i < 60_000 && problems.Count < 8; i++)
        {
            var parts = new List<string>();
            for (var n = rng.Next(0, 7); n > 0; n--) parts.Add(atoms[rng.Next(atoms.Length)]);
            var text = string.Join(rng.Next(8) == 0 ? "" : "+", parts);
            try
            {
                var ok = HotkeyCombo.TryParse(text, out var combo, out var error);
                if (!ok)
                {
                    if (string.IsNullOrEmpty(error)) problems.Add($"rejected without a reason: {Show_(text)}");
                    continue;
                }

                if (combo.Modifiers == HotkeyModifiers.None) problems.Add($"accepted with no modifier: {Show_(text)}");
                if (combo.VirtualKey is <= 0 or > 0xFE) problems.Add($"accepted with virtual key {combo.VirtualKey}: {Show_(text)}");
                if (combo.VirtualKey is 0x5B or 0x5C) problems.Add($"accepted a Windows key: {Show_(text)}");
                if (!HotkeyCombo.TryParse(combo.ToString(), out var again, out _) || again != combo)
                    problems.Add($"does not round-trip: {Show_(text)} -> {combo}");
            }
            catch (Exception e)
            {
                problems.Add($"{e.GetType().Name} for {Show_(text)}");
            }
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    private static string Show_(string s) => "\"" + string.Concat(s.Select(c => c is >= ' ' and < '\u007f' ? c.ToString() : $"\\u{(int)c:X4}")) + "\"";

    [Fact]
    public void Hotkey_Huge_And_Degenerate_Strings_Are_Handled_Quickly()
    {
        var clock = Stopwatch.StartNew();
        foreach (var text in new[]
        {
            new string('+', 5_000_000),
            new string('a', 5_000_000),
            "Ctrl+" + new string('a', 5_000_000),
            string.Concat(Enumerable.Repeat("Ctrl+", 1_000_000)) + "A",
            string.Concat(Enumerable.Repeat("a+", 1_000_000)),
            new string(' ', 5_000_000),
            "Ctrl+Alt+Shift+" + new string('\u200b', 1_000_000),
            "Ctrl+F" + new string('0', 1_000_000) + "1",
            "Ctrl+Alt+Shift+Space" + new string(' ', 5_000_000),
        })
        {
            Assert.Null(Record.Exception(() => HotkeyCombo.TryParse(text, out _, out _)));
        }

        Assert.True(clock.ElapsedMilliseconds < 5000, $"{clock.ElapsedMilliseconds} ms for the degenerate strings");
        Assert.Null(Record.Exception(() => HotkeyCombo.TryParse(null, out _, out _)));
    }

    [Fact]
    public void Hotkey_Windows_Key_Spellings_Are_All_Rejected()
    {
        foreach (var key in new[] { "Win", "win", "WIN", "Windows", "Super", "Meta", "Cmd", "LWin", "RWin", "Command", "Logo", "Start", "#", "VK_LWIN" })
        {
            foreach (var shape in new[] { "Ctrl+{0}+A", "{0}+Shift+A", "Ctrl+Alt+Shift+{0}", "{0}+A", "Ctrl+ {0} +A" })
            {
                var text = string.Format(shape, key);
                Assert.False(HotkeyCombo.TryParse(text, out var combo, out _), $"{text} was accepted as {combo}");
            }
        }
    }

    [Theory]
    [InlineData("Shift+A")]
    [InlineData("Shift+Z")]
    [InlineData("Shift+1")]
    [InlineData("Shift+Space")]
    [InlineData("Shift+Enter")]
    [InlineData("Shift+Tab")]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+X")]
    [InlineData("Ctrl+Z")]
    [InlineData("Ctrl+A")]
    [InlineData("Alt+F4")]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Esc")]
    [InlineData("Ctrl+Shift+Esc")]
    [InlineData("Ctrl+Alt+Delete")]
    public void Hotkey_Combos_That_Take_Over_Typing_Or_System_Shortcuts_Are_Rejected(string text)
    {
        // DEFECT (fails until fixed): the parser only demands "at least one of Ctrl, Alt or Shift", and its
        // own message claims that is what keeps a keybind from taking over normal typing. Shift+A still
        // takes over every capital A on the machine, Ctrl+C every copy, Alt+F4 every window close. Nothing
        // checks them, and RegisterHotKey will happily grant most of them (fact 2: it takes the keys away
        // from every program, silently). Windows itself refuses only a few (Ctrl+Alt+Delete among them).
        Assert.False(HotkeyCombo.TryParse(text, out var combo, out _), $"{text} was accepted as {combo}");
    }

    [Theory]
    [InlineData("Ctrl+F 1")]
    [InlineData("Ctrl+F 12")]
    [InlineData("Ctrl+F01")]
    [InlineData("Ctrl+F\t5")]
    public void Hotkey_F_Keys_Only_Accept_Plain_Spelling(string text)
    {
        // DEFECT (low; fails until fixed): the F-key branch hands the rest of the name to int.TryParse,
        // which accepts surrounding spaces, a sign and leading zeros, so "F 1", "F +5" and "F01" are
        // taken as F1, F5 and F1. Harmless in effect (they round-trip as F1/F5), but the file then holds a
        // spelling the parser did not mean to allow.
        Assert.False(HotkeyCombo.TryParse(text, out var combo, out _), $"{text} was accepted as {combo}");
    }

    // =====================================================================================
    // 7. Refusal texts
    // =====================================================================================

    [Fact]
    public void Hotkey_Refusal_Fits_The_255_Character_Balloon_For_Every_Built_In_Page()
    {
        // Windows shows at most 255 characters of a notification. The refusal register says every message
        // keeps its three parts (what happened, why, what to do), and the last part is at the end.
        var longestCombo = HotkeyCombo.Parse("Ctrl+Alt+Shift+Space");
        var problems = new List<string>();
        foreach (var page in Pages.BuiltIn)
        {
            var message = Refusals.ForHotkeyTaken(longestCombo, page.Name).Message;
            if (message.Length > 255) problems.Add($"{page.Name}: {message.Length} characters, the end is cut off: \"...{message[250..]}\"");
        }

        foreach (var r in Refusals.All.Where(r => r.Code != "HOTKEY_TAKEN"))
            if (r.Message.Length > 255) problems.Add($"{r.Code}: {r.Message.Length} characters");
        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Refusal_Placeholders_Cannot_Be_Injected_By_A_Page_Name_Or_Combo()
    {
        var hostile = new Page("x", "{combo} {category} {0}", "#000000", "x", null, false);
        var combo = HotkeyCombo.Parse("Ctrl+Alt+Shift+Space");
        var message = Refusals.ForHotkeyTaken(combo, hostile.Name).Message;
        Assert.StartsWith("Ctrl+Alt+Shift+Space is already used", message);
        Assert.Contains("so {combo} {category} {0} has no keybind", message);
        Assert.Contains("Pick another combination in Settings, Key.", message); // WORK-ORDER-13 (Dan's P12): no file to edit
    }

    // =====================================================================================
    // 8. The other pure parts
    // =====================================================================================

    [Fact]
    public void Perimeter_Fuzz_Pieces_Join_Cover_The_Asked_Length_And_Never_Seam()
    {
        var rng = new Random(3);
        double Dim() => rng.Next(10) switch { 0 => 0, 1 => 1e-6, 2 => 30, 3 => 76, 4 => 502, 5 => 2000, _ => Math.Exp(rng.NextDouble() * 9 - 3) };
        var problems = new List<string>();
        for (var i = 0; i < 6000 && problems.Count < 6; i++)
        {
            double w = Dim(), h = Dim(), r = rng.Next(4) == 0 ? Math.Min(w, h) / 2 : (rng.Next(3) == 0 ? 1e6 : Dim());
            var p = new RoundedPerimeter(rng.NextDouble() * 10 - 5, rng.NextDouble() * 10 - 5, w, h, r);
            var label = $"[{w:G6} x {h:G6} r {r:G6}]";
            if (!double.IsFinite(p.Length) || p.Length < 0) { problems.Add($"{label} length {p.Length}"); continue; }
            var tolerance = 1e-6 * (1 + p.Length);

            var start = rng.Next(5) switch { 0 => 0.0, 1 => 1.0, 2 => -0.37, 3 => 12345.678, _ => rng.NextDouble() };
            var asked = rng.Next(5) switch { 0 => 1.0, 1 => 0.28, 2 => 0.0, 3 => 1e-12, _ => rng.NextDouble() };
            var pieces = p.Walk(start, asked);
            double covered = 0;
            for (var k = 0; k < pieces.Count; k++)
            {
                var piece = pieces[k];
                if (!new[] { piece.From.X, piece.From.Y, piece.To.X, piece.To.Y }.All(double.IsFinite)) { problems.Add($"{label} non-finite piece"); break; }
                var chord = Math.Sqrt(Math.Pow(piece.To.X - piece.From.X, 2) + Math.Pow(piece.To.Y - piece.From.Y, 2));
                covered += piece.IsArc && piece.Radius > 0 ? piece.Radius * 2 * Math.Asin(Math.Min(1, chord / (2 * piece.Radius))) : chord;
                if (k + 1 < pieces.Count)
                {
                    var gap = Math.Sqrt(Math.Pow(piece.To.X - pieces[k + 1].From.X, 2) + Math.Pow(piece.To.Y - pieces[k + 1].From.Y, 2));
                    if (gap > tolerance) problems.Add($"{label} pieces {k} and {k + 1} do not join (gap {gap:G4}) start {start} asked {asked}");
                }
            }

            if (Math.Abs(covered - asked * p.Length) > Math.Max(tolerance, 1e-9 * p.Length * 100)) problems.Add($"{label} covered {covered:G8} of {asked * p.Length:G8} (start {start}, asked {asked})");

            // The point on the outline moves by at most the distance asked for: no seam at the wrap.
            var step = 1.0 / 997;
            var f = rng.NextDouble();
            for (var k = 0; k < 40; k++, f += step)
            {
                var a = p.PointAt(f);
                var b = p.PointAt(f + step);
                if (Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2)) > step * p.Length * 1.0001 + tolerance)
                {
                    problems.Add($"{label} the outline jumps near fraction {f % 1:F5}");
                    break;
                }
            }
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Perimeter_NaN_And_Infinite_Fractions_Return_Without_Hanging()
    {
        var p = new RoundedPerimeter(0, 0, 466, 76, 38);
        var finished = CompletesWithin(() =>
        {
            foreach (var f in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300, -1e300 })
            {
                p.PointAt(f);
                p.Walk(f, 0.28);
                p.Walk(0.1, f);
                ArcClock.Head(f);
                ArcClock.SecondHead(f);
            }
        }, TimeSpan.FromSeconds(5), out var error);
        Assert.Null(error);
        Assert.True(finished);
    }

    [Fact]
    public void Reveal_Timeline_Fuzz_Stays_In_Range_And_Never_Jumps_When_Set_Or_Reset()
    {
        var rng = new Random(8);
        var problems = new List<string>();
        for (var seed = 0; seed < 300 && problems.Count < 6; seed++)
        {
            var count = rng.Next(0, 8);
            var timeline = new RevealTimeline(count);
            double now = 0;
            for (var i = 0; i < 80; i++)
            {
                now += rng.Next(4) == 0 ? -rng.NextDouble() * 300 : rng.NextDouble() * (rng.Next(3) == 0 ? 2000 : 60);
                var before = Enumerable.Range(0, timeline.Count).Select(e => timeline.At(now, e)).ToList();
                if (rng.Next(8) == 0) { timeline.Reset(rng.Next(0, 8)); before = []; }
                else timeline.Set(rng.Next(2) == 0, now);

                for (var e = 0; e < before.Count && e < timeline.Count; e++)
                {
                    var after = timeline.At(now, e);
                    if (Math.Abs(after.Opacity - before[e].Opacity) > 1e-9 || Math.Abs(after.Move - before[e].Move) > 1e-9 || Math.Abs(after.Blur - before[e].Blur) > 1e-9)
                        problems.Add($"[seed {seed} step {i}] element {e} jumped when set at {now}: {before[e]} -> {after}");
                }

                for (var e = 0; e < timeline.Count; e++)
                {
                    var v = timeline.At(now + rng.NextDouble() * 900, e);
                    if (!double.IsFinite(v.Opacity) || v.Opacity < -1e-9 || v.Opacity > 1 + 1e-9 || !double.IsFinite(v.Move) || v.Blur < -1e-9 || v.Blur > LookConstants.ContentsBlurStart + 1e-9)
                        problems.Add($"[seed {seed} step {i}] element {e} out of range: {v}");
                }
            }
        }

        Assert.True(problems.Count == 0, Join(problems));
    }

    [Fact]
    public void Colour_Transition_Fuzz_Never_Jumps_At_A_Retarget_Even_With_Backwards_Time()
    {
        var palette = new[] { LookConstants.MediaColor, LookConstants.FoldersColor, LookConstants.AppsColor, LookConstants.VibeColor, LookConstants.BrowserColor }
            .Select(Rgb.FromHex).ToArray();
        var rng = new Random(21);
        for (var seed = 0; seed < 200; seed++)
        {
            var transition = new ColourTransition(palette[0]);
            double now = 0, highest = 0;
            for (var i = 0; i < 200; i++)
            {
                now += rng.Next(5) == 0 ? -rng.NextDouble() * 500 : rng.NextDouble() * 400;
                var before = transition.At(now);
                var snap = rng.Next(6) == 0;
                transition.Retarget(palette[rng.Next(palette.Length)], now, snap);
                var after = transition.At(now);
                if (!snap) Assert.True(Math.Abs(before.R - after.R) + Math.Abs(before.G - after.G) + Math.Abs(before.B - after.B) < 1e-6, $"seed {seed} step {i}: colour jumped");
                Assert.True(new[] { after.R, after.G, after.B }.All(c => double.IsFinite(c) && c >= -1e-6 && c <= 255 + 1e-6), $"seed {seed} step {i}: {after}");
                highest = Math.Max(highest, now);
                Assert.Equal(transition.Target, transition.At(highest + LookConstants.ColorChangeMs + 1));
            }
        }
    }

    [Fact]
    public void Window_Metrics_Hold_The_Widest_Built_In_Capsule_At_The_Spring_Peak()
    {
        // Guard on the fixed window: the widest built-in capsule at its overshoot, with shadow reach on both
        // sides, must fit. (A custom page of seven or more rows would not; see attack.md, reasoned.)
        var widest = Pages.AllPlaceholders.Max(c => CapsuleLayout.SizeFor(c).Width);
        Assert.True(WindowMetrics.Width >= WindowMetrics.PeakCapsuleWidth + 2 * Math.Max(WindowMetrics.ShadowReach, WindowMetrics.BloomReach));
        Assert.True(WindowMetrics.PeakCapsuleWidth > widest);
        Assert.True(WindowMetrics.PeakCapsuleWidth < widest * 1.1);
    }
}
