using Island.Core;

namespace Island.Tests.Agents;

/// <summary>What is done to a helper's settings file (WORK-ORDER-7 section 4, WORK-ORDER-11 section 3): the copy before the first write, the fresh copy at an update, the temporary file, the copy of Island.Notify. On a temporary folder with invented text.</summary>
public class HelperFileEditorTests
{
    [Fact]
    public void The_File_As_It_Was_Is_Saved_Beside_It_Before_The_First_Write_And_Never_Again()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json");
        File.WriteAllText(file, "{\"alpha\":1}");

        Assert.True(HelperFileEditor.Write(file, "{\"alpha\":2}", freshSecondCopy: false));
        Assert.True(HelperFileEditor.Write(file, "{\"alpha\":3}", freshSecondCopy: false));

        Assert.Equal("{\"alpha\":3}", File.ReadAllText(file));
        Assert.Equal("{\"alpha\":1}", File.ReadAllText(file + HelperFileEditor.FirstCopySuffix)); // the first copy stays as it was
        Assert.False(File.Exists(file + HelperFileEditor.UpdateCopySuffix));
        Assert.False(File.Exists(file + HelperFileEditor.TemporarySuffix));
    }

    [Fact]
    public void An_Update_Saves_A_Fresh_Second_Copy_Over_Whatever_Second_Copy_There_Is()
    {
        using var dir = new TempDir();
        var file = dir.File("hooks.json");
        File.WriteAllText(file, "one");
        HelperFileEditor.Write(file, "two", freshSecondCopy: true);
        HelperFileEditor.Write(file, "three", freshSecondCopy: true);

        Assert.Equal("one", File.ReadAllText(file + HelperFileEditor.FirstCopySuffix));
        Assert.Equal("two", File.ReadAllText(file + HelperFileEditor.UpdateCopySuffix)); // the file as it was just before the last update
        Assert.Equal("three", File.ReadAllText(file));
    }

    [Fact]
    public void With_No_File_There_Is_Nothing_To_Copy_And_The_File_Is_Made()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, ".invented", "settings.json");

        Assert.True(HelperFileEditor.Write(file, "{}", freshSecondCopy: true));

        Assert.Equal("{}", File.ReadAllText(file));
        Assert.False(File.Exists(file + HelperFileEditor.FirstCopySuffix));
        Assert.False(File.Exists(file + HelperFileEditor.UpdateCopySuffix));
    }

    [Fact]
    public void A_Write_That_Cannot_Be_Done_Says_So_And_Leaves_The_File_As_It_Was()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json");
        File.WriteAllText(file, "kept");
        Directory.CreateDirectory(file + HelperFileEditor.TemporarySuffix); // the temporary name is taken by a folder: the write cannot be made

        Assert.False(HelperFileEditor.Write(file, "new", freshSecondCopy: false));

        Assert.Equal("kept", File.ReadAllText(file));
    }

    [Fact]
    public void A_File_That_Is_Not_There_Reads_As_Empty_And_One_That_Cannot_Be_Read_Says_So()
    {
        using var dir = new TempDir();
        Assert.Equal(string.Empty, HelperFileEditor.ReadText(dir.File("none.json"), out var failed));
        Assert.False(failed);

        var held = dir.File("held.json");
        File.WriteAllText(held, "{}");
        using var hold = new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None); // another program has it open and does not share it
        HelperFileEditor.ReadText(held, out failed);
        Assert.True(failed);
    }

    private static void MakeProgramFiles(string folder, params string[] names)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in names) File.WriteAllText(Path.Combine(folder, name), name);
    }

    [Fact]
    public void Island_Notify_Is_Copied_With_The_Files_It_Needs()
    {
        using var dir = new TempDir();
        var from = Path.Combine(dir.Path, "from");
        var to = Path.Combine(dir.Path, "to", "notify");
        MakeProgramFiles(from, "Island.Core.dll", "Island.Notify.dll", "Island.Notify.deps.json", "Island.Notify.runtimeconfig.json", "Island.Notify.exe", "Island.App.exe");

        Assert.True(HelperFileEditor.CopyNotify(from, to));

        Assert.Equal(["Island.Core.dll", "Island.Notify.deps.json", "Island.Notify.dll", "Island.Notify.exe", "Island.Notify.runtimeconfig.json"], Directory.GetFiles(to).Select(Path.GetFileName).Order().ToArray());
    }

    [Fact]
    public void A_Copy_That_Stops_Halfway_Never_Leaves_The_New_Program_On_An_Old_Library()
    {
        using var dir = new TempDir();
        var from = Path.Combine(dir.Path, "from");
        var to = Path.Combine(dir.Path, "to");
        MakeProgramFiles(from, "Island.Core.dll", "Island.Notify.exe"); // the program's own library is missing: the copy stops after the first files
        Assert.False(HelperFileEditor.CopyNotify(from, to));
        Assert.False(File.Exists(Path.Combine(to, "Island.Notify.exe")));
    }

    [Fact]
    public void Without_The_Program_Beside_The_Island_Nothing_Is_Copied()
    {
        using var dir = new TempDir();
        var from = Path.Combine(dir.Path, "from");
        MakeProgramFiles(from, "Island.Core.dll");
        Assert.False(HelperFileEditor.CopyNotify(from, Path.Combine(dir.Path, "to")));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "to")));
    }
}
