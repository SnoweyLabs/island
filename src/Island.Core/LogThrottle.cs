namespace Island.Core;

/// <summary>
/// Says whether a line of the log is worth writing: a line that is the same as one of the last few, within <see cref="SameLineWithinMs"/>, is not (an exception thrown on every frame would
/// otherwise write about 16 KB a second). Memory only; thread-safe.
/// </summary>
public sealed class LogThrottle
{
    public const long SameLineWithinMs = 5_000;

    private readonly object _lock = new();
    private const int Remembered = 8;

    private readonly Dictionary<string, long> _seen = [];

    public bool ShouldWrite(string line, long nowMs)
    {
        lock (_lock)
        {
            if (_seen.TryGetValue(line, out var at) && nowMs - at < SameLineWithinMs && nowMs >= at) return false;
            if (_seen.Count >= Remembered && !_seen.ContainsKey(line))
                _seen.Remove(_seen.MinBy(p => p.Value).Key); // the line written longest ago is forgotten first
            _seen[line] = nowMs;
            return true;
        }
    }
}
