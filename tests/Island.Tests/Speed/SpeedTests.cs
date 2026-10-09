using System.Collections.Concurrent;
using Island.Core;
using Island.Core.Speed;

namespace Island.Tests.Speed;

/// <summary>WORK-ORDER-12 section 1: the speed table is kept when review/perf.md is rewritten, and the watchdog reports how long a thread did not answer.</summary>
public class SpeedTableTests
{
    private const string Rewritten = "# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 1 MB |\n";

    private static string Section(string label) => SpeedTable.Markdown(label, "Measured once.", [SpeedRow.Of("2", "main key to first frame", [10, 12, 11, 30, 9])]);

    [Fact]
    public void What_Was_Written_About_Speed_Is_Kept_When_Perf_Md_Is_Rewritten()
    {
        var earlier = Rewritten + "\n" + Section("before") + "\n" + Section("where it goes") + "\n" + Section("after")
            + "\n## What the open island costs (WORK-ORDER-12) — after\n\n| Kind | % |\n|---|---|\n| Half rate | 30 |\n";

        var text = PerfSections.WithKept("# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 2 MB |\n", earlier);

        Assert.Contains("| Memory | 2 MB |", text);
        Assert.DoesNotContain("| Memory | 1 MB |", text);
        Assert.Contains(SpeedTable.Heading("before"), text);
        Assert.Contains(SpeedTable.Heading("where it goes"), text);
        Assert.Contains(SpeedTable.Heading("after"), text);
        Assert.Contains("| Half rate | 30 |", text);
        Assert.Equal(4, PerfSections.Kept(text).Count);
        Assert.Equal(text, PerfSections.WithKept("# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 2 MB |\n", text));
    }

    [Fact]
    public void The_Middle_Of_Five_Readings_Is_The_Third_Smallest_And_The_Spread_Is_Highest_Minus_Lowest()
    {
        var row = SpeedRow.Of("2", "x", [10, 12, 11, 30, 9]);
        Assert.Equal(11, row.Median);
        Assert.Equal(21, row.Spread);
        Assert.Equal(0, SpeedRow.MiddleOf([]));
    }

    [Fact]
    public void Upsert_Puts_A_Table_In_Place_Of_The_Earlier_One_Of_The_Same_Heading()
    {
        var first = PerfSections.Upsert(Rewritten, Section("before"));
        var again = PerfSections.Upsert(first, SpeedTable.Markdown("before", "Measured twice.", [SpeedRow.Of("2", "main key to first frame", [1, 1, 1, 1, 1])]));
        Assert.Contains("Measured twice.", again);
        Assert.DoesNotContain("Measured once.", again);
        Assert.Single(PerfSections.Kept(again));
    }
}

public class UiWatchdogTests
{
    /// <summary>A thread of its own that runs what is posted to it, one thing after another, as a drawing thread does.</summary>
    private sealed class Worker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;

        public Worker()
        {
            _thread = new Thread(() =>
            {
                foreach (var work in _queue.GetConsumingEnumerable()) work();
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Post(Action work) => _queue.Add(work);

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(2000);
        }
    }

    [Fact]
    public void A_Thread_Held_For_A_Known_Time_Is_Reported_Silent_For_About_That_Long()
    {
        using var worker = new Worker();
        using var dog = new UiWatchdog(worker.Post);
        dog.Start();
        Thread.Sleep(100); // served and quiet: nothing long yet
        dog.Reset();
        Thread.Sleep(100);
        Assert.True(dog.LongestSilenceMs < 200, $"idle thread: {dog.LongestSilenceMs} ms"); // far under the 400 ms hold below, even on a laptop that is busy with other tests
        Assert.True(dog.Replies > 0);

        dog.Reset();
        worker.Post(() => Thread.Sleep(400)); // the drawing thread is held for 400 ms
        Thread.Sleep(700);
        Assert.InRange(dog.LongestSilenceMs, 300, 900);
    }

    [Fact]
    public void Reset_Starts_The_Longest_Silence_Again()
    {
        using var worker = new Worker();
        using var dog = new UiWatchdog(worker.Post);
        dog.Start();
        worker.Post(() => Thread.Sleep(250));
        Thread.Sleep(450);
        Assert.True(dog.LongestSilenceMs >= 150);
        dog.Reset();
        Thread.Sleep(150);
        Assert.True(dog.LongestSilenceMs < 200, $"{dog.LongestSilenceMs} ms");
    }

    [Fact]
    public void A_Queue_That_Is_Gone_Ends_The_Watching_Without_An_Error()
    {
        var dog = new UiWatchdog(_ => throw new InvalidOperationException());
        dog.Start();
        Thread.Sleep(50);
        dog.Dispose(); // must not throw or hang
    }
}
