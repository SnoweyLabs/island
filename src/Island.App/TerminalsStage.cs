using System.IO;
using System.Windows;
using System.Windows.Interop;
using Island.Core;
using Island.Core.Terminals;

namespace Island.App;

/// <summary>
/// WORK-ORDER-11 sections 1 and 2 with an invented world and invented tables (no real window, console or process list is ever read): two terminal windows of one
/// invented terminal program, one window of an invented AI program and one ordinary window give three tiles in that order; a process named as a helper whose parent owns
/// a hidden console window owned by the first terminal window turns that tile into the helper's (its name, two letters on its colour) and back when it is gone; a click on
/// the second tile asks the gate to bring that window forward, which the gate refuses and counts; a title that changes redraws that tile in place; a new window widens the
/// capsule without a page swap; closing the first window leaves two tiles and the selection on the window it was on; the X, Delete, Down and search do nothing here. The
/// foreground window at the end is the one from before, or foregroundGranted: false. Invented names only.
/// </summary>
internal sealed class TerminalsStage(SelfTestReport report, TimeSpan hangLimit, string folder)
{
    private const int Down = 0x28, Delete = 0x2E;

    private sealed class InventedProbe : ITerminalProbe
    {
        public TerminalProbeFacts Facts { get; set; } = TerminalProbeFacts.Empty;

        public int Reads { get; private set; }

        /// <summary>The thread the very first reading was made on (the page's own timer, not a call of this stage).</summary>
        public int FirstReadThread { get; private set; } = -1;

        public TerminalProbeFacts Read()
        {
            if (Reads == 0) FirstReadThread = Environment.CurrentManagedThreadId;
            Reads++;
            return Facts;
        }
    }

    private static readonly TerminalTables Tables = new(
        ["INVENTED_TERMINAL_CLASS"],
        [new TerminalProgramRow("alpha-term.exe", "Alpha Term")],
        [new AiProgramRow("Beta Assistant", ["beta-assistant.exe"], [])],
        [new HelperProgramRow("alpha-helper.exe", "Alpha Helper"), new HelperProgramRow("invented-helper.exe", TerminalConstants.ClaudeCode), new HelperProgramRow("invented-codex.exe", TerminalConstants.Codex)],
        new Dictionary<string, HelperColor> { ["Alpha Helper"] = new(10, 200, 30), [TerminalConstants.ClaudeCode] = TerminalConstants.ClaudeCodeColor, [TerminalConstants.Codex] = TerminalConstants.CodexColor });

    public async Task RunAsync()
    {
        var other = new Window { Title = "Island self-test: stand-in for another program", Width = 240, Height = 90, Left = 40, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual };
        other.Show();
        var before = new WindowInteropHelper(other).EnsureHandle();
        await Task.Delay(150);
        OutsideForeground.BringForward(before);
        var granted = Native.GetForegroundWindow() == before;
        report.Info["foregroundGrantedForTerminals"] = granted;

        var pretend = new PretendWorld
        {
            Windows =
            [
                new OpenWindow(5001, "alpha-term.exe", null, "first shell", 0, "INVENTED_TERMINAL_CLASS", 4001),
                new OpenWindow(5002, "alpha-term.exe", null, "second shell", 1, "INVENTED_TERMINAL_CLASS", 4002),
                new OpenWindow(5003, "beta-assistant.exe", null, "assistant window", 2, "InventedClass", 4003),
                new OpenWindow(5004, "plain.exe", null, "an ordinary window", 3, "InventedClass", 4004),
            ],
        };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var picksPath = Path.Combine(folder, "terminals-stage-picks.json");
        var book = new PickBook(PickStore.Empty, Path.Combine(Path.GetTempPath(), "island-terminals-stage-" + Guid.NewGuid().ToString("N") + ".json"), canSave: false);
        var probe = new InventedProbe();
        var helpers = new HelperSessions(Tables);
        var page = new TerminalsPage(world, probe, Tables, readsWindows: true, helpers, synchronousIcons: true);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true) { Terminals = page };
        using var rt = new IslandRuntime(30, pages, book, takeKeyboard: false);
        rt.Show();
        await Task.Delay(150);
        var c = rt.Controller;
        var m = c.Machine;

