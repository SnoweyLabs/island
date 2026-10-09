using Island.Core;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: the island's machine at the edges of time (start, sleep and wake, a clock that steps, a number that is no time) and the pieces that are driven by it.
/// Everything that could loop has a time limit.
/// </summary>
public class MachineClockTests
{
    private static void WithinSeconds(int seconds, Action work)
    {
        var task = Task.Run(work);
        Assert.True(task.Wait(TimeSpan.FromSeconds(seconds)), "did not finish: a loop that never ends");
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void Every_Input_Ignores_A_Time_That_Is_Not_A_Number_And_Changes_Nothing()
    {
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var m = new IslandMachine();
            m.MainKey(1000);
            m.Tick(3000);
            var shape = (m.Phase, m.PageId, m.SelectedItem, m.SecondRowOpen, m.ShowsPill, m.SearchOpen, m.HasKeyboard, m.IdleDeadlineMs, m.DrawnWidth, m.DrawnHeight, m.DrawnY);
            m.MainKey(bad);
            m.ShowHideKey(bad);
            m.PageKey(PageIds.Folders, bad);
            m.ToggleSecondRow(bad);
            m.CloseSecondRow(bad);
            m.Activity(bad);
            m.OtherKey(bad);
            m.FocusLost(bad);
            m.SetPill(true, true, bad);
            m.SetNotice(true, bad);
            m.OpenSearch(300, bad);
            m.SetIdleSeconds(4, bad);
            m.SetPages(Pages.BuiltIn, bad);
            m.ContentsChanged(bad);
            m.ItemClick(0, bad);
            m.DigitKey(2, bad);
            m.EscapeKey(bad);
            m.MoveSelection(1, bad);
            m.SelfFillingItemsChanged(bad);
            m.Tick(bad);
            Assert.Equal(shape, (m.Phase, m.PageId, m.SelectedItem, m.SecondRowOpen, m.ShowsPill, m.SearchOpen, m.HasKeyboard, m.IdleDeadlineMs, m.DrawnWidth, m.DrawnHeight, m.DrawnY));
        }
    }

    [Fact]
    public void A_Huge_Clock_Value_Still_Ends_And_The_Island_Leaves_At_The_End_Of_Its_Idle_Time()
    {
        // 2^53 ms is 285,000 years; 1e21 is where "now + idle time" is no longer different from "now"; 1e300 is the end of the range.
        foreach (var start in new[] { 0.0, Math.Pow(2, 53), 1e17, 1e21, 1e300 })
        {
            WithinSeconds(10, () =>
            {
                var m = new IslandMachine(5);
                m.MainKey(start);
                m.Tick(start + 100);
                m.Tick(start + 10_000);
                m.Tick(start + 20_000);
                m.Tick(start * 2 + 1e6);
                Assert.Equal(IslandPhase.Hidden, m.Phase);
                Assert.True(double.IsFinite(m.DrawnY) && double.IsFinite(m.DrawnWidth) && double.IsFinite(m.DrawnHeight) && double.IsFinite(m.DrawnRadius));
            });
        }
    }

    [Fact]
    public void A_Laptop_That_Slept_With_The_Island_Open_Wakes_To_A_Hidden_Island_Not_A_Stuck_One()
    {
        WithinSeconds(10, () =>
        {
            var m = new IslandMachine(60);
            m.MainKey(1000);
            m.Tick(3000);
            Assert.Equal(IslandPhase.Open, m.Phase);
            m.Tick(3000 + 8 * 3600 * 1000.0); // eight hours later, the first frame after the wake
            Assert.Equal(IslandPhase.Hidden, m.Phase);
            m.MainKey(3000 + 8 * 3600 * 1000.0 + 5); // and the key summons it again
            m.Tick(3000 + 8 * 3600 * 1000.0 + 2000);
            Assert.Equal(IslandPhase.Open, m.Phase);
            Assert.True(m.IsAtRest);
        });
    }

    [Fact]
    public void A_Clock_That_Steps_Back_While_The_Island_Is_Open_Never_Throws_And_The_Island_Still_Leaves()
    {
        WithinSeconds(10, () =>
        {
            var m = new IslandMachine(5);
            m.MainKey(10_000);
            m.Tick(12_000);
            m.Tick(5_000); // stepped back five seconds
            m.Activity(5_100);
            m.PageKey(PageIds.Folders, 5_200);
            m.Tick(5_300);
            for (var t = 5_300; t < 60_000; t += 16) m.Tick(t);
            Assert.Equal(IslandPhase.Hidden, m.Phase);
            Assert.True(double.IsFinite(m.DrawnY));
        });
    }

    [Fact]
    public void Random_Sequences_Of_Inputs_At_Random_Odd_Times_Never_Throw_Or_Loop_And_The_Shape_Stays_A_Number()
    {
        var rng = new Random(2026);
        string[] pageIds = [.. Pages.BuiltIn.Select(p => p.Id), "nonexistent", ""];
        for (var run = 0; run < 400; run++)
        {
            WithinSeconds(10, () =>
            {
                var m = new IslandMachine(rng.Next(1, 8));
                var now = rng.NextDouble() * 1e6;
                for (var step = 0; step < 150; step++)
                {
                    now += rng.Next(5) switch { 0 => 0, 1 => 16.6, 2 => 250, 3 => -rng.NextDouble() * 500, _ => rng.NextDouble() * 20_000 };
                    switch (rng.Next(16))
                    {
                        case 0: m.MainKey(now); break;
                        case 1: m.ShowHideKey(now); break;
                        case 2: m.PageKey(pageIds[rng.Next(pageIds.Length)], now); break;
                        case 3: m.ToggleSecondRow(now); break;
                        case 4: m.EscapeKey(now); break;
                        case 5: m.SetPill(rng.Next(2) == 0, rng.Next(2) == 0, now); break;
                        case 6: m.SetNotice(rng.Next(2) == 0, now); break;
                        case 7: m.OpenSearch(rng.NextDouble() * 900, now, rng.Next(2) == 0); break;
                        case 8: m.CloseSearch(now); break;
                        case 9: m.SetPillWidth(rng.NextDouble() * 300, now); break;
                        case 10: m.SetNoticeWidth(rng.NextDouble() * 300, now); break;
                        case 11: m.ItemClick(rng.Next(-2, 10), now); break;
                        case 12: m.ContentsChanged(now); break;
                        case 13: m.SetIdleSeconds(rng.Next(-1, 70), now); break;
                        case 14: m.FocusLost(now); break;
                        default: m.Tick(now); break;
                    }

                    Assert.True(double.IsFinite(m.DrawnY) && double.IsFinite(m.DrawnWidth) && double.IsFinite(m.DrawnHeight) && double.IsFinite(m.DrawnRadius) && m.DrawnWidth > 0 && m.DrawnHeight > 0);
                    Assert.True(m.SelectedItem >= 0);
                }
            });
        }
    }

    [Fact]
    public void Spring_A_Stall_Of_Any_Size_Is_Caught_Up_In_Bounded_Work_And_Lands_On_Its_Target()
    {
        foreach (var gap in new[] { 0.0, 0.0083, 5, 60, 3600, 1e9, double.MaxValue, double.PositiveInfinity, double.NaN, -3 })
        {
            WithinSeconds(5, () =>
            {
                var s = new Spring(0, 0, 100);
                s = s.Frame(gap);
                s = s.Frame(10);
                Assert.True(double.IsFinite(s.Drawn));
                if (double.IsFinite(gap) && gap >= 5) Assert.True(Math.Abs(s.Drawn - 100) < 0.01, $"gap {gap}: {s.Drawn}");
            });
        }
    }
}
