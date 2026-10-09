using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>WORK-ORDER-10 section 2 attacked: the pure function over every combination, the machine under every key in every state, Delete, Tab, text.</summary>
public class KeyAttackTests
{
    private static readonly int[] AllKeys =
    [
        KeyCodes.Left, KeyCodes.Right, KeyCodes.Up, KeyCodes.Down, KeyCodes.Tab, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete, KeyCodes.Escape,
        0x41, KeyCodes.Digit1, KeyCodes.Pad1, 0x08, 0x10, 0x11, 0x74,
    ];

    private static readonly KeyInput[] Modifiers =
    [
        new(0), new(0, Shift: true), new(0, Ctrl: true), new(0, Alt: true), new(0, Win: true), new(0, Shift: true, Ctrl: true),
    ];

    private static KeyInput With(int vk, KeyInput mods, bool repeat) => mods with { VirtualKey = vk, IsRepeat = repeat };

    // ---- The pure function --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Decide_Obeys_Its_Own_Rules_For_A_Million_Random_Contexts()
    {
        var rng = new Random(1007);
        bool Flip() => rng.Next(2) == 0;
        for (var n = 0; n < 1_000_000; n++)
        {
            var key = new KeyInput(AllKeys[rng.Next(AllKeys.Length)], rng.Next(5) == 0, rng.Next(8) == 0, rng.Next(8) == 0, rng.Next(10) == 0, rng.Next(3) == 0);
            var context = new KeyContext
            {
                HasKeyboard = rng.Next(5) != 0, SearchOpen = rng.Next(6) == 0, SettingsShowing = rng.Next(8) == 0, TileLifted = rng.Next(8) == 0, IslandLeaving = rng.Next(8) == 0,
                RowIsCurrentPage = rng.Next(4) != 0, PageChangeUnderWay = rng.Next(4) == 0, SecondRowOpen = Flip(), SelectionInSecondRow = Flip(), SecondRowHasItems = Flip(),
                Selected = (SelectedTile)rng.Next(3), AtFirst = Flip(), AtLast = Flip(), PageCount = rng.Next(0, 4), PlusClickActs = Flip(), MediaCanPlayPause = Flip(),
                DeletePending = Flip(), SelectedIsPendingPick = Flip(),
            };

            var d = IslandKeys.Decide(key, context);
            var tag = $"{key} {context}";

            if (!d.Handled) Assert.Equal(KeyAction.Nothing, d.Action);
            if (d.Action == KeyAction.Nothing) Assert.False(d.Handled, tag);
            if (!context.HasKeyboard || context.IslandLeaving || context.SearchOpen)
            {
                Assert.Equal(KeyAction.Nothing, d.Action);
                Assert.False(d.Handled);
                continue;
            }

            var acts = d.Action is not (KeyAction.Nothing or KeyAction.CountsAsUse or KeyAction.CancelPendingDelete);
            if (acts)
            {
                Assert.False(context.TileLifted, tag);
                Assert.False(key.Ctrl || key.Alt || key.Win, tag);
                Assert.False(context.SettingsShowing, tag);
                if (key.IsRepeat) Assert.True(d.Action is KeyAction.MoveLeft or KeyAction.MoveRight, tag);
            }

            switch (d.Action)
            {
                case KeyAction.MoveLeft: Assert.True(!context.AtFirst && !key.Shift && key.VirtualKey == KeyCodes.Left, tag); break;
                case KeyAction.MoveRight: Assert.True(!context.AtLast && !key.Shift && key.VirtualKey == KeyCodes.Right, tag); break;
                case KeyAction.Activate:
                    Assert.True(key.VirtualKey == KeyCodes.Enter && !key.Shift && context.Selected != SelectedTile.None && context.RowIsCurrentPage && !context.PageChangeUnderWay, tag);
                    Assert.False(context.SecondRowOpen && context.SelectionInSecondRow, tag);
                    break;
                case KeyAction.SecondRowJump or KeyAction.SecondRowAdd:
                    Assert.True(context.SecondRowOpen && context.SelectionInSecondRow && context.SecondRowHasItems && context.RowIsCurrentPage && !context.PageChangeUnderWay, tag);
                    Assert.Equal(key.Shift, d.Action == KeyAction.SecondRowAdd);
                    break;
                case KeyAction.AskRemove:
                    Assert.True(key.VirtualKey == KeyCodes.Delete && !context.DeletePending && context.Selected == SelectedTile.Pick && !key.Shift && context.RowIsCurrentPage && !context.PageChangeUnderWay, tag);
                    Assert.False(context.SecondRowOpen && context.SelectionInSecondRow, tag);
                    break;
                case KeyAction.ConfirmRemove:
                    Assert.True(key.VirtualKey == KeyCodes.Delete && context.DeletePending && context.SelectedIsPendingPick && context.Selected == SelectedTile.Pick && !key.Shift && !key.IsRepeat
                                && context.RowIsCurrentPage && !context.PageChangeUnderWay, tag);
                    Assert.False(context.SecondRowOpen && context.SelectionInSecondRow, tag);
                    Assert.False(d.CancelsPendingDelete); // it uses the ask up itself
                    break;
                case KeyAction.PlayPause:
                    Assert.True(key.VirtualKey == KeyCodes.Space && context.MediaCanPlayPause && context.RowIsCurrentPage && !context.PageChangeUnderWay && !key.Shift, tag);
                    break;
                case KeyAction.NextPage or KeyAction.PreviousPage:
                    Assert.True(key.VirtualKey == KeyCodes.Tab && context.PageCount >= 2 && key.Shift == (d.Action == KeyAction.PreviousPage), tag);
                    break;
                case KeyAction.OpenSecondRow or KeyAction.OpenSecondRowAndEnter:
                    Assert.True(key.VirtualKey == KeyCodes.Down && context.RowIsCurrentPage && !context.PageChangeUnderWay && !key.Shift, tag);
                    break;
                case KeyAction.LeaveSecondRow:
                    Assert.True(key.VirtualKey == KeyCodes.Up && context.SecondRowOpen && context.SelectionInSecondRow, tag);
                    break;
            }

            // A pending Delete is forgotten by every key except a held Delete and the Delete that uses it up.
            if (context.DeletePending && key.VirtualKey != KeyCodes.Escape && !context.TileLifted)
            {
                var keepsIt = key.VirtualKey == KeyCodes.Delete && key.IsRepeat;
                if (d.Action == KeyAction.ConfirmRemove || keepsIt) Assert.False(d.CancelsPendingDelete, tag);
                else Assert.True(d.CancelsPendingDelete, tag);
            }
        }
    }

