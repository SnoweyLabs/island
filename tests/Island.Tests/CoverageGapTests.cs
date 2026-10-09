using Island.Core;
using Island.Core.SettingsEdit;
using Island.Core.Speed;
using Island.Tests.SettingsEdit;

namespace Island.Tests;

/// <summary>
/// Tests for promises that <c>review/coverage.md</c> found without a proof (WORK-ORDER-12 section 5): small pure facts that a work order or EVALS.md promised and that no test pinned.
/// Each test says in its name which promise it holds.
/// </summary>
public class CoverageGapTests
{
    // ---- the light ------------------------------------------------------------------------------------------------------------------------

    // ---- the modes ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Breath_Of_Vibe_And_The_Dashed_Rim_Of_Do_Not_Disturb_Have_The_Ordered_Numbers()
    {
        // The owner may change these numbers (STATE.md OWNER DECISIONS); a change must be seen: it turns this red and is written next to it.
        Assert.Equal(0.4, ModeMark.BreathLow);
        Assert.Equal(2.2, ModeMark.BreathSeconds);
        Assert.Equal(1.6, ModeMark.DashedRimWidth);
        Assert.Equal(0.55, ModeMark.DashedRimAlpha);
        Assert.Equal(1.0, ModeMark.Breath(0), 9); // a breath starts at 1 and comes back to it
        Assert.Equal(ModeMark.BreathLow, Enumerable.Range(0, 220).Min(i => ModeMark.Breath(i / 100.0)), 2);
    }

    // ---- the window that holds the island ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Window_Holds_The_Largest_Shape_With_The_Overshoot_Of_The_Springs_And_The_Shadow()
    {
        Assert.Equal(CapsuleLayout.Width(ChoiceConstants.MaxVisibleTiles + 1, isMedia: true), WindowMetrics.LargestCapsuleWidth);
        Assert.True(WindowMetrics.PeakCapsuleWidth >= Pages.AllPlaceholders.Max(c => CapsuleLayout.SizeFor(c).Width), "the spring's peak is at least where the widest page lands");
        Assert.True(WindowMetrics.PeakTwoRowHeight >= ChoiceConstants.TwoRowHeight);

        var reach = Math.Max(WindowMetrics.ShadowReach, WindowMetrics.BloomReach);
        Assert.True(WindowMetrics.Width >= WindowMetrics.PeakCapsuleWidth + 2 * reach, $"the window is {WindowMetrics.Width} wide");
        Assert.True(WindowMetrics.Height >= LookConstants.TopGap + WindowMetrics.PeakTwoRowHeight + WindowMetrics.ShadowReach, $"the window is {WindowMetrics.Height} high");
        // under the one-row capsule the drop zone of a drag (8 below it, 40 high) is inside the height too
        Assert.True(WindowMetrics.Height >= LookConstants.TopGap + LookConstants.CapsuleHeight + 8 + 40);
        Assert.Equal(Math.Ceiling(WindowMetrics.Width), WindowMetrics.Width);
        Assert.Equal(Math.Ceiling(WindowMetrics.Height), WindowMetrics.Height);
    }

    // ---- the notice -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Notice_Is_A_Pill_Fifty_High_Smaller_Than_The_Capsule_With_The_Approved_Numbers()
    {
        Assert.Equal(50, NoticeLayout.Height);
        Assert.Equal(25, NoticeLayout.Radius);
        Assert.Equal(36, NoticeLayout.Disc);
        Assert.Equal(7, NoticeLayout.PadLeft);
        Assert.Equal(14, NoticeLayout.PadRight);
        Assert.True(NoticeLayout.Height < LookConstants.CapsuleHeight, "EVALS A1: a notice smaller than the capsule");
        Assert.Equal(NoticeLayout.Height / 2, NoticeLayout.Radius); // a full pill
        Assert.Equal(NoticeLayout.PadLeft + NoticeLayout.Disc + NoticeLayout.Gap + NoticeLayout.TextMaxWidth + NoticeLayout.PadRight, NoticeLayout.Width(double.PositiveInfinity));
    }

    // ---- the tray and the ring -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Tray_Opens_The_Settings_Screen_Instead_Of_The_File_When_Packaged()
    {
        Assert.Equal(TrayChoice.SettingsFileAction.OpenTheScreen, TrayChoice.OpenSettingsFile(isPackaged: true));
        Assert.Equal(TrayChoice.SettingsFileAction.OpenTheFile, TrayChoice.OpenSettingsFile(isPackaged: false));
    }

