using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;
using Island.Core.Speed;

namespace Island.App;

/// <summary>
/// WORK-ORDER-12 section 1, started as <c>--selftest &lt;folder&gt; --speed</c>: how long the island takes to answer the person, in milliseconds, each figure the middle one of
/// five readings with their spread and every reading. No real key is pressed (the handlers a key reaches are called); the self-test's pretend world is used except for the
/// app's own start, the settings screen and the first icons, which go through the app's real start and the real icon reader (and read nothing that is written down: only times).
/// A watchdog on a thread of its own times how long the drawing thread did not answer during the movements; the late frames are counted as MotionStage counts them.
/// The table goes into review/perf.md under "How quickly it answers (WORK-ORDER-12) — &lt;label&gt;" (<c>--speed-label before|after</c>). It is not part of the ordinary self-test.
/// </summary>
internal sealed class SpeedStage(SelfTestReport report, TimeSpan hangLimit, string folder, string label, bool warmUp = false)
{
    private const int Samples = 5;
    private const int Enter = 0x0D, Escape = 0x1B, Tab = 0x09, DigitOne = 0x31;

    private readonly List<SpeedRow> _rows = [];
    private readonly Dictionary<string, List<double>> _silences = [];
    private readonly Dictionary<string, List<double>> _intervals = [];
    private UiWatchdog _dog = null!;
    private double _refreshMs;

    public async Task RunAsync()
    {
        var ui = Dispatcher.CurrentDispatcher;
        if (warmUp) await IslandWarmUp.RunAsync(ui); // what the app does after a silent start, before the person presses the key (the settings' own part is done with the first app start below)
        _dog = new UiWatchdog(work => ui.BeginInvoke(DispatcherPriority.Send, work));
        _dog.Start();
        try
        {
            await IslandRowsAsync();
            await AppRowsAsync();
            await IconRowAsync();
        }
        finally
        {
            _dog.Dispose();
        }

        AddWatchdogRows();
        AddColdRows();
        Write();
        report.Check("every row of the speed table was measured", _rows.All(r => r.Readings.Count > 0), $"{_rows.Count} rows");
        report.Check("the table was written to perf.md", File.Exists(Path.Combine(folder, "perf.md")), "records, not gates");
    }

    // ---- the island on the self-test's pretend world: rows 2 to 6 and 9 -----------------------------------------------------------------------------------------