    [Fact]
    public void Holds_Page_After_Tab_Goes_Round_And_Back()
    {
        for (var count = 0; count <= 7; count++)
        for (var index = -3; index <= 9; index++)
        {
            var next = IslandKeys.PageAfterTab(index, count, backwards: false);
            var back = IslandKeys.PageAfterTab(index, count, backwards: true);
            if (count < 2 || index < 0 || index >= count)
            {
                Assert.Equal(index, next);
                Assert.Equal(index, back);
                continue;
            }

            Assert.InRange(next, 0, count - 1);
            Assert.InRange(back, 0, count - 1);
            Assert.NotEqual(index, next);
            Assert.Equal(index, IslandKeys.PageAfterTab(next, count, backwards: true));
            var walk = index;
            for (var i = 0; i < count; i++) walk = IslandKeys.PageAfterTab(walk, count, backwards: false);
            Assert.Equal(index, walk);
        }

        Assert.Equal(0, IslandKeys.PageAfterTab(int.MaxValue - 1, int.MaxValue, backwards: false)); // no overflow at the top of the range
        Assert.Equal(int.MaxValue - 1, IslandKeys.PageAfterTab(0, int.MaxValue, backwards: true));
    }

    private static object[] Cp(params int[] codePoints) => [string.Concat(codePoints.Select(char.ConvertFromUtf32))];

    public static IEnumerable<object[]> BlankTexts =>
    [
        Cp(0x200B), Cp(0x200E), Cp(0x200F), Cp(0x202E), Cp(0x2066, 0x2069), Cp(0xFEFF), Cp(0), Cp(9), Cp(13, 10), Cp(8), Cp(0x7F), Cp(0x1B), Cp(0x20), Cp(0xA0), Cp(0x2028), Cp(0x3000),
        Cp(0x2800), Cp(0x3164), Cp(0x115F, 0x1160), Cp(0xFFA0), Cp(0x0301), Cp(0xAD), Cp(0x180E), Cp(0x034F), Cp(0xFE0F), Cp(0xE0020), Cp(0x20, 0x200B, 9, 0, 0x202E, 0x20),
    ];

