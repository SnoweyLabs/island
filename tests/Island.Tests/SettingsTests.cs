using Island.Core;

namespace Island.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "island-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* temp folder, nothing depends on it */ }
    }
}

public class SettingsTests
{
    [Fact]
    public void Missing_File_Gives_Defaults()
    {
        using var dir = new TempDir();
        var load = Settings.Load(dir.File("settings.json"));
        Assert.Equal(SettingsStatus.Missing, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
        Assert.Equal("Ctrl+Q", load.Settings.ShowHide.ToString());
        Assert.Equal(5, load.Settings.IdleSeconds);
        Assert.All(Pages.BuiltIn, p => Assert.Null(load.Settings.KeyFor(p.Id)));
        Assert.False(System.IO.File.Exists(dir.File("settings.json")), "Loading must not create the file.");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"hotkeys\":{\"media\":\"Win+1\"}}")]
    [InlineData("{\"hotkeys\":{\"media\":5}}")]
    [InlineData("{\"hotkeys\":{\"media\":\"Ctrl+Q\"}}")]
    [InlineData("{\"idleSeconds\":0}")]
    [InlineData("{\"idleSeconds\":\"soon\"}")]
    [InlineData("")]
    public void Broken_File_Gives_Defaults_And_Is_Left_Untouched(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        System.IO.File.WriteAllText(path, content);
        var before = System.IO.File.GetLastWriteTimeUtc(path);

        var load = Settings.Load(path);

        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
        Assert.False(string.IsNullOrEmpty(load.Detail));
        Assert.Equal(content, System.IO.File.ReadAllText(path));
        Assert.Equal(before, System.IO.File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Valid_File_Overrides_Only_What_It_Names()
    {
        var load = Settings.Parse("""
            { // my keys
              "hotkeys": { "media": "Ctrl+Alt+M", },
              "idleSeconds": 5,
            }
            """);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal("Ctrl+Alt+M", load.Settings.KeyFor("media")!.Value.ToString());
        Assert.Equal(Settings.Defaults.KeyFor("browser"), load.Settings.KeyFor("browser"));
        Assert.Equal(5, load.Settings.IdleSeconds);
    }

    [Fact]
    public void Defaults_Round_Trip_Through_Json()
    {
        var load = Settings.Parse(Settings.Defaults.ToJson());
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
    }

    [Fact]
    public void EnsureExists_Writes_Defaults_Once_And_Never_Overwrites()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "sub", "settings.json");
        Assert.True(Settings.EnsureExists(path));
        Assert.Equal(Settings.Defaults, Settings.Load(path).Settings);

        System.IO.File.WriteAllText(path, "mine");
        Assert.False(Settings.EnsureExists(path));
        Assert.Equal("mine", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void KeyFor_Maps_Each_Page_To_Its_Own_Combo()
    {
        var load = Settings.Parse("""{ "hotkeys": { "media": "Ctrl+Alt+1", "folders": "Ctrl+Alt+2", "apps": "Ctrl+Alt+3", "vibe": "Ctrl+Alt+4", "browser": "Ctrl+Alt+5" } }""");
        var combos = Pages.BuiltIn.Select(p => load.Settings.KeyFor(p.Id)).ToList();
        Assert.Equal(6, combos.Distinct().Count()); // five keys and the sixth page, Terminals, with none
    }

    // What the first build wrote on this laptop (Ctrl+Alt+Shift+Space, Ctrl+Alt+Shift+1..5, 60 s).
    private const string OldDefaultsFile = """
        {
          "hotkeys": {
            "showHide": "Ctrl+Alt+Shift+Space",
            "media": "Ctrl+Alt+Shift+1",
            "folders": "Ctrl+Alt+Shift+2",
            "apps": "Ctrl+Alt+Shift+3",
            "vibe": "Ctrl+Alt+Shift+4",
            "browser": "Ctrl+Alt+Shift+5"
          },
          "idleSeconds": 60
        }
        """;

    [Fact]
    public void Untouched_Old_Defaults_Become_The_New_Defaults()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        System.IO.File.WriteAllText(path, OldDefaultsFile);

        var load = Settings.Load(path);

        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
        Assert.Equal(Settings.Defaults.ToJson(), System.IO.File.ReadAllText(path));
        Assert.Equal(Settings.Defaults, Settings.Load(path).Settings);
        Assert.Equal("Ctrl+Q", Settings.Load(path).Settings.ShowHide.ToString());
        Assert.Equal(5, Settings.Load(path).Settings.IdleSeconds);
    }

    [Theory]
    [InlineData("5", "Ctrl+Alt+Shift+1", "Ctrl+Alt+Shift+Space", null)]     // idle changed
    [InlineData("60", "Ctrl+Alt+Shift+9", "Ctrl+Alt+Shift+Space", null)]    // a page key changed
    [InlineData("60", "Ctrl+Alt+Shift+1", "Ctrl+Alt+Shift+Enter", null)]    // the main key changed
    [InlineData("60", "Ctrl+Alt+Shift+1", "Ctrl+Alt+Shift+Space", "\"extra\": 1")] // anything added
    public void A_Changed_Old_File_Is_Left_Alone(string idle, string media, string main, string? extra)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var text = "{ \"hotkeys\": { \"showHide\": \"" + main + "\", \"media\": \"" + media
            + "\", \"folders\": \"Ctrl+Alt+Shift+2\", \"apps\": \"Ctrl+Alt+Shift+3\", \"vibe\": \"Ctrl+Alt+Shift+4\", \"browser\": \"Ctrl+Alt+Shift+5\" }, \"idleSeconds\": "
            + idle + (extra is null ? "" : ", " + extra) + " }";
        System.IO.File.WriteAllText(path, text);

        var load = Settings.Load(path);

        Assert.Equal(text, System.IO.File.ReadAllText(path));
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(main, load.Settings.ShowHide.ToString());
        Assert.Equal(media, load.Settings.KeyFor("media")!.Value.ToString());
        Assert.Equal(double.Parse(idle), load.Settings.IdleSeconds);
    }

