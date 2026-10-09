using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 3 (code-3-*): the small pure functions that the repairs of round 2 added (TrayChoice, TerminalRing.ArcAngle, the names' composition), tried with inputs that were not in the repairs' own tests. Invented names only.</summary>
public sealed class Round3SmallTests
{
    [Fact]
    public void Held_TrayChoice_A_Package_Opens_The_Screen_And_An_Ordinary_Install_Opens_The_File()
    {
        Assert.Equal(TrayChoice.SettingsFileAction.OpenTheScreen, TrayChoice.OpenSettingsFile(isPackaged: true));
        Assert.Equal(TrayChoice.SettingsFileAction.OpenTheFile, TrayChoice.OpenSettingsFile(isPackaged: false));
    }

    [Fact]
    public void Held_TerminalRing_ArcAngle_Without_Animations_Is_Zero_For_Every_Time_And_With_Them_Stays_Within_One_Turn_For_Every_Finite_Time()
    {
        var rng = new Random(11);
        for (var n = 0; n < 200_000; n++)
        {
            var seconds = (rng.NextDouble() - 0.1) * Math.Pow(10, rng.Next(-12, 12));
            Assert.Equal(0, TerminalRing.ArcAngle(seconds, animationsOn: false));
            var angle = TerminalRing.ArcAngle(seconds, animationsOn: true);
            Assert.InRange(angle, 0, 360); // a tiny negative time gives exactly one turn (360), which is the same place as 0: no jump on screen
        }

        Assert.Equal(0, TerminalRing.ArcAngle(double.NaN, animationsOn: false));
        Assert.Equal(0, TerminalRing.ArcAngle(double.PositiveInfinity, animationsOn: false));
    }

    [Fact]
    public void Held_Canonically_Equivalent_Spellings_Beyond_The_Accent_Are_One_Page_Name()
    {
        // The Angstrom sign (U+212B) composes to the Latin letter with a ring (U+00C5); a Hangul syllable and its jamo spelling are one name; the compatibility ligature is NOT folded (the rule is
        // canonical equivalence, as scenes compare).
        var store = PageStore.Default.Create("Ångström", "#112233").Store;
        Assert.NotNull(store.Create("Ångström", "#445566").Refusal);
        var hangul = PageStore.Default.Create("한", "#112233").Store;
        Assert.NotNull(hangul.Create("한", "#445566").Refusal);
        var ligature = PageStore.Default.Create("ofﬁce", "#112233").Store;
        Assert.Null(ligature.Create("office", "#445566").Refusal);
    }

    [Fact]
    public void Held_A_Page_Whose_Name_Differs_Only_In_Capitals_After_Composition_Is_Refused_And_The_Refusal_Names_The_Other_Page()
    {
        var store = PageStore.Default.Create("Café", "#112233").Store;
        var refusal = store.Create("CAFÉ", "#445566").Refusal;
        Assert.Equal(SettingsText.PageNameTaken("Café"), refusal);
    }
}
