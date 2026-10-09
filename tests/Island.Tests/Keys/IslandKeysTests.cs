using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 section 2: the island by keyboard, as the pure function that decides what a key means.</summary>
public class IslandKeysTests
{
    private static readonly int[] OurKeys =
    [
        KeyCodes.Left, KeyCodes.Right, KeyCodes.Up, KeyCodes.Down,
        KeyCodes.Tab, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete,
    ];

    /// <summary>The island is at rest, called with the main key, on a page of three, the selection on a pick in the middle of the row.</summary>
    private static KeyContext Ready() => new()
    {
        HasKeyboard = true,
        RowIsCurrentPage = true,
        Selected = SelectedTile.Pick,
        PageCount = 3,
        PlusClickActs = true,
    };

    /// <summary>The second row is open, has things in it, and the selection is on one of them.</summary>
    private static KeyContext InSecondRow() => Ready() with { SecondRowOpen = true, SelectionInSecondRow = true, SecondRowHasItems = true };

    private static KeyDecision Press(int vk, KeyContext c, bool shift = false, bool repeat = false) =>
        IslandKeys.Decide(new KeyInput(vk, Shift: shift, IsRepeat: repeat), c);

    private static void AssertOnly(KeyAction action, KeyDecision d)
    {
        Assert.Equal(action, d.Action);
        Assert.True(d.Handled);
    }

    // ---- The keys, one by one ------------------------------------------------

