using System.Diagnostics;
using Island.Core;
using Island.Core.Agents.Sessions;
using Island.Core.Terminals;
using TermProcess = Island.Core.Terminals.ProcessFact;
using SessionProcess = Island.Core.Agents.Sessions.ProcessFact;

namespace Island.Attack11.Tests;

/// <summary>Seeded random worlds through the page logic and through the book, with the invariants that must hold for every world. Every name is invented.</summary>
public class FuzzAttackTests
{
    private static readonly string[] Exes = ["WindowsTerminal.exe", "pwsh.exe", "cmd.exe", "claude.exe", "Claude.exe", "codex.exe", "agy.exe", "node.exe", "alpha.exe", "Cursor.exe", "conhost.exe", "explorer.exe", "ChatGPT.exe", ""];

    private static readonly string[] Titles = ["Alpha", "", "  ", "Beta \u200B gamma", "x", "A very long title that keeps going on and on and on and on and on", "\u0007\u0007", "日本語のタイトル", "😀😀😀"];

    private sealed record World(TerminalReading Reading);

    private static World RandomWorld(Random r)
    {
        var processCount = r.Next(0, 40);
        var processes = new List<TermProcess>();
        for (var i = 0; i < processCount; i++)
        {
            var id = r.Next(1, 60); // few ids: parent loops, repeats and reuse happen
            processes.Add(new TermProcess(id, r.Next(0, 60), Exes[r.Next(Exes.Length)]));
        }

        var windows = new List<TermWindowFact>();
        for (var i = 0; i < r.Next(0, 25); i++)
        {
            var kind = r.Next(4);
            var owner = r.Next(1, 60);
            var handle = 1000L + r.Next(0, 40); // handles repeat now and then
            windows.Add(kind switch
            {
                0 => new TermWindowFact(handle, owner, "WindowsTerminal.exe", null, Fact.TermClass, Titles[r.Next(Titles.Length)], r.Next(0, 30)),
                1 => new TermWindowFact(handle, owner, "cmd.exe", null, Fact.ClassicClass, Titles[r.Next(Titles.Length)], r.Next(0, 30)),
                2 => new TermWindowFact(handle, owner, Exes[r.Next(Exes.Length)], null, "Chrome_WidgetWin_1", Titles[r.Next(Titles.Length)], r.Next(0, 30)),
                _ => new TermWindowFact(handle, owner, "alpha.exe", r.Next(2) == 0 ? "Anthropic.Claude_x" : null, "Alpha", Titles[r.Next(Titles.Length)], r.Next(0, 30)),
            });
        }

        var consoles = new List<ConsoleWindowFact>();
        for (var i = 0; i < r.Next(0, 10); i++)
            consoles.Add(new ConsoleWindowFact(3000 + i, r.Next(2) == 0 ? Fact.Pseudo : Fact.ClassicClass, r.Next(1, 60), r.Next(2) == 0 ? 1000L + r.Next(0, 40) : 0));

        var sessions = new List<HelperSessionFact>();
        for (var i = 0; i < r.Next(0, 12); i++)
        {
            var chain = Enumerable.Range(0, r.Next(0, 6)).Select(_ => r.Next(-2, 60)).ToArray();
            sessions.Add(new HelperSessionFact(new[] { "Claude Code", "Codex", "", "Gemini" }[r.Next(4)], new[] { "alpha", "", "beta", "x y z" }[r.Next(4)], chain,
                r.Next(0, 60), (HelperState)r.Next(0, 4), r.Next(0, 100)));
        }

        return new World(new TerminalReading(windows, consoles, processes, sessions));
    }

    private static void AssertTilesSound(IReadOnlyList<TerminalTile> tiles, TerminalReading reading, string where)
    {
        Assert.Equal(tiles.Count, tiles.Select(t => t.WindowHandle).Distinct().Count());
        var pageHandles = PageWindows.From(reading.Windows, TerminalTables.Default).All.Select(w => w.Handle).ToHashSet();
        Assert.True(tiles.Select(t => t.WindowHandle).ToHashSet().SetEquals(pageHandles), $"{where}: the tiles are not the page's windows");
        foreach (var t in tiles)
        {
            Assert.True(t.FirstLine.Length is > 0 and <= TerminalConstants.MaxTitleChars + ProjectName.MaxChars, $"{where}: first line '{t.FirstLine}'");
            Assert.True(t.SecondLine.Length > 0 && t.SecondLine.Length <= 200, $"{where}: second line");
            Assert.DoesNotContain(t.FirstLine + t.SecondLine, c => char.IsControl(c));
            Assert.InRange(t.Dots, 0, ChoiceConstants.MaxDots);
            Assert.True(t.Ring == HelperState.Idle || t.Kind != WindowRole.None);
            if (t.Kind == WindowRole.AiProgram) Assert.Null(t.HelperName);
            if (t.Face.Kind == FaceKind.HelperDisc) Assert.Equal(WindowRole.Terminal, t.Kind);
            Assert.True(t.Face.Letters.Length > 0, $"{where}: letters");
            Assert.True(t.Ring == HelperState.Idle ? t.Dots <= ChoiceConstants.MaxDots : true);
        }
    }

