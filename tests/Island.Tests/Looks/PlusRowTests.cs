using Island.Core;

namespace Island.Tests;

public class PlusRowTests
{
    private static OpenSnapshot Open(params string[] exes) =>
        new([.. exes.Select((e, i) => new OpenWindow(100 + i, e, null, "invented", i))], [], [], false);

    private static PickStore Store() => new([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);

    [Fact]
    public void Lists_Open_Things_That_Are_Not_Picks()
    {
        var entries = PlusRow.Entries(Open("alpha.exe", "beta.exe", "gamma.exe"), Store(), []);

        Assert.Equal(["program:beta.exe", "program:gamma.exe"], entries.Select(e => e.Key)); // the picked one is not offered
        Assert.Equal("2 open", PlusRow.AddSubtitle(entries.Count));
        Assert.Empty(PlusRow.Entries(Open("alpha.exe"), Store(), []));
        Assert.Equal("Nothing else is open", PlusRow.NothingElse);
        Assert.True(PlusRow.Entries(Open(Enumerable.Range(0, 100).Select(i => $"p{i}.exe").ToArray()), PickStore.Empty, []).Count <= PlusRow.MaxEntries);
    }

    [Fact]
    public void Add_Moves_It_To_The_First_Row()
    {
        var store = Store();
        var open = Open("alpha.exe", "beta.exe");
        var beta = PlusRow.Entries(open, store, []).Single();

        var after = PlusRow.AddedTo(store, beta, PageIds.Apps);

        Assert.Equal(2, after.ForPage(PageIds.Apps).Count); // it appears in the first row, on the page being shown
        Assert.Empty(PlusRow.Entries(open, after, [])); // and it leaves the second
        Assert.Same(after, PlusRow.AddedTo(after, beta, PageIds.Apps).ForPage(PageIds.Apps).Count == 2 ? after : null); // adding it again changes nothing

        // An entry that cannot be stored is not added.
        var odd = new PlusEntry("site:localhost", PickKind.Site, "localhost", null, null, null, "localhost", 1, [1]);
        Assert.Same(after, PlusRow.AddedTo(after, odd, PageIds.Apps));
    }

    [Fact]
    public void Jump_Adds_Nothing()
    {
        var store = Store();
        var beta = PlusRow.Entries(Open("alpha.exe", "beta.exe"), store, []).Single();

        Assert.Equal(101, PlusRow.JumpTarget(beta)); // the window of that program, once
        Assert.Single(store.Picks); // the jump has no way to touch the picks
        Assert.Null(PlusRow.JumpTarget(new PlusEntry("program:none", PickKind.Program, "None", "none.exe", null, null, null, 0, [])));
    }

    [Fact]
    public void One_Escape_Does_One_Thing_In_Order()
    {
        // The rule, from the first of these that applies.
        Assert.Equal(EscapeAction.CancelDrag, EscapeRule.Decide(dragging: true, secondRowOpen: true));
        Assert.Equal(EscapeAction.CancelDrag, EscapeRule.Decide(dragging: true, secondRowOpen: false));
        Assert.Equal(EscapeAction.CloseRow, EscapeRule.Decide(dragging: false, secondRowOpen: true));
        Assert.Equal(EscapeAction.Dismiss, EscapeRule.Decide(dragging: false, secondRowOpen: false));

        // In the machine: the first Esc closes the row and leaves the island, the second sends it away.
        var m = new IslandMachine();
        m.MainKey(0);
        for (var t = 100; t < 1500; t += 10) m.Tick(t);
        Assert.True(m.IsAtRest);

        m.ToggleSecondRow(1500);
        Assert.True(m.SecondRowOpen);
        m.EscapeKey(1510);
        Assert.False(m.SecondRowOpen);
        Assert.Equal(IslandPhase.Open, m.Phase);
        Assert.True(m.HasKeyboard);

        m.EscapeKey(1520);
        Assert.Equal(IslandPhase.Closing, m.Phase);
    }

    [Fact]
    public void A_Page_Change_Closes_The_Row_First()
    {
        var m = new IslandMachine();
        m.MainKey(0);
        for (var t = 100; t < 1500; t += 10) m.Tick(t);
        m.ToggleSecondRow(1500);
        Assert.True(m.SecondRowOpen);
        Assert.Equal(ChoiceConstants.TwoRowHeight, m.Height.Target);

        m.DigitKey(2, 1600); // the second page

        Assert.False(m.SecondRowOpen);
        Assert.Equal(PageIds.Folders, m.PageId);
        Assert.True(m.RowClosedOrder > 0 && m.RowClosedOrder < m.PageChangedOrder, $"row closed {m.RowClosedOrder}, page changed {m.PageChangedOrder}");
        Assert.Equal(LookConstants.CapsuleHeight, m.Height.Target); // the height heads back to one row at once
        for (var t = 1700; t < 3500; t += 10) m.Tick(t);
        Assert.Equal(PageIds.Folders, m.ContentsPageId);
        Assert.Equal(LookConstants.CapsuleHeight, m.Height.Value, 1);
    }

    [Fact]
    public void The_Row_Opens_Only_While_The_Capsule_Is_Open_And_Closes_With_The_Island()
    {
        var m = new IslandMachine();
        m.ToggleSecondRow(0);
        Assert.False(m.SecondRowOpen); // hidden: nothing to grow

        m.MainKey(10);
        m.ToggleSecondRow(20);
        Assert.False(m.SecondRowOpen); // still flying in
        for (var t = 100; t < 1500; t += 10) m.Tick(t);

        m.ToggleSecondRow(1500);
        Assert.True(m.SecondRowOpen);
        m.ToggleSecondRow(1510); // a second click on the + closes it
        Assert.False(m.SecondRowOpen);
        m.ToggleSecondRow(1520);
        Assert.True(m.SecondRowOpen);

        m.MainKey(1530); // the island leaves: the row goes with it
        Assert.False(m.SecondRowOpen);
        Assert.Equal(IslandPhase.Closing, m.Phase);

        // A new summon starts with one row.
        for (var t = 1600; t < 4000; t += 10) m.Tick(t);
        m.MainKey(4000);
        for (var t = 4100; t < 5500; t += 10) m.Tick(t);
        Assert.False(m.SecondRowOpen);
        Assert.Equal(LookConstants.CapsuleHeight, m.Height.Value, 1);
    }

    [Fact]
    public void The_Numbers_Of_The_Second_Row_Are_Pinned()
    {
        Assert.Equal(150, ChoiceConstants.TwoRowHeight);
        Assert.Equal(2 * ChoiceConstants.RowHeight + 2 * LookConstants.BorderWidth, ChoiceConstants.TwoRowHeight); // two rows of 74 and the border
        Assert.Equal(76, LookConstants.CapsuleHeight);
        Assert.Equal(0.14, ChoiceConstants.RowLineAlpha);
        Assert.Equal(12, ChoiceConstants.RowLabelSize);
        Assert.Equal(0.85, ChoiceConstants.RowLabelAlpha);
        Assert.Equal(16, ChoiceConstants.BadgeSize);
        Assert.Equal(5, ChoiceConstants.BadgeReach);
        Assert.Equal(200, ChoiceConstants.RowFadeInMs);
        Assert.Equal(4, StripLayout.TilesThatFit(4 * 48 - 8));
        Assert.Equal(0, StripLayout.TilesThatFit(10));
    }
}