    public static IEnumerable<object[]> DrawnTexts =>
    [
        Cp('a'), Cp('1'), Cp(0xE9), Cp(0x1F600), Cp(0x05D0), Cp(0x200F, 'a'), Cp(0x20, 'a', 0x20), Cp(0x0301, 'a'), Cp('?'), Cp(0x2423),
    ];

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void Holds_Text_That_Draws_As_Nothing_Does_Not_Open_Search(string text) => Assert.False(IslandKeys.IsOpeningText(text));

    [Theory]
    [MemberData(nameof(DrawnTexts))]
    public void Holds_Text_That_Draws_Opens_Search(string text) => Assert.True(IslandKeys.IsOpeningText(text));

    [Fact]
    public void Holds_Null_And_Empty_Text_Do_Not_Open_Search()
    {
        Assert.False(IslandKeys.IsOpeningText(null));
        Assert.False(IslandKeys.IsOpeningText(string.Empty));
    }

    // ---- The machine, with the controller's wiring -----------------------------------------------------------------------------------------------

    private static IEnumerable<(string Name, Func<KeyRig> Build)> States()
    {
        yield return ("open", () => new KeyRig());
        yield return ("opening", () => new KeyRig(settleMs: 150));
        yield return ("no keyboard", () => new KeyRig(keyboard: false));
        yield return ("hidden", () =>
        {
            var r = new KeyRig();
            r.M.MainKey(r.C.Now);
            r.C.Run(4000);
            return r;
        });
        yield return ("leaving", () =>
        {
            var r = new KeyRig();
            r.M.MainKey(r.C.Now);
            r.C.Run(100);
            return r;
        });
        yield return ("second row open, selection in the first row", () =>
        {
            var r = new KeyRig();
            r.M.ToggleSecondRow(r.C.Now);
            r.C.Run(600);
            return r;
        });
        yield return ("second row open, selection in it", () =>
        {
            var r = new KeyRig();
            r.M.ToggleSecondRow(r.C.Now);
            r.C.Run(600);
            r.M.EnterSecondRow(r.Row2Count, r.C.Now);
            return r;
        });
        yield return ("second row open and empty", () =>
        {
            var r = new KeyRig { Row2Tiles = 0 };
            r.M.ToggleSecondRow(r.C.Now);
            r.C.Run(600);
            return r;
        });
        yield return ("delete pending", () =>
        {
            var r = new KeyRig();
            r.Key(new KeyInput(KeyCodes.Delete));
            return r;
        });
        yield return ("search open", () =>
        {
            var r = new KeyRig();
            r.M.OpenSearch(500, r.C.Now);
            r.C.Run(700);
            return r;
        });
        yield return ("a tile lifted", () => new KeyRig { Lifted = true });
        yield return ("settings showing", () => new KeyRig { SettingsShowing = true });
        yield return ("a page with no picks", () =>
        {
            var r = new KeyRig();
            r.M.PageKey("other", r.C.Now);
            r.C.Run(900);
            return r;
        });
        yield return ("a page of forty", () =>
        {
            var r = new KeyRig();
            var list = r.PicksOf(KeyRig.AppsPage);
            list.Clear();
            list.AddRange(Enumerable.Range(1, 40).Select(i => "q" + i));
            r.M.ContentsChanged(r.C.Now);
            r.C.Run(900);
            return r;
        });
        yield return ("media page", () =>
        {
            var r = new KeyRig();
            r.M.PageKey(PageIds.Media, r.C.Now);
            r.C.Run(900);
            return r;
        });
        yield return ("mid page change", () =>
        {
            var r = new KeyRig();
            r.M.PageKey(PageIds.Media, r.C.Now);
            r.C.Run(30);
            return r;
        });
        yield return ("the pill", () =>
        {
            var r = new KeyRig();
            r.M.SetPill(true, true, r.C.Now);
            r.M.MainKey(r.C.Now);
            r.C.Run(3000);
            return r;
        });
    }

