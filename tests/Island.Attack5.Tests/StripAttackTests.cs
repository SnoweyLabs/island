using Island.Core;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 section 4: StripScroll, StripLayout and the width rule, on pages of up to forty (and thousands of) picks.</summary>
public class StripAttackTests
{
    private static void Settle(StripScroll strip, int frames = 900)
    {
        for (var i = 0; i < frames; i++) strip.Tick(1.0 / 60);
    }

    private static void AssertSane(StripScroll s, string what)
    {
        Assert.InRange(s.Target, 0, s.MaxPosition);
        Assert.Equal(Math.Floor(s.Target), s.Target); // one tile per notch: the target only ever lands on whole tiles
        Assert.True(double.IsFinite(s.Position), $"{what}: position {s.Position}");
        Assert.InRange(s.Position, -3, s.MaxPosition + 3); // the spring may pass an end, not leave the neighbourhood
        Assert.InRange(s.Visible, 0, ChoiceConstants.MaxVisibleTiles);
    }

    // ---- what held ----

    [Fact]
    public void Holds_Wheel_Storm_Of_Extreme_Amounts_On_Forty_Picks()
    {
        var rng = new Random(5);
        int[] extremes = [0, 1, -1, 119, -119, 120, -120, 121, int.MaxValue, int.MinValue, int.MinValue + 1, 12000, -12000, 12001, -12001];
        double[] steps = [0, 1.0 / 240, 1.0 / 60, 0.5, 5, 6, double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 1e300, double.MaxValue, double.Epsilon];
        var s = new StripScroll(40);
        for (var i = 0; i < 20_000; i++)
        {
            s.Wheel(rng.Next(5) == 0 ? extremes[rng.Next(extremes.Length)] : rng.Next(-400, 400));
            if (rng.Next(3) == 0) s.Tick(steps[rng.Next(steps.Length)]);
            if (rng.Next(50) == 0) s.ArrowLeft();
            if (rng.Next(50) == 0) s.ArrowRight();
            AssertSane(s, $"step {i}");
        }

        Settle(s);
        Assert.Equal(s.Target, s.Position, 3);
        Assert.False(s.Moving);
    }

    [Fact]
    public void Holds_Hundreds_Of_Notches_Both_Ways_Return_To_Where_They_Started()
    {
        var s = new StripScroll(40); // seven show: the target runs 0..33
        for (var i = 0; i < 400; i++) s.Wheel(-StripScroll.WheelDelta);
        Assert.Equal(33, s.Target);
        for (var i = 0; i < 400; i++) s.Wheel(StripScroll.WheelDelta);
        Assert.Equal(0, s.Target);

        // 840 amounts of one unit are seven notches, in either direction; nothing is lost on the way.
        for (var i = 0; i < 840; i++) s.Wheel(-1);
        Assert.Equal(7, s.Target);
        for (var i = 0; i < 840; i++) s.Wheel(1);
        Assert.Equal(0, s.Target);
    }

    [Fact]
    public void Holds_The_Sum_Of_Small_Amounts_Is_Within_One_Notch_Of_What_The_Target_Moved()
    {
        var rng = new Random(11);
        var s = new StripScroll(2000);
        s.Reset(1000);
        var start = s.Target;
        long total = 0;
        for (var i = 0; i < 3000; i++)
        {
            var d = rng.Next(-119, 120);
            total += d;
            s.Wheel(d);
            var moved = start - s.Target; // positive wheel (away from the user) shows earlier picks: the target goes down
            Assert.True(Math.Abs(moved * StripScroll.WheelDelta - total) < StripScroll.WheelDelta, $"after {i + 1} amounts: sum {total}, moved {moved}");
        }
    }