        try
        {
            c.MainKey();
            await Waiter.UntilAsync(() => m.IsAtRest, "the island open after the main key", hangLimit, report);
            report.Check("the page is not laid out while another page shows, so nothing was read yet", !page.LaidOut && probe.Reads == 0, $"laid out {page.LaidOut}, {probe.Reads} readings");
            c.PageKey(PageIds.Terminals);
            await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Terminals, "the Terminals page at rest", hangLimit, report);
            await Waiter.UntilAsync(() => page.LaidOut && probe.Reads >= 1, "the page laid out and read once", hangLimit, report);
            report.Check("the page is laid out now and the console windows and the process list were read once", page.LaidOut && probe.Reads >= 1, $"{probe.Reads} readings");
            report.Check("the process list is read on a thread of its own, never on the drawing thread", probe.FirstReadThread != -1 && probe.FirstReadThread != Environment.CurrentManagedThreadId, $"first read on thread {probe.FirstReadThread}, drawing thread {Environment.CurrentManagedThreadId}");

            var items = m.ContentsItems;
            report.Check("the page shows three tiles in the order the windows were first seen: the two terminals, then the AI program",
                items.Count == 3 && items[0].Title == "first shell" && items[1].Title == "second shell" && items[2].Title == "Beta Assistant" && items[2].Subtitle == "assistant window"
                && items.All(i => i.PickId is null && !i.IsPlus && i.WindowKey is not null), $"{items.Count} tiles");
            report.Check("the page has no + tile and no second row, and Down does not open one", items.All(i => !i.IsPlus) && PressAndSettle(c, Down) && !m.SecondRowOpen, "no + tile");
            report.Check("a terminal's second line reads terminal", items[0].Subtitle == "terminal", items[0].Subtitle);
            report.Check("a search for a window's name finds nothing of this page (only the service tiles, which are no window)",
                await SearchFindsNothingAsync(rt, c), "search never reads or lists this page");

            // A helper process whose parent owns a hidden console window that the first terminal window owns.
            probe.Facts = new TerminalProbeFacts(
                [new ConsoleWindowFact(7001, "PseudoConsoleWindow", 4200, 5001)],
                [new ProcessFact(4200, 1, "invented-shell.exe"), new ProcessFact(4300, 4200, "alpha-helper.exe")]);
            page.ProbeOnce();
            await Waiter.UntilAsync(() => m.ContentsItems[0].Disc is not null, "the first tile turned into the helper's", hangLimit, report);
            var first = m.ContentsItems[0];
            report.Check("the first tile reads the helper's name and shows two letters on the helper's colour",
                first.Title == "Alpha Helper" && first.Mark == "AH" && first.Disc is { R: 10, G: 200, B: 30 }, $"{first.Title} / {first.Mark}");
            probe.Facts = new TerminalProbeFacts([new ConsoleWindowFact(7001, "PseudoConsoleWindow", 4200, 5001)], [new ProcessFact(4200, 1, "invented-shell.exe")]);
            page.ProbeOnce();
            await Waiter.UntilAsync(() => m.ContentsItems[0].Disc is null, "the first tile a plain terminal again", hangLimit, report);
            report.Check("with the helper gone from the list the tile is a plain terminal again, in the same place", m.ContentsItems[0].Title == "first shell" && m.ContentsItems[0].Disc is null && m.ContentsItems.Count == 3, m.ContentsItems[0].Title);

            await HelperSessionsAsync(rt, page, probe, helpers);

