using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests;

/// <summary>
/// WORK-ORDER-8 section 1 (EVALS U2): the files of version 1, written by the app before schema numbers existed, load as they did; a file without a number is
/// version 1; a newer file is left alone and never saved over; reading never rewrites a file. The fixtures are never edited: a change of a file's shape adds a
/// folder beside v1.
/// </summary>
public class UpgradeTests
{
    private static string Fixture(string name) => RepoPaths.File("tests", "Island.Tests", "Fixtures", "v1", name);

    [Fact]
    public void Every_V1_File_Loads_Unchanged_In_Meaning()
    {
        var settings = Settings.Load(Fixture("settings.json"), bindIdleTime: false);
        Assert.Equal(SettingsStatus.Loaded, settings.Status);
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+K"), settings.Settings.ShowHide);
        Assert.Equal(12, settings.Settings.IdleSeconds);
        Assert.Equal(GlassKind.Darker, settings.Settings.Glass);
        Assert.Equal(Mode.DND, settings.Settings.Mode);
        Assert.Equal(9, settings.Settings.NoticeSeconds);
        Assert.False(settings.Settings.ShowPill);
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+A"), settings.Settings.PickKeyFor("program:alpha"));
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+E"), settings.Settings.PickKeyFor("scene:scene-1"));
        Assert.True(settings.Settings.NeverOver.Contains("alpha-game.exe"));

        var pages = PageStore.Load(Fixture("pages.json"));
        Assert.Equal(PageStoreStatus.Loaded, pages.Status);
        Assert.Equal(["Media", "Folders", "Apps", "Vibe coding", "Browser", "Alpha pages", "Terminals"], pages.Store.Pages.Select(p => p.Name));

        var picks = PickStore.Load(Fixture("picks.json"));
        Assert.Equal(PickStoreStatus.Loaded, picks.Status);
        Assert.Equal(["program:alpha", "folder:downloads", "site:alpha.example"], picks.Store.Picks.Select(p => p.Id));

        var scenes = SceneStore.Load(Fixture("scenes.json"));
        Assert.Equal(SceneStoreStatus.Loaded, scenes.Status);
        Assert.Equal(["program:alpha", "site:alpha.example"], scenes.Store.Items.Single().Things.Select(t => t.Id));
    }

    [Fact]
    public void A_File_Without_A_Schema_Number_Is_Version_One()
    {
        // The v1 files carry none (settings) or the old "version" (the others); none of them is newer than this build.
        foreach (var name in new[] { "settings.json", "pages.json", "picks.json", "scenes.json" })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Fixture(name)));
            Assert.Equal(1, FileSchema.Of(doc.RootElement));
            Assert.Null(FileSchema.Problem(doc.RootElement));
        }

        using var bare = System.Text.Json.JsonDocument.Parse("{}");
        Assert.Equal(1, FileSchema.Of(bare.RootElement));
        // What this build writes carries the number.
        using var written = System.Text.Json.JsonDocument.Parse(Settings.Defaults.ToJson());
        Assert.Equal(FileSchema.Current, written.RootElement.GetProperty("schema").GetInt32());
        foreach (var json in new[] { PageStore.Default.ToJson(), PickStore.Empty.ToJson(), SceneStore.Empty.ToJson() })
        {
            using var d = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(FileSchema.Current, d.RootElement.GetProperty("schema").GetInt32());
        }
    }

    [Fact]
    public void A_Newer_File_Is_Left_Untouched_And_Never_Saved_Over()
    {
        using var dir = new TempDir();
        var newer = FileSchema.Current + 1;
        var texts = new Dictionary<string, string>
        {
            ["settings.json"] = $"{{\"schema\":{newer},\"hotkeys\":{{\"showHide\":\"Ctrl+Alt+K\"}},\"future\":true}}",
            ["pages.json"] = $"{{\"schema\":{newer},\"pages\":[]}}",
            ["picks.json"] = $"{{\"schema\":{FileSchema.CurrentPicks + 1},\"picks\":[],\"future\":1}}", // the picks file has its own number since WORK-ORDER-10
            ["scenes.json"] = $"{{\"schema\":{newer},\"scenes\":[]}}",
        };
        foreach (var (name, text) in texts) File.WriteAllText(dir.File(name), text);

        var settings = Settings.Load(dir.File("settings.json"));
        Assert.Equal(SettingsStatus.Unreadable, settings.Status);
        Assert.True(FileSchema.IsNewer(settings.Detail));
        Assert.Equal(Settings.Defaults.ShowHide, settings.Settings.ShowHide); // it runs on defaults
        Assert.True(FileSchema.IsNewer(PageStore.Load(dir.File("pages.json")).Detail));
        Assert.True(FileSchema.IsNewer(PickStore.Load(dir.File("picks.json")).Detail));
        Assert.True(FileSchema.IsNewer(SceneStore.Load(dir.File("scenes.json")).Detail));

        // The session built over them refuses every edit that would write them, and the files stay byte for byte as they were.
        var files = new SettingsFiles(dir.File("settings.json"), dir.File("pages.json"), dir.File("picks.json"), dir.File("scenes.json"));
        var session = new SettingsSession(files, settings, PageStore.Load(files.PagesPath), PickStore.Load(files.PicksPath), new SettingsEdit.FakeRegistrar(), () => [], () => false,
            scenes: SceneStore.Load(files.ScenesPath!));
        Assert.False(session.SetMode(Mode.Focus).Ok);
        Assert.False(session.CreatePage("Alpha", "#7CE04A").Ok);
        Assert.False(session.CreateScene("Evening").Ok);
        foreach (var (name, text) in texts) Assert.Equal(text, File.ReadAllText(dir.File(name)));

        // The refusal for it is in the register, in the words of the order.
        Assert.Contains(Refusals.All, r => r.Code == "SETTINGS_FROM_NEWER_VERSION");
        Assert.StartsWith("Your settings were written by a newer Island", Refusals.SettingsFromNewerVersion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_Alone_Never_Rewrites_A_File()
    {
        using var dir = new TempDir();
        var names = new[] { "settings.json", "pages.json", "picks.json", "scenes.json" };
        foreach (var name in names) File.Copy(Fixture(name), dir.File(name));
        var before = names.ToDictionary(n => n, n => (File.ReadAllBytes(dir.File(n)), File.GetLastWriteTimeUtc(dir.File(n))));

        _ = Settings.Load(dir.File("settings.json"));
        _ = PageStore.Load(dir.File("pages.json"));
        _ = PickStore.Load(dir.File("picks.json"));
        _ = SceneStore.Load(dir.File("scenes.json"));
        var files = new SettingsFiles(dir.File("settings.json"), dir.File("pages.json"), dir.File("picks.json"), dir.File("scenes.json"));
        _ = new SettingsSession(files, Settings.Load(files.SettingsPath), PageStore.Load(files.PagesPath), PickStore.Load(files.PicksPath), new SettingsEdit.FakeRegistrar(), () => [], () => false,
            scenes: SceneStore.Load(files.ScenesPath!));

        foreach (var name in names)
        {
            Assert.Equal(before[name].Item1, File.ReadAllBytes(dir.File(name)));
            Assert.Equal(before[name].Item2, File.GetLastWriteTimeUtc(dir.File(name)));
        }
    }
}
