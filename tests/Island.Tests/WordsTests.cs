using Island.Core;
using Island.Core.SettingsEdit;
using Island.Tests.SettingsEdit;

namespace Island.Tests;

/// <summary>WORK-ORDER-13, Dan's P11 P12 P14 P16 P17 P18 P32: the words and the small wiring that keep them true.</summary>
public class WordsTests
{
    [Fact]
    public void The_Add_On_Folder_Is_Found_Beside_The_Island_Or_Above_It_And_Nowhere_Else()
    {
        var root = Path.Combine(Path.GetTempPath(), "island-addon-test", "Island");
        var folder = Path.Combine(root, "extension");
        bool Exists(string p) => p == Path.Combine(folder, "manifest.json");
        Assert.Equal(folder, AddonFolder.Find(Path.Combine(root, "dist", "Island"), Exists));
        Assert.Equal(folder, AddonFolder.Find(root, Exists));
        Assert.Null(AddonFolder.Find(Path.Combine(Path.GetTempPath(), "island-addon-test", "Elsewhere"), Exists));
        Assert.Null(AddonFolder.Find("", Exists));
    }

    [Fact]
    public void The_Button_Opens_The_Folder_Or_Says_Why_It_Did_Not()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));
        var refused = session.OpenAddonFolder();
        Assert.False(refused.Ok);
        Assert.Equal(AddonText.FolderNotOpened.Message, refused.Refusal);
        session.FindAddonFolder = () => "C:/somewhere/extension";
        session.OpenFolderAt = _ => false;
        Assert.False(session.OpenAddonFolder().Ok);
        string? opened = null;
        session.OpenFolderAt = p => { opened = p; return true; };
        Assert.True(session.OpenAddonFolder().Ok);
        Assert.Equal("C:/somewhere/extension", opened);
    }

    [Fact]
    public void The_Folder_Refusal_Has_Three_Parts_That_Fit_A_Balloon()
    {
        var r = AddonText.FolderNotOpened;
        Assert.All(new[] { r.WhatHappened, r.Why, r.NextAction }, p => Assert.EndsWith(".", p));
        Assert.True(r.Message.Length <= 255, $"{r.Message.Length} characters");
    }

    [Fact]
    public void A_Long_Paused_Line_Gives_Way_At_The_Name_And_Keeps_Its_End()
    {
        static double Width(string text) => text.Length * 6.0;
        var view = new NowPlayingView(
            Title: "Alpha track", Artist: null, Where: "Alpha-player Deluxe Edition", SecondLine: "x", State: PlaybackState.Paused, IsPaused: true,
            PositionSeconds: null, LengthSeconds: null, Progress: null,
            Target: new MediaTarget(MediaTargetKind.Session, "alpha"), CanControl: true, SourceApp: "alpha-player.exe", Host: null, IsBrowserSession: false);
        var line = PlayingTile.FittedSecondLine(view, 130, Width);
        Assert.EndsWith(" · app · paused", line);
        Assert.Contains("…", line);
        Assert.True(Width(line) <= 130, line);
        Assert.StartsWith("Alpha", line);
        // a line that fits is left alone, and a room too small for any of the name still keeps the end
        Assert.Equal(PlayingTile.SecondLine(view), PlayingTile.FittedSecondLine(view, 1000, Width));
        Assert.EndsWith(" · app · paused", PlayingTile.FittedSecondLine(view, 10, Width));
    }

    [Fact]
    public void The_Notice_Line_Is_Told_To_A_Screen_Reader_When_It_Changes_And_A_Refused_Add_Says_Why_Inside_Its_Panel()
    {
        var view = File.ReadAllText(RepoPaths.File("src", "Island.SettingsUi", "SettingsView.cs"));
        Assert.Contains("CreatePeerForElement(line)", view);
        var key = File.ReadAllText(RepoPaths.File("src", "Island.SettingsUi", "KeySection.cs"));
        Assert.Contains("if (AddByHand.PageOf(host.OpenPanel) is not null) return new Border { Height = 0 };", key);
        var add = File.ReadAllText(RepoPaths.File("src", "Island.SettingsUi", "AddByHand.cs"));
        Assert.Contains("panel.Children.Add(KeySection.PanelNoticeLine(host));", add);
    }
}