    private async Task IslandRowsAsync()
    {
        var pretend = new PretendWorld
        {
            Windows = [new OpenWindow(9001, "alpha.exe", null, "invented", 0), new OpenWindow(9002, "tunes.exe", null, "invented", 1)],
        };
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var store = new PickStore(
        [
            Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForProgram("Beta", PageIds.Apps, "beta.exe", null), Pick.ForProgram("Tunes", PageIds.Apps, "tunes.exe", null),
            Pick.ForProgram("Tutorial", PageIds.Apps, "tutorial.exe", null), Pick.ForProgram("Delta", PageIds.Apps, "delta.exe", null),
            Pick.ForProgram("Alpha Player", PageIds.Media, "alpha-player.exe", null), Pick.ForSite("Example Site", "example.org", PageIds.Media),
            Pick.ForFolder("Downloads", PageIds.Folders),
        ]);
        var book = new PickBook(store, null, canSave: false);
        var pages = new PickPages(() => book.Store, world, synchronousIcons: true);
        using var rt = new IslandRuntime(3600, pages, book); // the idle time is longer than the whole run
        rt.Keyboard.PretendGranted = true;
        rt.Show();
        await Task.Delay(300);
        var c = rt.Controller;
        var m = c.Machine;
        c.SetMode(Mode.Focus, instant: true);

        var keyReturns = new List<double>();
        var firstFrame = new List<double>();
        var atRest = new List<double>();
        var enterOpen = new List<double>();
        var clickClosed = new List<double>();
        var typed = new List<double>();
        var digit = new List<double>();
        var tab = new List<double>();
        var secondRow = new List<double>();

        // 2 and 3: the main key to the first frame drawn after it, and to the capsule at rest.
        for (var i = 0; i < Samples; i++)
        {
            await HideAsync(c, m);
            var frames = c.FramesDrawn;
            Begin("2", c);
            var t0 = Stopwatch.GetTimestamp();
            c.MainKey();
            keyReturns.Add(Ms(t0));
            firstFrame.Add(await UntilAsync(() => c.FramesDrawn > frames, t0));
            atRest.Add(await UntilAsync(() => m.IsAtRest, t0));
            End("2", c); // the watchdog and the late frames cover the whole movement, to rest
            await Task.Delay(400);
        }

        // 4: Enter on an open pick, and a click on a closed one, to the moment the outside door is asked. (The door's reply is not waited for.)
        for (var i = 0; i < Samples; i++)
        {
            await Waiter.UntilAsync(() => m.IsAtRest, "the island open for the click", hangLimit, report);
            var calls = recording.Calls.Count;
            var t0 = Stopwatch.GetTimestamp();
            c.HandleKey(Enter);
            enterOpen.Add(recording.Calls.Count > calls ? Ms(t0) : double.NaN);
            await Task.Delay(150);
            calls = recording.Calls.Count;
            t0 = Stopwatch.GetTimestamp();
            c.TileClicked(1); // Beta: closed, so the click asks the door to start it
            clickClosed.Add(recording.Calls.Count > calls ? Ms(t0) : double.NaN);
            await Task.Delay(150);
        }

        // 5: a typed letter to the search results laid out. 6: a digit, and Tab, to the new page's row laid out. Second row opening.
        for (var i = 0; i < Samples; i++)
        {
            if (m.PageId != PageIds.Apps) c.PageKey(PageIds.Apps); // every round starts on the Apps page, which holds the picks the letter finds
            await Waiter.UntilAsync(() => m.IsAtRest && !m.SearchOpen && m.ContentsPageId == PageIds.Apps, "the Apps page at rest before the letter", hangLimit, report);
            Begin("5", c);
            var t0 = Stopwatch.GetTimestamp();
            c.HandleText("t");
            typed.Add(await UntilAsync(() => m.SearchOpen && rt.View.Search.FieldText == "t" && rt.View.Search.TilesDrawn > 0, t0));
            await UntilAsync(() => m.IsAtRest, t0);
            End("5", c);
            c.HandleKey(Escape);
            c.HandleKey(Escape);
            await Waiter.UntilAsync(() => !m.SearchOpen && m.IsAtRest, "the page back after search", hangLimit, report);

            var others = Pages.BuiltIn.Select((p, at) => (Page: p, At: at)).Where(x => x.Page.Id != PageIds.Apps).ToList(); // the Apps page is where every round starts
            var (page, index) = others[i % others.Count];
            var from = m.PageId;
            Begin("6", c);
            t0 = Stopwatch.GetTimestamp();
            c.HandleKey(DigitOne + index);
            digit.Add(m.PageId == page.Id && from != page.Id ? await UntilAsync(() => PageLaidOut(rt, m), t0) : double.NaN);
            await UntilAsync(() => m.IsAtRest, t0);
            End("6", c);

            from = m.PageId;
            Begin("6", c);
            t0 = Stopwatch.GetTimestamp();
            c.HandleKey(Tab);
            tab.Add(m.PageId != from ? await UntilAsync(() => PageLaidOut(rt, m), t0) : double.NaN);
            await UntilAsync(() => m.IsAtRest, t0);
            End("6", c);

            c.PageKey(PageIds.Apps);
            await Waiter.UntilAsync(() => m.IsAtRest && m.ContentsPageId == PageIds.Apps && PageLaidOut(rt, m), "the Apps page for the second row", hangLimit, report);
            await Task.Delay(200);
            Begin("2row", c);
            t0 = Stopwatch.GetTimestamp();
            c.TogglePlusRow();
            secondRow.Add(await UntilAsync(() => m.SecondRowOpen && rt.View.Contents.SecondRowIsBuilt && m.IsAtRest, t0));
            End("2row", c);
            c.TogglePlusRow();
            await Waiter.UntilAsync(() => !m.SecondRowOpen && m.IsAtRest, "the second row closed", hangLimit, report);
        }

        c.FrameIntervals = null;
        var all = _intervals.Values.SelectMany(v => v).OrderBy(x => x).ToList();
        _refreshMs = all.Count == 0 ? 0 : all[all.Count / 2];

        _rows.Add(SpeedRow.Of("2a", "main key: the key handler returns", keyReturns, note: "the first drawing is done inside it"));
        _rows.Add(SpeedRow.Of("2", "main key to the first frame drawn after it", firstFrame));
        _rows.Add(SpeedRow.Of("3", "main key to the capsule at rest", atRest, note: "a record only: the length of the movement is approved"));
        _rows.Add(SpeedRow.Of("4", "Enter on an open pick to the moment the outside door is asked", enterOpen));
        _rows.Add(SpeedRow.Of("4b", "a click on a closed pick to the moment the outside door is asked", clickClosed));
        _rows.Add(SpeedRow.Of("5", "a typed letter to the search results laid out", typed));
        _rows.Add(SpeedRow.Of("6", "a page's digit to the new page's row laid out", digit));
        _rows.Add(SpeedRow.Of("6b", "Tab to the new page's row laid out", tab));
        _rows.Add(SpeedRow.Of("6c", "the + tile to the second row built and at rest", secondRow));
    }

