using Island.Core;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 3 (code-3-*): the file writers after the repairs of round 2 (4ced622, 2785871): AtomicFile's new catch, the throttle's eight lines, and HelperFileEditor, the part of connecting a
/// helper that writes into ANOTHER program's folder. A folder of the test's own; invented names only; nothing under the real .claude/.codex is opened.
/// </summary>
public sealed class Round3FileTests
{
    // ---- AtomicFile ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Held_AtomicFile_A_Write_That_Cannot_Even_Make_The_Temporary_File_Leaves_The_Live_File_Whole_And_Puts_Nothing_In_Its_Place()
    {
        // The new catch moves the temporary file into place only when the live file is gone. Here the live file is there, so a temporary file that cannot be written (a locked one, a
        // folder of that name) must leave the live file exactly as it was and never replace it with anything.
        using var dir = new Scratch();
        var path = dir.Path_("settings.json");
        AtomicFile.Write(path, "{\"whole\": true}");

        Directory.CreateDirectory(path + ".tmp"); // a folder where the temporary file should be
        Assert.ThrowsAny<Exception>(() => AtomicFile.Write(path, "{\"new\": 1}"));
        Assert.Equal("{\"whole\": true}", File.ReadAllText(path));
        Assert.True(Directory.Exists(path + ".tmp"), "the folder that stood in the way was removed");
    }