    [Fact]
    public void Arrows_Move_Along_The_Row_And_Stop_At_The_Ends()
    {
        AssertOnly(KeyAction.MoveLeft, Press(KeyCodes.Left, Ready()));
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, Ready()));

        // The stop is a fact of the context (AtFirst, AtLast): at an end the key is taken and only counts as use; it never wraps.
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Left, Ready() with { AtFirst = true }));
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, Ready() with { AtFirst = true }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Right, Ready() with { AtLast = true }));
        AssertOnly(KeyAction.MoveLeft, Press(KeyCodes.Left, Ready() with { AtLast = true }));

        // The same along the second row.
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, InSecondRow()));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Right, InSecondRow() with { AtLast = true }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Left, InSecondRow() with { AtFirst = true }));
    }

    [Fact]
    public void Enter_Is_A_Click_On_The_Selected_Tile()
    {
        AssertOnly(KeyAction.Activate, Press(KeyCodes.Enter, Ready() with { Selected = SelectedTile.Pick }));
        AssertOnly(KeyAction.Activate, Press(KeyCodes.Enter, Ready() with { Selected = SelectedTile.Plus }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Enter, Ready() with { Selected = SelectedTile.None }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Enter, Ready(), shift: true)); // Shift+Enter is the second row's
    }

    [Fact]
    public void Down_Opens_The_Second_Row_And_Enters_It()
    {
        var withThings = Ready() with { SecondRowHasItems = true };
        AssertOnly(KeyAction.OpenSecondRowAndEnter, Press(KeyCodes.Down, withThings));

        // Nothing in the second row: it opens and the selection stays.
        AssertOnly(KeyAction.OpenSecondRow, Press(KeyCodes.Down, Ready()));

        // Where a click on the + tile would do nothing, Down only counts as use.
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Down, withThings with { PlusClickActs = false }));

        // Already open with the selection still in the first row: only the move into it is left.
        AssertOnly(KeyAction.OpenSecondRowAndEnter, Press(KeyCodes.Down, withThings with { SecondRowOpen = true, PlusClickActs = false }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Down, Ready() with { SecondRowOpen = true }));

        // In the second row there is nothing below.
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Down, InSecondRow()));
    }

    [Fact]
    public void Up_Leaves_And_Closes_The_Second_Row()
    {
        AssertOnly(KeyAction.LeaveSecondRow, Press(KeyCodes.Up, InSecondRow()));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Up, Ready()));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Up, Ready() with { SecondRowOpen = true })); // open, but the selection is in the first row
    }

    [Fact]
    public void Shift_Enter_In_The_Second_Row_Adds()
    {
        AssertOnly(KeyAction.SecondRowAdd, Press(KeyCodes.Enter, InSecondRow(), shift: true));
        AssertOnly(KeyAction.SecondRowJump, Press(KeyCodes.Enter, InSecondRow()));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Enter, InSecondRow() with { SecondRowHasItems = false }, shift: true));
    }

    [Fact]
    public void Tab_Goes_Round_The_Pages()
    {
        AssertOnly(KeyAction.NextPage, Press(KeyCodes.Tab, Ready()));
        AssertOnly(KeyAction.PreviousPage, Press(KeyCodes.Tab, Ready(), shift: true));
        AssertOnly(KeyAction.NextPage, Press(KeyCodes.Tab, InSecondRow())); // a second row that is open closes first, as on any page change
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Tab, Ready() with { PageCount = 1 }));

        // The wrap: from the last page Tab goes to the first, and the other way round.
        Assert.Equal(1, IslandKeys.PageAfterTab(0, 5, backwards: false));
        Assert.Equal(0, IslandKeys.PageAfterTab(4, 5, backwards: false));
        Assert.Equal(4, IslandKeys.PageAfterTab(0, 5, backwards: true));
        Assert.Equal(3, IslandKeys.PageAfterTab(4, 5, backwards: true));
        Assert.Equal(0, IslandKeys.PageAfterTab(0, 1, backwards: false));
        Assert.Equal(7, IslandKeys.PageAfterTab(7, 5, backwards: false)); // an index outside the pages is left for the machine to clamp
    }

    [Fact]
    public void Space_Plays_Or_Pauses_Only_On_Media()
    {
        AssertOnly(KeyAction.PlayPause, Press(KeyCodes.Space, Ready() with { MediaCanPlayPause = true }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Space, Ready())); // any other page, or nothing playing
    }

    [Fact]
    public void Space_Never_Opens_Search()
    {
        // The key is taken whole: the existing path (which would offer it to search) never sees it.
        foreach (var media in new[] { false, true })
            Assert.True(Press(KeyCodes.Space, Ready() with { MediaCanPlayPause = media }).Handled);

        // And as a typed character: only spaces, control characters and characters that draw as nothing never open search.
        foreach (var blank in new[] { " ", "  ", "\t", "\r", "\n", "\r\n", "\u001b", "\0", " ", "​", "⠀", "", null })
            Assert.False(IslandKeys.IsOpeningText(blank));
        foreach (var text in new[] { "a", "Z", "5", "ă", "😀", " a" })
            Assert.True(IslandKeys.IsOpeningText(text));
    }

    [Fact]
    public void Delete_Twice_Removes_And_Anything_Else_Cancels()
    {
        var first = Press(KeyCodes.Delete, Ready());
        AssertOnly(KeyAction.AskRemove, first);
        Assert.False(first.CancelsPendingDelete);

        var pending = Ready() with { DeletePending = true, SelectedIsPendingPick = true };
        var second = Press(KeyCodes.Delete, pending);
        AssertOnly(KeyAction.ConfirmRemove, second);

        // Any other key, plain or not, cancels the ask: arrows, Enter, Space, Tab, Up, Down, a digit, a letter, a modifier.
        foreach (var vk in new[] { KeyCodes.Left, KeyCodes.Right, KeyCodes.Up, KeyCodes.Down, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Tab, 0x33, 0x61, 0x41, 0x10, 0x08 })
        {
            Assert.True(Press(vk, pending).CancelsPendingDelete, $"key 0x{vk:X}");
            Assert.NotEqual(KeyAction.ConfirmRemove, Press(vk, pending).Action);
        }

        // At the ends and in the other cases of "counts as use" the ask is cancelled too.
        Assert.True(Press(KeyCodes.Left, pending with { AtFirst = true }).CancelsPendingDelete);
        Assert.True(Press(KeyCodes.Enter, pending, shift: true).CancelsPendingDelete);
        Assert.True(Press(KeyCodes.Tab, pending with { PageCount = 1 }).CancelsPendingDelete);
        Assert.True(IslandKeys.Decide(new KeyInput(KeyCodes.Right, Ctrl: true), pending).CancelsPendingDelete);
        Assert.True(Press(KeyCodes.Delete, pending, shift: true).CancelsPendingDelete);
        Assert.True(Press(KeyCodes.Delete, pending with { PageCount = 3, TileLifted = true }).CancelsPendingDelete);

        // With no ask pending nothing needs cancelling.
        Assert.False(Press(KeyCodes.Left, Ready()).CancelsPendingDelete);
    }

    [Fact]
    public void A_Held_Delete_Never_Removes()
    {
        // The first press asks; the auto-repeats that follow neither remove nor cancel the ask.
        AssertOnly(KeyAction.AskRemove, Press(KeyCodes.Delete, Ready()));
        var asked = Ready() with { DeletePending = true, SelectedIsPendingPick = true };
        for (var i = 0; i < 50; i++)
        {
            var d = Press(KeyCodes.Delete, asked, repeat: true);
            AssertOnly(KeyAction.CountsAsUse, d);
            Assert.False(d.CancelsPendingDelete);
        }

        // Nor does a held Delete ask or remove when nothing is pending.
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Delete, Ready(), repeat: true));
    }

    [Fact]
    public void A_Second_Delete_On_Another_Pick_Removes_Nothing()
    {
        var elsewhere = Ready() with { DeletePending = true, SelectedIsPendingPick = false };
        var d = Press(KeyCodes.Delete, elsewhere);
        AssertOnly(KeyAction.CountsAsUse, d);
        Assert.True(d.CancelsPendingDelete); // the old ask is gone; the new pick has to be asked about anew
    }

    [Fact]
    public void Enter_During_A_Page_Change_Opens_Nothing()
    {
        foreach (var busy in new[] { Ready() with { PageChangeUnderWay = true }, Ready() with { RowIsCurrentPage = false } })
        {
            foreach (var vk in new[] { KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete, KeyCodes.Down, KeyCodes.Up })
                AssertOnly(KeyAction.CountsAsUse, Press(vk, busy with { MediaCanPlayPause = true, SecondRowHasItems = true, SecondRowOpen = true, SelectionInSecondRow = true }));
            AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Enter, busy, shift: true));
        }
    }

    [Fact]
    public void Arrows_Typed_While_Opening_Are_Not_Lost()
    {
        // The island has the keyboard but the row is not laid out yet (opening, or a page change): the arrows still move.
        var opening = Ready() with { RowIsCurrentPage = false, PageChangeUnderWay = true };
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, opening));
        AssertOnly(KeyAction.MoveLeft, Press(KeyCodes.Left, opening));
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, opening, repeat: true));
    }

    [Fact]
    public void A_Repeat_Acts_Only_For_Left_And_Right()
    {
        var all = Ready() with { MediaCanPlayPause = true, SecondRowHasItems = true };
        AssertOnly(KeyAction.MoveLeft, Press(KeyCodes.Left, all, repeat: true));
        AssertOnly(KeyAction.MoveRight, Press(KeyCodes.Right, all, repeat: true));
        foreach (var vk in new[] { KeyCodes.Up, KeyCodes.Down, KeyCodes.Tab, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete, KeyCodes.Escape, 0x33, 0x63, 0x41 })
        {
            AssertOnly(KeyAction.CountsAsUse, Press(vk, all, repeat: true));
            AssertOnly(KeyAction.CountsAsUse, Press(vk, InSecondRow(), repeat: true));
        }

        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Tab, all, shift: true, repeat: true));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Enter, InSecondRow(), shift: true, repeat: true));
    }

    [Fact]
    public void No_Key_Acts_With_Ctrl_Alt_Or_Win_Held()
    {
        var all = InSecondRow() with { MediaCanPlayPause = true, DeletePending = true, SelectedIsPendingPick = true };
        foreach (var vk in OurKeys)
        {
            foreach (var input in new[]
            {
                new KeyInput(vk, Ctrl: true), new KeyInput(vk, Alt: true), new KeyInput(vk, Win: true),
                new KeyInput(vk, Shift: true, Ctrl: true, Alt: true, Win: true),
            })
            {
                var d = IslandKeys.Decide(input, all);
                Assert.Equal(KeyAction.CountsAsUse, d.Action);
                Assert.True(d.Handled);
            }
        }

        // The settings screen showing is the same: these keys do nothing.
        foreach (var vk in OurKeys)
            AssertOnly(KeyAction.CountsAsUse, Press(vk, Ready() with { SettingsShowing = true, MediaCanPlayPause = true, SecondRowHasItems = true }));
    }

    [Fact]
    public void Delete_Does_Nothing_On_The_Plus_Tile()
    {
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Delete, Ready() with { Selected = SelectedTile.Plus }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Delete, Ready() with { Selected = SelectedTile.None }));
        AssertOnly(KeyAction.CountsAsUse, Press(KeyCodes.Delete, InSecondRow())); // nor in the second row
        // With an ask pending, the + tile only cancels it.
        var d = Press(KeyCodes.Delete, Ready() with { Selected = SelectedTile.Plus, DeletePending = true });
        AssertOnly(KeyAction.CountsAsUse, d);
        Assert.True(d.CancelsPendingDelete);
    }

    [Fact]
    public void Escape_Cancels_A_Pending_Delete_First()
    {
        var pending = Ready() with { DeletePending = true, SelectedIsPendingPick = true };
        var d = Press(KeyCodes.Escape, pending);
        AssertOnly(KeyAction.CancelPendingDelete, d);
        Assert.True(d.CancelsPendingDelete);

        // Even with the second row open, the pending Delete goes first; the row stays for the next Esc.
        AssertOnly(KeyAction.CancelPendingDelete, Press(KeyCodes.Escape, pending with { SecondRowOpen = true }));

        // Nothing pending: Esc is the existing code's, unchanged (close the row, else send the island away).
        var plain = Press(KeyCodes.Escape, Ready());
        Assert.Equal(KeyAction.Nothing, plain.Action);
        Assert.False(plain.Handled);

        // A lifted tile: Esc cancels the drag first, which is the existing code's.
        var lifted = Press(KeyCodes.Escape, pending with { TileLifted = true });
        Assert.Equal(KeyAction.Nothing, lifted.Action);
        Assert.False(lifted.Handled);
    }

    [Fact]
    public void No_Key_Acts_Without_The_Keyboard()
    {
        foreach (var context in new[]
        {
            Ready() with { HasKeyboard = false, MediaCanPlayPause = true, SecondRowHasItems = true },
            Ready() with { IslandLeaving = true, MediaCanPlayPause = true, SecondRowHasItems = true },
        })
        {
            foreach (var vk in OurKeys.Append(KeyCodes.Escape).Append(0x31).Append(0x41))
            {
                var d = Press(vk, context);
                Assert.Equal(KeyAction.Nothing, d.Action);
                Assert.False(d.Handled);
            }
        }
    }

    // ---- What is left to the existing code -----------------------------------

    [Fact]
    public void Search_Digits_And_Other_Keys_Stay_With_The_Existing_Code()
    {
        // Search open: all keys are search's, repeats too (a held Backspace goes on deleting).
        foreach (var vk in OurKeys.Append(0x08).Append(0x41))
        {
            var d = Press(vk, Ready() with { SearchOpen = true }, repeat: true);
            Assert.Equal(KeyAction.Nothing, d.Action);
            Assert.False(d.Handled);
        }

        // Digits (both rows of them), letters and modifiers: not ours, the existing path runs.
        foreach (var vk in new[] { 0x30, 0x31, 0x39, 0x60, 0x61, 0x69, 0x41, 0x10, 0x11, 0x12, 0x5B, 0x08 })
        {
            var d = Press(vk, Ready());
            Assert.Equal(KeyAction.Nothing, d.Action);
            Assert.False(d.Handled);
        }

        // A lifted tile: every key but Esc only counts as use.
        foreach (var vk in OurKeys.Append(0x31))
            AssertOnly(KeyAction.CountsAsUse, Press(vk, Ready() with { TileLifted = true }));
    }

    // ---- Sweeps ------------------------------------------------------------------

    /// <summary>Every precondition a decision's action needs, written out once, independently of the function.</summary>
    private static void Need(bool ok, string what, KeyInput key, KeyDecision d)
    {
        if (!ok) Assert.Fail($"{what}: key {key} gave {d}");
    }

    private static void AssertPreconditions(KeyInput key, KeyContext c, KeyDecision d)
    {
        var vk = key.VirtualKey;
        var ours = Array.IndexOf(OurKeys, vk) >= 0;
        var inSecond = c.SecondRowOpen && c.SelectionInSecondRow;
        var ready = c.RowIsCurrentPage && !c.PageChangeUnderWay;
        var heldDelete = vk == KeyCodes.Delete && key.IsRepeat && c.HasKeyboard && !c.IslandLeaving && !c.SearchOpen && !c.TileLifted;

        // The pending Delete: cancelled by every key but the first Delete, a held Delete and the Delete that removes.
        var expectCancel = c.DeletePending && d.Action != KeyAction.ConfirmRemove && !heldDelete;
        Need(d.CancelsPendingDelete == expectCancel, "cancel flag", key, d);

        if (d.Action is KeyAction.Nothing or KeyAction.CountsAsUse)
        {
            Need(d.Action == KeyAction.CountsAsUse || !d.Handled || !c.HasKeyboard, "precondition", key, d);
            if (d.Action == KeyAction.CountsAsUse) Need(d.Handled, "precondition", key, d);
            return;
        }

        Need(d.Handled, "precondition", key, d);
        Need(c.HasKeyboard && !c.IslandLeaving && !c.SearchOpen, "precondition", key, d);

        if (d.Action == KeyAction.CancelPendingDelete)
        {
            Need(vk == KeyCodes.Escape && !key.IsRepeat && !c.TileLifted && c.DeletePending, "precondition", key, d);
            return;
        }

        Need(ours, "precondition", key, d);
        Need(!c.TileLifted, "precondition", key, d);
        Need(!c.SettingsShowing, "precondition", key, d);
        Need(!(key.Ctrl || key.Alt || key.Win), "precondition", key, d);
        Need(!key.IsRepeat || vk is KeyCodes.Left or KeyCodes.Right, "a repeat acted", key, d);

        switch (d.Action)
        {
            case KeyAction.MoveLeft: Need(vk == KeyCodes.Left && !key.Shift && !c.AtFirst, "precondition", key, d); break;
            case KeyAction.MoveRight: Need(vk == KeyCodes.Right && !key.Shift && !c.AtLast, "precondition", key, d); break;
            case KeyAction.Activate: Need(vk == KeyCodes.Enter && !key.Shift && ready && !inSecond && c.Selected != SelectedTile.None, "precondition", key, d); break;
            case KeyAction.OpenSecondRowAndEnter:
                Need(vk == KeyCodes.Down && !key.Shift && ready && !inSecond && c.SecondRowHasItems && (c.SecondRowOpen || c.PlusClickActs), "precondition", key, d);
                break;
            case KeyAction.OpenSecondRow:
                Need(vk == KeyCodes.Down && !key.Shift && ready && !c.SecondRowOpen && c.PlusClickActs && !c.SecondRowHasItems, "precondition", key, d);
                break;
            case KeyAction.LeaveSecondRow: Need(vk == KeyCodes.Up && !key.Shift && ready && inSecond, "precondition", key, d); break;
            case KeyAction.SecondRowJump: Need(vk == KeyCodes.Enter && !key.Shift && ready && inSecond && c.SecondRowHasItems, "precondition", key, d); break;
            case KeyAction.SecondRowAdd: Need(vk == KeyCodes.Enter && key.Shift && ready && inSecond && c.SecondRowHasItems, "precondition", key, d); break;
            case KeyAction.NextPage: Need(vk == KeyCodes.Tab && !key.Shift && c.PageCount >= 2, "precondition", key, d); break;
            case KeyAction.PreviousPage: Need(vk == KeyCodes.Tab && key.Shift && c.PageCount >= 2, "precondition", key, d); break;
            case KeyAction.PlayPause: Need(vk == KeyCodes.Space && !key.Shift && ready && c.MediaCanPlayPause, "precondition", key, d); break;
            case KeyAction.AskRemove:
                Need(vk == KeyCodes.Delete && !key.Shift && ready && !inSecond && c.Selected == SelectedTile.Pick && !c.DeletePending, "precondition", key, d);
                break;
            case KeyAction.ConfirmRemove:
                Need(vk == KeyCodes.Delete && !key.Shift && ready && !inSecond && c.Selected == SelectedTile.Pick
                     && c.DeletePending && c.SelectedIsPendingPick, "precondition", key, d);
                break;
            default: Assert.Fail($"unexpected action {d.Action}"); break;
        }
    }

    private static KeyContext FromBits(int bits, SelectedTile selected, int pageCount) => new()
    {
        HasKeyboard = (bits & 1) != 0,
        SearchOpen = (bits & 2) != 0,
        SettingsShowing = (bits & 4) != 0,
        TileLifted = (bits & 8) != 0,
        IslandLeaving = (bits & 16) != 0,
        RowIsCurrentPage = (bits & 32) != 0,
        PageChangeUnderWay = (bits & 64) != 0,
        SecondRowOpen = (bits & 128) != 0,
        SelectionInSecondRow = (bits & 256) != 0,
        SecondRowHasItems = (bits & 512) != 0,
        AtFirst = (bits & 1024) != 0,
        AtLast = (bits & 2048) != 0,
        PlusClickActs = (bits & 4096) != 0,
        MediaCanPlayPause = (bits & 8192) != 0,
        DeletePending = (bits & 16384) != 0,
        SelectedIsPendingPick = (bits & 32768) != 0,
        Selected = selected,
        PageCount = pageCount,
    };

    [Fact]
    public void Sweep_Every_Context_Against_The_Keys_That_Matter()
    {
        var keys = OurKeys.Append(KeyCodes.Escape).Append(0x31).ToArray();
        var modifiers = new (bool Shift, bool Ctrl)[] { (false, false), (true, false), (false, true) };
        var seen = new HashSet<KeyAction>();
        foreach (var selected in Enum.GetValues<SelectedTile>())
        {
            for (var bits = 0; bits < 1 << 16; bits++)
            {
                var context = FromBits(bits, selected, pageCount: 3);
                foreach (var vk in keys)
                    foreach (var (shift, ctrl) in modifiers)
                        foreach (var repeat in new[] { false, true })
                        {
                            var key = new KeyInput(vk, Shift: shift, Ctrl: ctrl, IsRepeat: repeat);
                            var d = IslandKeys.Decide(key, context);
                            AssertPreconditions(key, context, d);
                            seen.Add(d.Action);
                        }
            }
        }

        // The sweep reaches every action, so the checks above were not vacuous.
        foreach (var action in Enum.GetValues<KeyAction>()) Assert.Contains(action, seen);
    }

    [Fact]
    public void Sweep_Every_Key_With_Every_Modifier_Never_Throws_And_Unlisted_Keys_Never_Act()
    {
        var random = new Random(20261007);
        for (var n = 0; n < 1200; n++)
        {
            var context = FromBits(random.Next(1 << 16), (SelectedTile)random.Next(3), random.Next(-1, 5));
            for (var vk = -2; vk <= 260; vk++)
                for (var mods = 0; mods < 16; mods++)
                    foreach (var repeat in new[] { false, true })
                    {
                        var key = new KeyInput(vk, (mods & 1) != 0, (mods & 2) != 0, (mods & 4) != 0, (mods & 8) != 0, repeat);
                        var d = IslandKeys.Decide(key, context);
                        AssertPreconditions(key, context, d);
                        var listed = Array.IndexOf(OurKeys, vk) >= 0 || vk == KeyCodes.Escape;
                        if (!listed) Assert.True(d.Action is KeyAction.Nothing or KeyAction.CountsAsUse, $"key {vk}");
                    }
        }
    }

    [Fact]
    public void Key_Codes_Are_The_Documented_Virtual_Keys()
    {
        // Microsoft Learn, "Virtual-Key Codes", read 2026-10-07.
        Assert.Equal(0x09, KeyCodes.Tab);
        Assert.Equal(0x0D, KeyCodes.Enter);
        Assert.Equal(0x1B, KeyCodes.Escape);
        Assert.Equal(0x20, KeyCodes.Space);
        Assert.Equal(0x25, KeyCodes.Left);
        Assert.Equal(0x26, KeyCodes.Up);
        Assert.Equal(0x27, KeyCodes.Right);
        Assert.Equal(0x28, KeyCodes.Down);
        Assert.Equal(0x2E, KeyCodes.Delete);
        Assert.Equal(0x31, KeyCodes.Digit1);
        Assert.Equal(0x39, KeyCodes.Digit9);
        Assert.Equal(0x61, KeyCodes.Pad1);
        Assert.Equal(0x69, KeyCodes.Pad9);
    }
}
