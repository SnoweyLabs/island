using System.Windows;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-5 §6: dragging a pick off the island removes it. The tiles of the first row report the raw pointer here; the state
/// machine in Island.Core (<see cref="DragRemove"/>) decides what it means; this class draws the lifted tile and the drop zone,
/// and says what to do: a click on the release, or "remove this pick". Nothing that is open is ever closed. The lifted tile
/// is drawn only inside the window; letting go anywhere that is not the capsule removes, letting go over it, Esc and losing the
/// mouse all put the tile back. While a tile is lifted the idle time does not run and the wheel, the arrows and the page keys do nothing.
/// </summary>
internal sealed class DragHandler
{
    private readonly IslandController _controller;
    private readonly ContentsLayer _layer;
    private readonly DragView _view;
    private readonly DragRemove _drag = new(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
    private Item? _item;

    public DragHandler(IslandController controller, ContentsLayer layer, DragView view)
    {
        _controller = controller;
        _layer = layer;
        _view = view;
        layer.TilePressed += OnPressed;
        layer.TileMoved += OnMoved;
        layer.TileReleased += OnReleased;
        layer.TileMouseLost += OnMouseLost;
        controller.IsLifted = () => _drag.IsLifted;
        controller.IsPressed = () => _drag.IsPressed;
        controller.FrameHook = Tick;
    }

    /// <summary>Raised with the pick's id when a pick was dragged off: the book removes it. Only the list changes.</summary>
    public event Action<string>? Removed;

    public bool IsLifted => _drag.IsLifted;

    /// <summary>Called on every drawn frame: a drag ends the moment the island starts to leave (a tile is never removed because the capsule shrank under the pointer).</summary>
    private void Tick()
    {
        if (_drag.IsPressed && Machine.Phase != IslandPhase.Open) Cancel();
    }

    private IslandMachine Machine => _controller.Machine;

    /// <summary>The capsule as drawn now, in window coordinates: what "over the capsule" means.</summary>
    private Rect Capsule => new(WindowMetrics.Width / 2 - Machine.DrawnWidth / 2, Machine.DrawnY, Machine.DrawnWidth, Machine.DrawnHeight);

    private Point Home(int index) => new(_layer.TileCentreX(index) - LookConstants.ItemSize / 2,
        LookConstants.TopGap + LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth - LookConstants.ItemSize / 2);

    public void OnPressed(int index, Point at)
    {
        var items = Machine.ContentsItems;
        if (index < 0 || index >= items.Count) return;
        var item = items[index];
        var isPick = !item.IsPlus && item.PickId is not null;
        if (_drag.Press(index, DragRemove.CanLift(isPick, Machine.SecondRowOpen), new DragPoint(at.X, at.Y))) _item = item;
    }

    public void OnMoved(int index, Point at)
    {
        switch (_drag.Move(new DragPoint(at.X, at.Y)))
        {
            case DragResult.Lifted when _item is { } item && _drag.Index < _layer.TileCount:
                _layer.TileAt(_drag.Index).SetLifted(true);
                _view.Lift(item, Rgb.FromHex(Machine.Page.Color), Home(_drag.Index), at);
                _view.ShowZone(Capsule);
                _layer.ShowHoverText(item.Title, "let go to remove");
                _controller.Activity();
                break;
            default:
                if (_drag.IsLifted) _view.MoveTo(at);
                break;
        }
    }

    public void OnReleased(int index, Point at)
    {
        var index0 = _drag.Index;
        var item = _item;
        var result = _drag.Release(Capsule.Contains(at) || Machine.Phase != IslandPhase.Open); // while the island leaves, letting go puts the tile back
        switch (result)
        {
            case DragResult.Click:
                _controller.TileClicked(index0);
                break;
            case DragResult.Removed when item?.PickId is { } id:
                End(index0, springBack: false);
                Removed?.Invoke(id);
                break;
            case DragResult.SpringsBack:
                End(index0, springBack: true);
                break;
        }

        _item = null;
    }

    public void OnMouseLost(int index)
    {
        var index0 = _drag.Index;
        var wasLifted = _drag.IsLifted;
        if (_drag.LostMouse() == DragResult.Cancelled && wasLifted) End(index0, springBack: true);
        _item = null;
    }

    /// <summary>Esc: a lifted tile goes back to its place. True when there was a drag to cancel (the Esc is used up).</summary>
    public bool Cancel()
    {
        if (!_drag.IsPressed) return false;
        var index = _drag.Index;
        var wasLifted = _drag.IsLifted;
        _drag.Cancel();
        if (wasLifted) End(index, springBack: true);
        _item = null;
        return true;
    }

    private void End(int index, bool springBack)
    {
        _view.HideZone();
        _layer.ShowHoverText(null, null);
        var tile = index >= 0 && index < _layer.TileCount ? _layer.TileAt(index) : null;
        if (springBack) _view.SpringBack(Home(index), () => tile?.SetLifted(false));
        else
        {
            _view.Clear();
            tile?.SetLifted(false);
        }
    }
}
