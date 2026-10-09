namespace Island.Core;

/// <summary>
/// Icons that could not be read (the catalog of programs not read yet, a slow place on the network): not kept as "no icon" for the run but asked again, a few times, a while apart.
/// Thread-safe: the readers run off the drawing thread.
/// </summary>
public sealed class IconMisses
{
    public const int MaxTries = 3;
    public const long RetryAfterMs = 20_000;

    private readonly Dictionary<string, (int Tries, long At)> _missed = [];
    private readonly Func<long> _now;

    public IconMisses(Func<long>? now = null) => _now = now ?? (() => Environment.TickCount64);

    /// <summary>A read gave nothing: counted, and asked again no sooner than <see cref="RetryAfterMs"/> from now.</summary>
    public void Missed(string key)
    {
        lock (_missed) _missed[key] = (_missed.TryGetValue(key, out var m) ? m.Tries + 1 : 1, _now());
    }

    /// <summary>True once when a miss is old enough and has been tried fewer than <see cref="MaxTries"/> times; the caller then reads again (and calls <see cref="Missed"/> if it fails again).</summary>
    public bool ShouldRetry(string key)
    {
        lock (_missed)
        {
            if (!_missed.TryGetValue(key, out var m) || m.Tries >= MaxTries || _now() - m.At < RetryAfterMs) return false;
            _missed[key] = (m.Tries, long.MaxValue); // a read is under way
            return true;
        }
    }
}
