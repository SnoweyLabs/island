using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

/// <summary>Random storms of messages, process readings and names: whatever the order, the book keeps its invariants.</summary>
public class SessionStormTests
{
    private static readonly string[] Events =
    [
        "SessionStart", "UserPromptSubmit", "PermissionRequest", "Notification", "PostToolUse", "PostToolUseFailure",
        "Stop", "StopFailure", "SessionEnd", "PreToolUse", "Unknown",
    ];

    private static readonly string[] Kinds = ["", "permission_prompt", "idle_prompt", "Bash", "Edit", "startup"];
    private static readonly string[] Sids = ["", "a", "b", "c", "d", "e"];
    private static readonly string[] Exes = ["claude.exe", "node.exe", "sh.exe", "term.exe"];

    [Fact]
    public void A_Hundred_Random_Storms_Keep_The_Invariants()
    {
        var states = new HashSet<SessionState>();
        var shownSeen = 0;
        for (var seed = 0; seed < 100; seed++) shownSeen += Storm(seed, states);

        // The storms are not empty: sessions were shown, in every state.
        Assert.True(shownSeen > 1000, $"{shownSeen}");
        Assert.Equal(4, states.Count);
    }

    private static int Storm(int seed, HashSet<SessionState> states)
    {
        var shownSeen = 0;
        var rng = new Random(seed);
        long now = 1_000;
        var tracker = new SessionTracker(() => now, HelperSignalTable.Default, ["claude.exe"]);
        var aiOwners = new List<int>();

        for (var step = 0; step < 300; step++)
        {
            now += rng.Next(0, 50);
            var before = tracker.Sessions().ToDictionary(s => s.Id);
            var roll = rng.Next(10);
            ApplyResult? result = null;
            if (roll < 7)
            {
                var time = rng.Next(4) switch { 0 => (long?)null, 1 => now + rng.Next(1, 100_000), _ => now - rng.Next(0, 300) };
                var chain = Enumerable.Range(0, rng.Next(0, 5)).Select(_ => rng.Next(1, 13)).Distinct().ToArray();
                var message = new SessionMessage("claude", Pick(rng, Events), Pick(rng, Kinds), Pick(rng, Sids), time, @"Q:\Invented\Alpha", chain);
                result = tracker.Apply(message);
            }
            else if (roll < 9)
            {
                aiOwners = Enumerable.Range(1, 12).Where(_ => rng.Next(6) == 0).ToList();
                var processes = Enumerable.Range(1, 12).Where(_ => rng.Next(4) != 0)
                    .Select(id => new ProcessFact(id, rng.Next(1, 13), Pick(rng, Exes))).ToList();
                tracker.ApplyReading(processes, aiOwners);
            }
            else
            {
                var id = rng.Next(1, 13);
                tracker.FoundByName("claude", new ProcessFact(id, rng.Next(1, 13), "claude.exe"), [id, rng.Next(1, 13)]);
            }

            Check(tracker, aiOwners, before, result);
            foreach (var s in tracker.Sessions())
            {
                shownSeen++;
                states.Add(s.State);
            }
        }

        return shownSeen;
    }

    private static void Check(SessionTracker tracker, List<int> aiOwners, Dictionary<long, SessionInfo> before, ApplyResult? result)
    {
        Assert.InRange(tracker.Count, 0, SessionLimits.MaxSessions);
        var shown = tracker.Sessions();
        Assert.Equal(shown.Count, shown.Select(s => s.Id).Distinct().Count());
        foreach (var s in shown)
        {
            Assert.True(Enum.IsDefined(s.State));
            Assert.True(s.ToolName.Length == 0 || s.State == SessionState.NeedsYou, "a tool name only while it needs you");
            Assert.True(s.Process is not null || s.Chain.Any(aiOwners.Contains), "shown means a process or a window");
            Assert.NotNull(tracker.Get(s.Id));
        }

        var alone = shown.Where(s => s.Process is not null && !aiOwners.Contains(s.Process.Id)).GroupBy(s => (s.Helper, s.Process!.Id));
        Assert.All(alone, g => Assert.Single(g)); // one helper process holds one session

        if (result is { Outcome: ApplyOutcome.Dropped })
            foreach (var s in shown.Where(s => before.ContainsKey(s.Id)))
            {
                Assert.Equal(before[s.Id].State, s.State);
                Assert.Equal(before[s.Id].ToolName, s.ToolName);
            }
    }

    private static string Pick(Random rng, string[] values) => values[rng.Next(values.Length)];

    [Fact]
    public void Apply_And_Readings_From_Many_Threads_Do_Not_Break_The_Book()
    {
        var now = 1_000L;
        var tracker = new SessionTracker(() => Interlocked.Increment(ref now), HelperSignalTable.Default, ["claude.exe"]);
        var errors = new List<Exception>();

        void Work(int seed)
        {
            try
            {
                var rng = new Random(seed);
                for (var i = 0; i < 500; i++)
                {
                    if (i % 5 == 0)
                        tracker.ApplyReading([new ProcessFact(rng.Next(1, 9), 1, "claude.exe")], []);
                    else
                        tracker.Apply(new SessionMessage("claude", Pick(rng, Events), Pick(rng, Kinds), Pick(rng, Sids), null, "", [rng.Next(1, 9)]));
                    _ = tracker.Sessions();
                }
            }
            catch (Exception e)
            {
                lock (errors) errors.Add(e);
            }
        }

        var threads = Enumerable.Range(0, 4).Select(i => new Thread(() => Work(i))).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Empty(errors);
        Assert.InRange(tracker.Count, 0, SessionLimits.MaxSessions);
    }
}
