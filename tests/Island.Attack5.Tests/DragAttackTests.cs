using Island.Core;

namespace Island.Attack5.Tests;

/// <summary>WORK-ORDER-5 section 6: the drag-off state machine, given every event in every order.</summary>
public class DragAttackTests
{
    private static readonly DragPoint Origin = new(100, 100);
    private static readonly DragPoint Far = new(100, 300);

    private static DragRemove Pressed(bool canLift = true)
    {
        var d = new DragRemove(4, 4);
        Assert.True(d.Press(2, canLift, Origin));
        return d;
    }

    private static DragRemove Lifted()
    {
        var d = Pressed();
        Assert.Equal(DragResult.Lifted, d.Move(Far));
        return d;
    }

    // ---- what held ----

    [Fact]
    public void Holds_Events_Without_A_Press_Do_Nothing()
    {
        var d = new DragRemove(4, 4);
        Assert.Equal(DragResult.None, d.Move(Far)); // move before press
        Assert.Equal(DragResult.None, d.Release(overCapsule: false)); // release without press
        Assert.Equal(DragResult.None, d.Release(overCapsule: true));
        Assert.Equal(DragResult.None, d.Cancel());
        Assert.Equal(DragResult.None, d.LostMouse());
        Assert.False(d.IsPressed);
        Assert.False(d.IsLifted);
        Assert.Equal(-1, d.Index);
    }

    [Fact]
    public void Holds_Press_While_Pressed_Is_Ignored_And_Keeps_The_First_Press()
    {
        var d = Pressed();
        Assert.False(d.Press(5, true, new DragPoint(0, 0)));
        Assert.Equal(2, d.Index);
        Assert.Equal(DragResult.None, d.Move(new DragPoint(100, 102))); // still measured from the first press
        Assert.Equal(DragResult.Lifted, d.Move(Far));
        Assert.False(d.Press(7, true, Origin)); // and not while lifted
        Assert.Equal(2, d.Index);
        Assert.Equal(DragResult.Removed, d.Release(overCapsule: false));
        Assert.True(d.Press(7, true, Origin)); // after the release it can start again
    }

    [Fact]
    public void Holds_Lift_Then_Release_Twice_Removes_Once()
    {
        var d = Lifted();
        Assert.Equal(DragResult.SpringsBack, d.Release(overCapsule: true));
        Assert.Equal(DragResult.None, d.Release(overCapsule: false)); // the second release finds nothing pressed
        Assert.Equal(DragResult.None, d.Cancel());

        var e = Lifted();
        Assert.Equal(DragResult.Removed, e.Release(overCapsule: false));
        Assert.Equal(DragResult.None, e.Release(overCapsule: false));
        Assert.False(e.IsLifted);
    }

    [Fact]
    public void Holds_Lift_Then_Lose_The_Mouse_Then_Release_Never_Removes()
    {
        var d = Lifted();
        Assert.Equal(DragResult.Cancelled, d.LostMouse());
        Assert.False(d.IsLifted);
        Assert.Equal(DragResult.None, d.Release(overCapsule: false)); // a late button-up after another program took over: nothing
        Assert.Equal(DragResult.None, d.Release(overCapsule: true));

        // the same through Esc
        var e = Lifted();
        Assert.Equal(DragResult.Cancelled, e.Cancel());
        Assert.Equal(DragResult.None, e.Release(overCapsule: false));
    }

    [Fact]
    public void Holds_Cancel_In_Every_State()
    {
        Assert.Equal(DragResult.None, new DragRemove(4, 4).Cancel());

        var pressed = Pressed();
        Assert.Equal(DragResult.Cancelled, pressed.Cancel()); // dropped without a click
        Assert.Equal(DragResult.None, pressed.Release(false));

        var lifted = Lifted();
        Assert.Equal(DragResult.Cancelled, lifted.Cancel());
        Assert.False(lifted.IsPressed);
        Assert.Equal(DragResult.None, lifted.Move(Far));

        var notLiftable = Pressed(canLift: false);
        Assert.Equal(DragResult.Cancelled, notLiftable.Cancel());
    }

