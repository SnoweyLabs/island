using Island.Core;

namespace Island.Tests;

/// <summary>
/// WORK-ORDER-10 §2 at the machine: the selection along the first row, the second row's selection, a Delete waiting for its second press, the idle clock.
/// The decisions themselves are IslandKeysTests; what the screen does with them is the self-test. Invented picks only.
/// </summary>
public class IslandMachineKeysTests
{
    private static Item Pick(string id) => new(id, "open", id[..2], 200, PickId: "program:" + id);

    private static Item Plus() => new("Add", "", "+", 0, IsPlus: true);

    private static IReadOnlyList<Item> Row() => [Pick("alpha"), Pick("beta"), Pick("gamma"), Plus()];

    private static Clock Opened(double idleSeconds = 60, bool keyboard = true)
    {
        var machine = new IslandMachine(idleSeconds, itemsOf: _ => Row());
        var c = new Clock(machine);
        if (keyboard) machine.MainKey(c.Now);
        else machine.ShowHideKey(c.Now);
        c.Run(2000);
        Assert.Equal(IslandPhase.Open, machine.Phase);
        return c;
    }

    private static void Left(IslandMachine m, double now, int times) { for (var i = 0; i < times; i++) m.MoveSelection(-1, now); }

    private static void Right(IslandMachine m, double now, int times) { for (var i = 0; i < times; i++) m.MoveSelection(1, now); }

    [Fact]
    public void Arrows_Move_Along_The_Row_And_Stop_At_The_Ends()
    {
        var c = Opened();
        var m = c.M;
        var start = m.SelectedItem;
        for (var i = 0; i < 10; i++) m.MoveSelection(-1, c.Now);
        Assert.Equal(0, m.SelectedItem);
        m.MoveSelection(1, c.Now);
        m.MoveSelection(1, c.Now);
        Assert.Equal(2, m.SelectedItem);
        for (var i = 0; i < 10; i++) m.MoveSelection(1, c.Now);
        Assert.Equal(3, m.SelectedItem); // the + tile is the last stop; no wrap
        Assert.True(start >= 0);
    }

    [Fact]
    public void Arrows_Typed_While_Opening_Are_Not_Lost()
    {
        var machine = new IslandMachine(60, itemsOf: _ => Row());
        var c = new Clock(machine);
        machine.MainKey(c.Now);
        c.Run(120); // still flying in: the contents are not showing yet
        Assert.False(machine.ContentsVisible);
        machine.MoveSelection(1, c.Now);
        machine.MoveSelection(1, c.Now);
        c.Run(2000);
        Assert.Equal(2, machine.SelectedItem);
    }

    [Fact]
    public void No_Key_Acts_Without_The_Keyboard()
    {
        var c = Opened(keyboard: false);
        var m = c.M;
        var before = m.SelectedItem;
        m.MoveSelection(1, c.Now);
        Assert.Equal(before, m.SelectedItem);
        Assert.False(m.AskRemove(c.Now));
        Assert.False(m.EnterSecondRow(3, c.Now));
        Assert.Equal(-1, m.SecondRowSelected);
    }

    [Fact]
    public void A_Key_Resets_The_Idle_Clock()
    {
        var c = Opened(idleSeconds: 5);
        var m = c.M;
        c.Run(1500);
        var before = m.IdleDeadlineMs;
        m.MoveSelection(1, c.Now);
        Assert.True(m.IdleDeadlineMs > before, "an arrow starts the idle time again");
        c.Run(1500);
        Assert.Equal(IslandPhase.Open, m.Phase);
        var again = m.IdleDeadlineMs;
        m.OtherKey(c.Now);
        Assert.True(m.IdleDeadlineMs > again);
    }

    [Fact]
    public void Delete_Twice_Removes_The_Same_Pick_And_Only_That_One()
    {
        var c = Opened();
        var m = c.M;
        Left(m, c.Now, 10);
        Assert.True(m.AskRemove(c.Now));
        Assert.Equal("program:alpha", m.PendingDeleteId);
        Assert.Equal("program:alpha", m.ConfirmRemove(c.Now));
        Assert.Null(m.PendingDeleteId);
        Assert.Null(m.ConfirmRemove(c.Now)); // nothing is waiting: a Delete that comes again only asks
    }

