using Island.Core;

namespace Island.Tests;

public class DragRemoveTests
{
    private const double Distance = 4; // what the system gives (SM_CXDRAG / SM_CYDRAG are 4 pixels on this laptop; the logic takes any)

    private static DragRemove New() => new(Distance, Distance);

    private static readonly DragPoint Start = new(100, 50);

    [Fact]
    public void Small_Movement_Is_A_Click()
    {
        var drag = New();
        Assert.True(drag.Press(2, canLift: true, Start));

        Assert.Equal(DragResult.None, drag.Move(new DragPoint(103, 51))); // inside the distance
        Assert.Equal(DragResult.None, drag.Move(new DragPoint(97, 46))); // exactly the distance: still inside
        Assert.False(drag.IsLifted);

        Assert.Equal(DragResult.Click, drag.Release(overCapsule: true)); // acting on the release
        Assert.False(drag.IsPressed);
        Assert.Equal(DragResult.None, drag.Release(overCapsule: true)); // a second release does nothing
    }

    [Fact]
    public void Release_Off_The_Capsule_Removes_The_Pick()
    {
        var drag = New();
        drag.Press(1, canLift: true, Start);

        Assert.Equal(DragResult.Lifted, drag.Move(new DragPoint(100, 70)));
        Assert.True(drag.IsLifted);
        Assert.Equal(DragResult.None, drag.Move(new DragPoint(100, 120))); // it lifts once

        Assert.Equal(DragResult.Removed, drag.Release(overCapsule: false)); // the drop zone, or anywhere that is not the capsule
        Assert.False(drag.IsLifted);
        Assert.False(drag.IsPressed);
    }

    [Fact]
    public void Release_On_The_Capsule_Changes_Nothing()
    {
        var drag = New();
        drag.Press(1, canLift: true, Start);
        drag.Move(new DragPoint(140, 52));

        Assert.Equal(DragResult.SpringsBack, drag.Release(overCapsule: true));
        Assert.False(drag.IsLifted);
    }

    [Fact]
    public void Escape_Cancels()
    {
        var drag = New();
        drag.Press(1, canLift: true, Start);
        drag.Move(new DragPoint(100, 90));

        Assert.Equal(DragResult.Cancelled, drag.Cancel());
        Assert.False(drag.IsLifted);
        Assert.Equal(DragResult.None, drag.Release(overCapsule: false)); // the release that follows is not a removal
        Assert.Equal(DragResult.None, drag.Cancel());
    }

    [Fact]
    public void Losing_The_Mouse_Cancels_And_Never_Removes()
    {
        var drag = New();
        drag.Press(1, canLift: true, Start);
        drag.Move(new DragPoint(100, 400)); // far outside

        Assert.Equal(DragResult.Cancelled, drag.LostMouse());
        Assert.False(drag.IsLifted);
        Assert.NotEqual(DragResult.Removed, drag.Release(overCapsule: false));

        // Lost before anything lifted: no click either.
        var other = New();
        other.Press(0, canLift: true, Start);
        Assert.Equal(DragResult.Cancelled, other.LostMouse());
    }

    [Fact]
    public void Plus_Tile_Cannot_Be_Dragged()
    {
        Assert.False(DragRemove.CanLift(isPick: false, secondRowOpen: false));

        var drag = New();
        drag.Press(3, DragRemove.CanLift(isPick: false, secondRowOpen: false), Start);
        Assert.Equal(DragResult.None, drag.Move(new DragPoint(400, 400)));
        Assert.False(drag.IsLifted);
        Assert.Equal(DragResult.Cancelled, drag.Release(overCapsule: false)); // dragged away from the + tile it is not a click (found by ATTACK5)

        var still = New();
        still.Press(3, canLift: false, Start);
        Assert.Equal(DragResult.Click, still.Release(overCapsule: true)); // a press and release without leaving the distance is a click
    }

    [Fact]
    public void Nothing_Lifts_While_The_Second_Row_Is_Open()
    {
        Assert.True(DragRemove.CanLift(isPick: true, secondRowOpen: false));
        Assert.False(DragRemove.CanLift(isPick: true, secondRowOpen: true));

        var drag = New();
        drag.Press(0, DragRemove.CanLift(isPick: true, secondRowOpen: true), Start);
        Assert.Equal(DragResult.None, drag.Move(new DragPoint(100, 300)));
        Assert.False(drag.IsLifted);
    }

    [Fact]
    public void A_Second_Press_While_One_Is_Tracked_Is_Ignored()
    {
        var drag = New();
        Assert.True(drag.Press(1, true, Start));
        Assert.False(drag.Press(2, true, new DragPoint(0, 0)));
        Assert.Equal(1, drag.Index);
        drag.Cancel();
        Assert.True(drag.Press(2, true, Start));
    }

    [Fact]
    public void Odd_Input_Never_Throws()
    {
        var drag = new DragRemove(double.NaN, -1);
        drag.Press(-5, true, new DragPoint(double.NaN, double.PositiveInfinity));
        drag.Move(new DragPoint(double.NegativeInfinity, double.NaN));
        drag.Release(overCapsule: false);
        drag.Cancel();
        drag.LostMouse();
        Assert.False(drag.IsPressed);
    }
}