    [Fact]
    public void Holds_The_Drag_Distance_Edge_On_Each_Axis_Alone()
    {
        var d = new DragRemove(4, 6);
        Assert.True(d.Press(0, true, Origin));
        Assert.Equal(DragResult.None, d.Move(new DragPoint(104, 106))); // exactly the distance: still a click
        Assert.Equal(DragResult.None, d.Move(new DragPoint(96, 94)));
        Assert.Equal(DragResult.Lifted, d.Move(new DragPoint(104.0001, 100)));

        var y = new DragRemove(4, 6);
        y.Press(0, true, Origin);
        Assert.Equal(DragResult.Lifted, y.Move(new DragPoint(100, 106.0001)));

        // distance 0: not moving is not a drag, any movement is
        var z = new DragRemove(0, 0);
        z.Press(0, true, Origin);
        Assert.Equal(DragResult.None, z.Move(Origin));
        Assert.Equal(DragResult.Lifted, z.Move(new DragPoint(100.001, 100)));
    }

    [Fact]
    public void Holds_Small_Movement_And_Back_Is_A_Click_And_Lift_Happens_Once()
    {
        var d = Pressed();
        Assert.Equal(DragResult.None, d.Move(new DragPoint(103, 103)));
        Assert.Equal(DragResult.None, d.Move(Origin));
        Assert.Equal(DragResult.Click, d.Release(overCapsule: false)); // a click acts on the release wherever it is

        var e = Pressed();
        Assert.Equal(DragResult.Lifted, e.Move(Far));
        Assert.Equal(DragResult.None, e.Move(Origin)); // coming back inside the distance does not un-lift, and does not lift twice
        Assert.True(e.IsLifted);
        Assert.Equal(DragResult.SpringsBack, e.Release(overCapsule: true));
    }

    [Fact]
    public void Holds_Rules_For_What_Can_Be_Lifted()
    {
        Assert.True(DragRemove.CanLift(isPick: true, secondRowOpen: false));
        Assert.False(DragRemove.CanLift(isPick: true, secondRowOpen: true));
        Assert.False(DragRemove.CanLift(isPick: false, secondRowOpen: false));
        Assert.False(DragRemove.CanLift(isPick: false, secondRowOpen: true));

        var d = Pressed(canLift: false);
        Assert.Equal(DragResult.None, d.Move(Far));
        Assert.False(d.IsLifted); // the + tile, a second-row tile and a pick under an open second row are never lifted

        Assert.Equal(EscapeAction.CancelDrag, EscapeRule.Decide(true, true));
        Assert.Equal(EscapeAction.CancelDrag, EscapeRule.Decide(true, false));
        Assert.Equal(EscapeAction.CloseRow, EscapeRule.Decide(false, true));
        Assert.Equal(EscapeAction.Dismiss, EscapeRule.Decide(false, false));
    }

