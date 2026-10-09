namespace Island.Core;

/// <summary>
/// What the notice does now. <see cref="Showing"/> is the notice to draw (null: draw none). <see cref="Waiting"/> is true
/// when a notice is held back because the capsule is open. <see cref="HidesPill"/> is true when the small pill is up and
/// the notice takes its place; the pill returns when this goes back to false.
/// </summary>
public sealed record NoticeState(AgentNotice? Showing, bool Waiting, bool HidesPill)
{
    public static NoticeState None { get; } = new(null, false, false);

    /// <summary>A notice never takes the keyboard: the app shows it without activating any window.</summary>
    public bool TakesKeyboard => false;
}

/// <summary>
/// The rules of the notice (WORK-ORDER-7 section 4), as a state model with no screen and no clock of its own: the time
/// is handed in. One notice at a time. A newer notice replaces the one showing; a Stop for the same session while its
/// notice is still there changes nothing; while the capsule is open the notice waits and shows once the capsule has left
/// or shrunk; it leaves by itself after <see cref="Seconds"/>, and a pointer over it keeps it longer. Memory only.
/// </summary>
public sealed class NoticeQueue
{
    public const double DefaultSeconds = 6;
    public const double MinSeconds = 3;
    public const double MaxSeconds = 30;

    /// <summary>A notice held back this long (capsule open the whole time) is stale and dropped. My choice: the order names none.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    /// <summary>A pointer parked on the notice cannot keep it longer than this. My choice: the order names none.</summary>
    public static readonly TimeSpan HoverCap = TimeSpan.FromSeconds(60);

    private sealed class Slot(AgentNotice notice, DateTimeOffset waitingSince)
    {
        public AgentNotice Notice { get; } = notice;
        public DateTimeOffset WaitingSince { get; set; } = waitingSince;
        public DateTimeOffset? VisibleSince { get; set; }
        public DateTimeOffset Deadline { get; set; }
    }

    private readonly object _lock = new();
    private Slot? _slot;
    private DateTimeOffset _lastNow = DateTimeOffset.MinValue;
    private double _seconds = DefaultSeconds;

    public NoticeQueue(double seconds = DefaultSeconds) => Seconds = seconds;

    /// <summary>How long a notice stays, from 3 to 30 seconds; anything else is brought into that range.</summary>
    public double Seconds
    {
        get { lock (_lock) return _seconds; }
        set { lock (_lock) _seconds = Clamp(value); }
    }

    public static double Clamp(double seconds) =>
        double.IsNaN(seconds) ? DefaultSeconds : Math.Clamp(seconds, MinSeconds, MaxSeconds);

    /// <summary>A notice arrives. False when it changed nothing (a Stop for the session whose notice is still there).</summary>
    public bool Post(AgentNotice notice, DateTimeOffset now)
    {
        lock (_lock)
        {
            Rebase(now);
            DropIfOver(now);
            if (_slot is { } cur && notice.Signal == AgentSignal.Finished && cur.Notice.Signal == AgentSignal.Finished && cur.Notice.SessionKey == notice.SessionKey)
                return false;
            _slot = new Slot(notice, now);
            return true;
        }
    }

    /// <summary>Called on every tick of the app with what the screen is doing; says what to draw.</summary>
    public NoticeState Update(DateTimeOffset now, bool capsuleOpen, bool pillUp, bool pointerOver)
    {
        lock (_lock)
        {
            Rebase(now);
            if (_slot is not { } s) return NoticeState.None;

            if (capsuleOpen)
            {
                if (s.VisibleSince is not null) s.WaitingSince = now; // it was showing and goes back to waiting
                s.VisibleSince = null;
                DropIfOver(now);
                return _slot is null ? NoticeState.None : new NoticeState(null, true, false);
            }

            if (s.VisibleSince is null)
            {
                if (now - s.WaitingSince > MaxWait) return Drop();
                s.VisibleSince = now;
                s.Deadline = Later(now, TimeSpan.FromSeconds(_seconds));
            }
            else if (pointerOver)
            {
                var longer = Later(now, TimeSpan.FromSeconds(_seconds));
                var cap = Later(s.VisibleSince.Value, HoverCap);
                var wanted = longer > cap ? cap : longer;
                if (wanted > s.Deadline) s.Deadline = wanted;
            }

            return now >= s.Deadline ? Drop() : new NoticeState(s.Notice, false, pillUp);
        }
    }

    /// <summary>The notice is gone at once (it was clicked).</summary>
    public void Dismiss()
    {
        lock (_lock) _slot = null;
    }

    private NoticeState Drop()
    {
        _slot = null;
        return NoticeState.None;
    }

    // A waiting notice that is too old, or a showing one past its deadline, is no longer "still showing".
    /// <summary>A moment later, never past the last one a clock can name.</summary>
    private static DateTimeOffset Later(DateTimeOffset moment, TimeSpan by) =>
        DateTimeOffset.MaxValue - moment < by ? DateTimeOffset.MaxValue : moment + by;

    /// <summary>
    /// The time handed in is a wall clock: a time sync, a manual change or a resume can step it back. A notice is then timed again from the new time (it stays about as long as it would
    /// have, instead of for as long as the clock stepped), and one that was waiting waits from now.
    /// </summary>
    private void Rebase(DateTimeOffset now)
    {
        if (now < _lastNow && _slot is { } s)
        {
            s.WaitingSince = now;
            if (s.VisibleSince is not null)
            {
                s.VisibleSince = now;
                s.Deadline = Later(now, TimeSpan.FromSeconds(_seconds));
            }
        }

        _lastNow = now;
    }

    private void DropIfOver(DateTimeOffset now)
    {
        if (_slot is not { } s) return;
        var over = s.VisibleSince is null ? now - s.WaitingSince > MaxWait : now >= s.Deadline;
        if (over) _slot = null;
    }
}
