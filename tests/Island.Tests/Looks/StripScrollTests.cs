using Island.Core;

namespace Island.Tests;

public class StripScrollTests
{
    private static IReadOnlyList<Item> Items(int picks, bool plus = true)
    {
        var items = Enumerable.Range(0, picks).Select(i => new Item($"Pick {i}", "open", "Pk", i * 10, PickId: $"program:p{i}")).ToList();
        if (plus) items.Add(new Item("Add to the island", "what is open now", "+", 0, IsPlus: true));
        return items;
    }

    private static void Settle(StripScroll strip)
    {
        for (var i = 0; i < 600; i++) strip.Tick(1.0 / 60);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(7, 7)]
    [InlineData(8, 7)]
    [InlineData(40, 7)]
    [InlineData(1000, 7)]
    public void At_Most_Seven_Picks_Show(int picks, int shown)
    {
        Assert.Equal(shown, new StripScroll(picks).Visible);
        Assert.Equal(shown, StripLayout.VisiblePicks(picks));

        // The capsule is as wide as the width rule says for the picks shown plus the + tile, and never wider.
        var contents = new PageContents(Pages.Get(PageIds.Apps), Items(picks));
        Assert.Equal(CapsuleLayout.Width(shown + 1, isMedia: false), CapsuleLayout.SizeFor(contents).Width);
        Assert.True(CapsuleLayout.SizeFor(contents).Width <= CapsuleLayout.Width(ChoiceConstants.MaxVisibleTiles + 1, isMedia: false));
        Assert.Equal(shown + 1, StripLayout.ShownTiles(contents.Items));
    }

    [Fact]
    public void Target_Never_Leaves_Its_Range()
    {
        var strip = new StripScroll(12); // seven show: the target runs from 0 to 5
        Assert.Equal(5, strip.MaxPosition);

        for (var i = 0; i < 40; i++) strip.Wheel(-StripScroll.WheelDelta);
        Assert.Equal(5, strip.Target);
        for (var i = 0; i < 40; i++) strip.Wheel(StripScroll.WheelDelta);
        Assert.Equal(0, strip.Target);

        for (var i = 0; i < 5; i++) strip.ArrowRight();
        Assert.Equal(5, strip.Target);
        strip.ArrowLeft();
        strip.ArrowLeft();
        Assert.Equal(0, strip.Target);

        strip.Wheel(int.MaxValue);
        strip.Wheel(int.MinValue);
        strip.BringIntoView(int.MaxValue);
        Assert.InRange(strip.Target, 0, 5);
        strip.BringIntoView(-4);
        Assert.InRange(strip.Target, 0, 5);
    }

    [Fact]
    public void Arrow_Only_Where_More_Is_Hidden()
    {
        var strip = new StripScroll(12);
        Assert.False(strip.HiddenLeft);
        Assert.True(strip.HiddenRight);

        strip.Wheel(-StripScroll.WheelDelta * 3);
        Settle(strip);
        Assert.True(strip.HiddenLeft);
        Assert.True(strip.HiddenRight);

        strip.Wheel(-StripScroll.WheelDelta * 10);
        Settle(strip);
        Assert.True(strip.HiddenLeft);
        Assert.False(strip.HiddenRight);

        // Seven picks or fewer: nothing is hidden, so no arrow on either side.
        var few = new StripScroll(7);
        Settle(few);
        Assert.False(few.HiddenLeft);
        Assert.False(few.HiddenRight);
        Assert.False(new StripScroll(0).HiddenRight);
    }

    [Fact]
    public void Plus_Tile_Never_Slides()
    {
        var plusAtRest = StripLayout.TileLeft(12, isPlus: true, position: 0, visiblePicks: 7);
        foreach (var position in new[] { 0.0, 0.5, 2.0, 5.0, 5.4, -0.3 })
        {
            Assert.Equal(plusAtRest, StripLayout.TileLeft(12, isPlus: true, position, 7));
            Assert.Equal((0 - position) * LookConstants.WidthItemPitch, StripLayout.TileLeft(0, isPlus: false, position, 7)); // a pick does slide
        }

        // The + tile sits in the slot right after the last visible pick.
        Assert.Equal(7 * LookConstants.WidthItemPitch, plusAtRest);
        Assert.Equal(3 * LookConstants.WidthItemPitch, StripLayout.TileLeft(3, isPlus: true, 0, 3));
    }

