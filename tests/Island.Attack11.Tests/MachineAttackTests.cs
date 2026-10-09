using Island.Core;
using Island.Core.Terminals;

namespace Island.Attack11.Tests;

/// <summary>The machine with a frame clock, as the app drives it.</summary>
internal sealed class Clock(IslandMachine machine)
{
    public const double Frame = 1000.0 / 60;
    public double Now { get; private set; }

    public IslandMachine M { get; } = machine;

    public void RunTo(double ms)
    {
        while (Now + Frame <= ms)
        {
            Now += Frame;
            M.Tick(Now);
        }

        Now = ms;
        M.Tick(Now);
    }

    public void Run(double forMs) => RunTo(Now + forMs);
}

/// <summary>A rig: the six built-in pages, the Terminals page drawing from a list the test changes by hand (as the app's TerminalsPage does), the others from their placeholders.</summary>
internal sealed class TerminalRig
{
    public List<Item> Live { get; } = [];

    public List<int> Activated { get; } = [];

    public TerminalRig(int tiles = 0, bool keyboard = true, string startPage = PageIds.Media)
    {
        for (var i = 0; i < tiles; i++) Live.Add(Tile(100 + i));
        M = new IslandMachine(600, Pages.BuiltIn, page => page.Id == PageIds.Terminals ? [.. Live] : Pages.PlaceholderItems(page));
        C = new Clock(M);
        if (keyboard) M.MainKey(C.Now); else M.ShowHideKey(C.Now);
        C.Run(1500);
        if (startPage != PageIds.Media)
        {
            M.PageKey(startPage, C.Now);
            C.Run(1500);
        }
    }

    public IslandMachine M { get; }

    public Clock C { get; }

    public static Item Tile(long key, HelperState ring = HelperState.Idle) => new($"Alpha {key}", "terminal", "Al", 90, WindowKey: key, Ring: ring);

    public long? SelectedKey => M.SelectedItem >= 0 && M.SelectedItem < M.ContentsItems.Count ? M.ContentsItems[M.SelectedItem].WindowKey : null;

    public void Tiles(params long[] keys)
    {
        Live.Clear();
        Live.AddRange(keys.Select(k => Tile(k)));
        M.SelfFillingItemsChanged(C.Now);
    }

    /// <summary>The invariants of a machine that shows (or leaves) the Terminals page.</summary>
    public void AssertSound(string where)
    {
        var count = M.ContentsPageId == PageIds.Terminals ? Live.Count : M.ContentsItems.Count;
        Assert.True(M.SelectedItem >= 0 && (count == 0 ? M.SelectedItem == 0 : M.SelectedItem < count), $"{where}: selected {M.SelectedItem} of {count}");
        Assert.True(double.IsFinite(M.CapsuleTargetWidth) && M.CapsuleTargetWidth > 0, $"{where}: target width {M.CapsuleTargetWidth}");
        Assert.True(double.IsFinite(M.DrawnWidth) && double.IsFinite(M.DrawnHeight), $"{where}: drawn size");
        Assert.True(M.PendingDeleteId is null, $"{where}: a Delete is waiting on a tile that is not a pick");
    }
}

/// <summary>WORK-ORDER-11 section 1, how a page that changes by itself is drawn, and every key on the page: attacked through IslandMachine and IslandKeys (the controller is WPF and cannot run here).</summary>
public class MachineAttackTests
{
    private static Page TerminalsPage => Pages.Get(PageIds.Terminals);

    // ---- The selection is a window, not a place -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Selected_Tile_Stays_Selected_Wherever_It_Now_Stands()
    {
        var rig = new TerminalRig(startPage: PageIds.Terminals);
        rig.Tiles(1, 2, 3, 4);
        rig.M.ItemClick(2, rig.C.Now);
        Assert.Equal(3L, rig.SelectedKey);
        rig.Tiles(1, 3, 4); // a tile to the left of it closed
        Assert.Equal(1, rig.M.SelectedItem);
        Assert.Equal(3L, rig.SelectedKey);
        rig.Tiles(9, 8, 3, 4, 7); // new ones came to the left (not what the page does, but the machine must cope)
        Assert.Equal(2, rig.M.SelectedItem);
        Assert.Equal(3L, rig.SelectedKey);
    }

