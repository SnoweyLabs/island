using Island.Core;

namespace Island.Tests;

public class TerminalChoiceTests
{
    private static WindowFact W(long handle, int pid, string title, int rank) => new(handle, pid, title, rank);

    [Fact]
    public void Nearest_Program_With_A_Window_Wins()
    {
        // The chain is nearest first: the agent (100), a shell (200), a terminal (300), a launcher (400).
        int[] chain = [100, 200, 300, 400];
        var windows = new[]
        {
            W(1, 400, "Launcher", 0),
            W(2, 300, "Terminal one", 3),
            W(3, 300, "Terminal two", 1),
            W(4, 999, "Unrelated", 2),
        };

        var chosen = TerminalChoice.Choose(chain, windows, "island");

        Assert.NotNull(chosen);
        Assert.Equal(300, chosen.OwnerProcessId); // not the launcher, although it was used more recently
        Assert.Equal(3, chosen.Handle); // no title has the name, so the most recently used of that program
    }

    [Fact]
    public void A_Title_With_The_Project_Name_Wins_If_Exactly_One_Has_It()
    {
        var windows = new[] { W(1, 300, "alpha - work", 0), W(2, 300, "Island - claude", 4), W(3, 300, "gamma", 1) };

        var chosen = TerminalChoice.Choose([300], windows, "island");

        Assert.Equal(2, chosen!.Handle);
    }

    [Fact]
    public void Two_Titles_With_The_Name_Fall_Back_To_The_Most_Recent_Window_Of_The_Program()
    {
        var windows = new[] { W(1, 300, "island a", 5), W(2, 300, "island b", 2), W(3, 300, "other", 0) };

        var chosen = TerminalChoice.Choose([300], windows, "island");

        Assert.Equal(3, chosen!.Handle);
    }

    [Fact]
    public void An_Empty_Name_Does_Not_Match_Every_Title()
    {
        var windows = new[] { W(1, 300, "a", 2), W(2, 300, "b", 1) };

        Assert.Equal(2, TerminalChoice.Choose([300], windows, "")!.Handle);
    }

    [Fact]
    public void No_Window_Found_Gives_Nothing()
    {
        Assert.Null(TerminalChoice.Choose([100, 200], [W(1, 300, "x", 0)], "island"));
        Assert.Null(TerminalChoice.Choose([], [W(1, 300, "x", 0)], "island"));
        Assert.Null(TerminalChoice.Choose([100], [], "island"));
    }

    [Fact]
    public void A_Nearer_Program_Is_Taken_Even_With_No_Title_Match_While_A_Farther_One_Matches()
    {
        var windows = new[] { W(1, 100, "plain", 9), W(2, 200, "island", 0) };

        Assert.Equal(1, TerminalChoice.Choose([100, 200], windows, "island")!.Handle);
    }
}