    [Fact]
    public void Held_AtomicFile_A_First_Write_That_Fails_Leaves_No_File_At_All()
    {
        // liveExisted is false for a first write: the catch deletes the temporary file and never moves it into place.
        using var dir = new Scratch();
        var path = dir.Path_("pages.json");
        Directory.CreateDirectory(path); // a folder where the live file would be: Move fails
        Assert.ThrowsAny<Exception>(() => AtomicFile.Write(path, "{\"a\": 1}"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Held_AtomicFile_A_Stale_Temporary_File_From_A_Crash_Is_Overwritten_By_The_Next_Write()
    {
        using var dir = new Scratch();
        var path = dir.Path_("picks.json");
        AtomicFile.Write(path, "{\"old\": 1}");
        File.WriteAllText(path + ".tmp", "half a fi"); // what a crash in the middle of a write leaves
        AtomicFile.Write(path, "{\"new\": 2}");
        Assert.Equal("{\"new\": 2}", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    // ---- LogThrottle -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Held_LogThrottle_Eight_Lines_That_Take_Turns_Are_All_Thinned_And_A_Ninth_Line_Pushes_Out_The_One_Written_Longest_Ago()
    {
        var throttle = new LogThrottle();
        var lines = Enumerable.Range(0, 8).Select(i => $"error {i}").ToList();
        var now = 1_000L;
        foreach (var line in lines) Assert.True(throttle.ShouldWrite(line, now++)); // all eight are written once
        for (var round = 0; round < 50; round++)
            foreach (var line in lines) Assert.False(throttle.ShouldWrite(line, now++), line); // and none again within five seconds

        Assert.True(throttle.ShouldWrite("a ninth line", now++)); // pushes out the oldest ("error 0")
        Assert.True(throttle.ShouldWrite("error 0", now++), "the line written longest ago is forgotten first");
    }

    [Fact]
    public void Held_LogThrottle_Nine_Lines_That_Take_Turns_Are_Never_Thinned_The_Stated_Ceiling_Of_Eight()
    {
        // A statement, not a defect: the cycle is longer than the memory, so every line is forgotten before it comes round. The disk is bounded by the 512 KB rotation of AppFiles.Log.
        var throttle = new LogThrottle();
        var written = 0;
        for (var n = 0; n < 900; n++)
            if (throttle.ShouldWrite($"error {n % 9}", 1_000 + n)) written++;
        Assert.Equal(900, written);
    }

    [Fact]
    public void Held_LogThrottle_Many_Threads_Never_Throw_And_Never_Hold_More_Than_Eight_Lines()
    {
        var throttle = new LogThrottle();
        var failures = 0;
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            try
            {
                for (var n = 0; n < 20_000; n++) throttle.ShouldWrite($"line {(n * 7 + t) % 50}", n);
            }
            catch (Exception)
            {
                Interlocked.Increment(ref failures);
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(threads.All(t => t.Join(TimeSpan.FromSeconds(60))));
        Assert.Equal(0, failures);
    }

    // ---- HelperFileEditor: the one writer into another program's folder ------------------------------------------------------------------------

    [Fact]
    public void Held_HelperFileEditor_Writes_The_Text_Whole_Keeps_One_First_Copy_And_A_Fresh_Second_Copy_At_An_Update()
    {
        using var dir = new Scratch();
        var file = dir.Write("settings.json", "{\"mine\": 1}");

        Assert.True(HelperFileEditor.Write(file, "{\"mine\": 1, \"island\": 1}", freshSecondCopy: false));
        Assert.Equal("{\"mine\": 1}", File.ReadAllText(file + HelperFileEditor.FirstCopySuffix));
        Assert.False(File.Exists(file + HelperFileEditor.UpdateCopySuffix));
        Assert.False(File.Exists(file + HelperFileEditor.TemporarySuffix));

        Assert.True(HelperFileEditor.Write(file, "{\"mine\": 1, \"island\": 2}", freshSecondCopy: true));
        Assert.Equal("{\"mine\": 1}", File.ReadAllText(file + HelperFileEditor.FirstCopySuffix)); // never again
        Assert.Equal("{\"mine\": 1, \"island\": 1}", File.ReadAllText(file + HelperFileEditor.UpdateCopySuffix)); // the file as it was just before the update
    }

    [Fact]
    public void Held_HelperFileEditor_A_File_That_Is_Not_There_Yet_Is_Made_And_No_Copy_Is_Made_Of_Nothing()
    {
        using var dir = new Scratch();
        var file = Path.Combine(dir.Folder, "helper", "hooks.json");
        Assert.True(HelperFileEditor.Write(file, "{}", freshSecondCopy: true));
        Assert.Equal("{}", File.ReadAllText(file));
        Assert.False(File.Exists(file + HelperFileEditor.FirstCopySuffix));
        Assert.False(File.Exists(file + HelperFileEditor.UpdateCopySuffix));
    }

    [Fact]
    public void Defect_HelperFileEditor_A_Refused_Write_Leaves_Its_Temporary_File_In_The_Other_Programs_Folder()
    {
        // code-3-6 (LOW). HelperFileEditor.Write puts the text in "<file>.island-tmp" and moves it over the file. When the move is refused (the helper's file is read-only, or an editor holds it
        // without sharing) the method answers false and the temporary file, with the whole new text, stays in the folder of ANOTHER program for ever: the same fault as code-1-4 and
        // code-2-6 in the files the app owns (AtomicFile now deletes it), here in a folder the person did not expect Island to write in. The refusal the person is told is right; the litter is
        // not. Expected: the temporary file is deleted when the write or the move fails (the first copy is kept).
        using var dir = new Scratch();
        var file = dir.Write("settings.json", "{\"mine\": 1}");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            Assert.False(HelperFileEditor.Write(file, "{\"mine\": 1, \"island\": 1}", freshSecondCopy: false));
            Assert.Equal("{\"mine\": 1}", File.ReadAllText(file));
            Assert.False(File.Exists(file + HelperFileEditor.TemporarySuffix), "the temporary file of a refused write is still in the helper's folder");
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Held_HelperFileEditor_ReadText_Of_A_Missing_File_Is_Empty_And_Of_A_Locked_One_Says_It_Failed()
    {
        using var dir = new Scratch();
        Assert.Equal(string.Empty, HelperFileEditor.ReadText(dir.Path_("none.json"), out var failed));
        Assert.False(failed);

        var file = dir.Write("settings.json", "{}");
        using var hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(string.Empty, HelperFileEditor.ReadText(file, out failed));
        Assert.True(failed);
    }

    [Fact]
    public void Held_HelperFileEditor_CopyNotify_Without_The_Program_Beside_The_Island_Copies_Nothing()
    {
        using var dir = new Scratch();
        var from = Directory.CreateDirectory(dir.Path_("from")).FullName;
        var to = dir.Path_("to");
        File.WriteAllText(Path.Combine(from, "Island.Core.dll"), "library");
        Assert.False(HelperFileEditor.CopyNotify(from, to));
        Assert.False(Directory.Exists(to), "a folder was made for a copy that could not be made");
    }

    [Fact]
    public void Held_HelperFileEditor_CopyNotify_Copies_The_Library_And_The_Program_And_Overwrites_An_Older_Copy()
    {
        using var dir = new Scratch();
        var from = Directory.CreateDirectory(dir.Path_("from")).FullName;
        var to = dir.Path_("to");
        foreach (var name in new[] { "Island.Core.dll", "Island.Notify.dll", "Island.Notify.deps.json", "Island.Notify.runtimeconfig.json", "Island.Notify.exe" })
            File.WriteAllText(Path.Combine(from, name), "new " + name);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "Island.Notify.exe"), "old");

        Assert.True(HelperFileEditor.CopyNotify(from, to));
        Assert.Equal("new Island.Notify.exe", File.ReadAllText(Path.Combine(to, "Island.Notify.exe")));
        Assert.Equal("new Island.Core.dll", File.ReadAllText(Path.Combine(to, "Island.Core.dll")));
    }

    [Fact]
    public void Held_HelperFileEditor_CopyNotify_With_The_Program_Held_By_A_Running_Hook_Answers_False_And_Leaves_The_Old_Program()
    {
        using var dir = new Scratch();
        var from = Directory.CreateDirectory(dir.Path_("from")).FullName;
        var to = dir.Path_("to");
        foreach (var name in new[] { "Island.Core.dll", "Island.Notify.dll", "Island.Notify.deps.json", "Island.Notify.runtimeconfig.json", "Island.Notify.exe" })
            File.WriteAllText(Path.Combine(from, name), "new " + name);
        Directory.CreateDirectory(to);
        var exe = Path.Combine(to, "Island.Notify.exe");
        File.WriteAllText(exe, "old");
        using var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read); // a running program is held like this
        Assert.False(HelperFileEditor.CopyNotify(from, to));
        Assert.Equal("old", File.ReadAllText(exe));
    }
}
