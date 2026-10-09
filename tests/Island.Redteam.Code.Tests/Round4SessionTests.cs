using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 4 (code-4-*): the words a person is told when the key of a switched-off pick cannot be given back, after the repair of ease-3-8 / code-3-8a (956ac18). Invented names only.</summary>
public sealed class Round4SessionTests
{
    private sealed class Registrar : IHotkeyRegistrar
    {
        public HashSet<HotkeyCombo> Held { get; } = [];

        public HashSet<HotkeyCombo> Refused { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            if (Held.Contains(combo)) return true;
            if (Refused.Contains(combo))
            {
                error = 1409;
                return false;
            }

            Held.Add(combo);
            return true;
        }

        public void Release(HotkeyCombo combo) => Held.Remove(combo);
    }

    private static readonly HotkeyCombo K = HotkeyCombo.Parse("Ctrl+Alt+A");

    private static (SettingsSession Session, Scratch Dir, Registrar Registrar, Pick Pick) Make()
    {
        var pick = Pick.ForSite("Alpha", "alpha.example.org", PageIds.Browser);
        var dir = new Scratch();
        var registrar = new Registrar();
        registrar.Held.Add(K);
        var settings = Settings.Defaults.WithPickKey(pick.Id, K);
        Assert.True(settings.Save(dir.Path_("settings.json")));
        var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
        var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore([pick]), PickStoreStatus.Loaded, null), registrar, () => [], () => false);
        return (session, dir, registrar, pick);
    }

    private static OnIslandRow Row(SettingsSession session, Pick pick) => session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id);

    [Fact]
    public void Held_The_Warning_For_A_Key_Another_Program_Took_Names_The_Key_And_The_Program()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            session.SetPick(Row(session, pick), false);
            registrar.Refused.Add(K);
            var warning = session.SetPick(Row(session, pick), true).Warning;
            Assert.NotNull(warning);
            Assert.Contains("Ctrl+Alt+A", warning, StringComparison.Ordinal);
            Assert.Contains("another program", warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Defect_The_Warning_For_A_Key_That_Was_Not_Given_Back_Says_Your_Old_Key_Still_Works_When_There_Is_No_Key_At_All()
    {
        // code-4-9 (LOW; false text, made by the repair of code-3-8a). SettingsSession.SetPick builds the warning as KeyNotGivenBackBecause(key, refusal.Message) with the refusal that
        // KeybindEditor.Assign gives for a key that is pressed anew: three of them end "Your old key still works." (KeyTakenByAnotherProgram, KeyRefusedByWindows, KeyNotSaved). Here there is
        // no old key: the pick was switched off, its key was let go, and the warning says the key was not given back; the next sentence tells the person an old key works. Expected: the
        // reason without the sentence about an old key (or the words of the give-back's own).
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            session.SetPick(Row(session, pick), false);
            registrar.Refused.Add(K); // Windows says another program has it now
            var warning = session.SetPick(Row(session, pick), true).Warning;
            Assert.NotNull(warning);
            Assert.Null(session.Settings.PickKeyFor(pick.Id)); // the premise: no key works
            Assert.DoesNotContain("old key still works", warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Defect_The_Warning_For_A_Key_That_Could_Not_Be_Saved_When_The_Pick_Comes_Back_Says_Your_Old_Key_Still_Works()
    {
        // code-4-9, second case: the settings file cannot be written (a folder where its temporary file goes) when the pick is switched on again: "The new key could not be saved. Island does not
        // change a key it cannot remember. Your old key still works. Try again." - for a key that is the old one, that is not "new", and that does not work.
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            session.SetPick(Row(session, pick), false);
            Directory.CreateDirectory(dir.Path_("settings.json.tmp"));
            var warning = session.SetPick(Row(session, pick), true).Warning;
            Assert.NotNull(warning);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.DoesNotContain("old key still works", warning, StringComparison.Ordinal);
            Assert.DoesNotContain("The new key", warning, StringComparison.Ordinal);
        }
    }
}

public sealed class Round4SessionTests2
{
    [Fact]
    public void Defect_A_New_Page_Whose_Key_Windows_Refuses_Is_Refused_With_Your_Old_Key_Still_Works()
    {
        // code-4-9, third place (the same sentence, older: WORK-ORDER-6). SettingsSession.CreatePage assigns the page's first key through KeybindEditor.Assign and shows its refusal as it is:
        // for a key another program holds: "... already used by another program. Island left it alone, because taking it would break that program. Your old key still works." A page that is
        // being made has no old key. Expected: no sentence about an old key when there is none.
        using var dir = new Scratch();
        var registrar = new TakenRegistrar();
        var settings = Settings.Defaults;
        registrar.Held.Add(settings.ShowHide);
        Assert.True(settings.Save(dir.Path_("settings.json")));
        var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
        var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), registrar, () => [], () => false);
        var taken = HotkeyCombo.Parse("Ctrl+Alt+A");
        registrar.Refused.Add(taken);
        var result = session.CreatePage("Alpha", "#112233", taken);
        Assert.False(result.Ok);
        Assert.NotNull(result.Refusal);
        Assert.DoesNotContain("old key still works", result.Refusal, StringComparison.Ordinal);
    }

    private sealed class TakenRegistrar : IHotkeyRegistrar
    {
        public HashSet<HotkeyCombo> Held { get; } = [];

        public HashSet<HotkeyCombo> Refused { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            if (Held.Contains(combo)) return true;
            if (Refused.Contains(combo))
            {
                error = 1409;
                return false;
            }

            Held.Add(combo);
            return true;
        }

        public void Release(HotkeyCombo combo) => Held.Remove(combo);
    }
}

