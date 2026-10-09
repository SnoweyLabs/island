using Island.Core;

namespace Island.Tests;

public class StartupSwitchTests
{
    private const string Program = @"C:\Apps\Alpha\Island.App.exe";

    private static string Line(string path) => $"\"{path}\" --autostart";

    [Fact]
    public void On_Writes_One_Quoted_Value_With_Autostart()
    {
        var registry = new MemoryStartupRegistry();

        var result = new StartupSwitch(registry, Program).TurnOn();

        Assert.True(result.Ok);
        Assert.Equal(1, registry.Writes);
        Assert.Equal(0, registry.Removes);
        Assert.Equal("\"C:\\Apps\\Alpha\\Island.App.exe\" --autostart", registry.Value);
    }

    [Fact]
    public void Off_Deletes_It()
    {
        var registry = new MemoryStartupRegistry(Line(Program));

        var result = new StartupSwitch(registry, Program).TurnOff();

        Assert.True(result.Ok);
        Assert.Equal(1, registry.Removes);
        Assert.Null(registry.Value);
        Assert.Equal(0, registry.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"C:\\Other\\Beta.exe\" --autostart")]
    [InlineData("C:\\Stale\\Alpha.exe")]
    public void Launch_Never_Writes(string? held)
    {
        // Launch is: build the switch, ask what it shows, read the argument. Nothing there may touch the registry,
        // whether the value is missing, names another program, or is left over from a folder that moved.
        var registry = new MemoryStartupRegistry(held);

        var sw = new StartupSwitch(registry, Program);
        _ = sw.IsOn();
        _ = sw.CommandLine;
        _ = StartupSwitch.IsAutostartLaunch(["--autostart"]);

        Assert.Equal(0, registry.Writes);
        Assert.Equal(0, registry.Removes);
        Assert.Equal(held, registry.Value);
    }

    [Fact]
    public void The_Switch_Shows_What_The_Registry_Holds()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, Program);
        Assert.False(sw.IsOn());

        sw.TurnOn();
        Assert.True(sw.IsOn());

        // Somebody else removes the value (Registry Editor, a cleaner): the switch follows, nothing remembers.
        registry.Remove();
        Assert.False(sw.IsOn());
    }

    [Fact]
    public void A_Value_That_Names_Another_Program_Shows_Off_And_Is_Not_Deleted()
    {
        var other = Line(@"C:\Apps\Beta\Island.App.exe");
        var registry = new MemoryStartupRegistry(other);
        var sw = new StartupSwitch(registry, Program);

        Assert.False(sw.IsOn());
        Assert.True(sw.TurnOff().Ok);

        Assert.Equal(other, registry.Value);
        Assert.Equal(0, registry.Removes);
    }

    [Fact]
    public void A_Longer_Path_That_Starts_Like_This_One_Is_Not_This_Program()
    {
        var registry = new MemoryStartupRegistry(Line(Program + ".bak"));

        Assert.False(new StartupSwitch(registry, Program).IsOn());
    }

    [Fact]
    public void The_Path_Is_Compared_Without_Regard_To_Case()
    {
        var registry = new MemoryStartupRegistry(Line(Program.ToUpperInvariant()));

        Assert.True(new StartupSwitch(registry, Program).IsOn());
    }

    [Fact]
    public void A_Too_Long_Path_Is_Refused()
    {
        // The command line is the path, two quotes, a space and "--autostart": 14 characters beyond the path.
        var longest = Program + new string('a', StartupKey.MaxCommandLineLength - 14 - Program.Length);
        var tooLong = longest + "a";
        var registry = new MemoryStartupRegistry();

        var refused = new StartupSwitch(registry, tooLong).TurnOn();
        Assert.False(refused.Ok);
        Assert.Equal("STARTUP_PATH_TOO_LONG", refused.Refusal?.Code);
        Assert.Equal(0, registry.Writes);
        Assert.Null(registry.Value);

        // The longest line the page allows is still written.
        Assert.Equal(StartupKey.MaxCommandLineLength, Line(longest).Length);
        Assert.True(new StartupSwitch(registry, longest).TurnOn().Ok);
        Assert.Equal(Line(longest), registry.Value);
    }

    [Fact]
    public void A_Path_With_Spaces_Is_Quoted_As_One_Piece()
    {
        var path = @"C:\Program Files\My Island\Island.App.exe";
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, path);

        sw.TurnOn();

        Assert.Equal("\"C:\\Program Files\\My Island\\Island.App.exe\" --autostart", registry.Value);
        Assert.True(sw.IsOn());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Island.App.exe")]
    [InlineData(@"dist\Island\Island.App.exe")]
    [InlineData("C:\\Apps\\Al\"pha\\Island.App.exe")]
    [InlineData("C:\\Apps\\Alpha\\")]
    [InlineData("C:\\Apps\\Alpha\\Island\0.exe")]
    [InlineData("C:\\Apps\\Alpha\\Island\r\n.exe")]
    public void A_Path_That_Cannot_Be_Quoted_Safely_Writes_Nothing(string path)
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, path);

        var result = sw.TurnOn();

        Assert.False(result.Ok);
        Assert.Null(result.Refusal);
        Assert.Null(sw.CommandLine);
        Assert.False(sw.IsOn());
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    public void A_Null_Path_Writes_Nothing()
    {
        var registry = new MemoryStartupRegistry();

        Assert.False(new StartupSwitch(registry, null!).TurnOn().Ok);
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    public void A_Registry_That_Refuses_Writes_Leaves_The_Switch_Off()
    {
        var registry = new MemoryStartupRegistry { RefuseChanges = true };
        var sw = new StartupSwitch(registry, Program);

        var result = sw.TurnOn();

        Assert.False(result.Ok);
        Assert.Null(result.Refusal);
        Assert.False(sw.IsOn());
    }

    [Fact]
    public void A_Registry_That_Refuses_Removal_Leaves_The_Switch_On()
    {
        var registry = new MemoryStartupRegistry(Line(Program)) { RefuseChanges = true };
        var sw = new StartupSwitch(registry, Program);

        var result = sw.TurnOff();

        Assert.False(result.Ok);
        Assert.True(sw.IsOn());
    }

    [Fact]
    public void Turning_On_Twice_Writes_Once()
    {
        var registry = new MemoryStartupRegistry();
        var sw = new StartupSwitch(registry, Program);

        sw.TurnOn();
        sw.TurnOn();

        Assert.Equal(1, registry.Writes);
    }

    [Fact]
    public void Turning_Off_When_It_Is_Off_Removes_Nothing()
    {
        var registry = new MemoryStartupRegistry();

        Assert.True(new StartupSwitch(registry, Program).TurnOff().Ok);
        Assert.Equal(0, registry.Removes);
    }

    [Fact]
    public void Turning_On_Replaces_A_Value_That_Names_Another_Program()
    {
        var registry = new MemoryStartupRegistry(Line(@"C:\Apps\Beta\Island.App.exe"));

        new StartupSwitch(registry, Program).TurnOn();

        Assert.Equal(Line(Program), registry.Value);
        Assert.Equal(1, registry.Writes);
    }

    [Fact]
    public void The_Key_Is_The_Current_Users_Run_Key_From_The_Learn_Page()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartupKey.KeyPath);
        Assert.Equal(260, StartupKey.MaxCommandLineLength);
    }
}
