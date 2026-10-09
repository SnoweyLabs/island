using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-7 section 4: where the notice sits among the hidden island, the pill and the capsule.</summary>
public class NoticeMachineTests
{
    private static Clock NewClock() => new(new IslandMachine(5));

    [Fact]
    public void A_Notice_Comes_In_By_Itself_Without_The_Keyboard_And_Leaves_When_The_App_Says()
    {
        var c = NewClock();

        c.M.SetNotice(true, c.Now);

        Assert.Equal(IslandPhase.FlyingIn, c.M.Phase);
        Assert.True(c.M.ShowsNotice);
        Assert.True(c.M.ShowsPill); // a small shape
        Assert.False(c.M.HasKeyboard);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        Assert.Equal(NoticeLayout.WidestWidth, c.M.Width.Target);
        Assert.Equal(NoticeLayout.Height, c.M.Height.Target);
        Assert.Equal(NoticeLayout.Radius, c.M.Radius.Target);

        // The idle time does not end it: the app does, when its own time is up.
        c.Run(60_000);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.M.SetNotice(false, c.Now);
        Assert.False(c.M.ShowsNotice);
        c.Run(3000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    [Fact]
    public void A_Notice_Takes_The_Pills_Place_And_The_Pill_Returns_Afterwards()
    {
        var c = NewClock();
        c.M.SetPill(true, true, c.Now);
        c.Run(2500);
        Assert.True(c.M.ShowsPill);
        Assert.False(c.M.ShowsNotice);

        c.M.SetNotice(true, c.Now);
        Assert.True(c.M.ShowsNotice);
        c.Run(2500);
        Assert.Equal(NoticeLayout.Height, c.M.Height.Target);
        Assert.Equal(IslandPhase.Open, c.M.Phase);

        c.M.SetNotice(false, c.Now);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase); // the pill is wanted and allowed: it comes back, the island does not leave
        Assert.True(c.M.ShowsPill);
        Assert.False(c.M.ShowsNotice);
        Assert.Equal(PillLayout.Height, c.M.Height.Target);
    }

    [Fact]
    public void A_Notice_Never_Replaces_The_Capsule()
    {
        var c = NewClock();
        c.M.MainKey(c.Now);
        c.Run(2500);

        c.M.SetNotice(true, c.Now);

        Assert.False(c.M.ShowsNotice);
        Assert.False(c.M.ShowsPill);
        Assert.Equal(CapsuleLayout.SizeFor(c.M.Contents).Width, c.M.Width.Target);
        Assert.True(c.M.HasKeyboard);
    }

    [Fact]
    public void The_Main_Key_On_A_Notice_Grows_It_Into_The_Capsule_And_The_Notice_Waits()
    {
        var c = NewClock();
        c.M.SetNotice(true, c.Now);
        c.Run(2500);

        c.M.MainKey(c.Now);

        Assert.False(c.M.ShowsNotice);
        Assert.False(c.M.ShowsPill);
        Assert.True(c.M.HasKeyboard);
        c.Run(2500);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
    }

    [Fact]
    public void A_Pill_That_The_Table_Refuses_Does_Not_End_A_Notice_That_The_Table_Allows()
    {
        // In Focus a notice may show over a fullscreen program where the pill may not: the pill's "stay away" must not take the notice away.
        var c = NewClock();
        c.M.SetNotice(true, c.Now);
        c.Run(2500);

        c.M.SetPill(true, false, c.Now);

        Assert.True(c.M.ShowsNotice);
        Assert.Equal(IslandPhase.Open, c.M.Phase);
        c.M.SetNotice(false, c.Now); // the notice goes: the pill is not allowed, so the island leaves
        c.Run(3000);
        Assert.Equal(IslandPhase.Hidden, c.M.Phase);
    }

    [Fact]
    public void Width_Follows_The_Text_And_Nonsense_Changes_Nothing()
    {
        var c = NewClock();
        c.M.SetNotice(true, c.Now);
        c.Run(2500);

        c.M.SetNoticeWidth(100, c.Now);
        c.Run(1500);
        Assert.InRange(c.M.DrawnWidth, NoticeLayout.Width(100) - 0.2, NoticeLayout.Width(100) + 0.2);

        c.M.SetNoticeWidth(double.NaN, c.Now);
        c.M.SetNotice(true, double.NaN);
        Assert.Equal(NoticeLayout.Width(100), c.M.NoticeWidth);

        Assert.Equal(50, NoticeLayout.Height);
        Assert.Equal(7 + 36 + 8 + 230 + 14, NoticeLayout.Width(9999)); // the gap is 8 since WORK-ORDER-13 (Dan's P22)
        Assert.Equal(NoticeLayout.Width(NoticeLayout.TextMinWidth), NoticeLayout.Width(0));
    }
}