    [Fact]
    public void The_Working_Arc_Turns_With_The_Islands_Clock_And_Stands_Still_At_The_Top_Without_Animations()
    {
        Assert.Equal(0, TerminalRing.ArcAngle(12.34, animationsOn: false));
        Assert.Equal(0, TerminalRing.ArcAngle(0, animationsOn: true));
        Assert.Equal(180, TerminalRing.ArcAngle(TerminalRing.TurnSeconds / 2, animationsOn: true), 6);
        Assert.Equal(TerminalRing.ArcAngle(1.5, animationsOn: true), TerminalRing.ArcAngle(1.5 + TerminalRing.TurnSeconds, animationsOn: true), 6); // a turn is periodic
    }

    // ---- the speed table's middle of three ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Middle_Of_Three_Readings_Is_The_Second_Smallest_And_The_Spread_Is_Highest_Minus_Lowest()
    {
        var row = SpeedRow.Of("1", "a thing", [9.0, 3.0, 5.0]);
        Assert.Equal(5.0, row.Median);
        Assert.Equal(6.0, row.Spread);
    }

    // ---- the page the Media page opens on ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Media_Page_Opens_On_The_Pick_That_Is_Playing_And_Every_Other_Page_On_The_First()
    {
        var playing = new Clock(new IslandMachine(60, null, null, page => page.Id == PageIds.Media ? 2 : -1));
        playing.M.PageKey(PageIds.Media, playing.Now);
        playing.Run(2000);
        Assert.Equal(2, playing.M.SelectedItem);

        var none = new Clock(new IslandMachine(60, null, null, _ => -1));
        none.M.PageKey(PageIds.Media, none.Now);
        none.Run(2000);
        Assert.Equal(0, none.M.SelectedItem);

        var other = new Clock(new IslandMachine(60, null, null, page => page.Id == PageIds.Media ? 2 : -1));
        other.M.PageKey(PageIds.Apps, other.Now);
        other.Run(2000);
        Assert.Equal(0, other.M.SelectedItem);
    }

    // ---- the list of programs the island never appears over ---------------------------------------------------------------------------------------