    private static void CheckMachine(KeyRig r, string tag)
    {
        var m = r.M;
        Assert.True(m.SecondRowSelected < 0 || m.SecondRowOpen, $"{tag}: a second row selection outlived its row");
        if (!m.SecondRowOpen) Assert.Equal(-1, m.SecondRowSelected);
        if (m.PendingDeleteId is not null)
        {
            Assert.True(m.HasKeyboard, $"{tag}: a pending Delete without the keyboard");
            Assert.Equal(IslandPhase.Open, m.Phase);
        }

        Assert.InRange(m.PageIndex, 0, m.PageCount - 1);
        Assert.True(r.Removed.Count <= 1, tag);
        if (m.Phase == IslandPhase.Open && m.ContentsVisible)
            Assert.InRange(m.SelectedItem, 0, Math.Max(0, m.ContentsItems.Count - 1));
    }

    [Fact]
    public void Holds_Every_Key_In_Every_State_With_Every_Modifier_And_Repeat_Throws_Nothing_And_Keeps_The_Machine_Sane()
    {
        foreach (var (name, build) in States())
        foreach (var vk in AllKeys)
        foreach (var mods in Modifiers)
        foreach (var repeat in new[] { false, true })
        {
            var r = build();
            var tag = $"{name}: {With(vk, mods, repeat)}";
            r.Key(With(vk, mods, repeat));
            r.Frame();
            r.C.Run(300);
            r.Frame();
            CheckMachine(r, tag);
            if (!r.M.HasKeyboard || r.SettingsShowing || r.Lifted) Assert.Empty(r.Removed);
        }
    }

    [Fact]
    public void Holds_Random_Key_Storms_Never_Remove_A_Pick_That_Was_Not_Asked_About()
    {
        var rng = new Random(424242);
        for (var round = 0; round < 60; round++)
        {
            var r = new KeyRig();
            for (var step = 0; step < 400; step++)
            {
                var pick = rng.Next(100);
                if (pick < 70)
                {
                    var key = new KeyInput(AllKeys[rng.Next(AllKeys.Length)], rng.Next(8) == 0, rng.Next(14) == 0, rng.Next(14) == 0, rng.Next(20) == 0, rng.Next(4) == 0);
                    var before = r.M.PendingDeleteId;
                    var asked = r.LastAsked;
                    var removedBefore = r.Removed.Count;
                    r.Key(key);
                    if (r.Removed.Count > removedBefore)
                    {
                        // The pick that went is the one the last first Delete asked about, which was still pending and still selected.
                        Assert.Equal(before, r.Removed[^1]);
                        Assert.Equal(asked, r.Removed[^1]);
                        Assert.False(key.IsRepeat);
                        Assert.Null(r.M.PendingDeleteId);
                    }
                }
                else if (pick < 78) r.Text(new[] { "a", " ", "\t", ((char)0x200B).ToString(), "9" }[rng.Next(5)]);
                else if (pick < 85) r.C.Run(rng.Next(5, 700));
                else if (pick < 88) r.Row2Tiles = rng.Next(0, 5);
                else if (pick < 90) r.Lifted = !r.Lifted;
                else if (pick < 92) r.SettingsShowing = !r.SettingsShowing;
                else if (pick < 94) r.M.FocusLost(r.C.Now);
                else if (pick < 96) r.M.MainKey(r.C.Now);
                else if (pick < 97) r.M.CloseSearch(r.C.Now);
                else if (pick < 98)
                {
                    // the store changes under the island, sometimes with the island told and sometimes not
                    var list = r.PicksOf(r.M.ContentsPageId);
                    if (list.Count > 0 && rng.Next(2) == 0) list.RemoveAt(rng.Next(list.Count));
                    else list.Add("n" + rng.Next(10_000));
                    if (rng.Next(2) == 0) r.M.ContentsChanged(r.C.Now);
                }
                else r.M.SetPages(rng.Next(2) == 0 ? KeyRig.ThreePages : [KeyRig.ThreePages[0]], r.C.Now);

                r.Frame();
                var m = r.M;
                Assert.True(m.SecondRowSelected < 0 || m.SecondRowOpen, "a second row selection outlived its row");
                if (m.PendingDeleteId is not null) Assert.True(m.HasKeyboard);
                Assert.InRange(m.PageIndex, 0, m.PageCount - 1);
                Assert.True(m.SelectedItem >= 0);
            }
        }
    }

