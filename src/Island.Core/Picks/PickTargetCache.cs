namespace Island.Core;

/// <summary>What is known about whether the place of a thing added by hand is still there.</summary>
public enum TargetPresence
{
    /// <summary>Nobody asked yet. It is drawn as present: a tile never waits for the disk and never turns grey on a guess.</summary>
    NotAsked,
    Present,
    Missing,
}

/// <summary>
/// The one question the logic asks of the disk, and only through the app: is there a thing of this kind at this place. The app's answer
/// reads the disk (File.Exists, Directory.Exists) and is called off the drawing thread, never while a row is laid out. The path is a REAL one,
/// in memory only. For a shortcut the place is the shortcut's own file; whether its target is there is the app's business.
/// </summary>
public interface IPickTargetProbe
{
    bool Exists(PickKind kind, string path);
}

/// <summary>What the logic needs from the app besides what is open: the profile folder, and the cache of answers.</summary>
public sealed record PickContext(string? ProfileFolder = null, PickTargetCache? Targets = null)
{
    public static PickContext None { get; } = new();
}

/// <summary>
/// Remembers the answers of an <see cref="IPickTargetProbe"/>. <see cref="Presence"/> never blocks and never reads the disk: a place that was not asked
/// about yet is <see cref="TargetPresence.NotAsked"/>, which the logic treats as present. The app calls <see cref="Refresh"/> on a worker thread and
/// redraws when it says something changed. Thread-safe; holds at most <see cref="MaxEntries"/> answers (the oldest go first); an answer older than
/// <see cref="RecheckAfterMs"/> is asked again, and keeps being used until the new answer comes. Only things with a place are asked about.
/// </summary>
public sealed class PickTargetCache(Func<long>? clockMs = null)
{
    /// <summary>(Claude) The most answers remembered: more than a person puts on an island (<see cref="PickStore.MaxPicks"/> is 1000), so none is dropped while in use.</summary>
    public const int MaxEntries = 2000;

    /// <summary>(Claude) How long an answer is trusted before it is asked again: a moved or restored file shows up within half a minute.</summary>
    public const long RecheckAfterMs = 30_000;

    private readonly Func<long> _now = clockMs ?? (() => Environment.TickCount64);
    private readonly Dictionary<string, (bool Exists, long At)> _answers = [];
    private readonly object _lock = new();

    public int Count
    {
        get
        {
            lock (_lock) return _answers.Count;
        }
    }

    /// <summary>What is known, at once. Present for a pick that has no place of its own.</summary>
    public TargetPresence Presence(Pick pick, string? profileFolder)
    {
        if (KeyOf(pick, profileFolder) is not { } key) return TargetPresence.Present;
        lock (_lock)
        {
            return !_answers.TryGetValue(key, out var answer) ? TargetPresence.NotAsked : answer.Exists ? TargetPresence.Present : TargetPresence.Missing;
        }
    }

    /// <summary>Remembers an answer (made for tests, and by <see cref="Refresh"/>). Returns true when it differs from what was known before.</summary>
    public bool Record(Pick pick, string? profileFolder, bool exists)
    {
        if (KeyOf(pick, profileFolder) is not { } key) return false;
        lock (_lock)
        {
            var changed = !_answers.TryGetValue(key, out var before) || before.Exists != exists;
            if (!_answers.ContainsKey(key) && _answers.Count >= MaxEntries) _answers.Remove(_answers.MinBy(a => a.Value.At).Key);
            _answers[key] = (exists, _now());
            return changed;
        }
    }

    /// <summary>The picks whose place was not asked about yet, or not for <see cref="RecheckAfterMs"/>.</summary>
    public IReadOnlyList<Pick> ToAsk(IEnumerable<Pick> picks, string? profileFolder)
    {
        var now = _now();
        lock (_lock)
        {
            return [.. picks.Where(p => KeyOf(p, profileFolder) is { } key && (!_answers.TryGetValue(key, out var a) || now - a.At >= RecheckAfterMs))];
        }
    }

    /// <summary>(Claude) A call to <see cref="Refresh"/> waits this long for one place before it goes on to the next; the late answer is kept when it comes and reported by the next call.</summary>
    public const int PlaceWaitMs = 500;

    /// <summary>(Claude) A call to <see cref="Refresh"/> gives up on the rest of its list after this long; they are asked at the next call.</summary>
    public const int WholeWaitMs = 2000;

    /// <summary>(Claude) After the whole list was started, a call waits at most this long for the places still answering, so what it reports is complete; a place that is still silent goes on alone.</summary>
    public const int FinalWaitMs = 10_000;

    private readonly HashSet<string> _asking = [];
    private int _changedSinceReported;

    /// <summary>
    /// Asks the probe about every pick that needs asking and remembers the answers. Called off the drawing thread. Places are asked one after another, but a place that does not
    /// answer within <see cref="PlaceWaitMs"/> (a drive that went away) is left asking on its own and holds back no other; a place being asked about is not asked about again by an
    /// overlapping call; a late answer is reported by the next call. A probe that throws leaves that pick unanswered (it is asked again next time). True when any answer changed,
    /// so the island is drawn again.
    /// </summary>
    public bool Refresh(IPickTargetProbe probe, IEnumerable<Pick> picks, string? profileFolder)
    {
        var started = Environment.TickCount64;
        Exception? fatal = null;
        var launched = new List<Task>();
        foreach (var pick in ToAsk(picks, profileFolder))
        {
            if (Environment.TickCount64 - started >= WholeWaitMs) break;
            if (PickPath.Expand(pick.Location, profileFolder) is not { } real || KeyOf(pick, profileFolder) is not { } key) continue;
            lock (_lock)
            {
                if (!_asking.Add(key)) continue; // an overlapping call is asking about it
            }

            var asking = Task.Run(() =>
            {
                try
                {
                    if (Record(pick, profileFolder, probe.Exists(pick.Kind, real))) Interlocked.Exchange(ref _changedSinceReported, 1);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // an unreadable disk is not "missing": nothing is claimed
                }
                catch (OutOfMemoryException e)
                {
                    fatal = e;
                }
                finally
                {
                    lock (_lock) _asking.Remove(key);
                }
            });
            launched.Add(asking);
            asking.Wait(PlaceWaitMs);
        }

        Task.WaitAll([.. launched], FinalWaitMs);
        if (fatal is not null) throw fatal;
        return Interlocked.Exchange(ref _changedSinceReported, 0) == 1;
    }

    // A thing that is a different kind at the same place (a file and a folder of one name cannot both be there) is asked separately.
    private static string? KeyOf(Pick pick, string? profileFolder) =>
        pick.Location is not null && PickPath.Expand(pick.Location, profileFolder) is { } real && PickPath.RealKey(real) is { } key
            ? (pick.Kind == PickKind.Folder ? "d|" : "f|") + key
            : null;
}
