using Island.Core;

namespace Island.Tests;

/// <summary>The complete table of WORK-ORDER-7 section 1, written out cell by cell, independently of the code under test.</summary>
public class ModeTableTests
{
    // front, mode, shows when asked, then by itself: island, pill, notice. Copied from the table, row by row.
    private static readonly (FrontState Front, Mode Mode, bool Asked, bool Island, bool Pill, bool Notice)[] Table =
    [
        (FrontState.Clear, Mode.Focus, true, true, true, true),
        (FrontState.Clear, Mode.Vibe, true, true, true, true),
        (FrontState.Clear, Mode.DND, true, false, false, false),

        (FrontState.FullscreenProgram, Mode.Focus, true, false, false, true),
        (FrontState.FullscreenProgram, Mode.Vibe, true, false, false, false),
        (FrontState.FullscreenProgram, Mode.DND, false, false, false, false),

        (FrontState.Presentation, Mode.Focus, true, false, false, false),
        (FrontState.Presentation, Mode.Vibe, true, false, false, false),
        (FrontState.Presentation, Mode.DND, false, false, false, false),

        (FrontState.ExclusiveFullscreen, Mode.Focus, false, false, false, false),
        (FrontState.ExclusiveFullscreen, Mode.Vibe, false, false, false, false),
        (FrontState.ExclusiveFullscreen, Mode.DND, false, false, false, false),
    ];

    /// <summary>One case per cell: 4 rows x 3 modes x 2 origins x 3 things, each named "front/mode/origin/thing".</summary>
    public static IEnumerable<object[]> Cells()
    {
        foreach (var (front, mode, asked, island, pill, notice) in Table)
        {
            foreach (var thing in Enum.GetValues<Appearer>())
            {
                yield return [$"{front}/{mode}/Asked/{thing}", front, mode, ShowOrigin.Asked, thing, asked];
                var itself = thing switch { Appearer.Island => island, Appearer.Pill => pill, _ => notice };
                yield return [$"{front}/{mode}/ByItself/{thing}", front, mode, ShowOrigin.ByItself, thing, itself];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_Cell_Of_The_Table(string cell, FrontState front, Mode mode, ShowOrigin origin, Appearer thing, bool shows)
    {
        var answer = ShowDecision.Decide(front, thing, origin, mode);

        Assert.True(shows == (answer == ShowAnswer.Show), $"{cell}: expected {(shows ? "Show" : "StayAway")}, got {answer}");
    }

    [Fact]
    public void The_Table_Above_Has_Every_Cell()
    {
        Assert.Equal(Enum.GetValues<FrontState>().Length * Enum.GetValues<Mode>().Length, Table.Length);
        Assert.Equal(Table.Length * 6, Cells().Count());
    }

    [Fact]
    public void Never_Over_List_Counts_As_Exclusive()
    {
        var list = NeverOverList.Empty.With(new NeverOverEntry("Alpha", "alpha.exe"));

        // A listed program in front and fullscreen is exclusive fullscreen (case of the file name ignored)...
        Assert.Equal(FrontState.ExclusiveFullscreen, list.Apply(FrontState.FullscreenProgram, "alpha.exe"));
        Assert.Equal(FrontState.ExclusiveFullscreen, list.Apply(FrontState.FullscreenProgram, "ALPHA.EXE"));
        // ...so the table keeps everything away, even when the person asked, in every mode.
        foreach (var mode in Enum.GetValues<Mode>())
            Assert.Equal(ShowAnswer.StayAway, ShowDecision.Decide(list.Apply(FrontState.FullscreenProgram, "alpha.exe"), Appearer.Island, ShowOrigin.Asked, mode));

        // Anything else is left alone.
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, "beta.exe"));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, null));
        Assert.Equal(FrontState.FullscreenProgram, list.Apply(FrontState.FullscreenProgram, ""));
        Assert.Equal(FrontState.Clear, list.Apply(FrontState.Clear, "alpha.exe"));
        Assert.Equal(FrontState.Presentation, list.Apply(FrontState.Presentation, "alpha.exe"));
        Assert.Equal(FrontState.ExclusiveFullscreen, list.Apply(FrontState.ExclusiveFullscreen, "beta.exe"));
        Assert.Equal(FrontState.Clear, NeverOverList.Empty.Apply(FrontState.Clear, null));
        Assert.Equal(FrontState.FullscreenProgram, NeverOverList.Empty.Apply(FrontState.FullscreenProgram, "alpha.exe"));
    }

    [Fact]
    public void The_List_Keeps_Names_And_File_Names_Never_Paths()
    {
        var list = NeverOverList.Empty
            .With(new NeverOverEntry("Alpha", "alpha.exe"))
            .With(new NeverOverEntry("Alpha again", "ALPHA.exe"))
            .With(new NeverOverEntry("Gamma", @"C:\Games\gamma.exe"))
            .With(new NeverOverEntry("Delta", "games/delta.exe"))
            .With(new NeverOverEntry("Epsilon", ""))
            .With(new NeverOverEntry("", "zeta.exe"))
            .With(new NeverOverEntry("Eta", "eta:stream.exe"));

        Assert.Equal(["alpha.exe"], list.Entries.Select(e => e.ExeFileName));
        Assert.False(list.Contains(@"C:\Games\gamma.exe"));
        Assert.False(list.Contains("gamma.exe"));

        var removed = list.Without("ALPHA.EXE");
        Assert.Empty(removed.Entries);
        Assert.Single(list.Entries); // immutable: the first list is unchanged
        Assert.Equal(["alpha.exe", "beta.exe"], NeverOverList.From([new NeverOverEntry("Alpha", "alpha.exe"), null, new NeverOverEntry("Beta", "beta.exe")]).Entries.Select(e => e.ExeFileName));
    }
}
