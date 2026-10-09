using System.Collections.Concurrent;
using System.Diagnostics;
using Island.Core;
using Island.Core.Speed;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: the speed measuring code (UiWatchdog, SpeedRow, SpeedTable) and the review/perf.md text it keeps (PerfSections). A thread of the test stands for the
/// drawing thread (never a window).
/// </summary>
public class SpeedEdgeTests
{
    /// <summary>A thread that runs posted work one piece at a time, as a dispatcher's thread does.</summary>
    private sealed class FakeUiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];
        private readonly Thread _thread;

        public FakeUiThread()
        {
            _thread = new Thread(() =>
            {
                foreach (var job in _work.GetConsumingEnumerable()) job();
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Post(Action a) => _work.Add(a);

        public void Block(TimeSpan time) => _work.Add(() => Thread.Sleep(time));

        public void Dispose() => _work.CompleteAdding();
    }

    [Fact]
    public void Watchdog_Reports_A_Block_Of_Known_Length_Within_A_Fair_Margin_And_A_Quiet_Thread_As_Quiet()
    {
        using var ui = new FakeUiThread();
        using var dog = new UiWatchdog(ui.Post);
        dog.Start();
        Thread.Sleep(300);
        Assert.True(dog.LongestSilenceMs < 60, $"a free thread read as silent for {dog.LongestSilenceMs:0} ms");
        Assert.True(dog.Replies > 8, $"{dog.Replies} replies in 300 ms"); // Windows' own timer step is about 15 ms when nothing has raised it, so a sleep of 1 ms is 15: 19 replies at most, 14 seen
        dog.Reset();
        ui.Block(TimeSpan.FromMilliseconds(250));
        Thread.Sleep(450);
        Assert.InRange(dog.LongestSilenceMs, 200, 420);
    }

    [Fact]
    public void Watchdog_A_Block_That_Is_Still_Going_On_Is_Seen_While_It_Lasts()
    {
        using var ui = new FakeUiThread();
        using var dog = new UiWatchdog(ui.Post);
        dog.Start();
        Thread.Sleep(100);
        dog.Reset();
        ui.Block(TimeSpan.FromMilliseconds(600));
        Thread.Sleep(350);
        Assert.True(dog.LongestSilenceMs >= 200, $"{dog.LongestSilenceMs:0} ms while the thread had been blocked for about 350");
    }

    [Fact]
    public void Watchdog_Dispose_Returns_Quickly_Even_While_The_Watched_Thread_Never_Answers()
    {
        var dog = new UiWatchdog(_ => { }); // work that is never run
        dog.Start();
        Thread.Sleep(100);
        var clock = Stopwatch.StartNew();
        dog.Dispose();
        Assert.True(clock.ElapsedMilliseconds < 1500, clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Watchdog_A_Queue_That_Is_Gone_Ends_The_Loop_Quietly_And_Dispose_Is_Fine()
    {
        var dog = new UiWatchdog(_ => throw new InvalidOperationException("the queue is gone"));
        dog.Start();
        Thread.Sleep(100);
        dog.Dispose();
        Assert.Equal(0, dog.Replies);
    }

    [Fact]
    public void Defect_Watchdog_A_Reading_Before_Start_Is_The_Uptime_Of_The_Computer_Not_Zero()
    {
        // code-1-10 (LATENT, a measuring tool only): UiWatchdog.LongestSilenceMs before Start computes "pending = now - _sentAt" with _sentAt still 0 and _replied not set, so it reads the
        // time since the computer started (137,283,004 ms here). Expected: 0 before anything was posted. SpeedStage reads only after Start, so the speed table is not affected.
        using var dog = new UiWatchdog(_ => { });
        Assert.True(dog.LongestSilenceMs < 1000, $"reading before Start: {dog.LongestSilenceMs:0} ms");
    }

    [Fact]
    public void Watchdog_Dispose_Twice_And_Dispose_Before_Start_Are_Fine()
    {
        var dog = new UiWatchdog(_ => { });
        dog.Dispose();
        Assert.Null(Record.Exception(() => dog.Dispose()));
        Assert.Null(Record.Exception(() => dog.Dispose()));
    }

    [Fact]
    public void Watchdog_Start_Twice_Is_A_Plain_Invalid_Operation_Not_A_Hang()
    {
        using var ui = new FakeUiThread();
        using var dog = new UiWatchdog(ui.Post);
        dog.Start();
        Assert.Throws<ThreadStateException>(dog.Start);
    }

    [Fact]
    public void SpeedRow_Middle_And_Spread_Are_Right_For_Even_Odd_Empty_And_Odd_Numbers()
    {
        Assert.Equal(3, SpeedRow.MiddleOf([5, 1, 3]));
        Assert.Equal(2.5, SpeedRow.MiddleOf([4, 1, 2, 3]));
        Assert.Equal(0, SpeedRow.MiddleOf([]));
        Assert.Equal(7, SpeedRow.MiddleOf([7]));
        var row = SpeedRow.Of("2", "x", [3.5, 1.5, 9]);
        Assert.Equal((3.5, 7.5), (row.Median, row.Spread));
        Assert.Equal(0, SpeedRow.Of("2", "x", []).Spread);
        Assert.Null(Record.Exception(() => SpeedRow.Of("2", "x", [double.NaN, 1, double.PositiveInfinity]).ToString()));
    }

    [Fact]
    public void SpeedTable_Is_Culture_Proof_And_A_Row_Of_Nothing_Still_Writes_A_Line()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ro-RO");
            var text = SpeedTable.Markdown("before", "measured on a test", [SpeedRow.Of("1", "start", [1234.5, 1200.25, 1300.75]), SpeedRow.Of("2", "nothing", [])]);
            Assert.Contains("1234.5 ms", text);
            Assert.DoesNotContain("1234,5", text);
            Assert.StartsWith(SpeedTable.Heading("before"), text);
            Assert.Equal(2, text.Split('\n').Count(l => l.StartsWith("| 1 |") || l.StartsWith("| 2 |")));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }

    [Fact]
    public void SpeedTable_A_Pipe_Or_A_Line_Break_In_A_Note_Would_Break_The_Row_So_Notes_Must_Not_Carry_Them()
    {
        // The table writes Note and What as they are. Today every note is a fixed sentence of the stage; this keeps the cost of a future one with a "|" in it in view.
        var text = SpeedTable.Markdown("x", "m", [SpeedRow.Of("9", "a | b", [1], note: "n")]);
        var cells = text.Split('\n').First(l => l.StartsWith("| 9 |")).Split('|').Length;
        Assert.True(cells > 8, "a '|' in a cell adds a column");
    }

    // ---- PerfSections: what is kept when review/perf.md is rewritten --------------------------------------------------------------------------------------------

    private const string Hidden = PerfSections.HiddenCost;

    [Fact]
    public void PerfSections_Every_Kept_Heading_Survives_A_Rewrite_In_Order_And_A_Second_Rewrite_Changes_Nothing()
    {
        var earlier = "# Cost\n\nold body\n\n" + Hidden + "\n\nhidden lines\n\n## Something else\n\ngone\n\n"
                      + SpeedTable.Heading("before") + "\n\nspeed lines\n\n"
                      + PerfSections.OpenCostPrefix12 + ") — after\n\nopen lines\n\n" + PerfSections.OpenCostPrefix + " §4) — before\n\nwo11\n";
        var once = PerfSections.WithKept("# Cost\n\nnew body\n", earlier);
        Assert.Contains("hidden lines", once);
        Assert.Contains("speed lines", once);
        Assert.Contains("open lines", once);
        Assert.Contains("wo11", once);
        Assert.DoesNotContain("gone", once);
        Assert.DoesNotContain("old body", once);
        Assert.True(once.IndexOf("hidden lines", StringComparison.Ordinal) < once.IndexOf("speed lines", StringComparison.Ordinal));
        Assert.Equal(once, PerfSections.WithKept("# Cost\n\nnew body\n", once));
    }

    [Fact]
    public void PerfSections_Upsert_Replaces_Its_Own_Section_Only_And_Works_On_Empty_And_Crlf_Text()
    {
        var heading = SpeedTable.Heading("after");
        var a = PerfSections.Upsert(null, heading + "\n\nfirst\n");
        Assert.Contains("first", a);
        var b = PerfSections.Upsert(a, heading + "\n\nsecond\n");
        Assert.DoesNotContain("first", b);
        Assert.Contains("second", b);
        var withOther = "# T\r\n\r\n## Other\r\n\r\nkeep me\r\n\r\n" + heading + "\r\n\r\nold\r\n\r\n## Last\r\n\r\nkeep too\r\n";
        var c = PerfSections.Upsert(withOther, heading + "\n\nnew\n");
        Assert.Contains("keep me", c);
        Assert.Contains("keep too", c);
        Assert.Contains("new", c);
        Assert.DoesNotContain("old", c);
        Assert.Single(c.Split('\n'), l => l == heading);
    }

    [Fact]
    public void PerfSections_Odd_Texts_Never_Throw_Or_Loop()
    {
        foreach (var text in new[] { "", "\n", "##", "## ", Hidden, Hidden + "\n", "x" + Hidden, "\n" + Hidden, new string('\n', 10_000), string.Concat(Enumerable.Repeat(Hidden + "\nabc\n", 500)) })
        {
            var task = Task.Run(() =>
            {
                PerfSections.Kept(text);
                PerfSections.WithKept("new", text);
                PerfSections.Upsert(text, SpeedTable.Heading("x") + "\nbody\n");
            });
            Assert.True(task.Wait(5000));
            task.GetAwaiter().GetResult();
        }
    }
}