    // The swap is over (the contents are the new page's) and the row drawn is that page's row: as many tiles as the page has, a page with none included.
    private static bool PageLaidOut(IslandRuntime rt, IslandMachine m) =>
        m.ContentsPageId == m.PageId && rt.View.Contents.TileCount == m.ContentsItems.Count;

    // ---- the app's own start, the settings screen and the memory: rows 1, 7 and 10 --------------------------------------------------------------------------------

    private async Task AppRowsAsync()
    {
        var starts = new List<double>();
        var settings = new List<double>();
        for (var i = 0; i < Samples; i++)
        {
            var dir = Path.Combine(Path.GetTempPath(), "island-speed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var files = new AppFiles(dir);
                var t0 = Stopwatch.GetTimestamp();
                using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
                starts.Add(host is null ? double.NaN : Ms(t0));
                if (host is null) continue;
                if (warmUp && i == 0) await host.WarmUpAsync(); // the app does this five seconds after a silent start
                await Task.Delay(200);

                Begin("7", host.Runtime.Controller);
                t0 = Stopwatch.GetTimestamp();
                host.Screen.Open();
                settings.Add(await UntilAsync(() => host.Screen.IsOpen, t0));
                await UntilAsync(() => host.Screen.Phase == SettingsScreen.SettingsScreenPhase.Open, t0);
                End("7", host.Runtime.Controller);
                await Task.Delay(300);
                host.Screen.Close();
                await Waiter.UntilAsync(() => !host.Screen.IsOpen, "the settings screen closed", hangLimit, report);
            }
            finally
            {
                TryDelete(dir);
            }
        }

        _rows.Add(SpeedRow.Of("1", "the app's own start to ready for the main key (AppHost.Start returns, keys registered)", starts, note: "the first reading is the cold one: it includes first-time code; the process's own start-up (the runtime loading) is not in it"));
        _rows.Add(SpeedRow.Of("7", "the settings asked for to their first frame", settings, note: "the screen is built on the drawing thread; the first reading of the five is the cold one"));

        await MemoryAsync();
    }

