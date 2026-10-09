using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 3 (code-3-*): the keys that SettingsSession keeps for the picks switched off during a visit (the repair of ease-2-6, dcb3af1) against everything else that changes keys. Invented names only.</summary>
public sealed class Round3SessionTests
{
    private sealed class HoldingRegistrar : IHotkeyRegistrar
    {
        public HashSet<HotkeyCombo> Held { get; } = [];

        public HashSet<HotkeyCombo> Refused { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
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

    private static (SettingsSession Session, Scratch Dir, HoldingRegistrar Registrar, Pick Pick) Make(bool withModeKey = false)
    {
        var pick = Pick.ForSite("Alpha", "alpha.example.org", PageIds.Browser);
        var dir = new Scratch();
        var registrar = new HoldingRegistrar();
        registrar.Held.Add(K); // the app registered it at the start
        var settings = Settings.Defaults.WithPickKey(pick.Id, K);
        if (withModeKey)
        {
            settings = settings with { ModeKey = HotkeyCombo.Parse("Ctrl+Alt+M") };
            registrar.Held.Add(HotkeyCombo.Parse("Ctrl+Alt+M"));
        }

        Assert.True(settings.Save(dir.Path_("settings.json")));
        var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
        var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore([pick]), PickStoreStatus.Loaded, null), registrar, () => [], () => false);
        return (session, dir, registrar, pick);
    }