    [Fact]
    public void Holds_A_Held_Delete_Never_Removes_And_One_Press_Removes_At_Most_One()
    {
        var r = new KeyRig();
        r.Key(new KeyInput(KeyCodes.Delete)); // asks about p1
        Assert.Equal("p1", r.M.PendingDeleteId);
        for (var i = 0; i < 50; i++) r.Key(new KeyInput(KeyCodes.Delete, IsRepeat: true));
        Assert.Empty(r.Removed);
        Assert.Equal("p1", r.M.PendingDeleteId); // a held key neither removes nor cancels

        r.Key(new KeyInput(KeyCodes.Delete)); // pressed anew: removes p1
        for (var i = 0; i < 50; i++) r.Key(new KeyInput(KeyCodes.Delete, IsRepeat: true)); // and the key is still held
        Assert.Equal(["p1"], r.Removed);
        Assert.Null(r.M.PendingDeleteId);
    }

    [Fact]
    public void Holds_Delete_On_The_Last_Pick_Of_A_Page_Leaves_The_Plus_Tile_Selected_And_A_Third_Delete_Does_Nothing()
    {
        var r = new KeyRig();
        r.PicksOf(KeyRig.AppsPage).RemoveRange(1, 3); // only p1 is left
        r.M.ContentsChanged(r.C.Now);
        r.C.Run(900);
        Assert.Equal(0, r.M.SelectedItem);

        r.Key(new KeyInput(KeyCodes.Delete));
        r.Key(new KeyInput(KeyCodes.Delete));
        r.C.Run(900);
        Assert.Equal(["p1"], r.Removed);
        Assert.Equal(0, r.M.SelectedItem);
        Assert.True(r.M.ContentsItems[r.M.SelectedItem].IsPlus);

        r.Key(new KeyInput(KeyCodes.Delete));
        r.Key(new KeyInput(KeyCodes.Delete));
        Assert.Equal(["p1"], r.Removed);
        Assert.Null(r.M.PendingDeleteId);
    }

    [Fact]
    public void Holds_A_Pending_Delete_Whose_Row_Changes_Under_It_Removes_Nothing_Else()
    {
        // The store changes without the island being told: the selected index now holds another pick. The second Delete must not take it.
        var r = new KeyRig();
        r.Key(new KeyInput(KeyCodes.Right)); // p2
        r.Key(new KeyInput(KeyCodes.Delete));
        Assert.Equal("p2", r.M.PendingDeleteId);
        r.PicksOf(KeyRig.AppsPage).RemoveAt(0); // p1 gone: index 1 is p3 now
        r.Key(new KeyInput(KeyCodes.Delete));
        Assert.Empty(r.Removed);
        Assert.Contains("p3", r.PicksOf(KeyRig.AppsPage));
        Assert.Null(r.M.PendingDeleteId); // and a Delete on another pick only cancels; it does not ask again

        // The same, with the pending pick itself removed elsewhere and the island told.
        var s = new KeyRig();
        s.Key(new KeyInput(KeyCodes.Delete)); // p1 asked
        s.PicksOf(KeyRig.AppsPage).Remove("p1");
        s.M.ContentsChanged(s.C.Now);
        s.C.Run(900);
        Assert.Null(s.M.PendingDeleteId);
        s.Key(new KeyInput(KeyCodes.Delete)); // asks about p2, which is selected now: not a removal
        Assert.Empty(s.Removed);
    }

    [Fact]
    public void Holds_Tab_At_Every_Step_Of_A_Page_Change_And_Enter_During_It_Opens_Nothing()
    {
        foreach (var gap in new[] { 0, 5, 16, 33, 80, 150, 400, 900 })
        {
            var r = new KeyRig();
            var seen = new List<int>();
            for (var press = 0; press < 12; press++)
            {
                var before = r.ItemsActivated;
                r.Key(new KeyInput(KeyCodes.Tab, Shift: press % 5 == 4));
                r.C.Run(gap);
                r.Frame();
                seen.Add(r.M.PageIndex);
                Assert.InRange(r.M.PageIndex, 0, 2);
                Assert.Equal(before, r.ItemsActivated);

                // Enter pressed now: only when the contents of the page now showing are laid out may it act.
                var contentsReady = r.M.Phase == IslandPhase.Open && r.M.ContentsVisible && r.M.ContentsPageId == r.M.PageId;
                var activatedBefore = r.ItemsActivated;
                r.Key(new KeyInput(KeyCodes.Enter));
                if (!contentsReady) Assert.Equal(activatedBefore, r.ItemsActivated);
                r.C.Run(gap);
                CheckMachine(r, $"tab storm gap {gap} press {press}");
            }

            // every press moved exactly one page, in the direction asked
            for (var i = 1; i < seen.Count; i++)
                Assert.NotEqual(seen[i - 1], seen[i]);
        }
    }

