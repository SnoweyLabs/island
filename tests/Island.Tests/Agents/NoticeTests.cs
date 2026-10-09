using Island.Core;

namespace Island.Tests;

public class NoticeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    private static AgentNotice Notice(AgentSignal signal = AgentSignal.Finished, string name = "island", string session = "s1") =>
        new(signal, name, session, [1]);

    private static NoticeState Tick(NoticeQueue q, double t, bool capsule = false, bool pill = false, bool over = false) =>
        q.Update(At(t), capsule, pill, over);

    [Fact]
    public void A_Newer_Notice_Replaces_The_Older()
    {
        var q = new NoticeQueue();
        q.Post(Notice(name: "alpha", session: "s1"), At(0));
        Assert.Equal("alpha", Tick(q, 0).Showing!.ProjectName);

        q.Post(Notice(name: "beta", session: "s2"), At(2));
        var state = Tick(q, 2);

        Assert.Equal("beta", state.Showing!.ProjectName);
        // The newer one has its own full time.
        Assert.NotNull(Tick(q, 7.9).Showing);
        Assert.Null(Tick(q, 8.1).Showing);
    }

    [Fact]
    public void A_Stop_For_The_Same_Session_While_Its_Notice_Shows_Changes_Nothing()
    {
        var q = new NoticeQueue();
        Assert.True(q.Post(Notice(session: "s1"), At(0)));
        Tick(q, 0);

        Assert.False(q.Post(Notice(session: "s1"), At(3)));
        Assert.Null(Tick(q, 6.1).Showing); // the clock did not restart

        Assert.True(q.Post(Notice(session: "s1"), At(10))); // gone now, so it shows again
        Assert.NotNull(Tick(q, 10).Showing);
    }

    [Fact]
    public void A_Permission_Prompt_For_The_Same_Session_Does_Replace()
    {
        var q = new NoticeQueue();
        q.Post(Notice(session: "s1"), At(0));
        Tick(q, 0);

        Assert.True(q.Post(Notice(AgentSignal.NeedsYourAnswer, session: "s1"), At(1)));
        Assert.Equal(AgentSignal.NeedsYourAnswer, Tick(q, 1).Showing!.Signal);
    }

    [Fact]
    public void A_Notice_Waits_While_The_Capsule_Is_Open()
    {
        var q = new NoticeQueue();
        q.Post(Notice(name: "alpha"), At(0));

        var open = Tick(q, 0, capsule: true);
        Assert.Null(open.Showing);
        Assert.True(open.Waiting);
        Assert.Null(Tick(q, 40, capsule: true).Showing); // however long the capsule stays

        var shrunk = Tick(q, 41);
        Assert.Equal("alpha", shrunk.Showing!.ProjectName);
        Assert.False(shrunk.Waiting);
        Assert.NotNull(Tick(q, 46.9).Showing); // it gets its full time from when it shows
        Assert.Null(Tick(q, 47.1).Showing);
    }

    [Fact]
    public void A_Showing_Notice_Goes_Back_To_Waiting_When_The_Capsule_Opens()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));
        Assert.NotNull(Tick(q, 0).Showing);

        Assert.True(Tick(q, 2, capsule: true).Waiting);
        var back = Tick(q, 3);

        Assert.NotNull(back.Showing);
        Assert.NotNull(Tick(q, 8.9).Showing);
    }

    [Fact]
    public void A_Newer_Notice_While_The_Capsule_Is_Open_Replaces_The_Waiting_One()
    {
        var q = new NoticeQueue();
        q.Post(Notice(name: "alpha", session: "s1"), At(0));
        Tick(q, 0, capsule: true);
        q.Post(Notice(name: "beta", session: "s2"), At(1));

        Assert.Equal("beta", Tick(q, 2).Showing!.ProjectName);
    }

    [Fact]
    public void A_Waiting_Notice_Does_Not_Wait_For_Ever()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));
        Tick(q, 0, capsule: true);

        var late = Tick(q, NoticeQueue.MaxWait.TotalSeconds + 1);

        Assert.Null(late.Showing);
        Assert.False(late.Waiting);
    }

    [Fact]
    public void It_Never_Takes_The_Keyboard()
    {
        var q = new NoticeQueue();
        var states = new List<NoticeState> { Tick(q, 0) };
        q.Post(Notice(AgentSignal.NeedsYourAnswer), At(1));
        states.Add(Tick(q, 1));
        states.Add(Tick(q, 2, pill: true));
        states.Add(Tick(q, 3, over: true));
        states.Add(Tick(q, 4, capsule: true));
        states.Add(Tick(q, 100));
        states.Add(NoticeState.None);

        Assert.All(states, s => Assert.False(s.TakesKeyboard));
        Assert.Contains(states, s => s.Showing is not null);
        Assert.Contains(states, s => s.Waiting);
    }

    [Fact]
    public void The_Small_Pill_Gives_Way_And_Returns()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));

        Assert.False(Tick(q, 0, pill: false).HidesPill);
        Assert.True(Tick(q, 1, pill: true).HidesPill);
        Assert.False(Tick(q, 7, pill: true).HidesPill); // the notice has left: the pill is back
        Assert.Null(Tick(q, 7, pill: true).Showing);
    }

    [Fact]
    public void The_Notice_Stays_Longer_While_The_Pointer_Is_Over_It()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));
        Tick(q, 0);

        Assert.NotNull(Tick(q, 5.5, over: true).Showing);
        Assert.NotNull(Tick(q, 9, over: true).Showing);
        Assert.NotNull(Tick(q, 14.9).Showing); // 6 s after the pointer was last over it
        Assert.Null(Tick(q, 15.1).Showing);
    }

    [Fact]
    public void A_Parked_Pointer_Cannot_Keep_It_For_Ever()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));
        for (double t = 0; t <= 59; t += 1) Assert.NotNull(Tick(q, t, over: true).Showing);

        Assert.Null(Tick(q, 60.5, over: true).Showing);
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(1, 3)]
    [InlineData(-5, 3)]
    [InlineData(3, 3)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(1e9, 30)]
    [InlineData(double.NaN, 6)]
    [InlineData(double.PositiveInfinity, 30)]
    [InlineData(double.NegativeInfinity, 3)]
    public void The_Time_Is_Kept_Between_Three_And_Thirty_Seconds(double asked, double kept)
    {
        Assert.Equal(kept, new NoticeQueue(asked).Seconds);
        Assert.Equal(kept, NoticeQueue.Clamp(asked));
    }

    [Fact]
    public void The_Setting_Applies_To_The_Next_Notice()
    {
        var q = new NoticeQueue { Seconds = 10 };
        q.Post(Notice(), At(0));
        Tick(q, 0);

        Assert.NotNull(Tick(q, 9.9).Showing);
        Assert.Null(Tick(q, 10.1).Showing);
    }

    [Fact]
    public void A_Click_Makes_It_Leave_At_Once()
    {
        var q = new NoticeQueue();
        q.Post(Notice(), At(0));
        Tick(q, 0);

        q.Dismiss();

        Assert.Null(Tick(q, 1).Showing);
        Assert.True(q.Post(Notice(), At(2))); // the same session may be told again
    }

    [Fact]
    public void Nothing_Showing_Is_Nothing_Drawn()
    {
        var q = new NoticeQueue();

        Assert.Equal(NoticeState.None, Tick(q, 0, capsule: true, pill: true, over: true));
    }
}