            // A click on the second tile asks the gate to bring that window forward; the gate refuses and counts.
            c.TileClicked(1);
            await Task.Delay(120);
            report.Check("a click on the second tile asks the one outside door to bring that window forward, once, and selects that tile",
                recording.Calls.Count(call => call == "bring:5002") == 1 && recording.Calls.Count == 1 && m.SelectedItem == 1, $"{recording.Calls.Count} requests");
            report.Check("the X does nothing on this page, whatever was clicked", rt.Close is { } close && close.Press().Action == Island.Core.CloseAction.Nothing && !rt.View.Contents.CloseDrawnReady, "dimmed");
            report.Check("Delete asks about nothing: no tile of this page is a pick", PressAndSettle(c, Delete) && m.PendingDeleteId is null, $"pending {m.PendingDeleteId}");

            // A title that changes redraws that tile in place and nothing else.
            await Task.Delay(400);
            await Waiter.UntilAsync(() => m.IsAtRest, "the page at rest before the title changes", hangLimit, report);
            var tileBefore0 = rt.View.Contents.TileAt(0);
            var tileBefore2 = rt.View.Contents.TileAt(2);
            pretend.Windows = [.. pretend.Windows.Select(w => w.Handle == 5003 ? w with { Title = "assistant window, renamed" } : w)];
            world.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems[2].Subtitle == "assistant window, renamed" && rt.View.Contents.Contents.Items[2].Subtitle == "assistant window, renamed", "the renamed tile drawn", hangLimit, report);
            report.Check("a title that changed redraws that one tile in its place, and the other tiles are the very ones that were drawn before",
                ReferenceEquals(rt.View.Contents.TileAt(0), tileBefore0) && !ReferenceEquals(rt.View.Contents.TileAt(2), tileBefore2) && rt.View.Contents.TileCount == 3, $"same tile 0: {ReferenceEquals(rt.View.Contents.TileAt(0), tileBefore0)}, new tile 2: {!ReferenceEquals(rt.View.Contents.TileAt(2), tileBefore2)}, {rt.View.Contents.TileCount} tiles");

            // A new window widens the capsule through the springs, with no page swap and no jump of the selection.
            var width = m.CapsuleTargetWidth;
            var swaps = m.PageChangeCount;
            pretend.Windows = [.. pretend.Windows, new OpenWindow(5005, "alpha-term.exe", null, "third shell", 4, "INVENTED_TERMINAL_CLASS", 4005)];
            world.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems.Count == 4 && rt.View.Contents.TileCount == 4, "a fourth tile drawn", hangLimit, report);
            await Waiter.UntilAsync(() => m.IsAtRest, "the capsule at rest at its new width", hangLimit, report);
            report.Check("a new window widens the capsule by one tile, with no page swap, and the selection stays on its window",
                Math.Abs(m.CapsuleTargetWidth - (width + LookConstants.WidthItemPitch)) < 0.01 && m.PageChangeCount == swaps && m.ContentsItems[m.SelectedItem].WindowKey == 5002 && Math.Abs(m.DrawnWidth - m.CapsuleTargetWidth) < 1,
                $"{width:0.#} to {m.CapsuleTargetWidth:0.#}");

            // Closing the first window leaves the rest, and the selection stays on the window it was on.
            pretend.Windows = [.. pretend.Windows.Where(w => w.Handle != 5001)];
            world.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems.Count == 3 && rt.View.Contents.TileCount == 3, "three tiles again", hangLimit, report);
            report.Check("closing the first window leaves three tiles and the selection on a tile, the window it was on", m.SelectedItem >= 0 && m.SelectedItem < 3 && m.ContentsItems[m.SelectedItem].WindowKey == 5002, $"selected {m.SelectedItem}");

            // With no window of a terminal or an AI program the page says so in its one line.
            var kept = pretend.Windows;
            pretend.Windows = [];
            world.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems.Count == 0 && rt.View.Contents.TileCount == 0, "no tile left on the page", hangLimit, report);
            await Task.Delay(300);
            report.Check("with no terminal open the page's one line reads \"No terminal is open\"", rt.View.Contents.TitleText == PickItems.NoTerminal, $"\"{rt.View.Contents.TitleText}\"");
            pretend.Windows = kept;
            world.Raise();
            await Waiter.UntilAsync(() => m.ContentsItems.Count == 3, "the tiles back", hangLimit, report);

            await Task.Delay(120);
            await WriteSnapshotAsync();
            var back = Native.GetForegroundWindow() == before;
            report.Check("after the page the foreground window is the one from before, or foregroundGranted: false", back || !granted, granted ? (back ? "same" : "different") : "foregroundGranted: false");
            if (!granted) report.NeedsHumanVerify.Add("Windows did not let the self-test bring its stand-in window forward (foregroundGranted: false), so the foreground after the Terminals page was not checked: Tab to Terminals, a click on a tile, and see that its window comes to the front.");

            // Leaving the island ends the reading of the process list.
            c.HandleKey(0x1B); // Esc: the island leaves
            await Waiter.UntilAsync(() => !page.LaidOut, "the page no longer laid out after the island left", hangLimit, report);
            var reads = probe.Reads;
            await Task.Delay(2600);
            report.Check("once the island has left, the page is no longer laid out and the process list is not read again", !page.LaidOut && probe.Reads == reads, $"{probe.Reads - reads} readings after leaving");
        }
        finally
        {
            page.Dispose();
            other.Close();
        }

        _ = picksPath;
    }

    /// <summary>
    /// WORK-ORDER-11 section 3, with Island.Notify started as a helper on a pipe invented for this run and example inputs copied from the hooks page (an invented folder ending
    /// "island", an invented session id): the invented process list holds this very process, named as a helper, and the invented world leads it to the first terminal tile. A
    /// prompt gives that tile a working ring, a permission request a needs-you ring, the tool finishing a working ring again, Stop a finished ring (and the notice, as before), and
    /// SessionEnd no ring. Each ring is read from the tile's own picture: its colour on a circle at the ring's radius.
    /// </summary>
    private async Task HelperSessionsAsync(IslandRuntime rt, TerminalsPage page, InventedProbe probe, HelperSessions helpers)
    {
        var m = rt.Machine;
        var pipe = "island.selftest." + Guid.NewGuid().ToString("N");
        using var server = new Island.Agents.AgentPipeServer(pipe);
        server.MessageReceived += helpers.Apply;
        var notices = 0;
        server.NoticeReceived += _ => Interlocked.Increment(ref notices);
        report.Check("the island listens on a pipe name invented for this run (the sessions)", server.Start(), "invented name");

        probe.Facts = new TerminalProbeFacts(
            [new ConsoleWindowFact(7002, "PseudoConsoleWindow", Environment.ProcessId, 5001)],
            [new ProcessFact(Environment.ProcessId, 1, "invented-helper.exe")]);

        async Task<bool> SendAsync(string json, Func<Item, bool> expected, string what, string agent = "claude")
        {
            var run = AgentsStage.RunNotifyWith(["--agent", agent, pipe], json);
            if (run.Exit != 0) return false;
            await Waiter.UntilAsync(() =>
            {
                page.ProbeOnce();
                return expected(m.ContentsItems[0]);
            }, what, hangLimit, report);
            return expected(m.ContentsItems[0]);
        }

        static string Json(string hookEvent, string? extra = null) =>
            "{\"session_id\":\"invented-session\",\"cwd\":\"Q:\\\\Invented\\\\island\",\"hook_event_name\":\"" + hookEvent + "\"" + (extra is null ? "" : "," + extra) + "}";

        var working = await SendAsync(Json("UserPromptSubmit"), i => i.Ring == HelperState.Working, "the first tile's ring working");
        report.Check("a prompt gives the first tile a working ring, its project and the helper's name", working && m.ContentsItems[0].Title == "island" && m.ContentsItems[0].Subtitle.StartsWith("Claude Code", StringComparison.Ordinal), m.ContentsItems[0].Subtitle);
        report.Check("the working ring is drawn blue, an arc of the circle on a faint track", RingIs(m.ContentsItems[0], angle: 50, expectBlue: true), "read from the tile's own picture");

        var asks = await SendAsync(Json("PermissionRequest", "\"tool_name\":\"Bash\""), i => i.Ring == HelperState.NeedsYou, "the first tile's ring needs you");
        report.Check("a permission request gives a needs-you ring, orange, full and still", asks && RingIs(m.ContentsItems[0], angle: 200, expectOrange: true), "read from the tile's own picture");

        var back = await SendAsync(Json("PostToolUse", "\"tool_name\":\"Bash\""), i => i.Ring == HelperState.Working, "the first tile's ring working again");
        report.Check("the tool that was asked about finishing makes the ring working again", back, m.ContentsItems[0].Ring.ToString());

        var noticesBefore = Volatile.Read(ref notices);
        var finished = await SendAsync(Json("Stop"), i => i.Ring == HelperState.Finished, "the first tile's ring finished");
        report.Check("Stop gives a finished ring, green, full and still, and the notice is raised as before", finished && RingIs(m.ContentsItems[0], angle: 200, expectGreen: true) && Volatile.Read(ref notices) == noticesBefore + 1, $"{Volatile.Read(ref notices) - noticesBefore} notice");

        // The conversation ends but the helper's process is still in the list: the tile stays the helper's, with no ring (it was found by name).
        var ended = await SendAsync(Json("SessionEnd"), i => i.Ring == HelperState.Idle, "the first tile without a ring");
        report.Check("SessionEnd takes the ring away", ended && m.ContentsItems[0].Disc is not null && m.ContentsItems[0].Title == "Claude Code", m.ContentsItems[0].Title);

        // WORK-ORDER-11 section 5: Codex's example input, sent as Codex's own helper ("--agent codex"), moves a ring through the same chain: the process list names this process as Codex's program.
        probe.Facts = new TerminalProbeFacts(
            [new ConsoleWindowFact(7002, "PseudoConsoleWindow", Environment.ProcessId, 5001)],
            [new ProcessFact(Environment.ProcessId, 1, "invented-codex.exe")]);
        var codexWorking = await SendAsync(Json("UserPromptSubmit"), i => i.Ring == HelperState.Working && i.Subtitle.StartsWith("Codex", StringComparison.Ordinal), "the first tile's ring working for Codex", "codex");
        report.Check("Codex's prompt gives the first tile a working ring, its project and Codex's name, drawn blue", codexWorking && m.ContentsItems[0].Title == "island" && RingIs(m.ContentsItems[0], angle: 50, expectBlue: true), $"{m.ContentsItems[0].Title} / {m.ContentsItems[0].Subtitle}");
        var codexDone = await SendAsync(Json("Stop"), i => i.Ring == HelperState.Finished, "the first tile's ring finished for Codex", "codex");
        report.Check("Codex's Stop gives a finished ring, green, full and still", codexDone && RingIs(m.ContentsItems[0], angle: 200, expectGreen: true), m.ContentsItems[0].Ring.ToString());

        // The process is gone from the list: a plain terminal again, and what the sessions held goes with it.
        probe.Facts = new TerminalProbeFacts([new ConsoleWindowFact(7002, "PseudoConsoleWindow", Environment.ProcessId, 5001)], []);
        await Waiter.UntilAsync(() =>
        {
            page.ProbeOnce();
            return m.ContentsItems[0].Disc is null && m.ContentsItems[0].Title == "first shell";
        }, "the first tile a plain terminal again", hangLimit, report);
        report.Check("with the helper's process gone the tile is a plain terminal again, and the sessions are empty", m.ContentsItems[0].Title == "first shell" && helpers.Facts().Count == 0, $"{helpers.Facts().Count} sessions");
    }

    // The ring read from the tile's own picture: the colour on a circle at the ring's radius, at one angle (degrees from the top, clockwise), with the arc held at the top.
    private static bool RingIs(Item item, double angle, bool expectBlue = false, bool expectOrange = false, bool expectGreen = false)
    {
        var tile = new Island.App.Visuals.TileView(item, 0);
        tile.SetRingClock(0);
        const double pad = 8, zoom = 4;
        var d = LookConstants.ItemSize;
        var host = new System.Windows.Controls.Grid { Width = d + 2 * pad, Height = d + 2 * pad, Background = System.Windows.Media.Brushes.Transparent };
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        tile.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(tile);
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();
        var snap = Snapshot.Of(host, (int)(host.Width * zoom), (int)(host.Height * zoom), 96 * zoom);
        var centre = host.Width * zoom / 2;
        var radius = (d / 2 + TerminalRing.InnerGap + TerminalRing.Thickness / 2) * zoom;
        var theta = angle * Math.PI / 180;
        var pixel = snap.At((int)Math.Round(centre + radius * Math.Sin(theta)), (int)Math.Round(centre - radius * Math.Cos(theta)));
        static bool Near((byte R, byte G, byte B, byte A) p, int r, int g, int b) => p.A > 200 && Math.Abs(p.R - r) <= 12 && Math.Abs(p.G - g) <= 12 && Math.Abs(p.B - b) <= 12;
        return (expectBlue && Near(pixel, 76, 141, 255)) || (expectOrange && Near(pixel, 255, 176, 32)) || (expectGreen && Near(pixel, 53, 196, 106));
    }

    private static bool PressAndSettle(IslandController c, int key)
    {
        c.HandleKey(key);
        return true;
    }

    private async Task<bool> SearchFindsNothingAsync(IslandRuntime rt, IslandController c)
    {
        c.HandleText("s");
        c.HandleText("h");
        await Task.Delay(200);
        var tiles = rt.Search?.TileCount ?? -1;
        var services = SearchServices.Tiles("sh").Count;
        c.HandleKey(0x1B); // Esc clears the text
        await Task.Delay(120);
        c.HandleKey(0x1B); // and leaves search
        await Waiter.UntilAsync(() => rt.Search?.IsOpen != true && rt.Machine.IsAtRest, "search closed", hangLimit, report);
        return tiles == services;
    }

    /// <summary>A snapshot of the page as it stands: two terminals, one of them a helper's, and an AI program. Invented names only.</summary>
    private Task WriteSnapshotAsync()
    {
        Item Tile(string title, string second, string mark, double hue, long key, HelperColor? disc = null, HelperState ring = HelperState.Idle) =>
            new(title, second, mark, hue, WindowKey: key, Disc: disc, Ring: ring);
        var items = new[]
        {
            Tile("first shell", "terminal", "At", 215, 1),
            Tile("Alpha Helper", "project · Alpha Helper", "Al", 0, 2, new HelperColor(10, 200, 30)),
            Tile("Beta Assistant", "assistant window", "Be", 300, 3),
        };
        var (snap, _, _) = LooksStage.Render(items, Pages.Get(PageIds.Terminals));
        var path = Path.Combine(folder, "choices", "terminals-page.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        snap.SavePng(path);
        report.Check("the page was drawn into review/choices/terminals-page.png, invented names only", File.Exists(path), "a snapshot proves it draws, not that it looks right");

        // Five tiles of the states (WORK-ORDER-11 section 3): the first is selected and has a state (no white ring, its glow and its brightness stay), then working with the arc at a
        // fixed angle, needs you, finished, and a plain terminal without a ring.
        var helper = new HelperColor(217, 119, 60);
        var states = new[]
        {
            Tile("island", "Claude Code · needs you", "Is", 0, 11, helper, HelperState.NeedsYou),
            Tile("alpha", "Claude Code · working", "Al", 0, 12, helper, HelperState.Working),
            Tile("beta", "Claude Code · needs you", "Be", 0, 13, helper, HelperState.NeedsYou),
            Tile("gamma", "Claude Code · finished", "Ga", 0, 14, helper, HelperState.Finished),
            Tile("a shell", "terminal", "Sh", 215, 15),
        };
        var (statesSnap, _, _) = LooksStage.Render(states, Pages.Get(PageIds.Terminals));
        var statesPath = Path.Combine(folder, "choices", "terminal-states.png");
        statesSnap.SavePng(statesPath);
        report.Check("the five states were drawn into review/choices/terminal-states.png, invented names only", File.Exists(statesPath), "a snapshot proves it draws, not that it looks right");
        return Task.CompletedTask;
    }
}
