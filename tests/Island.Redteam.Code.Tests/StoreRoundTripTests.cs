using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: what the stores save can always be loaded again, whatever the names hold (quotes, backslashes, line breaks, emoji, very long text, the markers of a path), and a
/// store that refuses an edit leaves what it had. Random sequences of edits with invented names; then the names that draw as nothing or are half a character.
/// </summary>
public class StoreRoundTripTests
{
    private static readonly string[] Names =
    [
        "Alpha", "A\"B", "back\\slash", "tab\tinside", "line\nbreak", "\U0001F600 smile", "\u00DF", "\u0130stanbul", new string('x', 30), new string('y', 5000), "<>&'", "a\u0000b",
        "C:\\Users\\Invented", "%USERPROFILE%", "  padded  ", "\u200Bhidden", "\u202Eflip", "A  B", "a/b", "../x", "program:x", "site:example.org", "",
    ];

    [Fact]
    public void Pages_Saved_Are_Loaded_Again_Equal_For_Any_Name_The_Store_Accepted()
    {
        using var s = new Scratch();
        var rng = new Random(4);
        var store = PageStore.Default;
        for (var step = 0; step < 600; step++)
        {
            var name = Names[rng.Next(Names.Length)] + (rng.Next(3) == 0 ? rng.Next(100) : "");
            var colour = rng.Next(5) == 0 ? "not a colour" : $"#{rng.Next(0x1000000):X6}";
            PageEdit edit = rng.Next(4) switch
            {
                0 => store.Create(name, colour),
                1 => store.Rename(store.Pages[rng.Next(store.Pages.Count)].Id, name),
                2 => store.Recolour(store.Pages[rng.Next(store.Pages.Count)].Id, colour),
                _ => new PageEdit(store.Delete(store.Pages[rng.Next(store.Pages.Count)].Id, PickStore.Empty, confirmed: true).Pages, null, null, null),
            };
            var before = store.ToJson();
            if (edit.Refused)
            {
                Assert.Equal(before, edit.Store.ToJson());
                continue;
            }

            store = edit.Store;
            var path = s.Path_("pages.json");
            var saved = false;
            Assert.Null(Record.Exception(() => saved = store.Save(path)));
            Assert.True(saved, "a store the code itself built could not be saved");
            var load = PageStore.Load(path);
            Assert.True(load.Status == PageStoreStatus.Loaded, $"step {step}: what was saved does not load: {load.Detail}");
            Assert.Equal(store.Pages.Select(p => (p.Id, p.Name, p.Color)), load.Store.Pages.Select(p => (p.Id, p.Name, p.Color)));
        }
    }

    [Fact]
    public void Picks_Saved_Are_Loaded_Again_Equal_For_Any_Pick_The_Store_Accepted()
    {
        using var s = new Scratch();
        var rng = new Random(8);
        var store = PickStore.Empty;
        for (var step = 0; step < 800; step++)
        {
            var name = Names[rng.Next(Names.Length)];
            var page = new[] { PageIds.Apps, PageIds.Media, PageIds.Folders, PageIds.Browser, PageIds.Vibe, PageIds.Terminals, "custom-1", "" }[rng.Next(8)];
            Pick pick = rng.Next(3) switch
            {
                0 => Pick.ForProgram(name, page, rng.Next(2) == 0 ? name.ToLowerInvariant() + ".exe" : null, rng.Next(2) == 0 ? "Pkg" + rng.Next(9) + "_abc" : null),
                1 => Pick.ForSite(name, rng.Next(2) == 0 ? "example.org" : name, page),
                _ => Pick.ForFolder(Pick.KnownFolders[rng.Next(Pick.KnownFolders.Count)], page),
            };
            store = rng.Next(5) switch
            {
                0 => store.Remove(store.Picks.Count == 0 ? "x" : store.Picks[rng.Next(store.Picks.Count)].Id),
                1 => store.Move(store.Picks.Count == 0 ? "x" : store.Picks[rng.Next(store.Picks.Count)].Id, page),
                _ => store.Add(pick, out _),
            };
            if (!store.Picks.All(p => p.IsStorable(out _))) continue; // a store may hold what it cannot save (it then refuses to save at all, as written)
            var path = s.Path_("picks.json");
            var saved = false;
            Assert.Null(Record.Exception(() => saved = store.Save(path)));
            if (!saved) continue;
            var load = PickStore.Load(path);
            Assert.True(load.Status == PickStoreStatus.Loaded, $"step {step}: what was saved does not load: {load.Detail}");
            Assert.Equal(store.Picks, load.Store.Picks);
        }
    }

    [Fact]
    public void Scenes_Saved_Are_Loaded_Again_Equal_For_Any_Scene_The_Store_Accepted()
    {
        using var s = new Scratch();
        var rng = new Random(15);
        var store = SceneStore.Empty;
        for (var step = 0; step < 500; step++)
        {
            var name = Names[rng.Next(Names.Length)];
            SceneEdit edit = rng.Next(4) switch
            {
                0 => store.Create(name),
                1 when store.Items.Count > 0 => store.Rename(store.Items[rng.Next(store.Items.Count)].Id, name),
                2 when store.Items.Count > 0 => store.SetThings(store.Items[rng.Next(store.Items.Count)].Id, Enumerable.Range(0, rng.Next(0, 6)).Select(_ => Pick.ForProgram(Names[rng.Next(Names.Length)], PageIds.Apps, "p" + rng.Next(5) + ".exe", null))),
                _ => store.Create("Scene " + rng.Next(1000)),
            };
            if (edit.Refused) continue;
            store = edit.Store;
            var path = s.Path_("scenes.json");
            var saved = false;
            Assert.Null(Record.Exception(() => saved = store.Save(path)));
            if (!saved) continue;
            var load = SceneStore.Load(path);
            Assert.True(load.Status == SceneStoreStatus.Loaded, $"step {step}: what was saved does not load: {load.Detail}");
            Assert.Equal(store.Items.Select(i => (i.Id, i.Name, i.Things.Count)), load.Store.Items.Select(i => (i.Id, i.Name, i.Things.Count)));
        }
    }

