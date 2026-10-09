using System.Text.Json;
using Island.Core;

namespace Island.Tests;

public class StartupLaunchTests
{
    [Theory]
    [InlineData(true, "--autostart")]
    [InlineData(true, "--data-dir", "x", "--autostart")]
    [InlineData(false)]
    [InlineData(false, "--quit")]
    [InlineData(false, "--AUTOSTART")]
    [InlineData(false, "--autostart=1")]
    [InlineData(false, "autostart")]
    [InlineData(false, "")]
    public void The_Autostart_Argument_Is_Recognised_Exactly(bool expected, params string[] args) =>
        Assert.Equal(expected, StartupSwitch.IsAutostartLaunch(args));

    [Fact]
    public void The_Line_The_Switch_Writes_Carries_The_Recognised_Argument()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, @"C:\Apps\Alpha\Island.App.exe");
        sw.TurnOn();

        // What Windows hands the program is the text after the quoted path.
        var held = registry.Value!;
        var afterPath = held[(held.LastIndexOf('"') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(StartupSwitch.IsAutostartLaunch(afterPath));
    }

    [Fact]
    public void The_Refusal_Has_The_Register_Text_Word_For_Word()
    {
        var refusal = StartupRefusals.PathTooLong;

        Assert.Equal("STARTUP_PATH_TOO_LONG", refusal.Code);
        Assert.Equal(
            "Island sits in a folder whose path is too long for Windows' start-up list, so it was not added. "
            + "Windows would cut the path and start nothing. "
            + "Move the Island folder somewhere with a shorter path, then switch this on again.",
            refusal.Message);
    }

    [Theory]
    [InlineData("{}", false, false, null)]
    [InlineData("{\"startWithWindows\": true}", true, true, null)]
    [InlineData("{\"startWithWindows\": false}", true, false, null)]
    public void The_Setting_Is_A_Yes_Or_No_And_Off_When_Absent(string json, bool found, bool value, string? problem)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(found, StartupSettingJson.Read(doc.RootElement, out var got, out var why));
        Assert.Equal(value, got);
        Assert.Equal(problem, why);
    }

    [Theory]
    [InlineData("{\"startWithWindows\": \"yes\"}")]
    [InlineData("{\"startWithWindows\": 1}")]
    [InlineData("{\"startWithWindows\": null}")]
    [InlineData("{\"startWithWindows\": {}}")]
    public void A_Setting_That_Is_Not_A_Yes_Or_No_Is_A_Problem_And_Stays_Off(string json)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.True(StartupSettingJson.Read(doc.RootElement, out var value, out var problem));
        Assert.False(value);
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_Root_That_Is_Not_An_Object_Reads_As_Absent()
    {
        using var doc = JsonDocument.Parse("[true]");

        Assert.False(StartupSettingJson.Read(doc.RootElement, out var value, out _));
        Assert.False(value);
    }
}
