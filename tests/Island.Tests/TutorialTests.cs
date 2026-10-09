using Island.Core;

namespace Island.Tests;

/// <summary>Dan's tutorial of the first start (WORK-ORDER-13, DECISIONS 8 Oct 2026): after the key, on the real island, each step done when the person does it, skippable.</summary>
public class TutorialTests
{
    [Fact]
    public void The_Steps_Follow_The_Order_Dan_Decided_And_The_First_Names_The_Key_That_Was_Chosen()
    {
        var run = new TutorialRun("Ctrl+Alt+K");
        Assert.Equal("Press Ctrl+Alt+K", run.Steps[0].Text);
        Assert.Equal(
            [PracticeEvent.Summoned, PracticeEvent.Moved, PracticeEvent.Chose, PracticeEvent.PageChanged, PracticeEvent.EnteredSecondRow, PracticeEvent.AddedFromRow, PracticeEvent.AskedToRemove, PracticeEvent.Removed, PracticeEvent.Left],
            run.Steps.Select(s => s.Expects).ToArray());
        Assert.All(run.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Text) || string.IsNullOrWhiteSpace(s.Hint)));
    }

    [Fact]
    public void A_Step_Is_Done_By_What_It_Waits_For_And_By_Nothing_Else()
    {
        var run = new TutorialRun("Ctrl+Q");
        Assert.False(run.Feed(PracticeEvent.Moved));
        Assert.Equal(0, run.Index);
        Assert.True(run.Feed(PracticeEvent.Summoned));
        Assert.Equal(1, run.Index);
        Assert.False(run.Feed(PracticeEvent.Summoned));
        Assert.Equal(1, run.Index);
    }

    [Fact]
    public void Every_Event_In_Order_Ends_The_Tutorial_And_It_Says_So_Each_Time()
    {
        var run = new TutorialRun("Ctrl+Q");
        var changes = 0;
        run.Changed += () => changes++;
        foreach (var step in run.Steps.ToList()) Assert.True(run.Feed(step.Expects));
        Assert.True(run.IsDone);
        Assert.Null(run.Current);
        Assert.Equal(run.Count, changes);
        Assert.False(run.Feed(PracticeEvent.Left)); // nothing after the end
    }

    [Fact]
    public void Skip_Step_Goes_On_One_And_Skip_Tutorial_Goes_To_The_End()
    {
        var run = new TutorialRun("Ctrl+Q");
        run.SkipStep();
        Assert.Equal(1, run.Index);
        run.SkipAll();
        Assert.True(run.IsDone);
        run.SkipStep(); // after the end nothing happens, and nothing throws
        run.SkipAll();
        Assert.Equal(run.Count, run.Index);
    }

    [Fact]
    public void Skipping_The_Last_Step_Ends_The_Tutorial()
    {
        var run = new TutorialRun("Ctrl+Q");
        for (var i = 0; i < run.Count; i++) run.SkipStep();
        Assert.True(run.IsDone);
    }

    [Fact]
    public void The_Practice_Is_A_Step_Of_The_First_Start_Right_After_The_Key()
    {
        var steps = FirstStart.Steps.Select(s => s.Step).ToList();
        Assert.Equal([SetupStep.Welcome, SetupStep.Key, SetupStep.Practice, SetupStep.Pages, SetupStep.OnTheIsland, SetupStep.Addon, SetupStep.Mode], steps);
        Assert.Equal("Done", FirstStart.ButtonText(FirstStart.Steps.Count - 1));
        Assert.Equal("Continue", FirstStart.ButtonText(2));
    }

    [Fact]
    public void While_The_Island_Is_Held_Open_The_Idle_Time_Does_Not_Close_It_But_The_Main_Key_Still_Does()
    {
        var held = new Clock(new IslandMachine(2) { HoldOpen = true });
        held.M.ShowHideKey(held.Now);
        held.Run(10_000);
        Assert.Equal(IslandPhase.Open, held.M.Phase);
        held.M.ShowHideKey(held.Now);
        held.Run(3000);
        Assert.Equal(IslandPhase.Hidden, held.M.Phase);

        var free = new Clock(new IslandMachine(2));
        free.M.ShowHideKey(free.Now);
        free.Run(10_000);
        Assert.Equal(IslandPhase.Hidden, free.M.Phase);
    }
}