    [Fact]
    public void Holds_Nonsense_Counts_Indexes_And_Room()
    {
        var s = new StripScroll(40);
        foreach (var n in new[] { int.MinValue, -1, 0, 1, 7, 8, int.MaxValue, 0, 40 })
        {
            s.SetCount(n);
            AssertSane(s, $"SetCount({n})");
            s.Wheel(-StripScroll.WheelDelta * 5);
            AssertSane(s, $"Wheel after SetCount({n})");
        }

        foreach (var room in new[] { int.MinValue, -1, 0, 1, 7, 8, 1000, int.MaxValue, 7 })
        {
            s.SetMaxVisible(room);
            Assert.InRange(s.Target, 0, s.MaxPosition);
            Assert.True(double.IsFinite(s.Position));
            s.ArrowRight();
            s.ArrowLeft();
            Assert.InRange(s.Target, 0, s.MaxPosition);
        }

        foreach (var index in new[] { int.MinValue, -1, 0, 3, 39, 40, 41, int.MaxValue })
        {
            s.SetMaxVisible(7);
            s.SetCount(40);
            s.BringIntoView(index);
            AssertSane(s, $"BringIntoView({index})");
            s.Reset(index);
            AssertSane(s, $"Reset({index})");
            Assert.Equal(s.Target, s.Position, 9);
        }

        var empty = new StripScroll(0);
        empty.BringIntoView(5);
        empty.Reset(5);
        empty.Wheel(-StripScroll.WheelDelta);
        empty.ArrowRight();
        Assert.Equal(0, empty.Target);
        Assert.False(empty.HiddenLeft);
        Assert.False(empty.HiddenRight);
    }

    [Fact]
    public void Holds_Fewer_Picks_Than_The_Target_Brings_It_Back_And_Never_Jumps()
    {
        var s = new StripScroll(40);
        s.Wheel(-StripScroll.WheelDelta * 100);
        Settle(s);
        Assert.Equal(33, s.Position, 2);

        var before = s.Position;
        s.SetCount(3); // fewer picks than the target: everything fits again
        Assert.Equal(0, s.Target);
        Assert.Equal(before, s.Position, 12); // set a target, never assigned the position
        Assert.True(s.HiddenLeft); // it is still on its way back: the left arrow shows until the spring has finished
        Settle(s);
        Assert.Equal(0, s.Position, 3);
        Assert.False(s.HiddenLeft);
        Assert.False(s.HiddenRight);
    }

    [Fact]
    public void Holds_No_Operation_Moves_The_Drawn_Position_At_Once()
    {
        var rng = new Random(3);
        var s = new StripScroll(40);
        for (var i = 0; i < 5000; i++)
        {
            var before = s.Position;
            switch (rng.Next(8))
            {
                case 0: s.Wheel(rng.Next(-1000, 1000)); break;
                case 1: s.ArrowLeft(); break;
                case 2: s.ArrowRight(); break;
                case 3: s.SetCount(rng.Next(-2, 60)); break;
                case 4: s.SetMaxVisible(rng.Next(-2, 12)); break;
                case 5: s.BringIntoView(rng.Next(-2, 70)); break;
                default: s.Tick(rng.NextDouble() / 30); continue;
            }

            Assert.Equal(before, s.Position, 12);
        }
    }

    [Fact]
    public void Holds_The_Drawn_Strip_And_The_Width_Rule_Agree_At_Every_Target()
    {
        const int picks = 40;
        var s = new StripScroll(picks);
        var visible = s.Visible;
        var stripWidth = StripLayout.StripWidth(visible);
        var capsuleStrip = CapsuleLayout.Width(visible + 1, isMedia: false) - CapsuleLayout.Width(0, isMedia: false);
        // the sliding part and the + tile together are exactly what the width rule gives the strip
        Assert.Equal(capsuleStrip, StripLayout.TileLeft(0, isPlus: true, 0, visible) + LookConstants.ItemSize);

        for (var target = 0; target <= s.MaxPosition; target++)
        {
            var onScreen = Enumerable.Range(0, picks)
                .Where(i => StripLayout.TileLeft(i, false, target, visible) is var left && left >= 0 && left + LookConstants.ItemSize <= stripWidth)
                .ToList();
            Assert.Equal(Enumerable.Range(target, visible), onScreen);
            // the + tile starts one gap after the last visible pick and never moves
            var last = StripLayout.TileLeft(target + visible - 1, false, target, visible);
            Assert.Equal(last + LookConstants.WidthItemPitch, StripLayout.TileLeft(picks, true, target, visible));
        }
    }

