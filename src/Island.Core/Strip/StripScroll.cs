namespace Island.Core;

/// <summary>
/// WORK-ORDER-5 §4: where the sliding picks are. The position is a TARGET and a spring: the target never leaves its range
/// (first pick at the left to last pick at the right) and the drawn position follows it through a spring with the constants
/// of the approved look, so it may pass an end by the spring's own overshoot but is never assigned while the island is on
/// screen (EVALS M3). Sliding by the wheel adds up what the wheel reports; each full notch moves the picks by
/// <see cref="ChoiceConstants.TilesPerNotch"/> tile; turning toward the user shows later picks.
/// </summary>
public sealed class StripScroll
{
    /// <summary>Windows' own constant for one wheel notch: WHEEL_DELTA (Microsoft Learn, WM_MOUSEWHEEL: "which is 120"). A smooth wheel sends smaller amounts that add up to it.</summary>
    public const int WheelDelta = 120;

    private Spring _position = Spring.At(0);
    private double _target;
    private long _wheelRemainder;

    private int _maxVisible;

    /// <param name="maxVisible">How many tiles show at once: seven for the capsule's own row, as many as fit for the second row.</param>
    public StripScroll(int picks = 0, int maxVisible = ChoiceConstants.MaxVisibleTiles)
    {
        _maxVisible = Math.Max(0, maxVisible);
        SetCount(picks);
    }

    /// <summary>The room for tiles changed (the capsule became wider or narrower): the target is brought back into the new range.</summary>
    public void SetMaxVisible(int maxVisible)
    {
        _maxVisible = Math.Max(0, maxVisible);
        _target = Clamp(_target);
        _position = _position.WithTarget(_target);
    }

    public int PickCount { get; private set; }

    /// <summary>How many picks show at once.</summary>
    public int Visible => StripLayout.VisiblePicks(PickCount, _maxVisible);

    /// <summary>The largest position: the last pick at the right end.</summary>
    public int MaxPosition => Visible == 0 ? 0 : Math.Max(0, PickCount - Visible);

    /// <summary>Where the picks are heading, in tiles from the first pick at the left; always inside 0 to <see cref="MaxPosition"/>.</summary>
    public double Target => _target;

    /// <summary>Where the picks are drawn now, in tiles: follows the target through the spring.</summary>
    public double Position => _position.Drawn;

    /// <summary>More picks are hidden to the left of what is drawn.</summary>
    public bool HiddenLeft => Visible > 0 && Position > 0.02;

    /// <summary>More picks are hidden to the right of what is drawn.</summary>
    public bool HiddenRight => Visible > 0 && Position < MaxPosition - 0.02;

    /// <summary>True while the drawn position still moves toward the target.</summary>
    public bool Moving => Math.Abs(_position.Value - _target) > 0.0005 || Math.Abs(_position.Velocity) > 0.0005;

    /// <summary>The number of picks changed (one was added or removed): the target is brought back into the new range.</summary>
    public void SetCount(int picks)
    {
        PickCount = Math.Max(0, picks);
        _target = Clamp(_target);
        _position = _position.WithTarget(_target);
    }

    /// <summary>
    /// A summon: the strip starts at the beginning, with one exception — the selected tile is always brought into view.
    /// Only while nothing of the island is on screen, so the position is set rather than sprung.
    /// </summary>
    public void Reset(int selectedPick)
    {
        _wheelRemainder = 0;
        _target = 0;
        BringIntoView(selectedPick);
        _position = Spring.At(_target);
    }

    /// <summary>Moves the target by the least amount that shows the pick at <paramref name="index"/>; the drawn position follows through the spring.</summary>
    public void BringIntoView(int index)
    {
        if (PickCount == 0 || index < 0) return;
        index = Math.Min(index, PickCount - 1);
        if (index < _target) _target = index;
        else if (index > _target + Visible - 1) _target = index - Visible + 1;
        _target = Clamp(_target);
        _position = _position.WithTarget(_target);
    }

    /// <summary>
    /// The wheel turned by <paramref name="delta"/> (positive: away from the user). Small amounts add up; each full notch moves
    /// the picks by one tile, toward the user showing later picks.
    /// </summary>
    public void Wheel(int delta)
    {
        _wheelRemainder += Math.Clamp(delta, -100 * WheelDelta, 100 * WheelDelta);
        var notches = _wheelRemainder / WheelDelta; // whole notches, toward zero
        if (notches == 0) return;
        _wheelRemainder -= notches * WheelDelta;
        MoveBy(-notches * ChoiceConstants.TilesPerNotch);
    }

    /// <summary>A click on the left arrow: back by the number of tiles shown.</summary>
    public void ArrowLeft() => MoveBy(-Visible);

    /// <summary>A click on the right arrow: forward by the number of tiles shown.</summary>
    public void ArrowRight() => MoveBy(Visible);

    private void MoveBy(double tiles)
    {
        _target = Clamp(_target + tiles);
        _position = _position.WithTarget(_target);
    }

    /// <summary>Advances the spring by the time that has passed, in seconds.</summary>
    public void Tick(double elapsedSeconds) => _position = _position.WithTarget(_target).Frame(elapsedSeconds);

    private double Clamp(double position) => Math.Clamp(position, 0, MaxPosition);
}
