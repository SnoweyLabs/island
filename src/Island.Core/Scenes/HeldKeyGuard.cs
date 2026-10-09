namespace Island.Core;

/// <summary>
/// "A key held down runs the scene once, not again and again" (EVALS S2). The app already registers keys with
/// MOD_NOREPEAT, so Windows sends one message per press and this guard is the second line: a hotkey message that
/// arrives within <see cref="RepeatGapMs"/> of the previous one for the same key is the keyboard repeating, not a
/// new press. Windows' repeat delay is at most about a second and its repeat interval at most half a second, so a
/// held key keeps arriving inside the gap and stays swallowed however long it is held. A real second press needs
/// a pause longer than the gap, or <see cref="Released"/> when the app knows the key came up. Everything is in
/// memory; the clock is a number handed in (a monotonic millisecond count), so tests need no real time.
/// </summary>
public sealed class HeldKeyGuard
{
    /// <summary>A little over the longest keyboard repeat delay Windows offers (1 second).</summary>
    public const long RepeatGapMs = 1100;

    private readonly Dictionary<string, long> _lastSeen = [];

    /// <summary>True when this message is a new press and the scene should run; false when it is the same press repeating.</summary>
    /// <param name="key">What the key runs: the scene's id.</param>
    /// <param name="nowMs">A monotonic clock reading in milliseconds. A clock that went backwards counts as a new press.</param>
    public bool Accept(string key, long nowMs)
    {
        var held = _lastSeen.TryGetValue(key, out var last) && last >= 0 && nowMs >= last && nowMs - last < RepeatGapMs;
        _lastSeen[key] = nowMs; // also for a repeat: a long hold keeps itself swallowed
        return !held;
    }

    /// <summary>The key came up: the next message is a new press, however soon it comes.</summary>
    public void Released(string key) => _lastSeen.Remove(key);
}