    private async Task MemoryAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "island-speed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var host = AppHost.Start(new AppFiles(dir), quit: () => { }, summonOnStart: false);
            if (host is null) return;
            var c = host.Runtime.Controller;
            await Task.Delay(1500);
            var hidden = MemoryMb();
            c.MainKey();
            await Waiter.UntilAsync(() => c.Machine.IsAtRest, "the island open for the memory reading", hangLimit, report);
            await Task.Delay(500);
            var open = MemoryMb();
            c.MainKey();
            await Waiter.UntilAsync(() => c.Machine.Phase == IslandPhase.Hidden, "the island hidden again", hangLimit, report);
            await Task.Delay(500);
            var again = MemoryMb();
            _rows.Add(new SpeedRow("10a", "memory held: working set, hidden before the first summon", hidden.Working, 0, [hidden.Working], "MB", $"private {hidden.Private:0.0} MB; one reading, after three garbage collections"));
            _rows.Add(new SpeedRow("10b", "memory held: working set, open", open.Working, 0, [open.Working], "MB", $"private {open.Private:0.0} MB"));
            _rows.Add(new SpeedRow("10c", "memory held: working set, hidden again", again.Working, 0, [again.Working], "MB", $"private {again.Private:0.0} MB"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static (double Working, double Private) MemoryMb()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        using var p = Process.GetCurrentProcess();
        p.Refresh();
        return (p.WorkingSet64 / 1048576.0, p.PrivateMemorySize64 / 1048576.0);
    }

    // ---- the first icons, with the real reader on Windows' own explorer.exe and the app's own file: row 8 --------------------------------------------------------

    private async Task IconRowAsync()
    {
        var own = Environment.ProcessPath;
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (own is null || !File.Exists(explorer))
        {
            _rows.Add(new SpeedRow("8", "the first icons asked for to drawn (real reader)", double.NaN, 0, [], Note: "not measured: the files were not there"));
            return;
        }

        var times = new List<double>();
        for (var i = 0; i < Samples; i++)
        {
            using var real = RealWorld.Create(listenForAddon: false); // a reader of its own each time: nothing it remembers from the last reading
            // What IconCache does for a tile, for two files: the real reader on its own thread, then the disc and the grey copy made, all off the drawing thread.
            var t0 = Stopwatch.GetTimestamp();
            var read = Task.Run(() =>
            {
                foreach (var path in new[] { explorer, own })
                    if (real.World.Icons.PlaceIcon(path) is { } icon) RoundIcons.Of(icon);
                return Ms(t0);
            });
            var done = await Task.WhenAny(read, Task.Delay(hangLimit));
            times.Add(done == read ? read.Result : double.NaN);
        }

        _rows.Add(SpeedRow.Of("8", "the first icons asked for to read and made into their discs (real reader, Windows' own explorer.exe and the app's own file)",
            [.. times], note: "off the drawing thread; a new reader each time (Windows' own icon cache is warm after the first reading); no icon is written anywhere"));
        report.Info["speedIconsReadingNote"] = "icons of explorer.exe and the app's own file only; nothing else is read or kept";
    }

    // ---- the watchdog and the late frames: row 9 ----------------------------------------------------------------------------------------------------------------

    private void AddWatchdogRows()
    {
        var names = new Dictionary<string, string>
        {
            ["2"] = "the main key", ["5"] = "a typed letter", ["6"] = "a page change", ["7"] = "the settings opening", ["2row"] = "the second row opening",
        };
        foreach (var (id, what) in names)
        {
            if (!_silences.TryGetValue(id, out var silences)) continue;
            var intervals = _intervals.GetValueOrDefault(id) ?? [];
            var late = _refreshMs > 0 ? intervals.Count(x => x > 2 * _refreshMs) : 0;
            var longest = intervals.Count == 0 ? 0 : intervals.Max();
            _rows.Add(SpeedRow.Of("9 " + id, $"the longest time the drawing thread did not answer during {what}", silences,
                note: $"late frames {late} of {intervals.Count} (later than twice the median interval of {_refreshMs:0.0} ms); the longest frame interval {longest:0.0} ms"));
        }
    }

    /// <summary>
    /// The first reading of the five is the one a person meets the first time after the app starts (the code is cold); the middle of five hides it. It gets rows of its own, one for each figure a
    /// person waits for, so that what is done about the cold start can be seen.
    /// </summary>
    private void AddColdRows()
    {
        foreach (var (id, what) in new[] { ("2", "main key to the first frame, the first time"), ("5", "a typed letter to the search results, the first time"), ("7", "the settings asked for to their first frame, the first time"), ("9 2", "the longest silence of the drawing thread at the first main key"), ("9 7", "the longest silence of the drawing thread at the first opening of the settings") })
        {
            var row = _rows.FirstOrDefault(r => r.Id == id);
            if (row is { Readings.Count: > 0 }) _rows.Add(new SpeedRow(id + " cold", what, row.Readings[0], 0, [row.Readings[0]], "ms", "the first of the five readings"));
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------------------------

    private void Begin(string id, IslandController c)
    {
        if (!_intervals.ContainsKey(id)) _intervals[id] = [];
        c.FrameIntervals = _intervals[id];
        _dog.Reset();
    }

    private void End(string id, IslandController c)
    {
        if (!_silences.ContainsKey(id)) _silences[id] = [];
        _silences[id].Add(_dog.LongestSilenceMs);
        c.FrameIntervals = null;
    }

    private async Task HideAsync(IslandController c, IslandMachine m)
    {
        if (m.Phase == IslandPhase.Hidden) return;
        c.ShowHide();
        await Waiter.UntilAsync(() => m.Phase == IslandPhase.Hidden, "the island hidden between readings", hangLimit, report);
        await Task.Delay(300);
    }

    private static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    /// <summary>The milliseconds from <paramref name="since"/> to the first moment, checked on every frame, that the condition holds; NaN after the hang limit.</summary>
    private Task<double> UntilAsync(Func<bool> done, long since)
    {
        if (done()) return Task.FromResult(Ms(since));
        var tcs = new TaskCompletionSource<double>();
        var clock = Stopwatch.StartNew();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (done()) Finish(Ms(since));
            else if (clock.Elapsed > hangLimit) Finish(double.NaN);
        };
        void Finish(double value)
        {
            CompositionTarget.Rendering -= handler;
            tcs.TrySetResult(value);
        }

        CompositionTarget.Rendering += handler;
        return tcs.Task;
    }

    private void Write()
    {
        var inv = CultureInfo.InvariantCulture;
        var measured = $"Measured by `--selftest <folder> --speed` on {DateTimeOffset.Now:yyyy-MM-dd}, one laptop, Release build. Each figure is the middle one of {Samples} readings, in milliseconds, with the spread (highest minus lowest) and every reading, in the order they were taken. "
                       + "No real key was pressed: the handlers a key reaches were called. Rows 2 to 6 and 9 are on the self-test's pretend world with the island in Focus; rows 1, 7 and 10 go through the app's real start; row 8 uses the real icon reader on two files only. "
                       + "Other programs were running (Dan's own copy of the island among them), so only differences larger than the spread mean anything. **A record, not a gate; one run on one laptop is an anecdote.**";
        var text = SpeedTable.Markdown(label, measured, _rows);
        var path = Path.Combine(folder, "perf.md");
        var existing = File.Exists(path) ? File.ReadAllText(path) : null;
        File.WriteAllText(path, PerfSections.Upsert(existing, text));
        foreach (var r in _rows) Console.WriteLine($"{r.Id}: {r.Median.ToString("0.0", inv)} {r.Unit} (spread {r.Spread.ToString("0.0", inv)})");
        report.Info["speed"] = _rows.Select(r => new { id = r.Id, what = r.What, median = Json(r.Median), spread = Json(r.Spread), readings = r.Readings.Select(Json).ToList(), unit = r.Unit }).ToList();
    }

    // A reading that timed out is NaN, which JSON cannot hold: it is written as null.
    private static double? Json(double v) => double.IsFinite(v) ? Math.Round(v, 2) : null;

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a temporary folder that stays is not a failure
        }
    }
}