    [Fact]
    public void Sliding_Never_Jumps()
    {
        var strip = new StripScroll(20);
        var last = strip.Position;
        var biggestStep = 0.0;
        for (var i = 0; i < 5; i++) strip.Wheel(-StripScroll.WheelDelta); // a target 5 tiles away, set at once
        for (var i = 0; i < 480; i++)
        {
            strip.Tick(1.0 / 240);
            biggestStep = Math.Max(biggestStep, Math.Abs(strip.Position - last));
            last = strip.Position;
        }

        // The target changed by 5 tiles at once; the drawn position never moved by more than a small part of that in one frame.
        Assert.True(biggestStep < 0.5, $"biggest step {biggestStep}");
        Assert.Equal(5, last, 3);
        Assert.False(strip.Moving);

        // The same path whatever the frame rate: the drawn position after one second is the same at 30 and at 144 frames a second.
        double After(int fps)
        {
            var s = new StripScroll(20);
            s.Wheel(-StripScroll.WheelDelta * 5);
            for (var i = 0; i < fps; i++) s.Tick(1.0 / fps);
            return s.Position;
        }

        Assert.Equal(After(30), After(144), 2);
    }

    [Fact]
    public void Small_Wheel_Amounts_Add_Up_To_A_Notch()
    {
        var strip = new StripScroll(12);
        for (var i = 0; i < 11; i++) strip.Wheel(-10); // 110 so far: not a notch
        Assert.Equal(0, strip.Target);
        strip.Wheel(-10); // 120: one notch
        Assert.Equal(1, strip.Target);

        for (var i = 0; i < 24; i++) strip.Wheel(-5); // 120 again
        Assert.Equal(2, strip.Target);

        // Turning back the other way is added the same way (toward the user shows later picks; away shows earlier ones).
        strip.Wheel(StripScroll.WheelDelta);
        Assert.Equal(1, strip.Target);
        strip.Wheel(60);
        strip.Wheel(60);
        Assert.Equal(0, strip.Target);
        Assert.Equal(120, StripScroll.WheelDelta); // Microsoft Learn, WM_MOUSEWHEEL: WHEEL_DELTA is 120
    }

    [Fact]
    public void Selected_Tile_Is_Brought_Into_View()
    {
        var strip = new StripScroll(20);
        strip.Reset(selectedPick: 12);
        Assert.InRange(12, strip.Target, strip.Target + strip.Visible - 1);
        Assert.Equal(strip.Target, strip.Position, 6); // a summon sets the position, it does not sling it

        strip.Reset(selectedPick: 2);
        Assert.Equal(0, strip.Target); // every summon starts at the beginning, unless the selected tile would be out of view
        strip.Reset(selectedPick: 19);
        Assert.Equal(13, strip.Target);

        // While showing: a pick that lies off to the right is brought in by the least movement.
        strip.Reset(0);
        strip.BringIntoView(9);
        Assert.Equal(3, strip.Target);
        strip.BringIntoView(4); // already inside: nothing moves
        Assert.Equal(3, strip.Target);
    }

    [Fact]
    public void Removing_A_Pick_Brings_The_Target_Back_In_Range()
    {
        var strip = new StripScroll(12);
        strip.Wheel(-StripScroll.WheelDelta * 10);
        Assert.Equal(5, strip.Target);

        strip.SetCount(10); // one of the twelve was removed, then another: the last pick is now the tenth
        Assert.Equal(3, strip.Target);
        strip.SetCount(6); // six picks: all of them show, nothing slides
        Assert.Equal(0, strip.Target);
        strip.SetCount(0);
        Assert.Equal(0, strip.Target);
        Assert.Equal(0, strip.Visible);

        Settle(strip);
        Assert.Equal(0, strip.Position, 3);
    }

    [Fact]
    public void The_Numbers_Of_The_Strip_Are_Pinned()
    {
        Assert.Equal(7, ChoiceConstants.MaxVisibleTiles);
        Assert.Equal(1, ChoiceConstants.TilesPerNotch);
        Assert.Equal(0.22, ChoiceConstants.StripFadeShare);
        Assert.Equal(16, ChoiceConstants.ArrowGlyphSize);
        Assert.Equal(0.85, ChoiceConstants.ArrowAlpha);
        Assert.Equal(24, ChoiceConstants.ArrowAreaWidth);
        Assert.Equal(40, ChoiceConstants.ArrowAreaHeight);
    }
}
