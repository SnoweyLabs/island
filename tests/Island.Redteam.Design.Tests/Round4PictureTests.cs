using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 4, brought to WORK-ORDER-13: the pictures of the newest FULL self-test run (<c>dist/test-full</c>) and of any single-stage runs written after it (<c>dist/test-cap</c>, <c>dist/test-settings</c>), compared pixel
/// by pixel with <c>review/</c>, where the pictures Dan's decisions changed are already replaced. Any difference is a change nobody approved. Two PNG files are decoded and compared: nothing is drawn, shown or captured.
/// </summary>
public class Round4PictureTests(ITestOutputHelper output)
{
    private const int Noise = 2;

    private static (int Different, int Largest, int MinX, int MinY, int MaxX, int MaxY) Diff(Pic before, Pic after)
    {
        int different = 0, largest = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < after.Height; y++)
            for (var x = 0; x < after.Width; x++)
            {
                var a = after.At(x, y);
                var b = before.At(x, y);
                var d = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
                if (d <= Noise) continue;
                different++;
                largest = Math.Max(largest, d);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }

        return (different, largest, minX, minY, maxX, maxY);
    }

    private static IEnumerable<(string Run, string Relative)> Pictures(string root, string run, DateTime since)
    {
        var dir = Path.Combine(root, "dist", run);
        if (!Directory.Exists(dir)) yield break;
        foreach (var path in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
            if (File.GetLastWriteTime(path) >= since)
                yield return (run, Path.GetRelativePath(dir, path).Replace(Path.DirectorySeparatorChar, '/'));
    }

    [Fact]
    public void Every_Picture_Of_The_Newest_Runs_Is_Pixel_Equal_To_The_One_In_Review()
    {
        var root = Pic.Root();
        if (root is null || !Directory.Exists(Path.Combine(root, "dist", "test-full"))) return;
        var since = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Local);
        var all = Pictures(root, "test-full", since).Concat(Pictures(root, "test-cap", since)).Concat(Pictures(root, "test-settings", since)).ToList();
        output.WriteLine($"{all.Count} picture(s) drawn on or after 2026-10-08 in test-full, test-cap and test-settings");
        int compared = 0, noEarlier = 0;
        var unequal = new List<string>();
        foreach (var (run, relative) in all)
        {
            Pic? after, before;
            try { after = Pic.TryLoad($"dist/{run}/{relative}"); before = Pic.TryLoad("review/" + relative); }
            catch (IOException) { continue; } // the main session may be writing it
            if (after is null) continue;
            if (before is null) { noEarlier++; output.WriteLine($"{run}/{relative}: no earlier picture in review/ ({after.Width}x{after.Height})"); continue; }
            if ((before.Width, before.Height) != (after.Width, after.Height)) { unequal.Add($"{run}/{relative}: size {before.Width}x{before.Height} -> {after.Width}x{after.Height}"); continue; }
            compared++;
            var (different, largest, minX, minY, maxX, maxY) = Diff(before, after);
            output.WriteLine($"{run}/{relative}: {after.Width}x{after.Height}, {different} px differ over {Noise} (largest step {largest}){(different > 0 ? $", box {minX},{minY}..{maxX},{maxY}" : "")}");
            if (different > 0) unequal.Add($"{run}/{relative}: {different} px differ (largest step {largest}) in the box {minX},{minY}..{maxX},{maxY}");
        }

        output.WriteLine($"{compared} compared with review/, {noEarlier} without an earlier picture");
        Assert.True(unequal.Count == 0, "a picture that no decision of Dan's changed differs: " + string.Join("; ", unequal));
    }
}
