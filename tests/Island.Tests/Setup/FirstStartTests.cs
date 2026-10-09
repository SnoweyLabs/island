using Island.Core;
using Island.Core.SettingsEdit;
using Island.Tests.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.Setup;

/// <summary>WORK-ORDER-7 section 6: when the setup runs, what its steps are, and that it only ever changes what the person touches.</summary>
public class FirstStartTests
{
    [Fact]
    public void Runs_Only_Without_A_Settings_File()
    {
        Assert.True(FirstStart.ShouldRun(settingsFileExisted: false, startedByWindows: false, selfTest: false));
        Assert.False(FirstStart.ShouldRun(settingsFileExisted: true, startedByWindows: false, selfTest: false));
    }

    [Theory]
    [InlineData(false, true, false)] // --autostart: Windows started it, quietly
    [InlineData(false, false, true)] // --selftest
    [InlineData(false, true, true)]
    public void Never_Under_Autostart_Or_Selftest(bool existed, bool autostart, bool selfTest) =>
        Assert.False(FirstStart.ShouldRun(existed, autostart, selfTest));

    [Fact]
    public void The_Steps_Are_The_Five_Of_The_Chosen_Preview_With_The_Practice_After_The_Key()
    {
        Assert.Equal([SetupStep.Welcome, SetupStep.Key, SetupStep.Practice, SetupStep.Pages, SetupStep.OnTheIsland, SetupStep.Addon, SetupStep.Mode], FirstStart.Steps.Select(s => s.Step)); // the practice is Dan's tutorial (WORK-ORDER-13)
        Assert.Equal("Welcome to Island", FirstStart.Steps[0].Title);
        Assert.Equal("Choose your key", FirstStart.Steps[1].Title);
        Assert.Equal("Try it out", FirstStart.Steps[2].Title);
        Assert.Equal("Your browser tabs", FirstStart.Steps[5].Title); // the Chrome step, Dan's of 2026-10-09 (version 1.0.1)
        Assert.Equal("When should it show up?", FirstStart.Steps[6].Title);
        Assert.Equal(["Start", "Continue", "Continue", "Continue", "Continue", "Continue", "Done"], Enumerable.Range(0, 7).Select(FirstStart.ButtonText));
    }

    [Fact]
    public void Leaving_Early_Keeps_What_Was_Chosen()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var registrar = new FakeRegistrar();
        Settings.Defaults.Save(files.SettingsPath);
        var session = SessionFixtures.Open(files, registrar: registrar);

        // The key is chosen in the second step; then the person leaves with Esc.
        Assert.True(session.PressKey(KeybindEditor.MainId, Press('Z', HotkeyModifiers.Control | HotkeyModifiers.Alt)).Ok);
        Assert.Equal(SetupEnd.JustClose, FirstStart.EndOf(done: false));

        var kept = Settings.Load(files.SettingsPath, Pages.BuiltIn, bindIdleTime: false).Settings;
        Assert.Equal(Combo("Ctrl+Alt+Z"), kept.ShowHide);
        Assert.Equal(Settings.Defaults.Mode, kept.Mode); // the rest stays as it was
        Assert.Equal(Settings.Defaults.IdleSeconds, kept.IdleSeconds);
    }

    [Fact]
    public void Run_Again_Shows_Current_Values_And_Changes_Nothing_Untouched()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var mine = Settings.Defaults with { ShowHide = Combo("Ctrl+Alt+K"), Mode = Mode.DND, IdleSeconds = 17 };
        mine.Save(files.SettingsPath);
        var before = File.ReadAllBytes(files.SettingsPath);
        var session = SessionFixtures.Open(files, settings: mine);

        // The steps show what is set now...
        Assert.Equal(Combo("Ctrl+Alt+K"), KeybindEditor.KeyOf(session.Settings, KeybindEditor.MainId));
        Assert.Equal(Mode.DND, session.Settings.Mode);

        // ...and walking through all of them without touching anything writes nothing.
        foreach (var _ in FirstStart.Steps) { }
        Assert.Equal(before, File.ReadAllBytes(files.SettingsPath));
        Assert.False(File.Exists(files.PagesPath));

        // Touching one thing changes that thing only.
        Assert.True(session.SetMode(Mode.Focus).Ok);
        var after = Settings.Load(files.SettingsPath, Pages.BuiltIn, bindIdleTime: false).Settings;
        Assert.Equal(Mode.Focus, after.Mode);
        Assert.Equal(Combo("Ctrl+Alt+K"), after.ShowHide);
        Assert.Equal(17, after.IdleSeconds);
    }

    [Fact]
    public void Done_Leaves_The_Island_Open()
    {
        // The screen shrinks to the ball, the ball flies to the top and the island takes its place, open; Esc only closes.
        Assert.Equal(SetupEnd.ShrinkFlyAndOpenIsland, FirstStart.EndOf(done: true));
        Assert.NotEqual(FirstStart.EndOf(done: true), FirstStart.EndOf(done: false));
    }
}