    [Theory]
    [InlineData("0.5", 2)]
    [InlineData("1", 2)]
    [InlineData("2", 2)]
    [InlineData("5.4", 5)]
    [InlineData("60", 60)]
    [InlineData("61", 60)]
    [InlineData("3600", 60)]
    public void Idle_Time_Is_Kept_Between_Two_And_Sixty(string written, double kept)
    {
        // A settings file read at a real start.
        var load = Settings.Parse("{ \"idleSeconds\": " + written + " }");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(kept, load.Settings.IdleSeconds);

        // The settings screen keeps the same limits, saves at once, and says the area changed.
        using var dir = new TempDir();
        var session = Island.Tests.SettingsEdit.SessionFixtures.Open(Island.Tests.SettingsEdit.SessionFixtures.Files(dir));
        var areas = new List<Island.Core.SettingsEdit.SettingsArea>();
        session.Changed += areas.Add;
        var result = session.SetIdleSeconds(double.Parse(written, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(result.Refusal);
        Assert.Equal(kept, session.Settings.IdleSeconds);
        Assert.Equal(kept, Settings.Load(dir.File("settings.json")).Settings.IdleSeconds);
        Assert.Equal(kept == Settings.Defaults.IdleSeconds ? 0 : 1, areas.Count);

        // The temporary settings the self-test writes for itself are not bound.
        Assert.Equal(1.5, Settings.Parse("{ \"idleSeconds\": 1.5 }", bindIdleTime: false).Settings.IdleSeconds);
    }

    [Fact]
    public void Idle_Time_That_Is_Not_A_Number_Is_Refused_And_Nothing_Changes()
    {
        using var dir = new TempDir();
        var session = Island.Tests.SettingsEdit.SessionFixtures.Open(Island.Tests.SettingsEdit.SessionFixtures.Files(dir));

        var nan = session.SetIdleSeconds(double.NaN);
        var infinite = session.SetIdleSeconds(double.PositiveInfinity);

        Assert.NotNull(nan.Refusal);
        Assert.NotNull(infinite.Refusal);
        Assert.Equal(Settings.Defaults.IdleSeconds, session.Settings.IdleSeconds);
        Assert.False(System.IO.File.Exists(dir.File("settings.json")));
        Assert.Equal(SettingsStatus.Unreadable, Settings.Parse("{ \"idleSeconds\": 86401 }").Status);
        Assert.Equal(SettingsStatus.Unreadable, Settings.Parse("{ \"idleSeconds\": -3 }").Status);
    }

    [Fact]
    public void Pick_Keys_Are_Read_And_Written_And_Nonsense_Is_Refused_Whole()
    {
        var good = Settings.Parse("""{ "pickKeys": { "program:spotify": "Ctrl+Alt+S", "site:example.org": "" } }""");
        Assert.Equal(SettingsStatus.Loaded, good.Status);
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+S"), good.Settings.PickKeyFor("program:spotify"));
        Assert.Single(good.Settings.PickKeys); // an empty text is no key
        Assert.Equal(good.Settings, Settings.Parse(good.Settings.ToJson()).Settings);

        foreach (var bad in new[]
        {
            """{ "pickKeys": 5 }""",
            """{ "pickKeys": { "media": "Ctrl+Alt+S" } }""",
            """{ "pickKeys": { "program:spotify": 5 } }""",
            """{ "pickKeys": { "program:spotify": "Win+S" } }""",
            """{ "pickKeys": { "program:spotify": "Ctrl+Q" } }""",
        })
        {
            Assert.Equal(SettingsStatus.Unreadable, Settings.Parse(bad).Status);
        }
    }

    [Fact]
    public void Mode_Round_Trips_And_Defaults_To_Vibe()
    {
        Assert.Equal(Mode.Vibe, Settings.Defaults.Mode);
        Assert.Equal(Mode.Vibe, Settings.Parse("{}").Settings.Mode);

        foreach (var mode in Enum.GetValues<Mode>())
        {
            var settings = Settings.Defaults with { Mode = mode };
            var back = Settings.Parse(settings.ToJson());
            Assert.Equal(SettingsStatus.Loaded, back.Status);
            Assert.Equal(mode, back.Settings.Mode);
            Assert.Equal(settings, back.Settings);
        }

        // The list of programs the island never appears over and the key for the next mode travel with it.
        var full = Settings.Defaults with
        {
            Mode = Mode.Focus,
            NeverOver = NeverOverList.Empty.With(new NeverOverEntry("Alpha", "alpha.exe")).With(new NeverOverEntry("Beta", "beta.exe")),
            ModeKey = HotkeyCombo.Parse("Ctrl+Alt+M"),
        };
        var reread = Settings.Parse(full.ToJson()).Settings;
        Assert.Equal(full, reread);
        Assert.Equal(["alpha.exe", "beta.exe"], reread.NeverOver.Entries.Select(e => e.ExeFileName));
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+M"), reread.ModeKey);

        // Nonsense is refused whole, and the file is left alone.
        foreach (var bad in new[]
        {
            """{ "mode": "party" }""",
            """{ "mode": 2 }""",
            """{ "neverOver": "alpha.exe" }""",
            """{ "neverOver": [ { "name": "A", "exe": "C:\\games\\a.exe" } ] }""",
            """{ "neverOver": [ { "name": "A" } ] }""",
            """{ "hotkeys": { "modeNext": "Ctrl+Q" } }""",
        })
            Assert.Equal(SettingsStatus.Unreadable, Settings.Parse(bad).Status);
    }

    [Fact]
    public void An_Old_Settings_File_Loads_With_No_Key_For_Terminals()
    {
        var load = Settings.Parse("""{ "hotkeys": { "media": "Ctrl+Alt+1", "folders": "Ctrl+Alt+2", "apps": "Ctrl+Alt+3", "vibe": "Ctrl+Alt+4", "browser": "Ctrl+Alt+5" } }""");

        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Null(load.Settings.KeyFor(PageIds.Terminals));
        Assert.Equal(HotkeyCombo.Parse("Ctrl+Alt+5"), load.Settings.KeyFor(PageIds.Browser));
        Assert.Contains(load.Settings.PageKeys, k => k.PageId == PageIds.Terminals && k.Combo is null);
    }
}
