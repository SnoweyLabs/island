namespace Island.Core;

public enum OutsideKind
{
    StartProgram,
    OpenFolder,
    OpenAddress,
    BringForward,
    MediaCommand,
    TabCommand,
    OpenFile,
    StartOwnCopy,

    // Added for NEXT-PLAN.md (work orders 6 and 7). Each is refused under the self-test until the section that
    // needs it says exactly when a window of the self-test's own may be touched.
    WriteStartupValue,
    PlaySound,
    EditAgentSettings,
    CloseWindow,

    /// <summary>WORK-ORDER-10 §3: Windows' own window for choosing a file or a folder, opened for the person in front of the settings screen. Refused under the self-test.</summary>
    ChoosePlace,
}

/// <summary>
/// The one switch every action on the outside world asks before acting. In normal running it allows
/// everything. Under the self-test it refuses everything except bringing forward or closing a window of the
/// self-test's own process and starting the self-test's own second copy, and it counts what it refused.
/// A refusal is counted, never logged with names: only kinds and numbers exist here.
/// </summary>
public sealed class OutsideGate
{
    private readonly int _ownProcessId;
    private readonly Dictionary<OutsideKind, int> _refused = [];
    private int _allowed;

    public OutsideGate(bool selfTest, int? ownProcessId = null)
    {
        SelfTest = selfTest;
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
    }

    /// <summary>The gate the app asks. Normal until the self-test replaces it.</summary>
    public static OutsideGate Current { get; set; } = new(selfTest: false);

    public bool SelfTest { get; }

    public int AllowedCount => Volatile.Read(ref _allowed);

    public int RefusedTotal
    {
        get
        {
            lock (_refused) return _refused.Values.Sum();
        }
    }

    public int Refused(OutsideKind kind)
    {
        lock (_refused) return _refused.GetValueOrDefault(kind);
    }

    /// <param name="kind">What is about to be done.</param>
    /// <param name="targetProcessId">The process that owns the window being brought forward, when it is known; 0 or less when it is not.</param>
    public bool Allow(OutsideKind kind, int targetProcessId = 0)
    {
        var ok = !SelfTest
                 || (kind is OutsideKind.BringForward or OutsideKind.CloseWindow && targetProcessId == _ownProcessId)
                 || kind == OutsideKind.StartOwnCopy;
        if (ok)
        {
            Interlocked.Increment(ref _allowed);
            return true;
        }

        lock (_refused) _refused[kind] = _refused.GetValueOrDefault(kind) + 1;
        return false;
    }

    /// <summary>The counts by kind, for selftest.json: numbers only.</summary>
    public IReadOnlyDictionary<string, int> RefusedCounts()
    {
        lock (_refused) return _refused.ToDictionary(p => p.Key.ToString(), p => p.Value);
    }
}

/// <summary>An <see cref="IOutsideActions"/> that only records what it was asked, for tests and for screens built before the real thing.</summary>
public sealed class RecordingOutside : IOutsideActions
{
    private readonly List<string> _calls = [];

    /// <summary>One line per call, e.g. "bring:42", "start:program:spotify", "folder:Downloads", "site:youtube.com".</summary>
    public IReadOnlyList<string> Calls => _calls;

    public bool BringForward(long windowHandle)
    {
        _calls.Add($"bring:{windowHandle}");
        return true;
    }

    public bool StartProgram(Pick pick)
    {
        _calls.Add($"start:{pick.Id}");
        return true;
    }

    public bool OpenFolder(string knownFolder)
    {
        _calls.Add($"folder:{knownFolder}");
        return true;
    }

    public bool OpenSite(string host)
    {
        _calls.Add($"site:{host}");
        return true;
    }

    /// <summary>Records only that a hand-added folder was opened, never its place.</summary>
    public bool OpenFolderAt(string realPath)
    {
        _calls.Add("folder-at");
        return true;
    }

    /// <summary>Records only that a hand-added file was opened, never its place.</summary>
    public bool OpenFile(string realPath)
    {
        _calls.Add("file");
        return true;
    }

    /// <summary>Records the service and how long the text was, never the text.</summary>
    public bool OpenSearch(SearchService service, string text)
    {
        _calls.Add($"search:{service}:{text.Length}");
        return true;
    }
}
