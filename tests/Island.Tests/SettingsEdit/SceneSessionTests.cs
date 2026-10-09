using Island.Core;

namespace Island.Tests.SettingsEdit;

/// <summary>WORK-ORDER-7 section 5, the settings side: none is made ready, edits are saved at once, and a file that could not be read is never written over.</summary>
public class SceneSessionTests
{
    [Fact]
    public void A_New_Session_Has_No_Scene_And_Says_What_A_Scene_Is()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));

        Assert.Empty(session.Scenes.Items);
        Assert.False(File.Exists(dir.File("scenes.json")));
        Assert.Contains("open together", Island.Core.SettingsEdit.SettingsSession.SceneExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Things_Are_Ticked_In_Order_Saved_At_Once_And_Come_Back()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files);
        Assert.True(session.CreateScene("Evening").Ok);
        var id = session.Scenes.Items[0].Id;
        var picks = session.AllPicks.Take(3).ToList();

        foreach (var pick in new[] { picks[2], picks[0], picks[1] }) Assert.True(session.SetSceneThing(id, pick, true).Ok);
        Assert.True(session.SetSceneThing(id, picks[0], false).Ok);

        var again = SceneStore.Load(files.ScenesPath!).Store.ById(id)!;
        Assert.Equal([picks[2].Id, picks[1].Id], again.Things.Select(t => t.Id).ToList());
        Assert.True(session.RenameScene(id, "Late").Ok);
        Assert.Equal("Late", SceneStore.Load(files.ScenesPath!).Store.ById(id)!.Name);
    }

    [Fact]
    public void A_Scenes_File_That_Could_Not_Be_Read_Is_Never_Written_Over()
    {
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        File.WriteAllText(files.ScenesPath!, "not json at all");
        var load = SceneStore.Load(files.ScenesPath!);
        var session = SessionFixtures.Open(files, scenes: load);

        Assert.Equal(SceneStoreStatus.Unreadable, load.Status);
        Assert.False(session.CreateScene("Evening").Ok);
        Assert.Equal("not json at all", File.ReadAllText(files.ScenesPath!));
    }

    [Fact]
    public void A_Bad_Name_Is_Refused_In_Plain_Words()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));

        Assert.Equal(SceneText.NameEmpty, session.CreateScene("   ").Refusal);
        Assert.True(session.CreateScene("Evening").Ok);
        Assert.Equal(SceneText.NameTaken("Evening"), session.CreateScene("evening").Refusal);
    }
}
