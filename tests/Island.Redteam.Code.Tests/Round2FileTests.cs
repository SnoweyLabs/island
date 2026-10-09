using System.Collections.Concurrent;
using Island.Core;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 2 (code-2-*): AtomicFile (8b32550), the one way every file the app owns is written now. Invented names only; a folder of the test's own.</summary>
public sealed class Round2FileTests
{
    private static string Text(int writer, int n) => $"writer {writer} write {n}\n" + new string((char)('a' + writer % 26), 20_000) + $"\nend {writer} {n}\n";

    private static bool IsWhole(string text) => text.StartsWith("writer ", StringComparison.Ordinal) && text.Contains("\nend ", StringComparison.Ordinal) && text.EndsWith("\n", StringComparison.Ordinal);

    [Fact]
    public void Two_Writers_Of_One_File_At_Once_Leave_One_Whole_Text_And_No_Temporary_File_Whatever_Their_Own_Answers_Are()
    {
        // The temporary name is one name per file (path + ".tmp"). Two writers at once open the same temporary file: the second one's text can be moved in place of the live file
        // as the first one's, and the first one's move can then find nothing to move. The file on disk is always one whole text; what each caller is TOLD can differ.
        using var dir = new Scratch();
        var path = dir.Path_("settings.json");
        AtomicFile.Write(path, Text(99, 0));
        var failures = new ConcurrentBag<Type>();
        var threads = Enumerable.Range(0, 6).Select(w => new Thread(() =>
        {
            for (var n = 0; n < 40; n++)
            {
                try
                {
                    AtomicFile.Write(path, Text(w, n));
                }
                catch (Exception e)
                {
                    failures.Add(e.GetType());
                }
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(threads.All(t => t.Join(TimeSpan.FromSeconds(60))), "a writer did not finish");

        Assert.All(failures, t => Assert.True(typeof(IOException).IsAssignableFrom(t) || t == typeof(UnauthorizedAccessException), t.FullName));
        Assert.True(IsWhole(File.ReadAllText(dir.Path_("settings.json"))), "the live file is cut");
        Assert.False(File.Exists(path + ".tmp"), "a temporary file was left");
    }

    [Fact]
    public void A_Reader_That_Reads_In_A_Loop_Never_Sees_A_Cut_File()
    {
        using var dir = new Scratch();
        var path = dir.Path_("pages.json");
        AtomicFile.Write(path, Text(0, 0));
        var stop = false;
        var cut = 0;
        var reads = 0;
        var reader = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                try
                {
                    if (!IsWhole(File.ReadAllText(path))) Interlocked.Increment(ref cut);
                    Interlocked.Increment(ref reads);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // the file was being replaced at that moment
                }
            }
        });
        reader.Start();
        var told = new List<bool>();
        for (var n = 1; n <= 60; n++)
        {
            try
            {
                AtomicFile.Write(path, Text(1, n));
                told.Add(true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                told.Add(false);
            }
        }

        Volatile.Write(ref stop, true);
        Assert.True(reader.Join(10_000));
        Assert.Equal(0, cut);
        Assert.True(reads > 0);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(IsWhole(File.ReadAllText(path)));
        if (told[^1]) Assert.Contains("write 60", File.ReadAllText(path));
    }

    [Fact]
    public void A_Read_Only_Live_File_Is_Left_As_It_Was_The_Caller_Is_Told_And_No_Temporary_File_Stays()
    {
        using var dir = new Scratch();
        var path = dir.Write("picks.json", "old whole text");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.Write(path, "new text"));
        var took = clock.ElapsedMilliseconds;
        File.SetAttributes(path, FileAttributes.Normal);
        Assert.Equal("old whole text", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        // the retries sleep on the calling thread: a refusal that cannot change (read only) costs the drawing thread about a quarter of a second, once per save
        Assert.InRange(took, (AtomicFile.Tries - 1) * AtomicFile.WaitMs - 20, 3000);
    }

    [Fact]
    public void A_Folder_Where_The_File_Should_Be_Fails_Plainly_And_Leaves_Nothing_Behind()
    {
        using var dir = new Scratch();
        var path = dir.Path_("scenes.json");
        Directory.CreateDirectory(path);
        Assert.ThrowsAny<Exception>(() => AtomicFile.Write(path, "text"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void A_Folder_Where_The_Temporary_File_Should_Be_Fails_Plainly_And_The_Live_File_Is_Untouched()
    {
        using var dir = new Scratch();
        var path = dir.Write("settings.json", "old whole text");
        Directory.CreateDirectory(path + ".tmp");
        Assert.ThrowsAny<Exception>(() => AtomicFile.Write(path, "new text"));
        Assert.Equal("old whole text", File.ReadAllText(path));
    }

    [Fact]
    public void A_Missing_Folder_Is_Made_And_A_Name_Without_A_Folder_Works()
    {
        using var dir = new Scratch();
        var deep = Path.Combine(dir.Folder, "a", "b", "settings.json");
        AtomicFile.Write(deep, "x");
        Assert.Equal("x", File.ReadAllText(deep));
        var bare = "island-redteam-round2-" + Guid.NewGuid().ToString("N") + ".json";
        try
        {
            AtomicFile.Write(bare, "y");
            Assert.Equal("y", File.ReadAllText(bare));
        }
        finally
        {
            File.Delete(bare);
        }
    }

    [Fact]
    public void An_Empty_Text_And_A_Large_Text_Are_Written_Whole()
    {
        using var dir = new Scratch();
        var path = dir.Path_("scenes.json");
        AtomicFile.Write(path, "");
        Assert.Equal("", File.ReadAllText(path));
        var big = new string('q', 30_000_000);
        AtomicFile.Write(path, big);
        Assert.Equal(big.Length, new FileInfo(path).Length);
    }

    [Fact]
    public void Defect_AtomicFile_A_Write_That_Fails_Part_Way_Through_The_Temporary_File_Leaves_It_Behind()
    {
        // code-2-6 (LOW). AtomicFile.Write calls File.WriteAllText(temp, text) BEFORE its try: only a failed MOVE deletes the temporary file (the repair of code-1-4 and code-1-5).
        // A write that fails part way (a full disk, a quota, a failing drive, a program that blocks the write) leaves the temporary file with whatever part of the refused list was
        // written, beside the live file: the thing code-1-4 was about ("the list that was refused must not stay behind in a file of its own"). Here the failure is made by a text
        // that UTF-8 cannot encode (a lone surrogate: File.WriteAllText throws EncoderFallbackException, an ArgumentException, which the stores' Save also catches); the temporary
        // file has been made and emptied by then. The stores' ToJson never makes such a text, so this stands in for any failure of the write.
        // Expected: no file named settings.json.tmp after the call. Repair: move the WriteAllText into the try.
        using var dir = new Scratch();
        var path = dir.Write("settings.json", "old whole text");
        Assert.ThrowsAny<ArgumentException>(() => AtomicFile.Write(path, "a\uD800b"));
        Assert.Equal("old whole text", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"), "the temporary file of the refused write was left behind");
    }
}
