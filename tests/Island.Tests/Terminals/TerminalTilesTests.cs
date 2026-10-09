using Island.Core.Terminals;
using static Island.Tests.Terminals.TerminalFx;

namespace Island.Tests.Terminals;

public class TerminalTilesTests
{
    [Fact]
    public void A_Terminal_Window_Is_One_Tile()
    {
        var reading = Reading([TermWindow(1, 300, "Alpha work")]);

        var tiles = Tiles(reading);

        var tile = Assert.Single(tiles);
        Assert.Equal(1, tile.WindowHandle);
        Assert.Equal(WindowRole.Terminal, tile.Kind);
        Assert.Equal("Alpha work", tile.FirstLine);
        Assert.Equal("terminal", tile.SecondLine);
        Assert.Equal(FaceKind.ProgramIcon, tile.Face.Kind);
        Assert.Equal(TermExe, tile.Face.ExeName);
        Assert.Equal("AT", tile.Face.Letters); // from the program's name ("Alpha Term"), never from the title
        Assert.Equal(HelperState.Idle, tile.Ring);
        Assert.Equal(0, tile.Dots);
        Assert.Null(tile.HelperName);
    }

    [Fact]
    public void Two_Windows_Of_One_Terminal_Program_Are_Two_Tiles()
    {
        var reading = Reading([TermWindow(1, 300, "One"), TermWindow(2, 300, "Two")]);

        var tiles = Tiles(reading);

        Assert.Equal([1L, 2L], tiles.Select(t => t.WindowHandle));
        Assert.Equal(["One", "Two"], tiles.Select(t => t.FirstLine));
    }

    [Fact]
    public void An_AI_Program_Window_Is_One_Tile()
    {
        var byExe = Tiles(Reading([AiWindow(5, 400, "A chat")]));
        var byPackage = Tiles(Reading([new TermWindowFact(6, 401, "other.exe", "Invented.Bravo_abc123", "Frame", "Second chat", 0)]));

        var tile = Assert.Single(byExe);
        Assert.Equal(WindowRole.AiProgram, tile.Kind);
        Assert.Equal("Bravo", tile.FirstLine);
        Assert.Equal("A chat", tile.SecondLine);
        Assert.Equal(FaceKind.ProgramIcon, tile.Face.Kind);
        var viaPackage = Assert.Single(byPackage);
        Assert.Equal("Bravo", viaPackage.FirstLine);
        Assert.Equal("Invented.Bravo_abc123", viaPackage.Face.PackageFamily);
    }

    [Fact]
    public void Any_Other_Window_Is_Not_On_The_Page()
    {
        var reading = Reading([OtherWindow(1, 10), new TermWindowFact(2, 11, null, null, "Frame", "Nameless", 0), new TermWindowFact(3, 12, "x.exe", "Other.Package_1", "Frame", "T", 0)]);

        Assert.Empty(Tiles(reading));
    }

    [Fact]
    public void Tiles_Keep_The_Order_They_Were_First_Seen_In()
    {
        var page = new TerminalPage(Tables);

        // Windows first seen in one reading take the order that reading gives (here by rank, not by handle).
        var first = page.Read(Reading([TermWindow(30, 300, "C"), TermWindow(10, 300, "A"), TermWindow(20, 300, "B")]));
        Assert.Equal([30L, 10L, 20L], first.Select(t => t.WindowHandle));

        // A new window goes on the right; the others never move, even when the reading lists them in another order.
        var second = page.Read(Reading([TermWindow(99, 300, "N"), TermWindow(20, 300, "B"), TermWindow(30, 300, "C"), TermWindow(10, 300, "A")]));
        Assert.Equal([30L, 10L, 20L, 99L], second.Select(t => t.WindowHandle));

        // A window that disappears is forgotten; when it comes back it is new and goes on the right.
        var third = page.Read(Reading([TermWindow(10, 300, "A"), TermWindow(99, 300, "N")]));
        Assert.Equal([10L, 99L], third.Select(t => t.WindowHandle));
        var fourth = page.Read(Reading([TermWindow(30, 300, "C"), TermWindow(10, 300, "A"), TermWindow(99, 300, "N")]));
        Assert.Equal([10L, 99L, 30L], fourth.Select(t => t.WindowHandle));
        Assert.Equal(fourth, page.Tiles);
    }