    [Fact]
    public void The_Selection_Moving_Forgets_The_Ask_And_A_Delete_On_Another_Pick_Removes_Nothing()
    {
        var c = Opened();
        var m = c.M;
        Left(m, c.Now, 10);
        Assert.True(m.AskRemove(c.Now));
        m.MoveSelection(1, c.Now);
        Assert.Null(m.PendingDeleteId);
        Assert.Null(m.ConfirmRemove(c.Now));

        Assert.True(m.AskRemove(c.Now));
        m.ItemClick(0, c.Now);
        Assert.Null(m.PendingDeleteId); // a click is any other key
    }

    [Fact]
    public void The_Ask_Belongs_To_The_Pick_Not_To_Its_Place()
    {
        // The row is laid out again with another pick in the selected place: the selected tile is another pick, so nothing is removed.
        var rows = new Queue<IReadOnlyList<Item>>([Row(), [Pick("delta"), Pick("beta"), Pick("gamma"), Plus()], [Pick("delta"), Pick("beta"), Pick("gamma"), Plus()]]);
        IReadOnlyList<Item> current = rows.Dequeue();
        var machine = new IslandMachine(60, itemsOf: _ => current);
        var c = new Clock(machine);
        machine.MainKey(c.Now);
        c.Run(2000);
        Left(machine, c.Now, 10);
        Assert.True(machine.AskRemove(c.Now));

        current = rows.Dequeue();
        machine.ContentsChanged(c.Now);
        Assert.Null(machine.PendingDeleteId); // laid out again: the ask is forgotten
        c.Run(2000);
        Assert.Null(machine.ConfirmRemove(c.Now));
    }

    [Fact]
    public void Delete_Does_Nothing_On_The_Plus_Tile_And_In_The_Second_Row()
    {
        var c = Opened();
        var m = c.M;
        Right(m, c.Now, 10); // the + tile
        Assert.False(m.AskRemove(c.Now));
        Left(m, c.Now, 10);
        m.ToggleSecondRow(c.Now);
        c.Run(500);
        Assert.True(m.EnterSecondRow(2, c.Now));
        Assert.False(m.AskRemove(c.Now));
    }

    [Fact]
    public void The_Second_Row_Selection_Moves_Stops_At_The_Ends_And_Ends_With_The_Row()
    {
        var c = Opened();
        var m = c.M;
        m.ToggleSecondRow(c.Now);
        c.Run(500);
        Assert.True(m.EnterSecondRow(3, c.Now));
        Assert.Equal(0, m.SecondRowSelected);
        m.MoveSecondRow(-1, 3, c.Now);
        Assert.Equal(0, m.SecondRowSelected);
        for (var i = 0; i < 6; i++) m.MoveSecondRow(1, 3, c.Now);
        Assert.Equal(2, m.SecondRowSelected);
        m.LeaveSecondRow(c.Now);
        Assert.Equal(-1, m.SecondRowSelected);
        Assert.False(m.SecondRowOpen);
    }

    [Fact]
    public void Pages_Are_Reached_By_Index_And_Wrap_Through_The_Pure_Rule()
    {
        var c = Opened();
        var m = c.M;
        Assert.True(m.PageCount >= 2);
        var next = IslandKeys.PageAfterTab(m.PageIndex, m.PageCount, backwards: false);
        m.PageKey(m.PageIdAt(next), c.Now);
        Assert.Equal(next, m.PageIndex);
        var last = IslandKeys.PageAfterTab(0, m.PageCount, backwards: true);
        Assert.Equal(m.PageCount - 1, last);
    }

    [Fact]
    public void Leaving_Or_Losing_The_Keyboard_Forgets_The_Ask()
    {
        var c = Opened();
        var m = c.M;
        Left(m, c.Now, 10);
        Assert.True(m.AskRemove(c.Now));
        m.FocusLost(c.Now);
        Assert.Null(m.PendingDeleteId);

        var d = Opened();
        Left(d.M, d.Now, 10);
        Assert.True(d.M.AskRemove(d.Now));
        d.M.EscapeKey(d.Now);
        Assert.Null(d.M.PendingDeleteId);
    }
}