    [Fact]
    public void Holds_When_Its_Own_Window_Closes_The_Selection_Takes_The_Tile_In_Its_Place_Or_The_Last()
    {
        var rig = new TerminalRig(startPage: PageIds.Terminals);
        rig.Tiles(1, 2, 3, 4);
        rig.M.ItemClick(1, rig.C.Now);
        rig.Tiles(1, 3, 4);
        Assert.Equal(1, rig.M.SelectedItem);
        Assert.Equal(3L, rig.SelectedKey);
        rig.M.ItemClick(2, rig.C.Now);
        rig.Tiles(1, 3);
        Assert.Equal(1, rig.M.SelectedItem); // the last
        rig.M.ItemClick(0, rig.C.Now);
        rig.Tiles();
        rig.AssertSound("no tile at all");
        rig.Tiles(5);
        Assert.Equal(0, rig.M.SelectedItem);
        Assert.Equal(5L, rig.SelectedKey);
    }

    [Fact]
    public void Holds_A_Change_Of_The_Tiles_Is_Not_A_Page_Swap_And_Does_Not_Count_As_Use()
    {
        var rig = new TerminalRig(3, startPage: PageIds.Terminals);
        var swaps = rig.M.PageChangeCount;
        var deadline = rig.M.IdleDeadlineMs;
        rig.C.Run(100);
        rig.Tiles(1, 2, 3, 4, 5);
        Assert.Equal(swaps, rig.M.PageChangeCount);
        Assert.True(rig.M.ContentsVisible);
        Assert.Equal(deadline, rig.M.IdleDeadlineMs); // does not count as use
        rig.C.Run(1000);
        Assert.Equal(CapsuleLayout.SizeFor(rig.M.Contents).Width, rig.M.DrawnWidth, 0.5);
    }

    [Fact]
    public void Holds_The_Capsule_Width_Follows_The_Number_Of_Tiles_Up_To_The_Shown_Maximum()
    {
        var rig = new TerminalRig(startPage: PageIds.Terminals);
        var widths = new List<double>();
        foreach (var n in new[] { 0, 1, 3, 7, 8, 40, 100 })
        {
            rig.Tiles([.. Enumerable.Range(1, n).Select(i => (long)i)]);
            rig.C.Run(1500);
            widths.Add(rig.M.CapsuleTargetWidth);
            Assert.Equal(CapsuleLayout.SizeFor(rig.M.Contents).Width, rig.M.CapsuleTargetWidth, 0.01);
            rig.AssertSound($"{n} tiles");
        }

        Assert.Equal(widths[3], widths[4]); // more than seven slide in the same width
        Assert.Equal(widths[3], widths[6]);
        Assert.True(widths[1] < widths[2] && widths[2] < widths[3]);
    }

    // ---- Tiles coming and going on every step of a page change ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PageIds.Media, PageIds.Terminals)]
    [InlineData(PageIds.Terminals, PageIds.Media)]
    [InlineData(PageIds.Terminals, PageIds.Apps)]
    [InlineData(PageIds.Browser, PageIds.Terminals)]
    public void Holds_Tiles_Coming_And_Going_On_Every_Frame_Of_A_Page_Change_Leave_A_Sound_Machine(string from, string to)
    {
        var rng = new Random(7);
        for (var trial = 0; trial < 30; trial++)
        {
            var rig = new TerminalRig(rng.Next(0, 6), startPage: from);
            rig.M.PageKey(to, rig.C.Now);
            var next = 1000L;
            for (var step = 0; step < 90; step++)
            {
                rig.C.Run(1000.0 / 60);
                switch (rng.Next(4))
                {
                    case 0: rig.Live.Add(TerminalRig.Tile(next++)); rig.M.SelfFillingItemsChanged(rig.C.Now); break;
                    case 1 when rig.Live.Count > 0: rig.Live.RemoveAt(rng.Next(rig.Live.Count)); rig.M.SelfFillingItemsChanged(rig.C.Now); break;
                    case 2: rig.Live.Clear(); rig.M.SelfFillingItemsChanged(rig.C.Now); break;
                    default: break;
                }

                rig.AssertSound($"{from}->{to}, trial {trial}, step {step}");
            }

            rig.C.Run(2000);
            Assert.Equal(to, rig.M.ContentsPageId);
            Assert.True(rig.M.IsAtRest || rig.M.Phase == IslandPhase.Hidden, "at rest or gone");
            if (rig.M.Phase == IslandPhase.Open) Assert.Equal(CapsuleLayout.SizeFor(rig.M.Contents).Width, rig.M.CapsuleTargetWidth, 0.01);
            rig.AssertSound($"{from}->{to}, trial {trial}, rest");
        }
    }