    [Fact]
    public void A_Title_Is_Cleaned_And_Cut()
    {
        string First(string title) => Tiles(Reading([TermWindow(1, 300, title)]))[0].FirstLine;
        var long80 = new string('x', 80);

        Assert.Equal("Alpha work", First("Alpha\u0007 ‮work\u0000")); // control and direction characters go
        Assert.Equal("Claude work", First("⠂ * - Claude work")); // leading characters that are neither letters nor digits go
        Assert.Equal("5 minutes", First("   5 minutes"));
        Assert.Equal(new string('x', 40), First(long80)); // cut to 40
        Assert.Equal("Alpha - the rest", First("││ Alpha - the rest"));
        Assert.Equal("Alpha Term", First("")); // an empty title reads as the program's name
        Assert.Equal("Alpha Term", First("   • - *")); // nothing letter-like left: the same
        Assert.Equal("Alpha Term", First("​​​")); // draws as nothing
        Assert.Equal(40, TerminalConstants.MaxTitleChars);
    }

    [Fact]
    public void A_Title_Cut_Never_Splits_A_Surrogate_Pair_And_Never_Ends_In_Space()
    {
        var emoji = "\U0001F600";
        var title = new string('a', 39) + emoji + "tail"; // the pair would straddle the 40th character

        var cut = TerminalClassify.CleanTitle(title);

        Assert.Equal(new string('a', 39), cut);
        Assert.Equal(new string('a', 38), TerminalClassify.CleanTitle(new string('a', 38) + "  " + "b")); // the cut falls on a space: not kept
    }

    [Fact]
    public void A_Megabyte_Title_Is_Cut_Without_Reading_It_All()
    {
        var huge = new string('w', 1_000_000);
        var spaces = new string(' ', 1_000_000) + "late";

        Assert.Equal(new string('w', 40), TerminalClassify.CleanTitle(huge));
        Assert.Equal("", TerminalClassify.CleanTitle(spaces)); // the first 512 characters are all that are looked at (Claude)
        Assert.Equal(512, TerminalConstants.TitleReadLimit);
    }

    [Fact]
    public void Letters_Never_Come_From_A_Title()
    {
        var spinner = Reading([TermWindow(1, 300, "Zed project"), TermWindow(2, 300, "Quux"), AiWindow(3, 400, "Yak")]);
        var changed = Reading([TermWindow(1, 300, "Mol project"), TermWindow(2, 300, "Wug"), AiWindow(3, 400, "Nib")]);

        var a = Tiles(spinner);
        var b = Tiles(changed);

        Assert.Equal(a.Select(t => t.Face), b.Select(t => t.Face)); // the face does not depend on the title at all
        Assert.All(a.Where(t => t.Kind == WindowRole.Terminal), t => Assert.Equal("AT", t.Face.Letters));
        Assert.Equal("Br", a[2].Face.Letters);

        // A helper's letters come from its project's name or its own name, not from the window's title either.
        var helperA = Tiles(HelperInWindowOne(title: "Zed project"))[0];
        var helperB = Tiles(HelperInWindowOne(title: "Mol project"))[0];
        Assert.Equal("CC", helperA.Face.Letters);
        Assert.Equal(helperA.Face, helperB.Face);
    }

    [Fact]
    public void A_Terminal_By_Class_Is_A_Terminal_Whatever_Its_Program_Is_Called()
    {
        var reading = Reading(
            [
                new TermWindowFact(1, 10, "mystery.exe", null, ClassicConsole, "Classic", 0),
                new TermWindowFact(2, 11, AiExe, null, Wt, "Hosted", 0), // an AI program's name on a terminal class: still a terminal
                new TermWindowFact(3, 12, null, null, "cascadia_hosting_window_class", "No program", 0),
            ]);

        var tiles = Tiles(reading);

        Assert.All(tiles, t => Assert.Equal(WindowRole.Terminal, t.Kind));
        Assert.Equal(["Classic", "Hosted", "No program"], tiles.Select(t => t.FirstLine));
        Assert.Equal("My", tiles[0].Face.Letters); // from the unknown program's file name, without ".exe"
        Assert.Equal("Te", tiles[2].Face.Letters); // no program at all: "Terminal"
    }

