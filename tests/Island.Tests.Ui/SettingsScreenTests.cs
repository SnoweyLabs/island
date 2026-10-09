using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Island.Tests.Ui.Harness;

namespace Island.Tests.Ui;

/// <summary>
/// What the settings screen says and lists (WORK-ORDER-4 section 3, WORK-ORDER-7 section 6, WORK-ORDER-11 section 1), read from the built view: a row for every action that has a key, the page that
/// fills itself, the refusal that the island has no room for the Terminals page, and a first start that is run again over a person's own things and changes nothing.
/// </summary>
public class SettingsScreenTests
{
    private static HashSet<string> Keys(SettingsView view) =>
        Tree.Descendants(view).Select(Tree.FocusKeyOf).Where(k => k is not null).Select(k => k!).ToHashSet();

    private static SettingsView Open(Fixture fixture, SettingsSection section, bool setup = false)
    {
        var view = fixture.NewView(setup);
        view.Section = section;
        Tree.Layout(view, 1920, 1080);
        return view;
    }

    [Fact]
    public void The_Keys_Screen_Lists_The_Main_Action_And_One_Row_For_Every_Page()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var keys = Keys(Open(fixture, SettingsSection.Key));

            Assert.Contains("key:main", keys);
            foreach (var page in fixture.Session.Pages.Pages) Assert.Contains($"key:{page.Id}", keys);
            Assert.Contains("key:all", keys); // and the way back to the original keys
        });
    }

    [Fact]
    public void The_Things_Screen_Has_A_Key_Row_For_Every_Pick_That_Is_On_The_Island()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var keys = Keys(Open(fixture, SettingsSection.OnTheIsland));

            Assert.NotEmpty(fixture.Session.Picks.Picks);
            foreach (var pick in fixture.Session.Picks.Picks) Assert.Contains($"key:{pick.Id}", keys);
        });
    }

    [Fact]
    public void The_Scenes_Screen_Has_A_Key_Row_For_Every_Scene_And_The_Mode_Screen_For_The_Next_Mode()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            Assert.True(fixture.Session.CreateScene("Alpha scene").Ok);
            var scene = fixture.Session.Scenes.Items[^1];

            Assert.Contains($"key:{KeybindEditor.SceneActionId(scene.Id)}", Keys(Open(fixture, SettingsSection.Scenes)));
            Assert.Contains($"key:{KeybindEditor.ModeNextId}", Keys(Open(fixture, SettingsSection.Mode)));
        });
    }

    [Fact]
    public void A_Key_Row_Reads_The_Key_It_Has_Now()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            Assert.True(fixture.Session.PressKey(fixture.Session.Pages.Pages[0].Id, new KeyPress(0x4A, HotkeyModifiers.Control | HotkeyModifiers.Alt, false)).Changed);
            var view = Open(fixture, SettingsSection.Key);

            var name = Tree.AccessibleName(Tree.ButtonKeyed(view, $"key:{fixture.Session.Pages.Pages[0].Id}"));
            Assert.Contains("Ctrl+Alt+J", name);
            Assert.Contains("Ctrl+Q", Tree.AccessibleName(Tree.ButtonKeyed(view, "key:main"))); // the main key, as it is now
        });
    }

    [Fact]
    public void The_Terminals_Page_In_The_Things_Screen_Says_It_Fills_Itself()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            Assert.Contains(fixture.Session.Pages.Pages, p => p.Id == PageIds.Terminals);
            var view = Open(fixture, SettingsSection.OnTheIsland);

            Assert.True(view.CountTextContaining(SettingsText.PageFillsItself) >= 1);
        });
    }

    [Fact]
    public void A_Full_Island_Says_There_Is_No_Room_For_The_Terminals_Page_In_The_Pages_List_And_Otherwise_Says_Nothing()
    {
        Sta.Run(() =>
        {
            using var full = Fixture.Make(terminalsNoRoom: true);
            Assert.True(Open(full, SettingsSection.Pages).CountTextContaining("Delete a page you do not use") >= 1);

            using var roomy = Fixture.Make();
            Assert.Equal(0, Open(roomy, SettingsSection.Pages).CountTextContaining("Delete a page you do not use"));
        });
    }

    [Fact]
    public void Running_The_Setup_Again_Over_A_Persons_Own_Things_Shows_Their_Key_And_Changes_Not_One_File()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            Assert.True(fixture.Session.PressKey(KeybindEditor.MainId, new KeyPress(0x4B, HotkeyModifiers.Control | HotkeyModifiers.Alt, false)).Changed);
            Assert.True(fixture.Session.SetMode(Mode.DND).Ok);
            var before = new[] { fixture.Files.SettingsPath, fixture.Files.PagesPath, fixture.Files.PicksPath }.Select(p => System.IO.File.Exists(p) ? System.IO.File.ReadAllBytes(p) : null).ToArray();

            var view = fixture.NewView(setup: true);
            Tree.Layout(view, 1920, 1080);
            var sawKey = false;
            foreach (var _ in Tree.SetupSections)
            {
                sawKey |= Tree.Of<System.Windows.Controls.Button>(view).Any(b => Tree.AccessibleName(b).Contains("Ctrl+Alt+K", StringComparison.Ordinal));
                view.PressContinue();
                Tree.Layout(view, 1920, 1080);
            }

            Assert.True(sawKey, "the setup did not show the key the person has now");
            var after = new[] { fixture.Files.SettingsPath, fixture.Files.PagesPath, fixture.Files.PicksPath }.Select(p => System.IO.File.Exists(p) ? System.IO.File.ReadAllBytes(p) : null).ToArray();
            for (var i = 0; i < before.Length; i++) Assert.True(before[i] is null ? after[i] is null : after[i] is not null && before[i]!.SequenceEqual(after[i]!), $"file {i} changed");
            Assert.Equal(Mode.DND, fixture.Session.Settings.Mode);
        });
    }
}

/// <summary>A refusal or a warning is shown at the foot of a section: it is scrolled into view so that it is not missed below the fold (design-3-3).</summary>
public class NoticeInViewTests
{
    [Fact]
    public void A_Refusal_Below_The_Fold_Is_Scrolled_Into_View()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make(graphicsLight: false);
            var view = fixture.NewView();
            view.Section = SettingsSection.General;
            Tree.Layout(view, 1920, 420); // a screen too low for the whole section

            // A refusal put in the notice line the way the screen does (the host's Report, then a redraw); the light pill that used to be pressed for it is gone (WORK-ORDER-13).
            typeof(SettingsView).GetMethod("Island.SettingsUi.ISectionHost.Report", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [new SessionResult(false, "A short refusal.")]);
            typeof(SettingsView).GetMethod("Rebuild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [null]);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            Tree.Layout(view, 1920, 420);

            var scroll = Tree.Of<System.Windows.Controls.ScrollViewer>(view).First();
            var notice = Tree.Of<System.Windows.Controls.TextBlock>(view).First(t => Tree.FocusKeyOf(t) == "notice");
            Assert.False(string.IsNullOrEmpty(notice.Text), "the refusal was not shown");
            var bounds = Tree.BoundsIn(notice, scroll);
            Assert.True(bounds.Top >= -1 && bounds.Bottom <= scroll.ViewportHeight + 1, $"the notice is at {bounds.Top:0}..{bounds.Bottom:0} in a viewport {scroll.ViewportHeight:0} high");
        });
    }
}
