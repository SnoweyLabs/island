using Island.Core;

namespace Island.Tests;

/// <summary>NEXT-PLAN "FIRST, ONCE" step 1: the self-test shares nothing with the copy Dan runs.</summary>
public class SelfTestIsolationTests
{
    private static readonly HotkeyCombo Main = Settings.Defaults.ShowHide;
    private static readonly HotkeyCombo F11 = HotkeyCombo.Parse("Ctrl+Alt+Shift+F11");
    private static readonly HotkeyCombo F10 = HotkeyCombo.Parse("Ctrl+Alt+Shift+F10");
    private static readonly HotkeyCombo F9 = HotkeyCombo.Parse("Ctrl+Alt+Shift+F9");

    [Fact]
    public void Lock_Name_Differs_Under_Selftest()
    {
        var real = InstanceNames.For(selfTest: false);
        var test = InstanceNames.For(selfTest: true);

        // The real names are what Dan's copy and stop.cmd have always used.
        Assert.Equal(@"Local\Island.Snowey.Running", real.Running);
        Assert.Equal(@"Local\Island.Snowey.Again", real.Again);
        Assert.Equal(@"Local\Island.Snowey.Quit", real.Quit);

        var all = new[] { real.Running, real.Again, real.Quit, test.Running, test.Again, test.Quit, InstanceNames.SelfTestRun };
        Assert.Equal(all.Length, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(new[] { test.Running, test.Again, test.Quit }, n => Assert.Contains("SelfTest", n));
    }

    [Fact]
    public void Stand_In_Key_Only_When_The_Real_One_Is_Taken()
    {
        Assert.Equal(Main, StandInKey.Choose(Main, _ => true));
        Assert.Equal(F11, StandInKey.Choose(Main, c => c != Main));
        Assert.Equal(F10, StandInKey.Choose(Main, c => c != Main && c != F11));
        Assert.Equal(F9, StandInKey.Choose(Main, c => c == F9));
        Assert.Null(StandInKey.Choose(Main, _ => false));

        // The real key is asked first and alone when it works: no stand-in is even tried.
        var asked = new List<HotkeyCombo>();
        Assert.Equal(Main, StandInKey.Choose(Main, c => { asked.Add(c); return true; }));
        Assert.Equal([Main], asked);

        // A stand-in that a page already uses is skipped.
        Assert.Equal(F10, StandInKey.Choose(Main, c => c != Main, [F11]));
    }

    [Fact]
    public void Stand_In_Is_In_The_Settings_Before_Registering()
    {
        var dir = Path.Combine(Path.GetTempPath(), "island-test-standin-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            Settings.EnsureExists(path);

            // Windows (pretend) refuses Ctrl+Q because another copy holds it.
            var written = StandInKey.Prepare(path, Pages.BuiltIn, c => c != Main);
            Assert.True(written);

            // What the app registers next is read from the file: the main key in it is the stand-in, and Windows takes it.
            var load = Settings.Load(path, Pages.BuiltIn);
            Assert.Equal(SettingsStatus.Loaded, load.Status);
            Assert.Equal(F11, load.Settings.ShowHide);
            Assert.Equal(Settings.Defaults with { ShowHide = F11 }, load.Settings);

            // A second look finds nothing to change once the stand-in is accepted and the real key is still refused...
            Assert.False(StandInKey.Prepare(path, Pages.BuiltIn, c => c == F11));
            // ...and a free real key is left alone.
            Settings.Defaults.Save(path);
            Assert.False(StandInKey.Prepare(path, Pages.BuiltIn, _ => true));
            Assert.Equal(Main, Settings.Load(path, Pages.BuiltIn).Settings.ShowHide);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_File_That_Cannot_Be_Read_Is_Left_Alone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "island-test-standin-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, "{ not json");
            Assert.False(StandInKey.Prepare(path, Pages.BuiltIn, c => c != Main));
            Assert.Equal("{ not json", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