    [Fact]
    public void A_Terminal_By_Program_Name_Is_A_Terminal_Whatever_Its_Class()
    {
        var tiles = Tiles(Reading([new TermWindowFact(1, 10, "ALPHA-TERM.EXE", null, "SomeOtherClass", "Shell", 0)]));

        Assert.Equal(WindowRole.Terminal, Assert.Single(tiles).Kind);
    }

    [Fact]
    public void One_Window_Is_One_Tile_Even_When_The_Reading_Lists_Its_Handle_Twice()
    {
        var tiles = Tiles(Reading([TermWindow(1, 300, "First"), TermWindow(1, 300, "Again")]));

        Assert.Equal("First", Assert.Single(tiles).FirstLine);
    }

    [Fact]
    public void An_AI_Windows_Empty_Title_Reads_As_The_Programs_Name()
    {
        var tile = Assert.Single(Tiles(Reading([AiWindow(5, 400, "  - ")])));

        Assert.Equal("Bravo", tile.FirstLine);
        Assert.Equal("Bravo", tile.SecondLine);
    }

    [Fact]
    public void Odd_Input_Never_Throws()
    {
        var odd = new TerminalReading(
            [null!, new TermWindowFact(1, -5, null, null, null!, null!, -1), TermWindow(2, 0, "\uD800 lone"), TermWindow(3, int.MaxValue, "ok")],
            [null!, new ConsoleWindowFact(1, null!, 0, 0)],
            [null!, new ProcessFact(0, 0, null!), new ProcessFact(-1, -1, ""), new ProcessFact(5, 5, HelperAExe), new ProcessFact(6, 7, HelperAExe), new ProcessFact(7, 6, HelperAExe)],
            [null!, new HelperSessionFact(null!, null!, null!, 0, HelperState.Working, 0), new HelperSessionFact("", "", [0, -1, 5, 5], 5, (HelperState)99, long.MinValue)]);
        var page = new TerminalPage(Tables);

        var tiles = page.Read(odd);
        var none = page.Read(null);

        Assert.True(tiles.Count >= 2);
        Assert.Empty(none);
        Assert.Empty(TerminalTiles.Build(null, Tables));
        Assert.Empty(TerminalTiles.Build(odd, TerminalTables.Empty));
    }

    [Fact]
    public void A_Hundred_Windows_Are_Worked_Out_In_Order_And_Quickly()
    {
        var windows = Enumerable.Range(1, 100).Select(i => i % 3 == 0 ? OtherWindow(i, i) : i % 3 == 1 ? TermWindow(i, 300 + i, "T" + i, i) : AiWindow(i, 600 + i, "C" + i, i)).ToList();
        var processes = Enumerable.Range(1, 100).Select(i => Proc(1000 + i, 300 + i, HelperAExe)).ToList();
        var page = new TerminalPage(Tables);
        var started = DateTime.UtcNow;

        var tiles = page.Read(Reading(windows, processes: processes));

        Assert.Equal(67, tiles.Count);
        Assert.Equal(tiles.Select(t => t.WindowHandle).Order(), tiles.Select(t => t.WindowHandle));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void The_Chosen_Constants_Are_Pinned()
    {
        Assert.Equal(" · working", TerminalConstants.WorkingWords);
        Assert.Equal(" · needs you", TerminalConstants.NeedsYouWords);
        Assert.Equal(" · finished", TerminalConstants.FinishedWords);
        Assert.Equal("terminal", TerminalConstants.TerminalWord);
        Assert.Equal("Terminal", TerminalConstants.UnknownTerminalName);
        Assert.Equal(32, TerminalConstants.MaxChainUsed);
        Assert.Equal(new HelperColor(217, 119, 60), TerminalConstants.ClaudeCodeColor);
        Assert.Equal(new HelperColor(16, 163, 127), TerminalConstants.CodexColor);
        Assert.Equal(new HelperColor(76, 141, 255), TerminalConstants.AntigravityColor);
        Assert.Equal(new HelperColor(142, 117, 255), TerminalConstants.GeminiColor);
        Assert.Equal(new HelperColor(128, 128, 140), TerminalConstants.UnknownHelperColor);
        Assert.Equal(3, TerminalConstants.UrgencyOf(HelperState.NeedsYou));
        Assert.Equal(2, TerminalConstants.UrgencyOf(HelperState.Working));
        Assert.Equal(1, TerminalConstants.UrgencyOf(HelperState.Finished));
        Assert.Equal(0, TerminalConstants.UrgencyOf(HelperState.Idle));
    }
}