    [Fact]
    public void Holds_Tiles_Changing_While_The_Island_Flies_In_Onto_The_Page()
    {
        var rig = new TerminalRig(2, keyboard: false);
        rig.M.ShowHideKey(rig.C.Now); // hide
        rig.C.Run(3000);
        Assert.Equal(IslandPhase.Hidden, rig.M.Phase);
        rig.M.PageKey(PageIds.Terminals, rig.C.Now);
        for (var i = 0; i < 40; i++)
        {
            rig.C.Run(1000.0 / 60);
            if (i % 5 == 0) { rig.Live.Add(TerminalRig.Tile(500 + i)); rig.M.SelfFillingItemsChanged(rig.C.Now); }
            if (i % 7 == 0 && rig.Live.Count > 0) { rig.Live.RemoveAt(0); rig.M.SelfFillingItemsChanged(rig.C.Now); }
            rig.AssertSound($"fly-in step {i}");
        }

        rig.C.Run(2000);
        Assert.Equal(CapsuleLayout.SizeFor(rig.M.Contents).Width, rig.M.CapsuleTargetWidth, 0.01);
    }

    [Fact]
    public void Holds_Tiles_Changing_While_The_Island_Is_Hidden_Never_Leave_A_Wrong_Selection_At_The_Next_Summon()
    {
        var rig = new TerminalRig(5, startPage: PageIds.Terminals);
        rig.M.ItemClick(4, rig.C.Now);
        rig.M.ShowHideKey(rig.C.Now);
        rig.C.Run(3000);
        Assert.Equal(IslandPhase.Hidden, rig.M.Phase);
        rig.Live.Clear();
        rig.Live.Add(TerminalRig.Tile(7));
        rig.M.SelfFillingItemsChanged(rig.C.Now);
        rig.M.MainKey(rig.C.Now);
        rig.C.Run(1500);
        rig.AssertSound("summoned");
        Assert.Equal(0, rig.M.SelectedItem);
    }

    [Fact]
    public void Defect_The_Second_Row_Can_Be_Opened_On_The_Terminals_Page_During_The_Swap_Onto_It()
    {
        // FINDING A11-05. ToggleSecondRow refuses only when ContentsPageId is the Terminals page, and ContentsPageId lags the page by the swap delay. A + click that reaches the
        // machine in that window (the old page's + tile is still on screen as the contents go out) opens the second row; the swap then lays the Terminals page out with
        // SecondRowOpen true and a two-row height. The work order: "On this page there is no + tile and no second row."
        var rig = new TerminalRig(2);
        rig.M.PageKey(PageIds.Terminals, rig.C.Now);
        rig.C.Run(30); // inside the swap delay: the contents of the old page are going out
        Assert.NotEqual(PageIds.Terminals, rig.M.ContentsPageId);
        rig.M.ToggleSecondRow(rig.C.Now);
        rig.C.Run(2000);
        Assert.Equal(PageIds.Terminals, rig.M.ContentsPageId);
        Assert.False(rig.M.SecondRowOpen);
    }

    [Fact]
    public void Holds_The_Second_Row_Cannot_Be_Opened_On_The_Page_Once_It_Is_Laid_Out()
    {
        var rig = new TerminalRig(2, startPage: PageIds.Terminals);
        rig.M.ToggleSecondRow(rig.C.Now);
        Assert.False(rig.M.SecondRowOpen);
    }

    [Fact]
    public void Holds_A_Page_Key_Closes_A_Second_Row_That_Was_Open_Before_The_Swap()
    {
        var rig = new TerminalRig(2);
        rig.M.ToggleSecondRow(rig.C.Now);
        Assert.True(rig.M.SecondRowOpen);
        rig.M.PageKey(PageIds.Terminals, rig.C.Now);
        Assert.False(rig.M.SecondRowOpen);
    }

    // ---- Every key on the page with no tile, and with a tile ------------------------------------------------------------------------------------------------

    private static readonly int[] EveryKey = [KeyCodes.Left, KeyCodes.Right, KeyCodes.Up, KeyCodes.Down, KeyCodes.Tab, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete, KeyCodes.Escape, KeyCodes.Digit1, KeyCodes.Digit9, 0x41, 0x08];

