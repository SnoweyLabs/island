using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 3: the pictures the self-test drew AFTER the repairs of round 2 (the main session ran single stages of the repaired build into dist/test-*), compared pixel by pixel with the earlier run in review/. Nothing is drawn here:
/// two sets of PNGs are decoded (a read of a file is not showing a window) and compared. What exists changes while the main session works; the test compares whatever exists and says what.
/// </summary>
public class Round3PictureTests(ITestOutputHelper output)
{
    /// <summary>The first repair of round 2 on main is db7c540, 2026-10-07 20:14:20 +03:00 (git log); the self-test that drew dist/test-wo12r1 ran at 19:43 to 19:48, before it.</summary>
    private static readonly DateTime FirstRepairOfRound2 = new(2026, 10, 7, 20, 14, 0, DateTimeKind.Local);

    private const int Noise = 2;

    [Fact]
    public void Every_Picture_Drawn_After_The_Repairs_Of_Round_2_Is_Pixel_Equal_To_The_Earlier_One_In_Review()
    {
        var root = Pic.Root();
        if (root is null || !Directory.Exists(Path.Combine(root, "dist"))) return;
        var found = new List<(string Run, string Relative, FileInfo File)>();
        foreach (var run in Directory.EnumerateDirectories(Path.Combine(root, "dist"), "test-*"))
            foreach (var f in Directory.EnumerateFiles(run, "*.png", SearchOption.AllDirectories).Select(p => new FileInfo(p)).Where(f => f.LastWriteTime >= FirstRepairOfRound2))
                found.Add((Path.GetFileName(run), Path.GetRelativePath(run, f.FullName).Replace(Path.DirectorySeparatorChar, '/'), f));

        output.WriteLine($"{found.Count} picture(s) written after the first repair of round 2: {string.Join(", ", found.Select(f => f.Run + "/" + f.Relative))}");
        foreach (var (run, relative, file) in found)
        {
            if (!File.Exists(Path.Combine(root, "review", relative.Replace('/', Path.DirectorySeparatorChar)))) { output.WriteLine($"{relative}: no earlier picture in review/"); continue; }
            Pic? after, before;
            try { after = Pic.TryLoad($"dist/{run}/{relative}"); before = Pic.TryLoad("review/" + relative); }
            catch (IOException) { continue; } // the main session may be writing it
            if (after is null || before is null) continue;
            Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
            int different = 0, largest = 0;
            for (var y = 0; y < after.Height; y++)
                for (var x = 0; x < after.Width; x++)
                {
                    var a = after.At(x, y);
                    var b = before.At(x, y);
                    var d = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
                    if (d <= Noise) continue;
                    different++;
                    largest = Math.Max(largest, d);
                }

            output.WriteLine($"{run}/{relative} ({file.LastWriteTime:HH:mm}): {after.Width}x{after.Height}, {different} pixels differ over {Noise} (largest step {largest})");
            // review/ holds the pictures Dan approved, the ones WORK-ORDER-13 changed among them: any run drawn since must equal them.
            Assert.Equal(0, different);
        }
    }
}
