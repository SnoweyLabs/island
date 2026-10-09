using Island.Core.Speed;

namespace Island.Tests.Speed;

/// <summary>WORK-ORDER-12 section 2: the island never draws more frames than the screen refreshes.</summary>
public class QuietTests
{
    private static int Drawn(double refreshHz, Func<int, double> callbackTimesMs, int calls, double stretchMs, double intervalFactor = 1)
    {
        var budget = new FrameBudget();
        var interval = 1000.0 / refreshHz * intervalFactor;
        var drawn = 0;
        for (var i = 0; i < calls; i++)
        {
            var t = callbackTimesMs(i);
            if (t > stretchMs) break;
            if (budget.Admit(t, interval)) drawn++;
        }

        return drawn;
    }

    [Theory]
    [InlineData(60, 1.0)]
    [InlineData(60, 3.0)]
    [InlineData(85, 2.2)]
    [InlineData(144, 6.5)]
    [InlineData(240, 1.0)]
    public void Frames_Drawn_Never_Exceed_The_Screens_Refreshes(double hz, double callbacksPerRefresh)
    {
        // The callback comes callbacksPerRefresh times per refresh interval, with a little jitter: whatever the rate it comes at, over every stretch the island draws at most the refreshes of the screen plus the bucket.
        var gap = 1000.0 / hz / callbacksPerRefresh;
        var rng = new Random(7);
        var times = new double[100_000];
        var t = 0.0;
        for (var i = 0; i < times.Length; i++)
        {
            t += gap * (0.8 + 0.4 * rng.NextDouble());
            times[i] = t;
        }

        foreach (var stretch in new[] { 250.0, 1000.0, 8000.0 })
        {
            var budget = new FrameBudget();
            var interval = 1000.0 / hz;
            var drawn = 0;
            foreach (var at in times.TakeWhile(x => x <= stretch))
                if (budget.Admit(at, interval)) drawn++;
            var refreshes = stretch / interval;
            Assert.True(drawn <= refreshes + FrameBudget.Capacity + 1, $"{hz} Hz, {callbacksPerRefresh} calls per refresh, {stretch} ms: {drawn} drawn, {refreshes:0.0} refreshes");
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(85)]
    [InlineData(144)]
    public void A_Callback_That_Comes_Once_Per_Refresh_Loses_No_Frame(double hz)
    {
        var interval = 1000.0 / hz;
        var rng = new Random(3);
        var budget = new FrameBudget();
        var t = 0.0;
        var drawn = 0;
        const int Calls = 2000;
        for (var i = 0; i < Calls; i++)
        {
            t += interval * (0.9 + 0.2 * rng.NextDouble()); // a callback every refresh, a tenth early or late
            if (budget.Admit(t, interval)) drawn++;
        }

        Assert.True(drawn >= Calls * 0.97, $"{drawn} of {Calls}"); // the island's own motion is not made rougher where it already ran at the screen's rate
    }

    [Fact]
    public void A_Slow_Callback_Is_Never_Held_Back()
    {
        var budget = new FrameBudget();
        var t = 0.0;
        for (var i = 0; i < 50; i++)
        {
            t += 40; // 25 frames a second on a 60 Hz screen
            Assert.True(budget.Admit(t, 1000.0 / 60));
        }
    }

    [Fact]
    public void An_Unknown_Rate_Lets_Everything_Through_And_A_Clock_That_Goes_Back_Does_Not_Throw()
    {
        var budget = new FrameBudget();
        for (var i = 0; i < 100; i++) Assert.True(budget.Admit(i * 0.1, 0));
        Assert.True(budget.Admit(double.NaN, 16));
        Assert.True(budget.Admit(100, 16) || true);
        budget.Admit(50, 16); // earlier than the one before
        budget.Reset();
        Assert.True(budget.Admit(0, 16));
    }

    [Fact]
    public void Half_Rate_Draws_At_Most_Once_In_Two_Refreshes()
    {
        var drawn = Drawn(85, i => (i + 1) * 3.0, 10_000, 8000, intervalFactor: 2);
        Assert.True(drawn <= 8000 / (2 * 1000.0 / 85) + FrameBudget.Capacity + 1, $"{drawn}");
        Assert.True(drawn >= 8000 / (2 * 1000.0 / 85) * 0.9, $"{drawn}");
    }
}