    // What IslandController.KeyContextOf makes of the machine on the Terminals page (read from the controller: a tile is "a pick" to the keys, the + click never acts here).
    private static KeyContext ContextOf(TerminalRig rig)
    {
        var m = rig.M;
        var items = m.ContentsItems;
        var selected = m.SelectedItem >= 0 && m.SelectedItem < items.Count ? items[m.SelectedItem] : null;
        return new KeyContext
        {
            HasKeyboard = m.HasKeyboard,
            SearchOpen = m.SearchOpen,
            SettingsShowing = false,
            TileLifted = false,
            IslandLeaving = m.Phase is IslandPhase.Hidden or IslandPhase.Closing,
            RowIsCurrentPage = m.Phase == IslandPhase.Open && m.ContentsVisible && m.ContentsPageId == m.PageId,
            PageChangeUnderWay = m.ContentsPageId != m.PageId,
            SecondRowOpen = m.SecondRowOpen,
            SelectionInSecondRow = m.SecondRowSelected >= 0,
            SecondRowHasItems = !m.SecondRowOpen,
            Selected = selected is null ? SelectedTile.None : selected.IsPlus ? SelectedTile.Plus : SelectedTile.Pick,
            AtFirst = m.SelectedItem <= 0,
            AtLast = m.SelectedItem >= items.Count - 1,
            PageCount = m.PageCount,
            PlusClickActs = m.Phase == IslandPhase.Open && !m.ShowsPill && !m.SearchOpen && m.ContentsPageId != PageIds.Terminals,
            MediaCanPlayPause = m.ContentsPageId == PageIds.Media,
            DeletePending = m.PendingDeleteId is not null,
            SelectedIsPendingPick = m.PendingDeleteId is not null && selected?.PickId == m.PendingDeleteId,
        };
    }