    private static SettingsSession SessionWith(TempDir dir, IReadOnlyList<RunningProgram> running)
    {
        var files = SessionFixtures.Files(dir);
        var known = Fixtures.EverythingInstalled;
        return new SettingsSession(
            files,
            new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore(StarterPicks.Build(known)), PickStoreStatus.Loaded, null),
            new FakeRegistrar(),
            () => known,
            () => false,
            running: () => running);
    }

    [Fact]
    public void A_Program_Is_Added_To_The_Never_Over_List_Once_Is_Listed_As_Running_Only_Until_Then_And_Is_Removed_Again()
    {
        using var dir = new TempDir();
        var session = SessionWith(dir, [new RunningProgram("Alpha Game", "alpha.exe"), new RunningProgram("Beta Game", "beta.exe"), new RunningProgram("Bad", @"C:\x\bad.exe")]);
        Assert.Equal(["alpha.exe", "beta.exe"], session.RunningNotOnList().Select(p => p.ExeFileName).ToArray()); // a path is not a file name: left out

        Assert.True(session.AddNeverOver(new NeverOverEntry("Alpha Game", "alpha.exe")).Changed);
        Assert.False(session.AddNeverOver(new NeverOverEntry("Alpha Game", "ALPHA.EXE")).Changed); // a repeat changes nothing
        Assert.Equal(["beta.exe"], session.RunningNotOnList().Select(p => p.ExeFileName).ToArray());
        Assert.True(session.Settings.NeverOver.Contains("alpha.exe"));
        Assert.True(Settings.Load(SessionFixtures.Files(dir).SettingsPath).Settings.NeverOver.Contains("alpha.exe")); // saved at once

        Assert.True(session.RemoveNeverOver("alpha.exe").Changed);
        Assert.False(session.Settings.NeverOver.Contains("alpha.exe"));
        Assert.False(session.RemoveNeverOver("alpha.exe").Changed);
    }

    [Theory]
    [InlineData("", "alpha.exe")]
    [InlineData("Alpha", "")]
    [InlineData("Alpha", "alpha")]
    [InlineData("Alpha", @"C:\Invented\alpha.exe")]
    [InlineData("Alpha", "a/b.exe")]
    public void A_Program_That_Cannot_Be_Matched_Is_Refused_With_The_Reason(string name, string exe)
    {
        using var dir = new TempDir();
        var session = SessionWith(dir, []);
        var result = session.AddNeverOver(new NeverOverEntry(name, exe));
        Assert.False(result.Changed);
        Assert.Equal(SettingsText.NeverOverRefused, result.Refusal);
    }

    [Fact]
    public void A_Full_Never_Over_List_Says_So()
    {
        using var dir = new TempDir();
        var session = SessionWith(dir, []);
        for (var i = 0; i < ModeSettingsJson.MaxNeverOver; i++) Assert.True(session.AddNeverOver(new NeverOverEntry($"Game {i}", $"game{i}.exe")).Changed);
        var result = session.AddNeverOver(new NeverOverEntry("One more", "more.exe"));
        Assert.False(result.Changed);
        Assert.Equal(SettingsText.NeverOverFull, result.Refusal);
    }

    [Fact]
    public void The_Question_Before_Restoring_The_Keys_Says_The_Main_Key_Is_Changed_Back_Not_Taken_Away()
    {
        var text = SettingsText.RestoreAllKeysQuestion(3);
        Assert.Contains("3 keys", text, StringComparison.Ordinal);
        Assert.Contains("the main key to the one it had", text, StringComparison.Ordinal);
        Assert.Contains("1 key", SettingsText.RestoreAllKeysQuestion(1), StringComparison.Ordinal);
    }

    // ---- the refusals of connecting a helper -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Refusals_Of_Connecting_A_Helper_Keep_Their_Three_Parts_And_Their_Codes()
    {
        var refusals = new[] { AgentRefusals.NotifyMissing, AgentRefusals.NotAllowed, AgentRefusals.NotifyPathInvalid, AgentRefusals.HooksFileUnreadable };
        Assert.Equal(["NOTIFY_MISSING", "AGENT_SETTINGS_NOT_ALLOWED", "NOTIFY_PATH_INVALID", "HOOKS_FILE_UNREADABLE"], refusals.Select(r => r.Code).ToArray());
        foreach (var r in refusals)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened) || string.IsNullOrWhiteSpace(r.Why) || string.IsNullOrWhiteSpace(r.NextAction), r.Code);
            Assert.DoesNotContain("Exception", r.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(" at ", r.Message.Replace("Island", string.Empty), StringComparison.Ordinal);
        }
    }

    // ---- the evidence of the blur test ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_Blur_Evidence_Holds_A_Verdict_And_Its_Readme_Opens_With_It()
    {
        var json = File.ReadAllText(RepoPaths.File("review", "blur", "blur.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("plain_backdrop_brush", doc.RootElement.GetProperty("methods").EnumerateObject().Any() ? "plain_backdrop_brush" : "none"); // the methods tried are in the file
        Assert.Contains("\"from_method\": \"plain_backdrop_brush\"", json, StringComparison.Ordinal);
        var readme = File.ReadAllText(RepoPaths.File("review", "blur", "README.md"));
        Assert.StartsWith("# Blur test", readme, StringComparison.Ordinal);
        Assert.Contains("**Verdict: YES", readme, StringComparison.Ordinal);
        Assert.True(File.Exists(RepoPaths.File("review", "blur", "host_backdrop_brush-ball.png")));
    }

    // ---- no test aims at the real settings of a helper ---------------------------------------------------------------------------------------------

    [Fact]
    public void No_Test_Builds_The_Place_Of_A_Helpers_Real_Settings_File()
    {
        // WORK-ORDER-7 section 4: the guard of src/ is not enough; "the source of every project, tests included". The one place the text appears in tests/ is a display string.
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.File("tests"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (Path.GetFileName(file) == nameof(CoverageGapTests) + ".cs") continue;
            foreach (var line in File.ReadLines(file))
            {
                var text = line.Replace(@"%USERPROFILE%\.claude\settings.json", string.Empty);
                if ((text.Contains("\".claude\"", StringComparison.Ordinal) || text.Contains("\".codex\"", StringComparison.Ordinal)) && text.Contains("UserProfile", StringComparison.Ordinal))
                    hits.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {line.Trim()}");
            }
        }

        Assert.Empty(hits);
    }
}