    [Fact]
    public void Holds_Width_Rule_For_Every_Place_Of_The_Plus_Tile_And_Every_Page_Size()
    {
        var page = Pages.Get(PageIds.Apps);
        var media = Pages.Get(PageIds.Media);
        foreach (var picks in new[] { 0, 1, 6, 7, 8, 40, 1000, 100_000 })
        {
            var shown = Math.Min(picks, 7);
            var items = Make.Items(picks, plus: false);
            var plusLast = new List<Item>(items) { new("+", "", "+", 0, IsPlus: true) };
            var plusFirst = new List<Item> { new("+", "", "+", 0, IsPlus: true) };
            plusFirst.AddRange(items);
            var plusMiddle = new List<Item>(items);
            plusMiddle.Insert(items.Count / 2, new Item("+", "", "+", 0, IsPlus: true));

            foreach (var withPlus in new[] { plusLast, plusFirst, plusMiddle })
            {
                var size = CapsuleLayout.SizeFor(new PageContents(page, withPlus));
                Assert.Equal(CapsuleLayout.Width(shown + 1, false), size.Width);
                Assert.Equal(76, size.Height);
                Assert.Equal(38, size.Radius);
                Assert.Equal(CapsuleLayout.Width(shown + 1, true), CapsuleLayout.SizeFor(new PageContents(media, withPlus)).Width);
            }

            Assert.Equal(CapsuleLayout.Width(shown, false), CapsuleLayout.SizeFor(new PageContents(page, items)).Width); // no + tile at all
        }

        var onlyPlus = new[] { new Item("+", "", "+", 0, IsPlus: true) };
        Assert.Equal(CapsuleLayout.Width(1, false), CapsuleLayout.SizeFor(new PageContents(page, onlyPlus)).Width);
        Assert.Equal(CapsuleLayout.Width(0, false), CapsuleLayout.SizeFor(new PageContents(page, [])).Width);
    }

    [Fact]
    public void Holds_TilesThatFit_Matches_The_Width_Rule_And_Survives_Nonsense()
    {
        for (var n = 1; n <= 1000; n++)
        {
            var width = StripLayout.StripWidth(n);
            Assert.Equal(n, StripLayout.TilesThatFit(width));
            Assert.Equal(n - 1, StripLayout.TilesThatFit(width - 1e-6));
        }

        foreach (var w in new[] { double.NaN, double.NegativeInfinity, -1e300, -1, 0, 39.999, double.PositiveInfinity, double.MaxValue })
            Assert.InRange(StripLayout.TilesThatFit(w), 0, int.MaxValue);
        Assert.Equal(0, StripLayout.TilesThatFit(39.999));
        Assert.Equal(0, StripLayout.StripWidth(0));
        Assert.Equal(0, StripLayout.StripWidth(-5));
    }

    // ---- what broke ----

    [Fact]
    public void Defect_A_Page_With_No_Room_Shows_A_Right_Arrow_That_Does_Nothing()
    {
        // The second row is built with room for 0 tiles until it knows its width (ContentsLayer: new StripScroll(0, 0)).
        // With picks and no room, HiddenRight says "more picks are hidden to the right" (so the arrow is drawn)
        // but a click on that arrow moves nothing, because it moves by the number of tiles shown (zero).
        var s = new StripScroll(10, maxVisible: 0);
        Assert.Equal(0, s.Visible);
        if (!s.HiddenRight) return; // no arrow, nothing dead
        var before = s.Target;
        s.ArrowRight();
        Assert.True(s.Target > before, "HiddenRight is true but ArrowRight cannot move the target: a dead arrow");
    }

    [Fact]
    public void Defect_Two_Plus_Tiles_Share_One_Slot_While_The_Width_Rule_Makes_Room_For_Both()
    {
        // Latent: the page builders only ever add one + tile. Two + tiles give a width for visible+2 tiles but TileLeft
        // puts both of them in the same slot, one on top of the other, and leaves the last slot empty.
        var items = new List<Item>(Make.Items(3, plus: false)) { new("+", "", "+", 0, IsPlus: true), new("+", "", "+", 0, IsPlus: true) };
        var tiles = StripLayout.ShownTiles(items);
        var lefts = items.Select((item, i) => StripLayout.TileLeft(i, item.IsPlus, 0, StripLayout.VisiblePicks(3))).Distinct().Count();
        Assert.Equal(tiles, lefts);
    }
}
