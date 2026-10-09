using Island.Core;

namespace Island.Tests;

public class PageTests
{
    [Fact]
    public void Built_In_Pages_Keep_Their_Pinned_Colours_And_Order()
    {
        Assert.Equal(
            [("media", "Media", "#FF4055"), ("folders", "Folders", "#FFB81C"), ("apps", "Apps", "#1F6FFF"),
             ("vibe", "Vibe coding", "#19E6B3"), ("browser", "Browser", "#E9A0FF"), ("terminals", "Terminals", "#B8F03A")],
            Pages.BuiltIn.Select(p => (p.Id, p.Name, p.Color)).ToArray());
        Assert.All(Pages.BuiltIn, p => Assert.True(p.IsBuiltIn));
    }

    [Fact]
    public void Default_Keybinds_Are_One_To_Five_And_All_Different()
    {
        Assert.Equal(
            ["Ctrl+Alt+Shift+1", "Ctrl+Alt+Shift+2", "Ctrl+Alt+Shift+3", "Ctrl+Alt+Shift+4", "Ctrl+Alt+Shift+5"],
            Pages.BuiltIn.Take(5).Select(p => p.Keybind!).ToArray());
        Assert.Null(Pages.BuiltIn[5].Keybind); // the sixth page, Terminals, has no key by default (WORK-ORDER-11 section 1)
    }

    [Fact]
    public void Pages_Are_A_List_Not_An_Enum()
    {
        // EVALS.md D1: no enum of five pages anywhere in the logic.
        var source = Directory.EnumerateFiles(RepoPaths.File("src", "Island.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(File.ReadAllText);
        Assert.DoesNotContain(source, text => System.Text.RegularExpressions.Regex.IsMatch(text, @"\benum\s+Categor"));
        Assert.DoesNotContain(typeof(Page).Assembly.GetTypes(), t => t.Name == "CategoryId");
    }

    [Fact]
    public void A_Custom_Page_Works_Like_A_Built_In_One()
    {
        var mine = new Page("games", "Games", "#12AB34", "grid", null, false);
        Page[] pages = [.. Pages.BuiltIn, mine];
        var m = new IslandMachine(60, pages);

        m.PageKey("games", 0);
        for (var t = 0.0; t < 3000; t += 1000.0 / 60) m.Tick(t);

        Assert.Equal("games", m.PageId);
        Assert.Equal(IslandPhase.Open, m.Phase);
        Assert.False(m.Page.IsBuiltIn);
        Assert.Equal("#12AB34", m.Page.Color);
        // No items yet: an empty page is a short, legal capsule, not a zero-width shape.
        Assert.Equal(CapsuleLayout.Width(0, false), m.CapsuleTargetWidth);
        Assert.True(m.CapsuleTargetWidth > LookConstants.BallSize);
    }

    [Fact]
    public void Unknown_Page_Key_Is_Ignored()
    {
        var m = new IslandMachine();
        m.PageKey("nope", 0);
        Assert.Equal(IslandPhase.Hidden, m.Phase);
    }

    [Fact]
    public void Settings_Hold_A_Keybind_Per_Page_Id_And_Allow_None()
    {
        var load = Settings.Parse("""{ "hotkeys": { "apps": "", "media": "Ctrl+Alt+M" } }""");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Null(load.Settings.KeyFor("apps"));
        Assert.NotNull(load.Settings.KeyFor("media"));

        var back = Settings.Parse(load.Settings.ToJson());
        Assert.Equal(load.Settings, back.Settings);
    }
}