public sealed class Round4SessionTests3
{
    private sealed class OneRefused(HotkeyCombo refused) : IHotkeyRegistrar
    {
        public HashSet<HotkeyCombo> Held { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            if (Held.Contains(combo)) return true;
            if (combo == refused)
            {
                error = 1409;
                return false;
            }

            Held.Add(combo);
            return true;
        }

        public void Release(HotkeyCombo combo) => Held.Remove(combo);
    }

    private static (SettingsSession Session, Scratch Dir) Make()
    {
        var dir = new Scratch();
        var settings = Settings.Defaults;
        Assert.True(settings.Save(dir.Path_("settings.json")));
        var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
        var registrar = new OneRefused(HotkeyCombo.Parse("Ctrl+Alt+A"));
        registrar.Held.Add(settings.ShowHide);
        var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), registrar, () => [], () => false);
        return (session, dir);
    }

    [Fact]
    public void Defect_The_First_Key_Of_A_Page_That_Another_Program_Holds_Is_Refused_With_Your_Old_Key_Still_Works()
    {
        // code-4-9, fourth place and the commonest (the same sentence, WORK-ORDER-6): no page has a key by default, so the first press of a key for a page that another program holds is told
        // "Ctrl+Alt+A is already used by another program. Island left it alone, because taking it would break that program. Your old key still works. Press a different combination." The
        // page has no old key. The sentence is true only for the main key and for a page that already has one. Expected: it is said only when there is an old key.
        var (session, dir) = Make();
        using (dir)
        {
            Assert.Null(session.Settings.KeyFor(PageIds.Folders)); // the premise: nothing to keep
            var combo = HotkeyCombo.Parse("Ctrl+Alt+A");
            var result = session.PressKey(PageIds.Folders, new KeyPress(combo.VirtualKey, combo.Modifiers));
            Assert.False(result.Ok);
            Assert.NotNull(result.Refusal);
            Assert.DoesNotContain("old key still works", result.Refusal, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Held_The_Same_Refusal_For_The_Main_Key_Is_True_Because_The_Main_Key_Always_Has_An_Old_Key()
    {
        var (session, dir) = Make();
        using (dir)
        {
            var combo = HotkeyCombo.Parse("Ctrl+Alt+A");
            var result = session.PressKey(KeybindEditor.MainId, new KeyPress(combo.VirtualKey, combo.Modifiers));
            Assert.False(result.Ok);
            Assert.Contains("old key still works", result.Refusal!, StringComparison.Ordinal);
            Assert.Equal(Settings.Defaults.ShowHide, session.Settings.ShowHide);
        }
    }
}
