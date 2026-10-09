using Island.Core;

namespace Island.Tests;

public class OutsideGateTests
{
    [Fact]
    public void Normal_Running_Allows_Everything()
    {
        var gate = new OutsideGate(selfTest: false);
        foreach (var kind in Enum.GetValues<OutsideKind>()) Assert.True(gate.Allow(kind, 12345));
        Assert.Equal(0, gate.RefusedTotal);
        Assert.Equal(Enum.GetValues<OutsideKind>().Length, gate.AllowedCount);
    }

    [Fact]
    public void Selftest_Refuses_Everything_But_Its_Own_Windows_And_Own_Copy_And_Counts()
    {
        var gate = new OutsideGate(selfTest: true, ownProcessId: 100);

        Assert.True(gate.Allow(OutsideKind.BringForward, 100));
        Assert.True(gate.Allow(OutsideKind.StartOwnCopy));

        Assert.False(gate.Allow(OutsideKind.BringForward, 101));   // someone else's window
        Assert.False(gate.Allow(OutsideKind.BringForward));        // owner unknown
        Assert.False(gate.Allow(OutsideKind.StartProgram));
        Assert.False(gate.Allow(OutsideKind.OpenFolder));
        Assert.False(gate.Allow(OutsideKind.OpenAddress));
        Assert.False(gate.Allow(OutsideKind.MediaCommand));
        Assert.False(gate.Allow(OutsideKind.TabCommand));
        Assert.False(gate.Allow(OutsideKind.OpenFile));

        Assert.Equal(8, gate.RefusedTotal);
        Assert.Equal(2, gate.Refused(OutsideKind.BringForward));
        Assert.Equal(1, gate.Refused(OutsideKind.MediaCommand));
        Assert.Equal(2, gate.AllowedCount);
        Assert.Equal(2, gate.RefusedCounts()["BringForward"]); // numbers by kind only: nothing about what was asked for
    }

    [Fact]
    public void Recording_Outside_Records_Calls_And_Does_Nothing_Else()
    {
        var o = new RecordingOutside();
        o.BringForward(5);
        o.StartProgram(Pick.ForProgram("Notepad", PageIds.Apps, "notepad.exe", null));
        o.OpenFolder("Downloads");
        o.OpenSite("youtube.com");
        Assert.Equal(["bring:5", "start:program:notepad", "folder:Downloads", "site:youtube.com"], o.Calls);
    }

    [Fact]
    public void A_Site_Address_Is_Built_From_A_Host_Only()
    {
        Assert.Equal("https://music.youtube.com/", SiteAddress.For("Music.YouTube.com"));
        Assert.Equal("https://youtube.com/", SiteAddress.For("www.youtube.com"));
    }
}

public class PretendWorldTests
{
    [Fact]
    public void The_Pretend_World_Drives_Pick_State_Like_The_Real_One_Will()
    {
        var world = new PretendWorld
        {
            Windows = [Fixtures.Window(1, "spotify.exe")],
            FolderWindows = [new FolderWindow(20, "Downloads", "pretend", 0)],
            Connected = true,
            Tabs = [Fixtures.Tab(1, "youtube.com", 5), Fixtures.Tab(2, "youtube.com", 6)],
        };
        var raised = 0;
        world.Changed += () => raised++;

        var spotify = Pick.ForProgram("Spotify", PageIds.Media, "Spotify.exe", null);
        var downloads = Pick.ForFolder("Downloads", PageIds.Folders);
        var documents = Pick.ForFolder("Documents", PageIds.Folders);
        var youtube = Pick.ForSite("YouTube", "youtube.com", PageIds.Media);
        var snap = world.Snapshot();

        Assert.True(PickStates.For(spotify, snap).IsOpen);
        Assert.True(PickStates.For(downloads, snap).IsOpen);
        Assert.False(PickStates.For(documents, snap).IsOpen);
        Assert.Equal(2, PickStates.For(youtube, snap).Count);

        world.Raise();
        Assert.Equal(1, raised);
        Assert.True(world.Send("none", MediaCommand.Next) == false);
        Assert.Equal(["media:none:Next"], world.Sent);
    }

    [Fact]
    public void Icons_Are_Found_By_Exe_Name_Or_Package_Family()
    {
        var world = new PretendWorld();
        world.Icons["Spotify.exe"] = new IconImage(1, 1, [0, 0, 0, 255]);
        Assert.NotNull(world.ProgramIcon("spotify.exe", null));
        Assert.Null(world.ProgramIcon("other.exe", null));
        Assert.Null(world.FolderIcon("Downloads"));
    }
}

public class GlassSettingTests
{
    [Fact]
    public void Glass_Defaults_To_Approved_And_Round_Trips()
    {
        Assert.Equal(GlassKind.Approved, Settings.Defaults.Glass);
        foreach (var kind in Enum.GetValues<GlassKind>())
        {
            var settings = Settings.Defaults with { Glass = kind };
            var load = Settings.Parse(settings.ToJson());
            Assert.Equal(SettingsStatus.Loaded, load.Status);
            Assert.Equal(kind, load.Settings.Glass);
        }
    }

    [Fact]
    public void An_Old_File_Without_Glass_Loads_As_Approved()
    {
        var load = Settings.Parse("""{ "hotkeys": { "showHide": "Ctrl+Q" }, "idleSeconds": 5 }""");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(GlassKind.Approved, load.Settings.Glass);
    }

    [Theory]
    [InlineData("""{ "glass": "frosted" }""")]
    [InlineData("""{ "glass": 3 }""")]
    [InlineData("""{ "glass": null }""")]
    public void A_Wrong_Glass_Value_Makes_The_File_Unreadable(string json)
    {
        Assert.Equal(SettingsStatus.Unreadable, Settings.Parse(json).Status);
    }

    [Fact]
    public void Save_Writes_The_Settings_And_A_Bad_Path_Does_Not_Throw()
    {
        using var dir = new TempDir();
        var path = dir.File("sub/settings.json");
        var changed = Settings.Defaults with { Glass = GlassKind.Darker };
        Assert.True(changed.Save(path));
        Assert.Equal(changed, Settings.Load(path).Settings);
        Assert.False(changed.Save(dir.Path)); // a folder in the way
    }
}
