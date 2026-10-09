using System.Text;
using Island.Core;

namespace Island.Attack6.Tests;

/// <summary>WO6 section 4 and 3, the file side: Settings.Parse / ToJson / Load, PickKeysJson, StartupSettingJson, with nonsense in every field.</summary>
public class SettingsFileAttackTests
{
    private static SettingsLoad Parse(string json, bool bind = true) => Settings.Parse(json, null, bind);

    private static string WithIdle(string valueJson) => "{\"idleSeconds\": " + valueJson + "}";

    // ---- idleSeconds -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1e999")]
    [InlineData("-1e999")]
    [InlineData("-0")]
    [InlineData("-0.0")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("86400.5")]
    [InlineData("86401")]
    [InlineData("1e308")]
    [InlineData("\"5\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[5]")]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("false")]
    public void Holds_An_Idle_Value_That_Is_Not_A_Number_Above_Zero_Up_To_86400_Makes_The_File_Unreadable_And_Gives_The_Defaults(string value)
    {
        var load = Parse(WithIdle(value));
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
        Assert.False(string.IsNullOrWhiteSpace(load.Detail));
    }

    [Theory]
    [InlineData("0.0001", 2)]
    [InlineData("1e-320", 2)]
    [InlineData("1", 2)]
    [InlineData("1.5", 2)]
    [InlineData("2", 2)]
    [InlineData("2.49", 2)]
    [InlineData("2.5", 2)]
    [InlineData("3", 3)]
    [InlineData("59.49", 59)]
    [InlineData("59.5", 60)]
    [InlineData("60", 60)]
    [InlineData("60.4", 60)]
    [InlineData("60.5", 60)]
    [InlineData("3600", 60)]
    [InlineData("86400", 60)]
    [InlineData("5.0", 5)]
    [InlineData("5e0", 5)]
    public void Holds_A_Usable_Idle_Value_Is_Kept_Between_Two_And_Sixty_As_A_Whole_Second_At_A_Real_Start(string value, double expected)
    {
        var load = Parse(WithIdle(value));
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(expected, load.Settings.IdleSeconds);
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("86400")]
    [InlineData("1")]
    [InlineData("123.456")]
    public void Holds_The_Self_Test_Reading_Keeps_The_Idle_Time_As_It_Is(string value)
    {
        var load = Parse(WithIdle(value), bind: false);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), load.Settings.IdleSeconds);
    }

    // ---- pickKeys ----------------------------------------------------------------------------------------------------

    private static string PickKeys(string body) => "{\"pickKeys\": " + body + "}";

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"program:a\"")]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("{\"program:a\": {}}")]
    [InlineData("{\"program:a\": []}")]
    [InlineData("{\"program:a\": 5}")]
    [InlineData("{\"program:a\": null}")]
    [InlineData("{\"program:a\": true}")]
    [InlineData("{\"program:a\": \"Ctrl+Alt+Nope\"}")]
    [InlineData("{\"program:a\": \"Ctrl+C\"}")]
    [InlineData("{\"program:a\": \"Win+A\"}")]
    [InlineData("{\"program:a\": \"A\"}")]
    [InlineData("{\"program:a\": \"Ctrl+Alt+Shift+Ctrl+A\"}")]
    [InlineData("{\"program:a\": \"Ctrl+Alt+Q\", \"site:b\": \"Ctrl+Alt+Q\"}")]
    [InlineData("{\"program:a\": \"Ctrl+Q\"}")]
    [InlineData("{\"noColon\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"a:\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a b\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a\\tb\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a\\u0000b\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a\\\\b\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a\\u00a0b\": \"Ctrl+Alt+A\"}")]
    [InlineData("{\"program:a\\ud800\": \"Ctrl+Alt+A\"}")]
    public void Holds_Nonsense_In_PickKeys_Makes_The_File_Unreadable_Never_Throws(string body)
    {
        var load = Parse(PickKeys(body));
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
    }

    [Fact]
    public void Holds_A_Name_Of_200_Characters_Is_Accepted_And_201_Is_Not()
    {
        var ok = "program:" + new string('a', 192);
        var bad = "program:" + new string('a', 193);
        Assert.Equal(SettingsStatus.Loaded, Parse(PickKeys($"{{\"{ok}\": \"Ctrl+Alt+A\"}}")).Status);
        Assert.Equal(SettingsStatus.Unreadable, Parse(PickKeys($"{{\"{bad}\": \"Ctrl+Alt+A\"}}")).Status);
    }

    [Fact]
    public void Holds_Empty_And_Missing_Entries_Mean_No_Key_And_A_Missing_Object_Means_None()
    {
        Assert.Empty(Parse("{}").Settings.PickKeys);
        Assert.Empty(Parse(PickKeys("{}")).Settings.PickKeys);
        var load = Parse(PickKeys("{\"program:a\": \"\", \"program:b\": \"Ctrl+Alt+B\"}"));
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(["program:b"], load.Settings.PickKeys.Select(k => k.PickId));
    }

    [Fact]
    public void Holds_Ten_Thousand_Empty_Entries_And_Ten_Thousand_Equal_Combinations_Are_Handled_Quickly()
    {
        var empties = string.Join(",", Enumerable.Range(0, 10_000).Select(i => $"\"program:p{i}\": \"\""));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var load = Parse(PickKeys("{" + empties + "}"));
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Empty(load.Settings.PickKeys);

        var same = string.Join(",", Enumerable.Range(0, 10_000).Select(i => $"\"program:p{i}\": \"Ctrl+Alt+A\""));
        Assert.Equal(SettingsStatus.Unreadable, Parse(PickKeys("{" + same + "}")).Status);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public void Holds_Every_Safe_Combination_Can_Be_A_Pick_Key_And_Round_Trips_Through_The_File()
    {
        var combos = SafeCombos().Where(c => c != Settings.Defaults.ShowHide).Take(500).ToList();
        var keys = combos.Select((c, i) => new PickKey($"program:p{i}", c)).ToList();
        var settings = Settings.Defaults with { PickKeys = keys };
        var back = Parse(settings.ToJson());
        Assert.Equal(SettingsStatus.Loaded, back.Status);
        Assert.True(settings.Equals(back.Settings));
    }

    [Fact]
    public void Defect_A_Repeated_Pick_Name_Loads_And_Then_Every_Save_Of_The_Settings_Fails()
    {
        // A hand-edited file with the same pick id twice (different keys, so the "same combination" rule does not catch it). JsonDocument
        // keeps both; Parse returns Loaded with two entries for one pick; ToJson builds a dictionary and throws; Save catches that and says
        // false, so the screen answers "settings.json could not be saved" to every change from then on.
        var load = Parse(PickKeys("{\"program:a\": \"Ctrl+Alt+A\", \"program:a\": \"Ctrl+Alt+B\"}"));
        var accepted = load.Status == SettingsStatus.Loaded;
        if (!accepted) return; // refusing the file would be fine
        using var temp = new TempFolder();
        Assert.True(load.Settings.Save(temp.File("s.json")), "the settings that were loaded cannot be saved again");
    }

    [Fact]
    public void Holds_Two_Page_Entries_With_The_Same_Name_Take_The_Last_And_Stay_Saveable()
    {
        var load = Parse("{\"hotkeys\": {\"apps\": \"Ctrl+Alt+A\", \"apps\": \"Ctrl+Alt+B\"}}");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        using var temp = new TempFolder();
        Assert.True(load.Settings.Save(temp.File("s.json")));
        Assert.Equal(SettingsStatus.Loaded, Settings.Load(temp.File("s.json")).Status);
    }

    // ---- startWithWindows and the rest ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("\"true\"")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Holds_Start_With_Windows_Must_Be_True_Or_False(string value)
    {
        Assert.Equal(SettingsStatus.Unreadable, Parse("{\"startWithWindows\": " + value + "}").Status);
    }

    [Fact]
    public void Holds_Start_With_Windows_Defaults_Off_And_Reads_True_And_False()
    {
        Assert.False(Parse("{}").Settings.StartWithWindows);
        Assert.True(Parse("{\"startWithWindows\": true}").Settings.StartWithWindows);
        Assert.False(Parse("{\"startWithWindows\": false}").Settings.StartWithWindows);
        Assert.False(Settings.Defaults.StartWithWindows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("\"x\"")]
    [InlineData("{\"hotkeys\": []}")]
    [InlineData("{\"hotkeys\": {\"showHide\": \"\"}}")]
    [InlineData("{\"hotkeys\": {\"showHide\": 5}}")]
    [InlineData("{\"glass\": 5}")]
    [InlineData("{\"glass\": \"purple\"}")]
    [InlineData("{\"glass\": \"\\ud800\"}")]
    [InlineData("{\"hotkeys\": {\"showHide\": \"Ctrl+Alt+A\", \"apps\": \"Ctrl+Alt+A\"}}")]
    [InlineData("{\"hotkeys\": {\"showHide\": \"Ctrl+Alt+A\"}, \"pickKeys\": {\"program:a\": \"ctrl+alt+a\"}}")]
    public void Holds_Broken_Files_Are_Unreadable_With_The_Defaults_And_Never_Throw(string json)
    {
        var load = Parse(json);
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(Settings.Defaults, load.Settings);
    }

    [Fact]
    public void Holds_Very_Deep_And_Very_Large_Files_Are_Unreadable_Not_A_Crash()
    {
        Assert.Equal(SettingsStatus.Unreadable, Parse(new string('[', 100_000)).Status);
        Assert.Equal(SettingsStatus.Unreadable, Parse("{\"a\":" + new string('[', 100_000) + "}").Status);
        var big = "{\"pickKeys\": {" + string.Join(",", Enumerable.Range(0, 200_000).Select(i => $"\"program:p{i}\": \"\"")) + "}}";
        Assert.Equal(SettingsStatus.Loaded, Parse(big).Status);
    }

    [Fact]
    public void Holds_Comments_Trailing_Commas_And_A_Byte_Order_Mark_Are_Read_And_Loading_Never_Writes_A_Broken_File()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        File.WriteAllText(path, "// note\n{ \"idleSeconds\": 9, /* c */ \"pickKeys\": { \"program:a\": \"Ctrl+Alt+A\", }, }", new UTF8Encoding(true));
        var load = Settings.Load(path);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(9, load.Settings.IdleSeconds);

        var broken = new byte[] { 0xFF, 0xFE, 0xFD, 0x7B, 0x7D };
        File.WriteAllBytes(path, broken);
        var after = Settings.Load(path);
        Assert.Equal(SettingsStatus.Unreadable, after.Status);
        Assert.Equal(broken, File.ReadAllBytes(path));
    }

    // ---- round trip -------------------------------------------------------------------------------------------------

    private static IEnumerable<HotkeyCombo> SafeCombos() =>
        AllCombos().Where(c => HotkeyCombo.TryParse(c.ToString(), out var back, out _) && back == c);

    private static IEnumerable<HotkeyCombo> AllCombos()
    {
        string keys = "BDEFGHIJKLMNOPQRSTUW0123456789";
        var mods = new[] { HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyModifiers.Alt | HotkeyModifiers.Shift, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, HotkeyModifiers.Alt, HotkeyModifiers.Control };
        foreach (var m in mods)
        {
            foreach (var ch in keys) yield return new HotkeyCombo(m, ch);
            for (var f = 0; f < 24; f++) yield return new HotkeyCombo(m, 0x70 + f);
            foreach (var vk in new[] { 0x20, 0x09, 0x0D, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D }) yield return new HotkeyCombo(m, vk);
        }
    }

    [Fact]
    public void Holds_Settings_Written_By_ToJson_Are_Read_Back_Equal_For_Many_Random_Settings()
    {
        var pages = Pages.BuiltIn.Concat([new Page("page-1", "One", "#112233", "dot", null, false), new Page("page-2", "Two", "#445566", "dot", null, false), new Page("page-9", "Nine", "#778899", "dot", null, false)]).ToList();
        var all = SafeCombos().ToList();
        for (var seed = 0; seed < 300; seed++)
        {
            var rng = new Random(seed);
            var pool = all.OrderBy(_ => rng.Next()).Take(60).ToList();
            var next = 0;
            HotkeyCombo? Maybe() => rng.Next(3) == 0 ? null : pool[next++];
            var pageKeys = pages.Select(p => new PageKey(p.Id, Maybe())).ToList();
            var picks = Enumerable.Range(0, rng.Next(0, 20)).Select(i => new PickKey($"{(i % 3 == 0 ? "site" : i % 3 == 1 ? "folder" : "program")}:p{i}.x-{rng.Next(1000)}", pool[next++])).DistinctBy(k => k.PickId).ToList();
            var idle = rng.Next(3) switch { 0 => (double)rng.Next(2, 61), 1 => 5, _ => Math.Round(rng.NextDouble() * 58 + 2) };
            var settings = new Settings(pool[next++], pageKeys, idle, (GlassKind)rng.Next(3), rng.Next(2) == 0) { PickKeys = picks };

            var back = Settings.Parse(settings.ToJson(), pages);
            Assert.Equal(SettingsStatus.Loaded, back.Status);
            Assert.True(settings.Equals(back.Settings), $"seed {seed} did not read back equal");
            Assert.Equal(settings.ToJson(), back.Settings.ToJson()); // and writing is stable
        }
    }

    [Fact]
    public void Holds_Self_Test_Settings_With_Any_Positive_Idle_Time_Read_Back_Equal_When_Not_Bound()
    {
        foreach (var idle in new[] { 0.0001, 0.5, 1, 7.25, 3599.9, 86400, double.Epsilon, 1e-300 })
        {
            var settings = Settings.Defaults with { IdleSeconds = idle };
            var back = Settings.Parse(settings.ToJson(), null, bindIdleTime: false);
            Assert.Equal(SettingsStatus.Loaded, back.Status);
            Assert.True(settings.Equals(back.Settings), $"idle {idle}");
        }
    }

    [Fact]
    public void Defect_Save_Writes_An_Idle_Time_That_The_Next_Start_Calls_Unreadable()
    {
        // Settings is a plain record: nothing stops a value of 0, a negative one or one above 86400, and Save writes it without a word.
        // The next start then reads the file as Unreadable, loses every key and locks the file. SetIdleSeconds clamps, so the screen
        // cannot make it; the type allows it (LATENT).
        using var temp = new TempFolder();
        foreach (var idle in new[] { 0.0, -1.0, 86401.0, 1e300 })
        {
            var path = temp.File("s.json");
            var saved = (Settings.Defaults with { IdleSeconds = idle }).Save(path);
            if (!saved) continue; // refusing to write it would be fine
            Assert.True(Settings.Load(path).Status == SettingsStatus.Loaded, $"idle {idle} was saved but cannot be read back");
        }
    }

    [Fact]
    public void Holds_Save_Refuses_To_Write_A_Number_That_Is_Not_One_And_Leaves_The_Old_File()
    {
        using var temp = new TempFolder();
        var path = temp.File("s.json");
        Assert.True(Settings.Defaults.Save(path));
        var before = File.ReadAllText(path);
        foreach (var idle in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.False((Settings.Defaults with { IdleSeconds = idle }).Save(path));
            Assert.Equal(before, File.ReadAllText(path));
        }
    }

    [Fact]
    public void Holds_Load_Never_Writes_An_Unreadable_File_And_The_Defaults_File_Is_Written_Only_When_Missing()
    {
        using var temp = new TempFolder();
        var path = temp.File("s.json");
        Assert.True(Settings.EnsureExists(path));
        Assert.False(Settings.EnsureExists(path));
        var text = File.ReadAllText(path);
        Assert.Equal(SettingsStatus.Loaded, Settings.Load(path).Status);
        Assert.Equal(text, File.ReadAllText(path));
        Assert.True(Settings.Defaults.Equals(Settings.Load(path).Settings));
    }

    [Fact]
    public void Holds_Equal_Settings_Have_Equal_Hash_Codes_And_Different_Pick_Keys_Make_Them_Unequal()
    {
        var a = Settings.Defaults with { PickKeys = [new PickKey("program:a", Combos.A)] };
        var b = Settings.Defaults with { PickKeys = [new PickKey("program:a", Combos.A)] };
        var c = Settings.Defaults with { PickKeys = [new PickKey("program:a", Combos.B)] };
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(null));
    }
}