    [Fact]
    public void Holds_Tab_Closes_The_Second_Row_And_Forgets_Its_Selection()
    {
        var r = new KeyRig();
        r.M.ToggleSecondRow(r.C.Now);
        r.C.Run(600);
        r.M.EnterSecondRow(3, r.C.Now);
        Assert.Equal(0, r.M.SecondRowSelected);
        r.Key(new KeyInput(KeyCodes.Tab));
        Assert.False(r.M.SecondRowOpen);
        Assert.Equal(-1, r.M.SecondRowSelected);
    }

    [Fact]
    public void Holds_Items_That_Shrink_Under_The_Machine_Do_Not_Break_It()
    {
        var r = new KeyRig();
        for (var i = 0; i < 3; i++) r.Key(new KeyInput(KeyCodes.Right)); // p4
        r.Key(new KeyInput(KeyCodes.Delete)); // asks about p4
        r.PicksOf(KeyRig.AppsPage).Clear(); // the store empties, the island is not told
        r.Key(new KeyInput(KeyCodes.Delete));
        r.Key(new KeyInput(KeyCodes.Enter));
        r.Key(new KeyInput(KeyCodes.Left));
        r.Key(new KeyInput(KeyCodes.Right));
        Assert.Empty(r.Removed);
        r.M.ContentsChanged(r.C.Now);
        r.C.Run(900);
        Assert.Equal(0, r.M.SelectedItem);
        Assert.True(r.M.ContentsItems[0].IsPlus);
    }

    [Fact]
    public void Holds_Ctrl_Alt_Win_Settings_Lifted_And_Search_Make_Every_Key_Inert()
    {
        foreach (var vk in new[] { KeyCodes.Left, KeyCodes.Right, KeyCodes.Up, KeyCodes.Down, KeyCodes.Tab, KeyCodes.Enter, KeyCodes.Space, KeyCodes.Delete })
        {
            foreach (var mods in new[] { new KeyInput(0, Ctrl: true), new KeyInput(0, Alt: true), new KeyInput(0, Win: true) })
            {
                var r = new KeyRig();
                r.M.PageKey(PageIds.Media, r.C.Now);
                r.C.Run(900);
                var (sel, page, rowOpen) = (r.M.SelectedItem, r.M.PageId, r.M.SecondRowOpen);
                r.Key(With(vk, mods, false));
                r.Key(With(vk, mods, false));
                Assert.Equal((sel, page, rowOpen), (r.M.SelectedItem, r.M.PageId, r.M.SecondRowOpen));
                Assert.Empty(r.Removed);
                Assert.Equal(0, r.PlayPauses);
            }

            var settings = new KeyRig { SettingsShowing = true };
            settings.Key(new KeyInput(vk));
            settings.Key(new KeyInput(vk));
            Assert.Equal(0, settings.M.SelectedItem);
            Assert.Empty(settings.Removed);

            var lifted = new KeyRig { Lifted = true };
            lifted.Key(new KeyInput(vk));
            lifted.Key(new KeyInput(vk));
            Assert.Equal(0, lifted.M.SelectedItem);
            Assert.Empty(lifted.Removed);
            Assert.False(lifted.M.SecondRowOpen);
        }
    }

    [Fact]
    public void Holds_Space_And_Enter_And_Tab_As_Text_Never_Open_Search()
    {
        var r = new KeyRig();
        foreach (var text in new[] { " ", "\r", "\n", "\t", "\u001B", "\u0008", "\u007F" }) r.Text(text);
        Assert.False(r.M.SearchOpen);
        r.Text("x");
        Assert.True(r.M.SearchOpen);
    }

