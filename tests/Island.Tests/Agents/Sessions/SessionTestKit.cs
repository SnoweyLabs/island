using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

/// <summary>Invented facts for the session tests: made-up folders, ids and programs only.</summary>
internal sealed class SessionTestKit
{
    public const string Claude = HelperNames.ClaudeCode;

    /// <summary>The island's own reading of the steady counter; a test moves it.</summary>
    public long Now = 10_000;

    public SessionTracker Tracker { get; }

    public SessionTestKit(params string[] helperExes) =>
        Tracker = new SessionTracker(() => Now, HelperSignalTable.Default, helperExes.Length == 0 ? ["claude.exe"] : helperExes);

    public static SessionMessage Msg(string ev, string kind = "", string sid = "s1", long? t = null, int[]? chain = null, string folder = @"Q:\Invented\Alpha") =>
        new(Claude, ev, kind, sid, t, folder, chain ?? [100, 50]);

    public ApplyResult Send(string ev, string kind = "", string sid = "s1", long? t = null, int[]? chain = null, string folder = @"Q:\Invented\Alpha") =>
        Tracker.Apply(Msg(ev, kind, sid, t, chain, folder));

    public SessionInfo Session(ApplyResult result) => Tracker.Get(result.SessionId) ?? throw new InvalidOperationException("no such session");

    public SessionState StateOf(ApplyResult result) => Session(result).State;

    public static ProcessFact P(int id, int parent, string exe) => new(id, parent, exe);

    public ReadingResult Read(IEnumerable<int>? aiOwners = null, params ProcessFact[] processes) =>
        Tracker.ApplyReading(processes, (aiOwners ?? []).ToList());

    /// <summary>The usual first reading: the helper (100) under a terminal program (50).</summary>
    public ReadingResult ReadUsual() => Read(null, P(100, 50, "claude.exe"), P(50, 1, "term.exe"));
}