    [Fact]
    public void Holds_Twenty_Thousand_Random_Worlds_Give_Sound_Tiles_And_The_Same_Tiles_Twice()
    {
        var r = new Random(20261007);
        var clock = Stopwatch.StartNew();
        for (var n = 0; n < 20_000; n++)
        {
            var world = RandomWorld(r);
            var page = new TerminalPage(TerminalTables.Default);
            var a = page.Read(world.Reading);
            AssertTilesSound(a, world.Reading, $"world {n}");
            var again = page.Read(world.Reading);
            Assert.Equal(a, again); // a reading that did not change gives the same tiles
            var fresh = new TerminalPage(TerminalTables.Default).Read(world.Reading);
            Assert.Equal(a.Select(t => t.WindowHandle).OrderBy(h => h), fresh.Select(t => t.WindowHandle).OrderBy(h => h));
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), clock.Elapsed.ToString());
    }

    [Fact]
    public void Holds_Random_Readings_One_After_The_Other_Never_Move_A_Tile_That_Stays()
    {
        var r = new Random(77);
        for (var run = 0; run < 300; run++)
        {
            var page = new TerminalPage(TerminalTables.Default);
            var previous = new List<long>();
            for (var step = 0; step < 15; step++)
            {
                var world = RandomWorld(r);
                var tiles = page.Read(world.Reading).Select(t => t.WindowHandle).ToList();
                var kept = previous.Where(tiles.Contains).ToList();
                Assert.Equal(kept, tiles.Where(kept.Contains).ToList());
                previous = tiles;
            }
        }
    }

    // ---- The book under random messages and readings ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Random_Messages_And_Readings_Keep_The_Book_Within_Its_Rules()
    {
        var r = new Random(4242);
        var clock = new Steady();
        for (var run = 0; run < 200; run++)
        {
            var tracker = new SessionTracker(clock.Read, AgentSignalTables.All, ["claude.exe", "codex.exe", "agy.exe"]);
            var events = new[] { ("SessionStart", ""), ("UserPromptSubmit", ""), ("PostToolUse", "ToolA"), ("PostToolUse", "ToolB"), ("Stop", ""), ("StopFailure", ""), ("Notification", "permission_prompt"),
                ("Notification", "idle_prompt"), ("PermissionRequest", "ToolA"), ("SessionEnd", ""), ("Interrupt", ""), ("Bogus", "") };
            for (var step = 0; step < 400; step++)
            {
                clock.Now += r.Next(0, 20);
                switch (r.Next(5))
                {
                    case 0 or 1 or 2:
                        var (ev, kind) = events[r.Next(events.Length)];
                        var chain = Enumerable.Range(0, r.Next(0, 5)).Select(_ => r.Next(1, 30)).ToArray();
                        tracker.Apply(new SessionMessage(new[] { "claude", "codex", "Claude", "gemini" }[r.Next(4)], ev, kind, r.Next(3) == 0 ? "" : "s" + r.Next(6),
                            r.Next(4) == 0 ? null : clock.Now - r.Next(-50, 200), "Q:\\Invented\\Alpha", chain));
                        break;
                    case 3:
                        tracker.ApplyReading([.. Enumerable.Range(0, r.Next(0, 30)).Select(_ => new SessionProcess(r.Next(1, 30), r.Next(0, 30), new[] { "claude.exe", "codex.exe", "pwsh.exe", "node.exe" }[r.Next(4)]))],
                            [.. Enumerable.Range(0, r.Next(0, 3)).Select(_ => r.Next(1, 30))]);
                        break;
                    default:
                        var pid = r.Next(1, 30);
                        tracker.FoundByName(new[] { "claude", "codex" }[r.Next(2)], new SessionProcess(pid, r.Next(0, 30), "claude.exe"), [pid, r.Next(1, 30)]);
                        break;
                }

                Assert.True(tracker.Count <= SessionLimits.MaxSessions);
                var shown = tracker.Sessions();
                Assert.Equal(shown.Count, shown.Select(s => s.Id).Distinct().Count());
                Assert.All(shown, s =>
                {
                    Assert.True(s.IsShown);
                    Assert.True(s.Process is not null || s.Chain.Count > 0);
                    Assert.True(s.State == SessionState.NeedsYou || s.ToolName.Length == 0, "a tool name is kept only while it needs you");
                });
            }
        }
    }
}