    [Fact]
    public void Holds_Every_Key_That_Reaches_An_Open_Island_With_The_Keyboard_Resets_The_Idle_Clock()
    {
        var builders = new (string Name, Func<KeyRig> Build)[]
        {
            ("open", () => new KeyRig()),
            ("opening", () => new KeyRig(settleMs: 150)),
            ("second row, selection in it", () =>
            {
                var r = new KeyRig();
                r.M.ToggleSecondRow(r.C.Now);
                r.C.Run(600);
                r.M.EnterSecondRow(r.Row2Count, r.C.Now);
                return r;
            }),
            ("delete pending", () =>
            {
                var r = new KeyRig();
                r.Key(new KeyInput(KeyCodes.Delete));
                return r;
            }),
            ("a page with no picks", () =>
            {
                var r = new KeyRig();
                r.M.PageKey("other", r.C.Now);
                r.C.Run(900);
                return r;
            }),
            ("media", () =>
            {
                var r = new KeyRig();
                r.M.PageKey(PageIds.Media, r.C.Now);
                r.C.Run(900);
                return r;
            }),
            ("a tile lifted", () => new KeyRig { Lifted = true }),
            ("settings showing", () => new KeyRig { SettingsShowing = true }),
        };
        foreach (var (name, build) in builders)
        foreach (var vk in AllKeys.Where(k => k != KeyCodes.Escape))
        foreach (var mods in Modifiers)
        foreach (var repeat in new[] { false, true })
        {
            var r = build();
            r.C.Run(7000); // the idle deadline is now well behind the clock
            var before = r.M.IdleDeadlineMs;
            Assert.Equal(IslandPhase.Open, r.M.Phase);
            r.Key(With(vk, mods, repeat));
            if (r.M.Phase != IslandPhase.Open) continue; // a key that dismissed the island ends the clock on purpose
            Assert.True(r.M.IdleDeadlineMs > before, $"{name}: {With(vk, mods, repeat)} did not reset the idle clock");
        }
    }

    // ---- Defects ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_A_Left_Press_Does_Not_Reset_The_Idle_Clock_When_The_Second_Row_Has_Emptied_Under_The_Selection()
    {
        // WORK-ORDER-10 section 2: "Every key resets the idle clock". The selection sits on the third tile of the second row; a window closes and the row has no tile left.
        // Left is decided as a move (the selected index is not the first), but MoveSecondRow returns before it pokes the clock when the row has no tiles.
        var r = new KeyRig { Row2Tiles = 3 };
        r.M.ToggleSecondRow(r.C.Now);
        r.C.Run(600);
        r.M.EnterSecondRow(3, r.C.Now);
        r.Key(new KeyInput(KeyCodes.Right));
        r.Key(new KeyInput(KeyCodes.Right));
        Assert.Equal(2, r.M.SecondRowSelected);

        r.Row2Tiles = 0;
        r.C.Run(5000); // the clock runs on
        var deadline = r.M.IdleDeadlineMs;
        r.Key(new KeyInput(KeyCodes.Left));
        Assert.True(r.M.IdleDeadlineMs > deadline, "the key was taken but the idle clock was not reset");
    }

    [Fact]
    public void Defect_The_Wheel_Does_Not_Cancel_A_Pending_Delete()
    {
        // WORK-ORDER-10 section 2: "Anything else cancels: any other key, a click, the wheel, the selection moving, the row being laid out again, the island leaving."
        // IslandController.Wheel only slides the strip and calls Activity(), and Activity does not touch the pending Delete (the mouse moving over the capsule calls it too).
        var source = Repo.Text("src", "Island.App", "IslandController.cs");
        var wheel = Repo.Body(source, "public void Wheel(");
        Assert.Contains("CancelPendingDelete", wheel);
    }

    [Fact]
    public void Defect_A_Row_Laid_Out_Again_By_A_Changed_Item_Does_Not_Cancel_A_Pending_Delete()
    {
        // The same sentence: "the row being laid out again". ContentsChanged (the pick list changed) clears it through SwitchWhileOpen, but a status change (a window opened or closed)
        // goes through ItemsChanged -> RedrawItemsIfChanged, which rebuilds the row without telling the machine, so the ask survives the new layout.
        var source = Repo.Text("src", "Island.App", "IslandController.cs");
        var redraw = Repo.Body(source, "private void RedrawItemsIfChanged(");
        Assert.Contains("CancelPendingDelete", redraw);
    }
}
