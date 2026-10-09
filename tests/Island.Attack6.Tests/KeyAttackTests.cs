using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack6.Tests;

/// <summary>WO6 section 4: KeybindEditor, SettingsSession keys, PickKeyHandler. The invariant behind most tests: no two things share a key, and what Windows holds is exactly what the settings say.</summary>
public class KeyAttackTests
{
    private static readonly HotkeyCombo[] Pool =
    [
        HotkeyCombo.Parse("Ctrl+Alt+A"), HotkeyCombo.Parse("Ctrl+Alt+B"), HotkeyCombo.Parse("Ctrl+Alt+C"),
        HotkeyCombo.Parse("Ctrl+Shift+D"), HotkeyCombo.Parse("Alt+F9"), HotkeyCombo.Parse("Ctrl+Q"),
    ];

    private static IEnumerable<HotkeyCombo> AllCombos(Settings s) =>
        new[] { s.ShowHide }.Concat(s.PageKeys.Where(k => k.Combo is not null).Select(k => k.Combo!.Value)).Concat(s.PickKeys.Select(k => k.Combo));

    private static string Describe(Settings s) =>
        "main " + s.ShowHide + " idle " + s.IdleSeconds + " pages " + string.Join(",", s.PageKeys.Select(k => k.PageId + "=" + k.Combo)) + " picks " + string.Join(",", s.PickKeys.Select(k => k.PickId + "=" + k.Combo));

    /// <summary>The same keys, idle time and glass, whatever the order of the lists (the order is a separate finding).</summary>
    private static bool SameKeys(Settings a, Settings b) =>
        a.ShowHide == b.ShowHide && a.IdleSeconds == b.IdleSeconds && a.Glass == b.Glass && a.StartWithWindows == b.StartWithWindows
        && a.PageKeys.Where(k => k.Combo is not null).ToDictionary(k => k.PageId, k => k.Combo).OrderBy(p => p.Key).SequenceEqual(b.PageKeys.Where(k => k.Combo is not null).ToDictionary(k => k.PageId, k => k.Combo).OrderBy(p => p.Key))
        && a.PickKeys.OrderBy(k => k.PickId).SequenceEqual(b.PickKeys.OrderBy(k => k.PickId));

    private static void AssertConsistent(SettingsSession session, PretendRegistrar registrar, string when)
    {
        var all = AllCombos(session.Settings).ToList();
        Assert.True(all.Distinct().Count() == all.Count, $"{when}: two things share a key: {string.Join(", ", all)}");
        Assert.True(registrar.Held.OrderBy(c => c.ToString()).SequenceEqual(all.Distinct().OrderBy(c => c.ToString())),
            $"{when}: Windows holds [{string.Join(", ", registrar.Held)}] but the settings say [{string.Join(", ", all)}]");
        // (A key an action already has is asked of Windows again on purpose, see Defect_Giving_An_Action_The_Key_It_Already_Has...; the real host holds it once.)
        foreach (var k in session.Settings.PickKeys) Assert.True(session.Picks.ById(k.PickId) is not null, $"{when}: a key is held for a pick that is gone ({k.PickId})");
        foreach (var k in session.Settings.PageKeys.Where(k => k.Combo is not null)) Assert.True(session.Pages.ById(k.PageId) is not null, $"{when}: a key is held for a page that is gone ({k.PageId})");
    }

    private static readonly Pick Alpha = Make.Program("Alpha", PageIds.Apps, "alpha.exe");
    private static readonly Pick Beta = Make.Program("Beta", PageIds.Apps, "beta.exe");
    private static readonly Pick Gamma = Make.Program("Gamma", PageIds.Media, "gamma.exe");
    private static readonly Pick Delta = Pick.ForSite("Delta", "example.org", PageIds.Browser);
    private static readonly Pick Folder = Pick.ForFolder("Downloads", PageIds.Folders);
    private static readonly Pick[] Picks = [Alpha, Beta, Gamma, Delta, Folder];

    private static (SettingsSession Session, PretendRegistrar Registrar) Fresh(TempFolder temp, IEnumerable<Pick>? picks = null)
    {
        var registrar = new PretendRegistrar();
        registrar.TryRegister(Settings.Defaults.ShowHide, out _); // the app holds the main key from its start
        return (Make.Session(temp, registrar, picks ?? Picks), registrar);
    }

    // ---- the same combination offered in every order ---------------------------------------------------------------