    [Fact]
    public void Holds_A_Storm_Of_Every_Event_In_Every_Order_Keeps_The_Machine_Consistent()
    {
        double[] edges = [0, 1, -1, 4, 5, 1e300, -1e300, double.MaxValue, double.MinValue, 1e-300];
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            var rng = new Random(seed);
            var d = new DragRemove(4, 4);
            var liftsThisPress = 0;
            for (var i = 0; i < 20_000; i++)
            {
                double Coordinate() => rng.Next(4) == 0 ? edges[rng.Next(edges.Length)] : 100 + rng.Next(-12, 13);
                var wasPressed = d.IsPressed;
                var wasLifted = d.IsLifted;
                DragResult result;
                switch (rng.Next(6))
                {
                    case 0:
                        if (d.Press(rng.Next(-1, 9), rng.Next(3) != 0, new DragPoint(Coordinate(), Coordinate()))) liftsThisPress = 0;
                        continue;
                    case 1:
                        result = d.Move(new DragPoint(Coordinate(), Coordinate()));
                        Assert.True(result is DragResult.None or DragResult.Lifted);
                        if (result == DragResult.Lifted)
                        {
                            Assert.True(wasPressed && !wasLifted, "lifted twice or without a press");
                            liftsThisPress++;
                        }

                        break;
                    case 2:
                        result = d.Release(rng.Next(2) == 0);
                        Assert.True(wasLifted ? result is DragResult.Removed or DragResult.SpringsBack : wasPressed ? result is DragResult.Click or DragResult.Cancelled : result == DragResult.None); // a press that left the drag distance is no click (defect 1 of ATTACK5, fixed)
                        break;
                    case 3:
                        result = d.Cancel();
                        Assert.Equal(wasPressed ? DragResult.Cancelled : DragResult.None, result);
                        break;
                    case 4:
                        result = d.LostMouse();
                        Assert.Equal(wasPressed ? DragResult.Cancelled : DragResult.None, result);
                        break;
                    default:
                        var pick = rng.Next(2) == 0;
                        var open = rng.Next(2) == 0;
                        Assert.Equal(pick && !open, DragRemove.CanLift(pick, open));
                        break;
                }

                Assert.True(!d.IsLifted || d.IsPressed, "lifted without being pressed");
                Assert.True(liftsThisPress <= 1);
                if (!d.IsPressed) Assert.Equal(-1, d.Index);
            }
        }
    }

    // ---- what broke ----

    [Fact]
    public void Defect_A_Pointer_Position_That_Is_Not_A_Number_Lifts_The_Tile()
    {
        // Every comparison with NaN is false, so "inside the drag distance" is false and the tile lifts. A press at NaN
        // makes the very next move a lift; a move to NaN (or infinity) lifts at once. Letting go anywhere off the capsule then removes the pick.
        var offenders = new List<string>();
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var movesToBad = Pressed();
            if (movesToBad.Move(new DragPoint(bad, 100)) == DragResult.Lifted) offenders.Add($"move to x={bad}");
            var y = Pressed();
            if (y.Move(new DragPoint(100, bad)) == DragResult.Lifted) offenders.Add($"move to y={bad}");

            var pressedAtBad = new DragRemove(4, 4);
            pressedAtBad.Press(1, true, new DragPoint(bad, bad));
            if (pressedAtBad.Move(new DragPoint(100, 100)) == DragResult.Lifted) offenders.Add($"press at {bad}, then an ordinary move");
        }

        Assert.True(offenders.Count == 0, "a non-finite point lifts the tile: " + string.Join("; ", offenders));
    }

    [Fact]
    public void Defect_A_Negative_Or_NaN_Drag_Distance_Lifts_The_Tile_Without_Any_Movement()
    {
        // The constructor takes the system drag distance on trust. With a negative or NaN distance, "not further than the
        // distance" is never true, so a press followed by a move to the very same point lifts: no click can ever happen.
        var offenders = new List<string>();
        foreach (var distance in new[] { -1.0, -0.0001, double.NaN })
        {
            var d = new DragRemove(distance, distance);
            d.Press(0, true, Origin);
            if (d.Move(Origin) == DragResult.Lifted) offenders.Add($"distance {distance}");
        }

        Assert.True(offenders.Count == 0, "lifted with no movement at: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Defect_A_Press_That_Cannot_Lift_Is_Still_A_Click_However_Far_It_Was_Dragged()
    {
        // The + tile, or a pick while the second row is open: pressed, dragged 200 units away and let go far from the tile.
        // Only a lifted tile has a "not a click" path, so Release reports Click and the app toggles the row (or jumps to the
        // program) although the person dragged away. A press that leaves the drag distance should never be a click.
        var d = Pressed(canLift: false);
        Assert.Equal(DragResult.None, d.Move(Far));
        var result = d.Release(overCapsule: false);
        Assert.NotEqual(DragResult.Click, result);
    }
}