    // The same switch as IslandController.HandleKey, for the actions that touch the machine.
    private static void Apply(TerminalRig rig, KeyInput key)
    {
        var m = rig.M;
        var now = rig.C.Now;
        var decision = IslandKeys.Decide(key, ContextOf(rig));
        if (decision.CancelsPendingDelete) m.CancelPendingDelete();
        if (!decision.Handled)
        {
            if (key.VirtualKey == KeyCodes.Escape) m.EscapeKey(now);
            else if (key.VirtualKey is >= KeyCodes.Digit1 and <= KeyCodes.Digit9) m.DigitKey(key.VirtualKey - KeyCodes.Digit1 + 1, now);
            else m.OtherKey(now);
            return;
        }

        switch (decision.Action)
        {
            case KeyAction.MoveLeft or KeyAction.MoveRight: m.MoveSelection(decision.Action == KeyAction.MoveLeft ? -1 : 1, now); break;
            case KeyAction.OpenSecondRowAndEnter or KeyAction.OpenSecondRow: if (!m.SecondRowOpen) m.ToggleSecondRow(now); break;
            case KeyAction.NextPage or KeyAction.PreviousPage: m.PageKey(m.PageIdAt(IslandKeys.PageAfterTab(m.PageIndex, m.PageCount, decision.Action == KeyAction.PreviousPage)), now); break;
            case KeyAction.AskRemove: if (!m.AskRemove(now)) m.OtherKey(now); break;
            case KeyAction.ConfirmRemove: m.ConfirmRemove(now); break;
            case KeyAction.Activate: rig.Activated.Add(m.SelectedItem); break;
            default: m.OtherKey(now); break;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Holds_Every_Key_Plain_Shifted_And_Held_Never_Breaks_The_Page_Whatever_Tiles_It_Has(int tiles)
    {
        foreach (var key in EveryKey)
        foreach (var shift in new[] { false, true })
        foreach (var repeat in new[] { false, true })
        {
            var rig = new TerminalRig(tiles, startPage: PageIds.Terminals);
            var before = rig.M.ContentsItems.Count;
            Apply(rig, new KeyInput(key, Shift: shift, IsRepeat: repeat));
            rig.C.Run(1500);
            rig.AssertSound($"key {key:X} shift {shift} repeat {repeat} tiles {tiles}");
            Assert.False(rig.M.SecondRowOpen, $"key {key:X}: the page has no second row");
            if (rig.M.ContentsPageId == PageIds.Terminals) Assert.Equal(before, rig.M.ContentsItems.Count);
        }
    }

    [Fact]
    public void Holds_Delete_Twice_Does_Nothing_To_A_Tile_And_Space_And_Down_Only_Count_As_Use()
    {
        var rig = new TerminalRig(3, startPage: PageIds.Terminals);
        for (var i = 0; i < 2; i++) Apply(rig, new KeyInput(KeyCodes.Delete));
        Assert.Null(rig.M.PendingDeleteId);
        Assert.Equal(KeyAction.CountsAsUse, IslandKeys.Decide(new KeyInput(KeyCodes.Space), ContextOf(rig)).Action);
        Assert.Equal(KeyAction.CountsAsUse, IslandKeys.Decide(new KeyInput(KeyCodes.Down), ContextOf(rig)).Action);
        Assert.Equal(3, rig.Live.Count);
    }

    [Fact]
    public void Holds_Enter_Acts_Only_When_There_Is_A_Tile()
    {
        var empty = new TerminalRig(0, startPage: PageIds.Terminals);
        Assert.Equal(KeyAction.CountsAsUse, IslandKeys.Decide(new KeyInput(KeyCodes.Enter), ContextOf(empty)).Action);
        var one = new TerminalRig(1, startPage: PageIds.Terminals);
        Assert.Equal(KeyAction.Activate, IslandKeys.Decide(new KeyInput(KeyCodes.Enter), ContextOf(one)).Action);
    }

    [Fact]
    public void Holds_Left_And_Right_Stop_At_The_Ends_And_Tab_Goes_Round_To_The_Terminals_Page_And_Back()
    {
        var rig = new TerminalRig(3, startPage: PageIds.Terminals);
        for (var i = 0; i < 10; i++) rig.M.MoveSelection(-1, rig.C.Now);
        Assert.Equal(0, rig.M.SelectedItem);
        for (var i = 0; i < 10; i++) rig.M.MoveSelection(1, rig.C.Now);
        Assert.Equal(2, rig.M.SelectedItem);
        Apply(rig, new KeyInput(KeyCodes.Tab));
        rig.C.Run(1500);
        Assert.Equal(PageIds.Media, rig.M.PageId); // Terminals is the last page: Tab wraps to the first
        Apply(rig, new KeyInput(KeyCodes.Tab, Shift: true));
        rig.C.Run(1500);
        Assert.Equal(PageIds.Terminals, rig.M.PageId);
        Assert.Equal(6, rig.M.PageCount);
        Assert.Equal(PageIds.Terminals, rig.M.PageIdAt(5)); // the sixth digit reaches it
    }

    [Fact]
    public void Holds_The_Sixth_Digit_Reaches_The_Page_And_A_Seventh_Digit_Does_Nothing()
    {
        var rig = new TerminalRig(2);
        rig.M.DigitKey(6, rig.C.Now);
        rig.C.Run(1500);
        Assert.Equal(PageIds.Terminals, rig.M.PageId);
        rig.M.DigitKey(7, rig.C.Now);
        rig.C.Run(500);
        Assert.Equal(PageIds.Terminals, rig.M.PageId);
    }

    [Fact]
    public void Holds_A_Click_On_A_Tile_That_Is_Gone_Selects_Nothing_And_Throws_Nothing()
    {
        // a window closed between being listed and being clicked: the row the person clicked was built from the older list; the index may be past the end of the new one
        var rig = new TerminalRig(5, startPage: PageIds.Terminals);
        rig.Tiles(1, 2);
        rig.M.ItemClick(4, rig.C.Now);
        rig.M.ItemClick(-1, rig.C.Now);
        rig.M.ItemClick(int.MaxValue, rig.C.Now);
        rig.AssertSound("click past the end");
        Assert.True(rig.M.SelectedItem < 2);
    }

    [Fact]
    public void Holds_A_Terminals_Tile_Is_Never_Taken_For_A_Pick_By_The_Machine()
    {
        var rig = new TerminalRig(3, startPage: PageIds.Terminals);
        Assert.False(rig.M.AskRemove(rig.C.Now));
        Assert.Null(rig.M.ConfirmRemove(rig.C.Now));
        Assert.All(rig.M.ContentsItems, i => Assert.True(i.PickId is null && !i.IsPlus && i.WindowKey is not null));
        Assert.NotNull(TerminalsPage);
    }

    [Fact]
    public void Holds_A_Page_List_Without_Terminals_Is_Sound_And_A_List_That_Loses_The_Page_Falls_Back()
    {
        var rig = new TerminalRig(2, startPage: PageIds.Terminals);
        rig.M.SetPages([.. Pages.BuiltIn.Where(p => p.Id != PageIds.Terminals)], rig.C.Now);
        rig.C.Run(1500);
        Assert.NotEqual(PageIds.Terminals, rig.M.ContentsPageId);
        rig.M.SelfFillingItemsChanged(rig.C.Now); // a late call from the page that is no longer there
        Assert.Equal(5, rig.M.PageCount);
    }

    [Fact]
    public void Holds_Setting_The_Pages_Back_With_Terminals_On_Another_Position_Is_Sound()
    {
        var rig = new TerminalRig(2);
        var terminals = Pages.Get(PageIds.Terminals);
        rig.M.SetPages([terminals, .. Pages.BuiltIn.Where(p => p.Id != PageIds.Terminals)], rig.C.Now);
        rig.C.Run(1500);
        rig.M.DigitKey(1, rig.C.Now);
        rig.C.Run(1500);
        Assert.Equal(PageIds.Terminals, rig.M.PageId);
        rig.AssertSound("terminals first");
    }
}