    [Fact]
    public void Held_A_Pick_Switched_Off_Lets_Its_Key_Go_And_Switched_On_Again_Gets_It_Back_In_The_File_And_In_Windows()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            Assert.True(session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false).Changed);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.DoesNotContain(K, registrar.Held);
            Assert.Null(Settings.Load(dir.Path_("settings.json")).Settings.PickKeyFor(pick.Id));

            var result = session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.True(result.Ok);
            Assert.Equal(K, session.Settings.PickKeyFor(pick.Id));
            Assert.Contains(K, registrar.Held);
            Assert.Equal(K, Settings.Load(dir.Path_("settings.json")).Settings.PickKeyFor(pick.Id));
        }
    }

    [Fact]
    public void Held_A_Key_Another_Action_Took_While_The_Pick_Was_Off_Is_Not_Taken_Back_And_The_Person_Is_Told()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false);
            Assert.True(session.PressKey(KeybindEditor.ModeNextId, new KeyPress(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt)).Changed); // Ctrl+Alt+A for the mode key now
            Assert.Equal(K, session.Settings.ModeKey);

            var result = session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.True(result.Ok);
            Assert.NotNull(result.Warning);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.Equal(K, session.Settings.ModeKey); // the other action keeps it
            Assert.Contains(K, registrar.Held);
        }
    }

    [Fact]
    public void Held_A_Key_Windows_Gave_To_Another_Program_Meanwhile_Is_Not_Forced_Back()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false);
            registrar.Refused.Add(K);
            var result = session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.True(result.Ok);
            Assert.NotNull(result.Warning);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.DoesNotContain(K, registrar.Held);
            Assert.Single(session.PickRows(PageIds.Browser), r => r.Pick.Id == pick.Id && r.On); // the pick itself is on
        }
    }

    [Fact]
    public void Held_Switching_On_Twice_Gives_The_Key_Back_Once_And_Switching_Off_Twice_Keeps_The_First_Key()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            var off = session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id);
            session.SetPick(off, false);
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false); // a faint row switched off again
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.Equal(K, session.Settings.PickKeyFor(pick.Id));
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.Equal(K, session.Settings.PickKeyFor(pick.Id));
            Assert.Single(session.Settings.PickKeys);
            Assert.Contains(K, registrar.Held);
        }
    }

    [Fact]
    public void Defect_Restore_The_Original_Keys_Does_Not_Reach_The_Key_Kept_For_A_Pick_Switched_Off_And_Switching_It_On_Gives_The_Key_Back()
    {
        // code-3-7 (LOW). SettingsSession._keysOfSwitchedOff (dcb3af1) keeps the key of every pick switched off during the visit and SetPick(on) gives it back. RestoreAllKeys goes through the
        // KeybindEditor over Settings only: the kept keys are not in Settings (they were let go at the switch-off), are not counted by KeysSetByYou and are not forgotten. So: the person
        // switches a hand-added pick off, then answers Yes to "Go back to the original keys?" (every key they set is taken away, the question says so), then switches the pick on again:
        // the key they had just had taken away is back, registered with Windows and written in the file. Expected: the original keys stay the original keys: restoring all keys forgets the kept ones.
        var (session, dir, registrar, pick) = Make(withModeKey: true); // a mode key is set too, so the restore is on offer
        using (dir)
        {
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false);
            Assert.Equal(1, session.KeysSetByYou); // the mode key; the question says "1 key"
            Assert.True(session.RestoreAllKeys().Changed);
            Assert.Equal(0, session.KeysSetByYou); // every key the person set is now taken away

            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.DoesNotContain(K, registrar.Held);
        }
    }

    [Fact]
    public void Defect_A_Key_That_Could_Not_Be_Saved_When_The_Pick_Is_Switched_On_Is_Told_As_Used_By_Something_Else()
    {
        // code-3-8 (LOW). SettingsSession.SetPick gives the key back through KeybindEditor.Assign and turns EVERY refusal of it into SettingsText.KeyNotGivenBack: "<key>, the key you had set
        // for this, is used by something else now, so it was not given back. Press a key for it again." Assign also refuses when settings.json cannot be saved (KeyNotSaved); then nothing
        // uses the key, the sentence is false and "press a key again" meets the same refusal. Expected: the refusal that Assign gave is the one told (KeyNotSaved for a file that cannot be
        // written), and the sentence about another user of the key only for a key that another action or another program holds.
        var (session, dir, _, pick) = Make();
        using (dir)
        {
            session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), false);
            File.SetAttributes(dir.Path_("settings.json"), FileAttributes.ReadOnly);
            try
            {
                var result = session.SetPick(session.PickRows(PageIds.Browser).Single(r => r.Pick.Id == pick.Id), true);
                Assert.True(result.Ok);
                Assert.Null(session.Settings.PickKeyFor(pick.Id)); // the premise: the key could not be given back
                Assert.NotNull(result.Warning);
                Assert.DoesNotContain("used by something else", result.Warning);
            }
            finally
            {
                File.SetAttributes(dir.Path_("settings.json"), FileAttributes.Normal);
            }
        }
    }

    [Fact]
    public void Held_Restore_The_Original_Keys_Takes_Every_Key_That_Is_In_Force_Away_And_Counts_Them()
    {
        var (session, dir, registrar, pick) = Make();
        using (dir)
        {
            Assert.Equal(1, session.KeysSetByYou);
            Assert.True(session.RestoreAllKeys().Changed);
            Assert.Equal(0, session.KeysSetByYou);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            Assert.DoesNotContain(K, registrar.Held);
        }
    }

    [Fact]
    public void Held_A_Deleted_Pages_Switched_Off_Picks_Take_Their_Kept_Keys_With_Them()
    {
        var dir = new Scratch();
        using (dir)
        {
            var pages = PageStore.Default.Create("Alpha page", "#FFAA33").Store;
            var pick = Pick.ForSite("Alpha", "alpha.example.org", "page-1");
            var registrar = new HoldingRegistrar();
            registrar.Held.Add(K);
            var settings = Settings.Defaults.WithPickKey(pick.Id, K);
            var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
            var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(pages, PageStoreStatus.Loaded, null),
                new PickStoreLoad(new PickStore([pick]), PickStoreStatus.Loaded, null), registrar, () => [], () => false);
            session.SetPick(session.PickRows("page-1").Single(), false);
            Assert.True(session.DeletePage("page-1").Changed);
            Assert.True(session.CreatePage("Beta page", "#33AAFF").Changed);
            Assert.Empty(session.PickRows("page-1"));
            // the same pick put on the new page by hand is a new pick: it gets no key from the old one
            var again = session.AddSite("page-1", "alpha.example.org");
            Assert.True(again.Added, again.Message);
            Assert.Null(session.Settings.PickKeyFor(pick.Id));
            session.SetPick(session.PickRows("page-1").Single(), false);
            session.SetPick(session.PickRows("page-1").Single(), true);
            Assert.Null(session.Settings.PickKeyFor(pick.Id)); // the key kept for the old pick was forgotten with its page
        }
    }
}
