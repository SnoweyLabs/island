using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: threads. The readers that change what the drawing reads (the session book, the tab model, the target cache) hit from several threads at once while another
/// thread reads them the way the drawing does. No exception, no broken invariant, no hang.
/// </summary>
public class ThreadEdgeTests
{
    private static void Run(int seconds, params Action<CancellationToken>[] workers)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var tasks = workers.Select(w => Task.Factory.StartNew(() => w(stop.Token), TaskCreationOptions.LongRunning)).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(seconds + 15)), "a worker did not stop");
        foreach (var t in tasks) t.GetAwaiter().GetResult();
    }

    [Fact]
    public void SessionBook_Writers_Readings_And_A_Drawing_Reader_At_Once_Keep_The_Limit_And_The_Ids()
    {
        long clock = 0;
        var book = new SessionTracker(() => Interlocked.Increment(ref clock), AgentSignalTables.All, ["claude.exe", "codex.exe"]);
        string[] events = ["SessionStart", "UserPromptSubmit", "PostToolUse", "Stop", "SessionEnd", "Notification", "PermissionRequest", "Bogus"];
        Action<CancellationToken> writer(int seed) => token =>
        {
            var rng = new Random(seed);
            while (!token.IsCancellationRequested)
            {
                var chain = Enumerable.Range(0, rng.Next(0, 5)).Select(_ => rng.Next(100, 220)).ToArray();
                book.Apply(new SessionMessage(rng.Next(2) == 0 ? "claude" : "codex", events[rng.Next(events.Length)], rng.Next(3) == 0 ? "permission_prompt" : "", "s" + rng.Next(150), rng.Next(2) == 0 ? null : rng.Next(0, 1_000_000), "Q:\\Invented\\Alpha" + rng.Next(5), chain));
            }
        };
        Action<CancellationToken> reader = token =>
        {
            var rng = new Random(99);
            while (!token.IsCancellationRequested)
            {
                var list = Enumerable.Range(100, 120).Where(_ => rng.Next(3) > 0).Select(id => new Island.Core.Agents.Sessions.ProcessFact(id, rng.Next(100, 220), rng.Next(2) == 0 ? "claude.exe" : "shell.exe")).Concat(Enumerable.Range(0, 3).Select(_ => new Island.Core.Agents.Sessions.ProcessFact(150, 100, "claude.exe"))).ToList();
                book.ApplyReading(list, rng.Next(2) == 0 ? [120, 121] : []);
                book.FoundByName("claude", new Island.Core.Agents.Sessions.ProcessFact(rng.Next(100, 220), 100, "claude.exe"));
            }
        };
        Action<CancellationToken> drawing = token =>
        {
            while (!token.IsCancellationRequested)
            {
                var sessions = book.Sessions();
                Assert.Equal(sessions.Count, sessions.Select(s => s.Id).Distinct().Count());
                Assert.True(book.Count <= SessionLimits.MaxSessions, book.Count.ToString());
                foreach (var s in sessions) _ = book.Get(s.Id);
            }
        };
        Run(4, writer(1), writer(2), writer(3), writer(4), reader, drawing);
        Assert.True(book.Count <= SessionLimits.MaxSessions);
    }

    [Fact]
    public void TabModel_Many_Connections_Writing_While_The_Drawing_Reads_The_List()
    {
        var model = new TabModel();
        for (var c = 0; c < 4; c++) model.Open("c" + c, "profile" + c);
        Action<CancellationToken> writer(int c) => token =>
        {
            var rng = new Random(c);
            while (!token.IsCancellationRequested)
            {
                var id = rng.Next(0, 60);
                AddonMessage message = rng.Next(6) switch
                {
                    0 => new SnapshotMessage([.. Enumerable.Range(0, rng.Next(0, 30)).Select(i => new TabObject(i, 1, "T" + i, "example.org", rng.Next(2) == 0, i == 3, false, false))]),
                    1 => new TabRemovedMessage(id),
                    2 => new TabActivatedMessage(id, 1),
                    3 => new MediaMessage(id, new TabMedia("t", null, PlaybackState.Playing, 1, 100, 1, null)),
                    _ => new TabMessage(new TabObject(id, 1, "T", rng.Next(2) == 0 ? "youtube.com" : "example.org", rng.Next(2) == 0, rng.Next(3) == 0, false, false)),
                };
                model.Apply("c" + c, message);
                if (rng.Next(200) == 0)
                {
                    model.Close("c" + c);
                    model.Open("c" + c, "profile" + c);
                }
            }
        };
        Action<CancellationToken> drawing = token =>
        {
            while (!token.IsCancellationRequested)
            {
                var tabs = model.Tabs;
                Assert.Equal(tabs.Count, tabs.Select(t => t.Key).Distinct().Count());
                Assert.True(tabs.Zip(tabs.Skip(1)).All(p => p.First.LastActiveOrder >= p.Second.LastActiveOrder));
                _ = model.LastStarted(h => h == "youtube.com");
                _ = model.ConnectionCount;
            }
        };
        Run(3, writer(0), writer(1), writer(2), writer(3), drawing);
    }

    [Fact]
    public void TargetCache_Records_Refreshes_And_Reads_At_Once_And_Never_Exceeds_Its_Size()
    {
        var cache = new PickTargetCache();
        var picks = Enumerable.Range(0, 2500).Select(i => new Pick("file:f" + i, PickKind.File, "F" + i, "apps", Location: "C:\\Invented\\f" + i)).ToList();
        Action<CancellationToken> recorder = token =>
        {
            var rng = new Random(1);
            while (!token.IsCancellationRequested) cache.Record(picks[rng.Next(picks.Count)], null, rng.Next(2) == 0);
        };
        Action<CancellationToken> reader = token =>
        {
            var rng = new Random(2);
            while (!token.IsCancellationRequested)
            {
                _ = cache.Presence(picks[rng.Next(picks.Count)], null);
                Assert.True(cache.Count <= PickTargetCache.MaxEntries);
            }
        };
        Action<CancellationToken> refresher = token =>
        {
            while (!token.IsCancellationRequested) cache.Refresh(new FlakyProbe(), picks.Take(40), null);
        };
        Run(3, recorder, recorder, reader, refresher);
        Assert.True(cache.Count <= PickTargetCache.MaxEntries);
    }

    private sealed class FlakyProbe : IPickTargetProbe
    {
        private int _n;

        public bool Exists(PickKind kind, string path)
        {
            var n = Interlocked.Increment(ref _n);
            if (n % 7 == 0) throw new IOException("invented");
            if (n % 11 == 0) Thread.Sleep(20);
            return n % 2 == 0;
        }
    }

    [Fact]
    public void NowPlaying_Updates_And_Reads_From_Several_Threads_Never_Break()
    {
        var playing = new NowPlaying();
        var policy = new NowPlayingPolicy(["youtube.com"], true, true);
        Action<CancellationToken> updater(int seed) => token =>
        {
            var rng = new Random(seed);
            while (!token.IsCancellationRequested)
            {
                var sessions = Enumerable.Range(0, rng.Next(0, 4)).Select(i => new MediaSessionInfo("s" + i, "app" + i + ".exe", false, "T" + i, "A", rng.Next(2) == 0 ? PlaybackState.Playing : PlaybackState.Paused, rng.NextDouble() * 100, 100)).ToList();
                playing.Update(DateTimeOffset.UtcNow, sessions, [], false, policy);
            }
        };
        Action<CancellationToken> reader = token =>
        {
            while (!token.IsCancellationRequested)
            {
                _ = playing.Current(DateTimeOffset.UtcNow);
                _ = playing.PlanFor(MediaCommand.PlayPause);
            }
        };
        Run(2, updater(1), updater(2), reader);
    }
}