    [Fact]
    public void Settings_Round_Trip_Holds_For_Random_Keys_Modes_Lights_Numbers()
    {
        using var s = new Scratch();
        var rng = new Random(23);
        for (var i = 0; i < 400; i++)
        {
            var keys = new List<HotkeyCombo>();
            HotkeyCombo Fresh()
            {
                while (true)
                {
                    var c = new HotkeyCombo((HotkeyModifiers)rng.Next(1, 8), "ABDEFGHIJKLMNOPRSTUVW123456789"[rng.Next(30)]);
                    if (HotkeyCombo.TryParse(c.ToString(), out var parsed, out _) && !keys.Contains(parsed))
                    {
                        keys.Add(parsed);
                        return parsed;
                    }
                }
            }

            var settings = Settings.Defaults with
            {
                Mode = Enum.GetValues<Mode>()[rng.Next(3)],
                Glass = Enum.GetValues<GlassKind>()[rng.Next(3)],
                IdleSeconds = rng.Next(2, 61),
                NoticeSeconds = rng.Next(3, 31),
                ShowPill = rng.Next(2) == 0,
                StartWithWindows = rng.Next(2) == 0,
            };
            settings = settings with { ShowHide = Fresh() };
            if (rng.Next(2) == 0) settings = settings with { ModeKey = Fresh() };
            foreach (var page in Pages.BuiltIn)
                if (rng.Next(2) == 0) settings = settings.WithPageKey(page.Id, Fresh());
            var path = s.Path_("settings.json");
            Assert.True(settings.Save(path));
            var load = Settings.Load(path);
            Assert.Equal(SettingsStatus.Loaded, load.Status);
            Assert.Equal(settings, load.Settings);
        }
    }

    [Fact]
    public void Defect_PageStore_Accepts_A_Name_That_Draws_As_Nothing_Reverses_The_Text_Or_Holds_A_Control_Character()
    {
        // code-1-16 (LOW): SceneStore.CheckName refuses a name with a hidden, control, private-use or unassigned character, a lone surrogate (it reads as U+FFFD) and a name that draws as
        // nothing (BlankText.HasVisible), after the attack on WORK-ORDER-7 (A7A-12, A7A-13: "a scene can be named with a blank-looking character"); its own comment says it follows "the way
        // PageStore tidies a page name". PageStore.CheckName (Create, Rename) only trims, collapses white space and counts: a page named with a zero-width space (U+200B) is accepted and shows
        // an empty label in the page list, the pill of the page and every sentence that names it; one named with U+202E reverses the text round it; a control character stays in the
        // name. The same rule written in two places, repaired in one. Expected: each of these refused (as a scene's name is).
        var accepted = new List<string>();
        foreach (var name in new[] { "\u200B", "\u202Eabc", "a\u0007b", "\u3164", "\u0301" })
            if (!PageStore.Default.Create(name, "#112233").Refused) accepted.Add(string.Join(" ", name.Select(c => $"U+{(int)c:X4}")));
        Assert.True(accepted.Count == 0, "a page was made with these names: " + string.Join("; ", accepted));
    }

    [Fact]
    public void Defect_PageStore_A_Name_With_A_Lone_Surrogate_Is_Saved_As_Another_Character_And_Two_Such_Pages_Make_The_Next_Start_Unreadable()
    {
        // code-1-16 (second input): the name "a" + U+D800 is accepted; JSON cannot hold a lone surrogate, so it is saved as "a" + U+FFFD. A second page named "a" + U+FFFD is a different name in
        // memory and the same name in the file: the next start reads "Two pages have the same name" and the whole pages file is Unreadable (every page's name and colour is lost to the
        // defaults, the file is left as it is). Expected: refused at the edit (as scenes are), so that what is shown is what is kept.
        using var s = new Scratch();
        var one = PageStore.Default.Create("a\uD800", "#112233");
        var two = one.Refused ? one : one.Store.Create("a\uFFFD", "#445566");
        var store = two.Refused ? one.Store : two.Store;
        var path = s.Path_("pages.json");
        Assert.True(store.Save(path));
        var load = PageStore.Load(path);
        Assert.True(load.Status == PageStoreStatus.Loaded, $"the pages file that was just saved does not load: {load.Detail}");
    }

    [Fact]
    public void Defect_Pick_A_Name_With_A_Lone_Surrogate_Is_Storable_And_Comes_Back_As_Another_Name()
    {
        // code-1-16 (third input): Pick.IsStorable asks only that the name is not blank; a lone surrogate is kept in memory, written as U+FFFD, and read back as another name. Expected: refused,
        // or kept as it will be read. (The path rule has had its half-character refusal since WORK-ORDER-10, A10-D20; the name never got it.)
        using var s = new Scratch();
        var pick = Pick.ForProgram("lone\uDC00half", PageIds.Apps, "alpha.exe", null);
        var store = new PickStore([pick]);
        var path = s.Path_("picks.json");
        if (!store.Save(path)) return; // refused to save: fine
        var back = PickStore.Load(path).Store.Picks.Single();
        Assert.Equal(pick.Name, back.Name);
    }
}
