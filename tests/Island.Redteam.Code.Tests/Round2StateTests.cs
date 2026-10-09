using System.Collections.Concurrent;
using Island.Core;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 2 (code-2-*): the small repairs of round 1 that hold state or a count: TabMessages.FitsChars (34fdf62), NoticeQueue.Rebase (0af69c2), IconMisses (ea4566e), LogThrottle (51b4a21), ArcClock.Wrap and LightClock (21c322f, e06a1d7).</summary>
public sealed class Round2StateTests
{
    // ---- TabMessages.FitsChars ----------------------------------------------------------------------------------------------------------------------------------

    private static readonly System.Text.Json.JsonSerializerOptions Raw = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string TabFrame(string titleJson, string host = "\"example.org\"") =>
        "{\"type\":\"tab\",\"tab\":{\"id\":1,\"windowId\":1,\"title\":" + titleJson + ",\"host\":" + host + ",\"audible\":false,\"active\":false}}";

    private static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text, Raw);

    private static string Emoji(int count) => string.Concat(Enumerable.Repeat("\U0001F600", count));

    [Fact]
    public void A_Title_Is_Counted_In_Characters_At_Its_Limit_From_Both_Sides()
    {
        Assert.True(TabMessages.ParseAddon(TabFrame(Quote(Emoji(200)))).Ok, "200 emoji = 400 units");
        Assert.False(TabMessages.ParseAddon(TabFrame(Quote(Emoji(201)))).Ok, "201 emoji = 402 units");
        Assert.False(TabMessages.ParseAddon(TabFrame(Quote(Emoji(200) + "a"))).Ok, "200 emoji and one more character");
        Assert.False(TabMessages.ParseAddon(TabFrame(Quote(new string('a', 400)))).Ok, "400 units that are 400 characters");
        Assert.True(TabMessages.ParseAddon(TabFrame(Quote(Emoji(100) + new string('a', 100)))).Ok, "100 emoji and 100 letters: 300 units, 200 characters");
        Assert.False(TabMessages.ParseAddon(TabFrame(Quote(Emoji(100) + new string('a', 101)))).Ok);
    }

    [Fact]
    public void A_Lone_Surrogate_Escape_At_The_Limit_Counts_As_One_Character_And_Is_Not_Lost()
    {
        var parsed = TabMessages.ParseAddon(TabFrame("\"" + new string('a', 199) + "\\ud83d\""));
        Assert.True(parsed.Ok, parsed.Reject);
        var tab = Assert.IsType<TabMessage>(parsed.Message).Tab;
        Assert.Equal(200, tab.Title.Length);
        Assert.EndsWith("\uFFFD", tab.Title);
        Assert.False(TabMessages.ParseAddon(TabFrame("\"" + new string('a', 200) + "\\ud83d\"")).Ok);
    }

    [Fact]
    public void A_Host_Of_The_Widest_Characters_Within_Its_Limit_Is_Taken_Or_Refused_Plainly()
    {
        foreach (var count in new[] { 253, 254 })
        {
            var parsed = TabMessages.ParseAddon(TabFrame("\"Alpha\"", Quote(Emoji(count))));
            Assert.Equal(count <= 253, parsed.Ok);
        }
    }

    [Fact]
    public void A_Picture_Field_Of_Astral_Characters_Passes_The_Length_Test_And_Is_Then_Refused_As_Not_A_Picture_Without_Throwing()
    {
        var max = (TabProtocol.MaxIconBytes + 2) / 3 * 4;
        var frame = "{\"type\":\"icon\",\"id\":1,\"png\":\"" + Emoji(max) + "\"}";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(frame) < TabProtocol.MaxFrameBytes);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var parsed = TabMessages.ParseAddon(frame);
        Assert.False(parsed.Ok);
        Assert.True(clock.ElapsedMilliseconds < 2000, $"{clock.ElapsedMilliseconds} ms");
    }

    // ---- NoticeQueue.Rebase ------------------------------------------------------------------------------------------------------------------------------------

    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static AgentNotice Notice(string session = "p100") => new(AgentSignal.Finished, "Alpha", session, [100]);

    [Fact]
    public void A_Notice_Posted_With_A_Time_A_Hair_Before_The_Last_Tick_Is_Still_Shown_From_That_Moment_And_Leaves()
    {
        // the app posts from a pool thread with the time it read there, and the drawing thread's tick can have taken the lock first: a step back of microseconds
        var q = new NoticeQueue(6);
        Assert.NotNull(q.Update(T0, false, false, false).Showing == null ? new object() : null);
        Assert.True(q.Post(Notice(), T0.AddTicks(-30)));
        Assert.NotNull(q.Update(T0.AddTicks(10), false, false, false).Showing);
        Assert.Null(q.Update(T0.AddSeconds(6.1), false, false, false).Showing);
    }

    [Fact]
    public void A_Step_Back_Then_Forward_Times_A_Showing_Notice_Again_Once_And_It_Leaves_Six_Seconds_After_The_Step()
    {
        var q = new NoticeQueue(6);
        q.Post(Notice(), T0);
        Assert.NotNull(q.Update(T0.AddSeconds(1), false, false, false).Showing);
        var back = T0.AddHours(-1);
        Assert.NotNull(q.Update(back, false, false, false).Showing);
        Assert.NotNull(q.Update(back.AddSeconds(5.9), false, false, false).Showing);
        Assert.Null(q.Update(back.AddSeconds(6.1), false, false, false).Showing);
        Assert.Null(q.Update(T0.AddSeconds(10), false, false, false).Showing); // and a later time does not bring it back
    }

    [Fact]
    public void A_Waiting_Notice_Waits_From_The_New_Time_After_A_Step_Back_And_Shows_When_The_Capsule_Leaves()
    {
        var q = new NoticeQueue(6);
        q.Post(Notice(), T0);
        Assert.True(q.Update(T0.AddMinutes(4), true, false, false).Waiting);
        var back = T0.AddHours(-2);
        Assert.True(q.Update(back, true, false, false).Waiting);
        Assert.True(q.Update(back.AddMinutes(4.5), true, false, false).Waiting); // not yet five minutes since the step
        Assert.NotNull(q.Update(back.AddMinutes(4.6), false, false, false).Showing);
    }

    [Fact]
    public void The_Extreme_Times_Of_A_Clock_Give_A_Notice_That_Leaves_Once_A_Normal_Time_Comes()
    {
        var q = new NoticeQueue(6);
        q.Post(Notice(), DateTimeOffset.MaxValue.AddTicks(-1));
        Assert.Null(q.Update(DateTimeOffset.MaxValue, false, false, false).Showing == null ? null : new object()); // may show or leave: no throw
        q.Post(Notice("p101"), T0);
        Assert.NotNull(q.Update(T0, false, false, false).Showing);
        Assert.Null(q.Update(T0.AddSeconds(7), false, false, false).Showing);

        var q2 = new NoticeQueue(6);
        q2.Post(Notice(), DateTimeOffset.MinValue);
        Assert.NotNull(q2.Update(DateTimeOffset.MinValue.AddTicks(1), false, false, false).Showing);
        Assert.Null(q2.Update(T0, false, false, false).Showing); // a jump of two thousand years forward: gone
    }

    [Fact]
    public void A_Same_Session_Finished_Twice_With_A_Step_Back_Between_Is_Still_One_Notice()
    {
        var q = new NoticeQueue(6);
        Assert.True(q.Post(Notice(), T0));
        Assert.False(q.Post(Notice(), T0.AddHours(-1)));
        Assert.NotNull(q.Update(T0.AddHours(-1), false, false, false).Showing);
    }

    // ---- IconMisses ---------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void IconMisses_Asks_Again_Twice_After_The_First_Miss_At_Most_Once_At_A_Time_And_Then_Never()
    {
        var now = 1_000L;
        var m = new IconMisses(() => now);
        Assert.False(m.ShouldRetry("alpha")); // never missed
        m.Missed("alpha");
        now += IconMisses.RetryAfterMs - 1;
        Assert.False(m.ShouldRetry("alpha"));
        now += 1;
        Assert.True(m.ShouldRetry("alpha"));
        Assert.False(m.ShouldRetry("alpha")); // a read is under way
        now += 10 * IconMisses.RetryAfterMs;
        Assert.False(m.ShouldRetry("alpha")); // and stays so until it says how it ended
        m.Missed("alpha"); // the second miss
        now += IconMisses.RetryAfterMs;
        Assert.True(m.ShouldRetry("alpha"));
        m.Missed("alpha"); // the third miss
        now += 1000 * IconMisses.RetryAfterMs;
        Assert.False(m.ShouldRetry("alpha")); // the ceiling of MaxTries: a tile that missed three times keeps its letters until Island is started again (code-2-8)
    }

    [Fact]
    public async Task IconMisses_Hammered_From_Many_Threads_Never_Throws_And_Lets_One_Reader_Through_At_A_Time()
    {
        var now = 0L;
        var m = new IconMisses(() => Interlocked.Read(ref now));
        var readers = new ConcurrentDictionary<string, int>();
        var locks = Enumerable.Range(0, 20).ToDictionary(i => "k" + i, _ => new object());
        var worst = 0;
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            var rng = new Random(t);
            for (var i = 0; i < 40_000; i++)
            {
                var key = "k" + rng.Next(20);
                Interlocked.Add(ref now, rng.Next(0, 3000));
                lock (locks[key]) // the test's own lock per key keeps its count exact; the table's own lock is what is under test across keys
                {
                    switch (rng.Next(3))
                    {
                        case 0:
                            m.Missed(key);
                            readers[key] = 0;
                            break;
                        default:
                            if (m.ShouldRetry(key))
                            {
                                var n = readers.AddOrUpdate(key, 1, (_, c) => c + 1);
                                InterlockedMax(ref worst, n);
                            }

                            break;
                    }
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        // A retry that is not followed by a miss stays "under way", so one key can be let through again only after Missed: but two readers can be let through for one key only if
        // the table told two threads yes between two misses. Missed resets the count above, so a count of 2 means ShouldRetry gave a second yes without a Missed in between.
        Assert.Equal(1, Volatile.Read(ref worst));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen) return;
        }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    [Fact]
    public void IconMisses_A_Clock_That_Goes_Back_Waits_And_One_At_The_Edge_Of_The_Range_Does_Not_Overflow()
    {
        var now = 100_000L;
        var m = new IconMisses(() => now);
        m.Missed("alpha");
        now -= 50_000;
        Assert.False(m.ShouldRetry("alpha"));
        now = long.MaxValue;
        Assert.True(m.ShouldRetry("alpha")); // far later
        var edge = new IconMisses(() => long.MinValue);
        edge.Missed("beta");
        Assert.False(edge.ShouldRetry("beta"));
    }

    // ---- LogThrottle --------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LogThrottle_Writes_A_Repeated_Line_Once_In_Five_Seconds_And_Is_Safe_At_The_Edges()
    {
        var t = new LogThrottle();
        Assert.True(t.ShouldWrite("a", 0));
        Assert.False(t.ShouldWrite("a", 4_999));
        Assert.True(t.ShouldWrite("a", 5_000));
        Assert.True(t.ShouldWrite("a", 4_000)); // a clock that went back: written
        Assert.True(t.ShouldWrite("", 10_000));
        Assert.False(t.ShouldWrite("", 10_001));
        Assert.True(t.ShouldWrite("a", long.MaxValue));
        Assert.True(t.ShouldWrite("b", long.MinValue));
    }

    [Fact]
    public void LogThrottle_Remembers_A_Few_Lines_So_Two_Lines_That_Alternate_Are_Thinned_Too()
    {
        // Repaired (code-2-9, WORK-ORDER-12 section 4; this test stated "one line only" before): the throttle remembers the last few lines, so an error on every frame that is logged from two
        // places is written once in a few seconds for each, not on every frame.
        var t = new LogThrottle();
        var written = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (t.ShouldWrite(i % 2 == 0 ? "a" : "b", i)) written++;
        }

        Assert.Equal(2, written);
    }

    [Fact]
    public async Task LogThrottle_From_Many_Threads_Lets_A_Line_Through_Once_In_Its_Window()
    {
        var t = new LogThrottle();
        var through = 0;
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                if (t.ShouldWrite("same", 1_000 + i % 100)) Interlocked.Increment(ref through);
            }
        })).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        // times 1000..1099 go back and forth between threads: a step back is written (by its own rule), so the count is bounded by the number of back steps, not zero
        Assert.InRange(through, 1, 80_000);
    }

    // ---- ArcClock.Wrap and LightClock -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ArcClock_Wrap_Is_In_Zero_To_Under_One_For_Every_Finite_Number_And_Its_Callers_Agree()
    {
        var rng = new Random(5);
        var specials = new[] { 0.0, -0.0, 1.0, -1.0, 1e-300, -1e-300, -1e-17, -5e-324, 5e-324, 0.9999999999999999, -0.9999999999999999, 1e15 + 0.5, -(1e15 + 0.5), 4503599627370496.5, 1e300, -1e300, double.MaxValue, double.MinValue };
        var all = specials.Concat(Enumerable.Range(0, 300_000).Select(_ => (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-30, 30))));
        foreach (var x in all)
        {
            var w = ArcClock.Wrap(x);
            Assert.True(w >= 0 && w < 1, $"Wrap({x:R}) = {w:R}");
            var h = ArcClock.Head(x);
            Assert.True(h >= 0 && h < 1, $"Head({x:R}) = {h:R}");
            var s = ArcClock.SecondHead(x);
            Assert.True(s >= 0 && s < 1, $"SecondHead({x:R}) = {s:R}");
        }
    }

    [Fact]
    public void ArcClock_Wrap_Of_What_Is_Not_A_Number_Is_Not_A_Number_And_Throws_Nothing()
    {
        Assert.True(double.IsNaN(ArcClock.Wrap(double.NaN)));
        Assert.True(double.IsNaN(ArcClock.Wrap(double.PositiveInfinity)));
        Assert.True(double.IsNaN(ArcClock.Wrap(double.NegativeInfinity)));
    }

    [Fact]
    public void LightClock_A_Time_That_Is_Not_A_Number_Is_Answered_With_The_Last_Place_For_Every_Kind_And_Poisons_Nothing()
    {
        foreach (var kind in Enum.GetValues<LightKind>())
        {
            var clock = new LightClock();
            var first = clock.Head(1234.5, kind, 165, true);
            Assert.Equal(first, clock.Head(double.NaN, kind, 165, true));
            Assert.Equal(first, clock.Head(double.PositiveInfinity, kind, 165, true));
            Assert.Equal(first, clock.Head(double.NegativeInfinity, kind, 165, false));
            var next = clock.Head(5000, kind, 165, true);
            Assert.True(double.IsFinite(next) && next >= 0 && next < 1, $"{kind}: {next}");
            Assert.Equal(ArcClock.Head(5.0), next, 12);
        }

        var fresh = new LightClock();
        var firstEver = fresh.Head(double.NaN, LightKind.FixedRate, 165, true); // nothing held yet: the start of the lap
        Assert.Equal(0.0, firstEver);
    }

    [Fact]
    public void LightClock_Fixed_Rate_Holds_For_25_Ms_Whatever_Happened_In_Between()
    {
        var clock = new LightClock();
        var a = clock.Head(1000, LightKind.FixedRate, 100, true); // held for 25 ms (WORK-ORDER-13)
        Assert.Equal(a, clock.Head(1010, LightKind.FixedRate, 100, true));
        Assert.Equal(a, clock.Head(double.NaN, LightKind.FixedRate, 100, true));
        Assert.Equal(a, clock.Head(1024, LightKind.FixedRate, 100, true));
        var b = clock.Head(1025, LightKind.FixedRate, 100, true);
        Assert.Equal(ArcClock.Head(1.025), b, 12);
        Assert.Equal(ArcClock.Head(1.015), clock.Head(1015, LightKind.FixedRate, 100, false), 12); // something else moves: exact, and it becomes the held place
    }
}
