namespace Island.Core;

/// <summary>A point, in whatever units the caller uses (the island's window, in device-independent pixels).</summary>
public readonly record struct DragPoint(double X, double Y);

/// <summary>What a mouse event did to a drag.</summary>
public enum DragResult
{
    /// <summary>Nothing happened.</summary>
    None,

    /// <summary>Pressed and released without leaving the drag distance: a click, acting on the release.</summary>
    Click,

    /// <summary>The tile just lifted: it follows the pointer, the drop zone appears.</summary>
    Lifted,

    /// <summary>Let go over the drop zone or anywhere that is not the capsule: the pick is removed from the island.</summary>
    Removed,

    /// <summary>Let go over the capsule: the tile springs back to its place and nothing changes.</summary>
    SpringsBack,

    /// <summary>Esc, or the window lost the mouse without the button coming up: the same as springing back. Losing the mouse never removes.</summary>
    Cancelled,
}

/// <summary>
/// WORK-ORDER-5 §6: removing a pick by dragging it off the island, as a small state machine that is given points, the drag
/// distance and events as plain values. Press on a pick and move further than Windows' own drag distance and the tile lifts; let go
/// anywhere that is not the capsule and the pick is removed; let go over the capsule, press Esc, or lose the mouse and nothing
/// changes. A press and release inside the drag distance is a click, acting on the release. Nothing but a pick of the first
/// row can be lifted, and none while the second row is open.
/// </summary>
public sealed class DragRemove(double distanceX, double distanceY)
{
    /// <summary>What a drag distance that is not a usable number (NaN, infinite, negative) counts as: the usual 4 pixels of Windows.</summary>
    public const double DefaultDistance = 4;

    private readonly double _distanceX = Usable(distanceX);
    private readonly double _distanceY = Usable(distanceY);
    private DragPoint _start;
    private bool _canLift;
    private bool _moved;

    private static double Usable(double distance) => double.IsFinite(distance) && distance >= 0 ? distance : DefaultDistance;

    private static bool Finite(DragPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    /// <summary>True from the press until the release, the Esc or the loss of the mouse.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>True while a tile is lifted: the wheel, the arrows and the page keys do nothing, and the idle time does not run.</summary>
    public bool IsLifted { get; private set; }

    /// <summary>The index of the tile that was pressed.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>Whether a tile can be lifted at all: a pick of the first row, and not while the second row is open (the + tile and the tiles of the second row are never lifted).</summary>
    public static bool CanLift(bool isPick, bool secondRowOpen) => isPick && !secondRowOpen;

    /// <summary>The button went down on a tile. A press while another is being tracked is ignored.</summary>
    public bool Press(int index, bool canLift, DragPoint at)
    {
        if (IsPressed || !Finite(at)) return false; // a point that is not a number starts nothing
        IsPressed = true;
        IsLifted = false;
        _moved = false;
        Index = index;
        _canLift = canLift;
        _start = at;
        return true;
    }

    /// <summary>The pointer moved with the button down.</summary>
    public DragResult Move(DragPoint at)
    {
        if (!IsPressed || IsLifted || !Finite(at)) return DragResult.None;
        if (Math.Abs(at.X - _start.X) <= _distanceX && Math.Abs(at.Y - _start.Y) <= _distanceY) return DragResult.None;
        _moved = true; // the press has left the drag distance: whatever happens next, it is no longer a click
        if (!_canLift) return DragResult.None;
        IsLifted = true;
        return DragResult.Lifted;
    }

    /// <summary>The button went up. <paramref name="overCapsule"/> is whether the pointer is over the capsule (the drop zone and everything else count as "not the capsule").</summary>
    public DragResult Release(bool overCapsule)
    {
        if (!IsPressed) return DragResult.None;
        var lifted = IsLifted;
        var moved = _moved;
        Reset();
        if (lifted) return overCapsule ? DragResult.SpringsBack : DragResult.Removed;
        return moved ? DragResult.Cancelled : DragResult.Click; // a press that could not lift but was dragged away is not a click either
    }

    /// <summary>Esc was pressed. A lifted tile goes back; a press that has not lifted anything is dropped without a click.</summary>
    public DragResult Cancel()
    {
        if (!IsPressed) return DragResult.None;
        Reset();
        return DragResult.Cancelled;
    }

    /// <summary>The window lost the mouse without the button coming up (another program took over). Never removes.</summary>
    public DragResult LostMouse() => Cancel();

    private void Reset()
    {
        IsPressed = false;
        IsLifted = false;
        _moved = false;
        Index = -1;
    }
}