    [Fact]
    public void Holds_The_Same_Combination_Offered_To_Main_Page_And_Pick_In_Every_Order_And_Count_Leaves_One_Holder()
    {
        string[] actions = [KeybindEditor.MainId, PageIds.Apps, Alpha.Id];
        foreach (var order in Permutations(actions))
            foreach (var repeats in new[] { 1, 2, 5 })
            {
                using var temp = new TempFolder();
                var (session, registrar) = Fresh(temp);
                var combo = HotkeyCombo.Parse("Ctrl+Alt+A");
                var holders = 0;
                for (var r = 0; r < repeats; r++)
                    foreach (var action in order)
                    {
                        var result = session.PressKey(action, Combos.Press(combo));
                        if (result.Changed) holders++;
                        AssertConsistent(session, registrar, $"{string.Join(">", order)} x{repeats}");
                    }

                // The first to ask wins; the same action asking again is a no-op; the others are refused with a reason.
                Assert.Equal(1, holders);
                Assert.Equal(1, AllCombos(session.Settings).Count(c => c == combo));
            }
    }

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (var i = 0; i < items.Length; i++)
            foreach (var rest in Permutations([.. items.Where((_, j) => j != i)]))
                yield return [items[i], .. rest];
    }

    [Fact]
    public void Holds_A_Refused_Duplicate_Names_The_Owner_And_Changes_Nothing()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        Assert.True(session.PressKey(PageIds.Apps, Combos.Press(Combos.A)).Changed);
        var before = session.Settings;
        var held = registrar.Registered;
        var refused = session.PressKey(Alpha.Id, Combos.Press(Combos.A));
        Assert.False(refused.Ok);
        Assert.False(refused.Changed);
        Assert.Equal(before, session.Settings);
        Assert.Equal(held, registrar.Registered); // the duplicate was stopped before it reached Windows
        var againMain = session.PressKey(KeybindEditor.MainId, Combos.Press(Combos.A));
        Assert.False(againMain.Ok);
    }

    [Fact]
    public void Holds_Random_Storm_Of_Key_And_Pick_And_Page_Operations_Keeps_Windows_And_Settings_And_File_In_Step()
    {
        foreach (var seed in new[] { 1, 2, 3, 4, 5 })
        {
            using var temp = new TempFolder();
            var (session, registrar) = Fresh(temp);
            var rng = new Random(seed);
            var removed = new List<Pick>();
            for (var step = 0; step < 600; step++)
            {
                var ids = new List<string> { KeybindEditor.MainId };
                ids.AddRange(session.Pages.Pages.Select(p => p.Id));
                ids.AddRange(session.Picks.Picks.Select(p => p.Id));
                var id = ids[rng.Next(ids.Count)];
                var combo = Pool[rng.Next(Pool.Length)];
                var what = rng.Next(100);
                string label;
                if (what < 40) { session.PressKey(id, Combos.Press(combo)); label = $"press {id} {combo}"; }
                else if (what < 52) { session.ClearKey(id); label = $"clear {id}"; }
                else if (what < 58) { session.RestoreKey(id); label = $"restore {id}"; }
                else if (what < 60) { session.RestoreAllKeys(); label = "restore all"; }
                else if (what < 75 && session.Picks.Picks.Count > 0)
                {
                    var pick = session.Picks.Picks[rng.Next(session.Picks.Picks.Count)];
                    session.SetPick(new OnIslandRow(pick, true), on: false);
                    removed.Add(pick);
                    label = $"remove {pick.Id}";
                }
                else if (what < 82 && removed.Count > 0)
                {
                    var pick = removed[rng.Next(removed.Count)];
                    removed.Remove(pick);
                    session.SetPick(new OnIslandRow(pick, false), on: true);
                    label = $"add {pick.Id}";
                }
                else if (what < 88 && session.Picks.Picks.Count > 0)
                {
                    var pick = session.Picks.Picks[rng.Next(session.Picks.Picks.Count)];
                    session.MovePick(pick.Id, session.Pages.Pages[rng.Next(session.Pages.Pages.Count)].Id);
                    label = $"move {pick.Id}";
                }
                else if (what < 94 && session.Pages.Pages.Count < PageStore.MaxPages)
                {
                    session.CreatePage("Page" + step, "#" + rng.Next(0x1000000).ToString("X6"), rng.Next(2) == 0 ? Pool[rng.Next(Pool.Length)] : null);
                    label = "create page";
                }
                else if (session.Pages.Pages.FirstOrDefault(p => !p.IsBuiltIn) is { } custom)
                {
                    session.DeletePage(custom.Id);
                    label = $"delete {custom.Id}";
                }
                else continue;

                AssertConsistent(session, registrar, $"seed {seed} step {step} ({label})");
                var load = Settings.Load(temp.File("settings.json"), session.Pages.Pages);
                Assert.Equal(SettingsStatus.Loaded, load.Status);
                Assert.True(SameKeys(session.Settings, load.Settings), $"seed {seed} step {step} ({label}): the file differs from the settings in force | {Describe(session.Settings)} | {Describe(load.Settings)}");
            }
        }
    }

    // ---- capture ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Every_Accepted_Capture_Is_Stored_And_Read_Back_As_The_Same_Combination()
    {
        var accepted = 0;
        for (var vk = -2; vk < 0x300; vk++)
            for (var mods = 0; mods < 8; mods++)
                foreach (var win in new[] { false, true })
                {
                    var capture = KeybindEditor.Capture(new KeyPress(vk, (HotkeyModifiers)mods, win));
                    if (capture.Combo is not { } combo) continue;
                    accepted++;
                    Assert.False(win);
                    Assert.True(HotkeyCombo.TryParse(combo.ToString(), out var back, out _), $"{combo} does not read back");
                    Assert.Equal(combo, back);
                    Assert.NotEqual(HotkeyModifiers.None, combo.Modifiers);
                }

        Assert.True(accepted > 400);
    }

    [Fact]
    public void Holds_Windows_Keys_And_Modifier_Only_Presses_Never_Make_A_Combination()
    {
        foreach (var vk in new[] { 0x5B, 0x5C })
            Assert.NotNull(KeybindEditor.Capture(new KeyPress(vk, HotkeyModifiers.Control)).Refusal);
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
        {
            var c = KeybindEditor.Capture(new KeyPress(vk, HotkeyModifiers.Control));
            Assert.True(c.Waiting);
            Assert.Null(c.Combo);
            Assert.Null(c.Refusal);
        }

        Assert.NotNull(KeybindEditor.Capture(new KeyPress('A', HotkeyModifiers.Control | HotkeyModifiers.Alt, WindowsKey: true)).Refusal);
    }

    [Theory]
    [InlineData(0x73, HotkeyModifiers.Alt)]
    [InlineData(0x09, HotkeyModifiers.Alt)]
    [InlineData(0x20, HotkeyModifiers.Alt)]
    [InlineData(0x1B, HotkeyModifiers.Alt)]
    [InlineData(0x1B, HotkeyModifiers.Control)]
    [InlineData(0x1B, HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData(0x2E, HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    [InlineData('C', HotkeyModifiers.Control)]
    [InlineData('A', HotkeyModifiers.Shift)]
    [InlineData('A', HotkeyModifiers.None)]
    public void Holds_Unsafe_Combinations_Are_Refused_For_The_Main_Key_A_Page_And_A_Pick(int vk, HotkeyModifiers mods)
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        foreach (var action in new[] { KeybindEditor.MainId, PageIds.Apps, Alpha.Id })
        {
            var result = session.PressKey(action, new KeyPress(vk, mods));
            Assert.False(result.Ok);
            Assert.False(result.Changed);
            AssertConsistent(session, registrar, "unsafe");
        }
    }

    [Fact]
    public void Defect_Capture_Accepts_Modifier_Bits_Outside_Ctrl_Alt_Shift_And_Registers_What_It_Cannot_Store()
    {
        // Another bit (the Windows modifier, 8) in KeyPress.Modifiers: the text of the combination has no word for it, so the file keeps
        // "Ctrl+W" while Windows is asked for Win+Ctrl+W. The screen sends the Windows key as its own flag today, so nothing produces this.
        var capture = KeybindEditor.Capture(new KeyPress('W', HotkeyModifiers.Control | (HotkeyModifiers)8));
        Assert.True(capture.Combo is null || (HotkeyCombo.TryParse(capture.Combo.Value.ToString(), out var back, out _) && back == capture.Combo.Value),
            "the captured combination is not the one its text stands for");
    }

    // ---- save and registrar failures --------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Save_That_Fails_Leaves_The_Old_Key_Held_And_Releases_The_New_One()
    {
        var registrar = new PretendRegistrar();
        var editor = new KeybindEditor(registrar, id => id);
        var s0 = Settings.Defaults;
        registrar.TryRegister(s0.ShowHide, out _);

        var change = editor.Assign(s0, KeybindEditor.MainId, Combos.A, save: _ => false);
        Assert.True(change.Refused);
        Assert.Equal(s0, change.Settings);
        Assert.Equal([s0.ShowHide], registrar.Held);

        var ok = editor.Assign(s0, PageIds.Apps, Combos.B, save: _ => true);
        Assert.Contains(Combos.B, registrar.Held);
        var cleared = editor.Clear(ok.Settings, PageIds.Apps, save: _ => false);
        Assert.True(cleared.Refused);
        Assert.Contains(Combos.B, registrar.Held); // a clear that could not be saved keeps the key
        Assert.Equal(ok.Settings, cleared.Settings);
    }

    [Fact]
    public void Defect_Giving_An_Action_The_Key_It_Already_Has_Does_Not_Try_To_Register_It_Again()
    {
        // At the app's start another program held Ctrl+Q, so the main key is in the settings but not held by Windows (HOTKEY_TAKEN is
        // a documented outcome). The program is closed; the person presses Ctrl+Q again in the settings screen, or "restore default".
        // Assign sees old == combo and returns "nothing to do" without asking Windows, so the key stays dead until the next start.
        // (The real HotkeyHost answers true for a combination it already holds, so asking again would cost nothing.)
        var registrar = new PretendRegistrar { ErrorFor = _ => 1409 }; // taken by another program at the start
        var editor = new KeybindEditor(registrar, id => id);
        var s = Settings.Defaults;
        Assert.False(registrar.TryRegister(s.ShowHide, out _));
        registrar.ErrorFor = null; // the other program has gone

        editor.Assign(s, KeybindEditor.MainId, s.ShowHide);
        editor.RestoreDefault(s, KeybindEditor.MainId);

        Assert.Contains(s.ShowHide, registrar.Held);
    }

    [Fact]
    public void Holds_A_Registrar_That_Refuses_Leaves_Everything_As_It_Was_With_Its_Own_Words_For_1409()
    {
        var registrar = new PretendRegistrar { ErrorFor = c => c == Combos.A ? 1409 : c == Combos.B ? 5 : 0 };
        var editor = new KeybindEditor(registrar, id => id);
        var s0 = Settings.Defaults;
        registrar.TryRegister(s0.ShowHide, out _);
        var taken = editor.Assign(s0, KeybindEditor.MainId, Combos.A);
        var other = editor.Assign(s0, KeybindEditor.MainId, Combos.B);
        Assert.True(taken.Refused);
        Assert.True(other.Refused);
        Assert.NotEqual(taken.Refusal!.Message, other.Refusal!.Message);
        Assert.Equal(s0, taken.Settings);
        Assert.Equal([s0.ShowHide], registrar.Held);
    }

    [Fact]
    public void Holds_A_Key_Press_On_A_Locked_Settings_File_Changes_Nothing()
    {
        using var temp = new TempFolder();
        var files = temp.Files();
        File.WriteAllText(files.SettingsPath, "{ nonsense");
        var load = Settings.Load(files.SettingsPath);
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        var registrar = new PretendRegistrar();
        var session = new SettingsSession(files, load, new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore([Alpha]), PickStoreStatus.Loaded, null), registrar, () => [], () => false);
        Assert.False(session.PressKey(Alpha.Id, Combos.Press(Combos.A)).Ok);
        Assert.False(session.ClearKey(Alpha.Id).Ok);
        Assert.False(session.RestoreAllKeys().Ok);
        Assert.False(session.SetIdleSeconds(10).Ok);
        Assert.Empty(registrar.Held);
        Assert.Equal("{ nonsense", File.ReadAllText(files.SettingsPath));
    }

    [Fact]
    public void Holds_Releasing_Keys_Of_Missing_Picks_Keeps_What_It_Could_Not_Save_And_Finishes_On_The_Next_Try()
    {
        var registrar = new PretendRegistrar();
        var editor = new KeybindEditor(registrar, id => id);
        var s = Settings.Defaults;
        registrar.TryRegister(s.ShowHide, out _);
        foreach (var (id, combo) in new[] { ("program:a", Combos.A), ("program:b", Combos.B), ("program:c", Combos.C) })
            s = editor.Assign(s, id, combo).Settings;
        Assert.Equal(4, registrar.Held.Count);

        var saves = 0;
        var partial = editor.ReleaseKeysOfMissingPicks(s, _ => false, save: _ => ++saves != 2);
        Assert.True(partial.Changed);
        Assert.True(partial.Refused);
        Assert.Single(s.PickKeys, k => k.PickId == "program:a");
        Assert.Equal(2, partial.Settings.PickKeys.Count);
        Assert.Equal(3, registrar.Held.Count);
        Assert.DoesNotContain(Combos.A, registrar.Held);

        var rest = editor.ReleaseKeysOfMissingPicks(partial.Settings, _ => false, save: _ => true);
        Assert.False(rest.Refused);
        Assert.Empty(rest.Settings.PickKeys);
        Assert.Equal([s.ShowHide], registrar.Held);
    }

    [Fact]
    public void Holds_Releasing_Keys_Of_Missing_Picks_Keeps_The_Keys_Of_Picks_That_Exist_And_Ignores_A_Pick_That_Never_Had_One()
    {
        var registrar = new PretendRegistrar();
        var editor = new KeybindEditor(registrar, id => id);
        var s = editor.Assign(editor.Assign(Settings.Defaults, "program:a", Combos.A).Settings, "program:b", Combos.B).Settings;
        var change = editor.ReleaseKeysOfMissingPicks(s, id => id == "program:b");
        Assert.True(change.Changed);
        Assert.Equal(["program:b"], change.Settings.PickKeys.Select(k => k.PickId));
        Assert.False(editor.ReleaseKeysOfMissingPicks(change.Settings, _ => true).Changed);
        Assert.False(editor.Clear(change.Settings, "program:never").Changed);
    }

    [Fact]
    public void Defect_A_Removed_Pick_Keeps_Its_Key_Registered_Silently_When_The_Settings_File_Cannot_Be_Saved()
    {
        // WO6 section 4: "A pick that is removed gives its key back to Windows at once". When settings.json cannot be saved the key stays
        // registered for a pick that is gone, SetPick still answers "done" with no warning, and nothing tries again until another pick changes.
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        Assert.True(session.PressKey(Alpha.Id, Combos.Press(Combos.A)).Ok);
        File.SetAttributes(temp.File("settings.json"), FileAttributes.ReadOnly);

        var result = session.SetPick(new OnIslandRow(Alpha, true), on: false);

        Assert.Null(session.Picks.ById(Alpha.Id));
        var stillHeld = registrar.Held.Contains(Combos.A);
        Assert.False(stillHeld && result.Refusal is null && result.Warning is null,
            "the key of the removed pick is still held by Windows and nobody was told");
    }

    [Fact]
    public void Holds_Removing_A_Pick_Releases_Its_Key_At_Once_And_A_Moved_Pick_Keeps_It()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        session.PressKey(Alpha.Id, Combos.Press(Combos.A));
        session.PressKey(Beta.Id, Combos.Press(Combos.B));
        Assert.True(session.MovePick(Alpha.Id, PageIds.Vibe).Changed);
        Assert.Equal(Combos.A, session.Settings.PickKeyFor(Alpha.Id));
        session.SetPick(new OnIslandRow(Beta, true), on: false);
        Assert.DoesNotContain(Combos.B, registrar.Held);
        Assert.Null(session.Settings.PickKeyFor(Beta.Id));
        AssertConsistent(session, registrar, "after removal");
    }

    [Fact]
    public void Holds_Deleting_A_Page_Releases_Its_Key_And_The_Keys_Of_Its_Picks()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        Assert.True(session.CreatePage("Zed", "#123456", Combos.C).Ok);
        var page = session.Pages.Pages[^1];
        session.MovePick(Alpha.Id, page.Id);
        session.PressKey(Alpha.Id, Combos.Press(Combos.A));
        Assert.True(session.DeletePage(page.Id).Ok);
        Assert.DoesNotContain(Combos.A, registrar.Held);
        Assert.DoesNotContain(Combos.C, registrar.Held);
        Assert.DoesNotContain(session.Settings.PageKeys, k => k.PageId == page.Id);
        AssertConsistent(session, registrar, "after delete");
    }

    [Fact]
    public void Defect_Delete_Page_Says_Nothing_Was_Changed_But_The_Page_Key_Is_Already_Gone_When_Picks_Cannot_Be_Saved()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        session.CreatePage("Zed", "#123456", Combos.C);
        var page = session.Pages.Pages[^1];
        File.SetAttributes(temp.File("picks.json"), FileAttributes.ReadOnly);

        var result = session.DeletePage(page.Id);

        Assert.False(result.Ok);
        Assert.Contains("nothing was changed", result.Refusal);
        Assert.NotNull(session.Pages.ById(page.Id)); // the page is still there ...
        Assert.Equal(Combos.C, session.Settings.KeyFor(page.Id)); // ... and must still have its key, as the message says
        Assert.Contains(Combos.C, registrar.Held);
    }

    [Fact]
    public void Defect_Delete_Page_Says_Nothing_Was_Changed_But_Picks_And_Key_Are_Gone_When_Pages_Cannot_Be_Saved()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        session.CreatePage("Zed", "#123456", Combos.C);
        var page = session.Pages.Pages[^1];
        session.MovePick(Alpha.Id, page.Id);
        File.SetAttributes(temp.File("pages.json"), FileAttributes.ReadOnly);

        var result = session.DeletePage(page.Id);

        Assert.False(result.Ok);
        Assert.Contains("nothing was changed", result.Refusal);
        Assert.NotNull(session.Pages.ById(page.Id));
        Assert.NotNull(session.Picks.ById(Alpha.Id)); // the page's pick must still be there
        Assert.Equal(Combos.C, session.Settings.KeyFor(page.Id));
        Assert.Contains(Combos.C, registrar.Held);
    }

    [Fact]
    public void Holds_Deleting_A_Built_In_Or_Missing_Page_Is_Refused_And_Touches_Nothing()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        session.PressKey(PageIds.Apps, Combos.Press(Combos.A));
        Assert.False(session.DeletePage(PageIds.Apps).Ok);
        Assert.False(session.DeletePage("nope").Ok);
        Assert.False(session.DeletePage("").Ok);
        Assert.Equal(Combos.A, session.Settings.KeyFor(PageIds.Apps));
        AssertConsistent(session, registrar, "refused delete");
    }

    [Fact]
    public void Defect_Restore_All_Keys_Is_Refused_When_A_Page_Holds_The_Default_Main_Key()
    {
        // The main key is given back first; Ctrl+Q is held by a page key at that moment, so "taken inside the app" refuses the whole
        // restore before the page keys are cleared. Clearing the page keys first (or the main key last) would work.
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        Assert.True(session.PressKey(KeybindEditor.MainId, Combos.Press(Combos.A)).Ok);
        Assert.True(session.PressKey(PageIds.Apps, Combos.Press(Settings.Defaults.ShowHide)).Ok);

        var result = session.RestoreAllKeys();

        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(Settings.Defaults.ShowHide, session.Settings.ShowHide);
        Assert.Null(session.Settings.KeyFor(PageIds.Apps));
        AssertConsistent(session, registrar, "restore all");
    }

    [Fact]
    public void Holds_Restore_All_Clears_Every_Page_And_Pick_Key_And_Brings_Back_The_Main_Key()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        session.PressKey(KeybindEditor.MainId, Combos.Press(Combos.A));
        session.PressKey(PageIds.Apps, Combos.Press(Combos.B));
        session.PressKey(Alpha.Id, Combos.Press(Combos.C));
        Assert.True(session.RestoreAllKeys().Ok);
        Assert.Equal(Settings.Defaults.ShowHide, session.Settings.ShowHide);
        Assert.Empty(session.Settings.PickKeys);
        Assert.All(session.Settings.PageKeys, k => Assert.Null(k.Combo));
        AssertConsistent(session, registrar, "restore all");
    }

    [Fact]
    public void Holds_The_Main_Key_Cannot_Be_Cleared_And_Clearing_Twice_Is_Quiet()
    {
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        Assert.False(session.ClearKey(KeybindEditor.MainId).Ok);
        Assert.False(session.ClearKey(PageIds.Apps).Changed);
        session.PressKey(PageIds.Apps, Combos.Press(Combos.A));
        Assert.True(session.ClearKey(PageIds.Apps).Changed);
        Assert.False(session.ClearKey(PageIds.Apps).Changed);
        Assert.True(session.ClearKey(PageIds.Apps).Ok);
        AssertConsistent(session, registrar, "clear twice");
    }

    // ---- ids that do not exist, or look wrong -----------------------------------------------------------------------

    [Fact]
    public void Defect_A_Key_Can_Be_Given_To_A_Page_Or_A_Pick_That_Does_Not_Exist_And_Stays_Registered()
    {
        // The screen only offers real rows, so nothing sends these ids today. The session registers the key with Windows and saves it for
        // a page or pick that is not there; nothing releases it until some later pick change (pages are never swept).
        using var temp = new TempFolder();
        var (session, registrar) = Fresh(temp);
        var page = session.PressKey("ghost-page", Combos.Press(Combos.A));
        var pick = session.PressKey("program:ghost", Combos.Press(Combos.B));
        Assert.False(page.Ok && page.Changed && registrar.Held.Contains(Combos.A), "a key is held for a page that does not exist");
        Assert.False(pick.Ok && pick.Changed && registrar.Held.Contains(Combos.B), "a key is held for a pick that does not exist");
    }

    [Fact]
    public void Defect_A_Storable_Pick_Id_Without_A_Colon_Is_Accepted_And_Its_Key_Goes_To_A_Page_Entry()
    {
        // PickKeysJson says a pick id "always has a colon"; Pick.IsStorable and PickStore.Add do not check it. With the id "apps" a key
        // pressed for the pick would replace the key of the Apps page. Nothing builds such an id today (ForProgram/ForFolder/ForSite all
        // write a prefix); a hand-edited picks.json can.
        var odd = new Pick("apps", PickKind.Program, "Odd", PageIds.Media, ExeName: "odd.exe");
        var store = PickStore.Empty.Add(odd, out var added);
        Assert.False(added, "a pick whose id cannot hold a key was accepted");
    }

    [Theory]
    [InlineData("program:a b")]
    [InlineData("program:a\tb")]
    [InlineData("program:a\u0001b")]
    [InlineData("x:")]
    public void Defect_A_Storable_Pick_Id_That_The_Key_File_Refuses_Makes_The_Whole_Settings_File_Unreadable(string id)
    {
        // Pick.IsStorable lets these ids into picks.json; PickKeysJson.IsUsableId then refuses the name, so the next start reads
        // settings.json as Unreadable and every key, the idle time and the glass go back to the defaults with the file locked.
        var pick = new Pick(id, PickKind.Program, "Odd", PageIds.Apps, ExeName: "odd.exe");
        if (!pick.IsStorable(out _)) return; // fixed at the root: the pick file now refuses an id the key file cannot hold, so no key can be given to it
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp, [pick]);
        var given = session.PressKey(id, Combos.Press(Combos.A));
        if (!given.Ok) return; // refusing the key would be fine
        var load = Settings.Load(temp.File("settings.json"), session.Pages.Pages);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
    }

    [Fact]
    public void Holds_A_Custom_Page_Id_Can_Never_Contain_A_Colon()
    {
        foreach (var id in new[] { "program:x", "a:b", "page:1", "A", "page_1", "", new string('a', 41) })
        {
            var json = $$"""{"pages":[{"id":"media","name":"Media","color":"#112233"},{"id":"folders","name":"Folders","color":"#112233"},{"id":"apps","name":"Apps","color":"#112233"},{"id":"vibe","name":"Vibe","color":"#112233"},{"id":"browser","name":"Browser","color":"#112233"},{"id":"{{id}}","name":"X","color":"#112233"}]}""";
            Assert.Equal(PageStoreStatus.Unreadable, PageStore.Parse(json).Status);
        }
    }

    [Fact]
    public void Holds_ActionFor_Tells_Main_Page_And_Pick_By_Their_Ids_Including_Ids_With_Several_Colons()
    {
        var editor = new KeybindEditor(new PretendRegistrar(), id => id);
        var s = Settings.Defaults;
        s = editor.Assign(s, PageIds.Apps, Combos.A).Settings;
        s = editor.Assign(s, "program:a:b:c", Combos.B).Settings;
        s = editor.Assign(s, "site:example.org:8080", Combos.C).Settings;
        Assert.Equal(KeybindEditor.MainId, KeybindEditor.ActionFor(s, s.ShowHide));
        Assert.Equal(PageIds.Apps, KeybindEditor.ActionFor(s, Combos.A));
        Assert.Equal("program:a:b:c", KeybindEditor.ActionFor(s, Combos.B));
        Assert.Equal("site:example.org:8080", KeyOfRoundTrip(s, Combos.C));
        Assert.Null(KeybindEditor.ActionFor(s, HotkeyCombo.Parse("Ctrl+Alt+Z")));
        Assert.Equal(Combos.B, KeybindEditor.KeyOf(s, "program:a:b:c"));
        Assert.Null(KeybindEditor.KeyOf(s, "program:other"));
        Assert.Null(KeybindEditor.KeyOf(s, ""));
    }

    private static string? KeyOfRoundTrip(Settings s, HotkeyCombo combo) =>
        KeybindEditor.ActionFor(Settings.Parse(s.ToJson()).Settings, combo);

    [Fact]
    public void Holds_Pick_Ids_That_Look_Alike_Are_Different_Actions()
    {
        var editor = new KeybindEditor(new PretendRegistrar(), id => id);
        var s = Settings.Defaults;
        var ids = new[] { "program:a", "program:A", "program:a ", "program:a%0020", "site:a", "folder:a", "program:é", "program:é", "program:" };
        var combos = Pool.Take(5).Append(HotkeyCombo.Parse("Ctrl+Alt+E")).Append(HotkeyCombo.Parse("Ctrl+Alt+F")).Append(HotkeyCombo.Parse("Ctrl+Alt+G")).Append(HotkeyCombo.Parse("Ctrl+Alt+H")).Append(HotkeyCombo.Parse("Ctrl+Alt+I")).ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            var change = editor.Assign(s, ids[i], combos[i]);
            Assert.True(change.Changed, $"id {i}");
            s = change.Settings;
        }

        Assert.Equal(ids.Length, s.PickKeys.Count);
        for (var i = 0; i < ids.Length; i++) Assert.Equal(combos[i], s.PickKeyFor(ids[i]));
    }

    // ---- the pick key handler -----------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Pick_Key_Jumps_First_Then_Comes_In_On_The_Picks_Own_Page_And_A_Missing_Pick_Does_Nothing()
    {
        var log = new List<string>();
        var handler = new PickKeyHandler(id => id == Alpha.Id ? Alpha : null, p => log.Add("jump " + p.Id), page => log.Add("in " + page));
        Assert.True(handler.Press(Alpha.Id));
        Assert.Equal([$"jump {Alpha.Id}", "in apps"], log);
        log.Clear();
        Assert.False(handler.Press("program:gone"));
        Assert.False(handler.Press(""));
        Assert.Empty(log);
    }

    [Fact]
    public void Holds_Pressing_The_Key_Twice_Quickly_Does_The_Thing_Twice_Not_Once_And_Never_Out_Of_Order()
    {
        var log = new List<string>();
        var handler = new PickKeyHandler(_ => Alpha, p => log.Add("jump"), page => log.Add("in"));
        for (var i = 0; i < 1000; i++) handler.Press(Alpha.Id);
        Assert.Equal(2000, log.Count);
        for (var i = 0; i < log.Count; i += 2) Assert.Equal(["jump", "in"], log.Skip(i).Take(2));
    }

    // ---- the idle time ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(double.MaxValue, 60)]
    [InlineData(1e300, 60)]
    [InlineData(86401, 60)]
    [InlineData(86400, 60)]
    [InlineData(60.5, 60)]
    [InlineData(60.4, 60)]
    [InlineData(59.5, 60)]
    [InlineData(59.49, 59)]
    [InlineData(2.49, 2)]
    [InlineData(1.5, 2)]
    [InlineData(1, 2)]
    [InlineData(0.0001, 2)]
    [InlineData(0.0, 2)]
    [InlineData(-5, 2)]
    [InlineData(double.MinValue, 2)]
    [InlineData(2, 2)]
    [InlineData(60, 60)]
    [InlineData(12.4, 12)]
    [InlineData(12.6, 13)]
    public void Holds_Idle_Seconds_Are_Kept_Between_Two_And_Sixty_As_A_Whole_Number_And_Saved_And_Read_Back(double asked, double expected)
    {
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp);
        var result = session.SetIdleSeconds(asked);
        Assert.True(result.Ok);
        Assert.Equal(expected, session.Settings.IdleSeconds);
        var load = Settings.Load(temp.File("settings.json"), session.Pages.Pages);
        Assert.Equal(expected, load.Settings.IdleSeconds);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Holds_An_Idle_Time_That_Is_Not_A_Number_Is_Refused_And_Changes_Nothing(double asked)
    {
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp);
        var before = File.ReadAllText(temp.File("settings.json"));
        var result = session.SetIdleSeconds(asked);
        Assert.False(result.Ok);
        Assert.False(result.Changed);
        Assert.Equal(LookConstants.IdleSeconds, session.Settings.IdleSeconds);
        Assert.Equal(before, File.ReadAllText(temp.File("settings.json")));
    }

    [Fact]
    public void Holds_Setting_The_Same_Idle_Time_Again_Does_Not_Save_Or_Raise_Changed_And_A_Failed_Save_Changes_Nothing()
    {
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp);
        var raised = 0;
        session.Changed += _ => raised++;
        Assert.True(session.SetIdleSeconds(20).Changed);
        Assert.False(session.SetIdleSeconds(20.2).Changed);
        Assert.Equal(1, raised);
        File.SetAttributes(temp.File("settings.json"), FileAttributes.ReadOnly);
        var failed = session.SetIdleSeconds(30);
        Assert.False(failed.Ok);
        Assert.Equal(20, session.Settings.IdleSeconds);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Holds_A_Negative_Zero_Idle_Time_Is_Two_And_Is_Saved_As_A_Readable_Number()
    {
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp);
        Assert.True(session.SetIdleSeconds(-0.0).Ok);
        Assert.Equal(2, session.Settings.IdleSeconds);
        Assert.Equal(SettingsStatus.Loaded, Settings.Load(temp.File("settings.json"), session.Pages.Pages).Status);
    }

    [Fact]
    public void Defect_Settings_Read_Back_Are_Not_Equal_When_Page_Keys_Were_Given_Out_Of_Page_Order()
    {
        // Same keys, same pages, same file: but PageKeys is a list in the order the keys were first given, and Parse builds it in page
        // order. Settings.Equals compares the lists in order, so a faithful read-back is "not equal".
        using var temp = new TempFolder();
        var (session, _) = Fresh(temp);
        Assert.True(session.CreatePage("One", "#111111").Ok);
        Assert.True(session.CreatePage("Two", "#999999").Ok);
        Assert.True(session.PressKey("page-2", Combos.Press(Combos.A)).Ok);
        Assert.True(session.PressKey("page-1", Combos.Press(Combos.B)).Ok);
        var load = Settings.Load(temp.File("settings.json"), session.Pages.Pages);
        Assert.True(SameKeys(session.Settings, load.Settings));
        Assert.True(session.Settings.Equals(load.Settings), "the file holds exactly these keys, yet Settings.Equals says it differs");
    }

    [Fact]
    public void Defect_ClampIdle_Lets_NaN_Through_Though_It_Promises_A_Whole_Second_Between_Two_And_Sixty()
    {
        // SettingsSession.SetIdleSeconds refuses NaN before calling it, and a file cannot hold NaN; the public helper alone does not.
        var kept = Settings.ClampIdle(double.NaN);
        Assert.InRange(kept, Settings.MinIdleSeconds, Settings.MaxSetIdleSeconds);
    }

    [Fact]
    public void Holds_Idle_Rounding_Is_To_A_Whole_Second_Even_At_Halves_NoteBankersRounding()
    {
        // Math.Round rounds halves to even: 2.5 -> 2, 3.5 -> 4, 4.5 -> 4. Stated here so the behaviour is on record; the work order says only "whole seconds".
        Assert.Equal(2, Settings.ClampIdle(2.5));
        Assert.Equal(4, Settings.ClampIdle(3.5));
        Assert.Equal(4, Settings.ClampIdle(4.5));
    }
}
