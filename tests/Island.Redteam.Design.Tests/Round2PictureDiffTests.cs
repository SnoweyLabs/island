using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 2, part 1, brought to WORK-ORDER-13: the pictures of <c>review/</c> are the look Dan approved; the newest full self-test (<c>dist/test-full/</c>, read only) must draw every one of them again, pixel for pixel
/// (the pictures WORK-ORDER-13's decisions changed were replaced in <c>review/</c> with the new ones, so the old Blur-note exception of WORK-ORDER-12 is gone). Decoded with WPF's decoder, never shown.
/// Nothing here draws a picture; it only reads two sets of PNGs.
/// </summary>
public class Round2PictureDiffTests(ITestOutputHelper output)
{
    /// <summary>A difference of at most this much in every channel of a pixel is noise (the self-test draws the same pixels twice with the same numbers: none is expected at all).</summary>
    private const int Noise = 2;

    private sealed record Diff(string File, int Width, int Height, int Different, int Left, int Top, int Right, int Bottom, int Largest);

    private static IEnumerable<string> RelativePictures(string root, string folder)
    {
        var dir = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
            yield return Path.GetRelativePath(dir, file).Replace('\\', '/');
    }

    private static Diff Compare(string relative)
    {
        var after = Pic.TryLoad("dist/test-full/" + relative)!;
        var before = Pic.TryLoad("review/" + relative)!;
        if (after.Width != before.Width || after.Height != before.Height)
            return new Diff(relative, after.Width, after.Height, int.MaxValue, 0, 0, after.Width, after.Height, 255);
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1, largest = 0;
        for (var y = 0; y < after.Height; y++)
            for (var x = 0; x < after.Width; x++)
            {
                var a = after.At(x, y);
                var b = before.At(x, y);
                var d = Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
                if (d <= Noise) continue;
                count++;
                largest = Math.Max(largest, d);
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }

        return new Diff(relative, after.Width, after.Height, count, left, top, right, bottom, largest);
    }

    [Fact]
    public void Every_Picture_Of_The_Newest_Full_Run_Is_Pixel_Equal_To_The_One_In_Review()
    {
        var root = Pic.Root();
        if (root is null || !Directory.Exists(Path.Combine(root, "dist", "test-full"))) return; // the main folder (or this run's pictures) is not above the worktree
        var now = RelativePictures(root, "dist/test-full").ToList();
        Assert.True(now.Count >= 40, $"only {now.Count} pictures found in dist/test-full");

        var compared = new List<Diff>();
        var onlyNow = new List<string>();
        foreach (var file in now)
        {
            if (!File.Exists(Path.Combine(root, "review", file.Replace('/', Path.DirectorySeparatorChar)))) { onlyNow.Add(file); continue; }
            compared.Add(Compare(file));
        }

        var changed = compared.Where(d => d.Different > 0).ToList();
        output.WriteLine($"{now.Count} pictures in the newest full run, {compared.Count} compared with review/, {compared.Count - changed.Count} pixel equal (every channel within {Noise}), {changed.Count} different");
        foreach (var d in changed)
            output.WriteLine($"DIFFERENT {d.File}: {d.Different} pixels over {Noise}, in the box x {d.Left}..{d.Right}, y {d.Top}..{d.Bottom} of {d.Width} by {d.Height}, largest channel step {d.Largest}");
        foreach (var f in onlyNow) output.WriteLine("only in the new run: " + f);

        // Anything that moves is a change nobody approved: Dan's changes are already in review/.
        Assert.Empty(changed.Select(d => d.File));
    }

    /// <summary>Pictures that the earlier run holds and this run does not draw: they are records (their folder's README says so) or not redrawn by the settings stage any more, and they show the state of an older build.</summary>
    [Fact]
    public void Pictures_Held_In_Review_That_The_Current_Self_Test_No_Longer_Draws_Are_Listed()
    {
        var root = Pic.Root();
        if (root is null || !Directory.Exists(Path.Combine(root, "dist", "test-full"))) return;
        var now = RelativePictures(root, "dist/test-full").ToHashSet();
        var old = RelativePictures(root, "review").Where(f => !f.StartsWith("blur/") && !f.StartsWith("glass/") && !f.StartsWith("agents/") && !f.StartsWith("redteam/")).ToList();
        var stale = old.Where(f => !now.Contains(f)).ToList();
        foreach (var f in stale) output.WriteLine("not drawn by this run: " + f);
        output.WriteLine($"{stale.Count} pictures of review/ are not redrawn by the self-test");
        // The restore question, the faint chip and the focus ring of the fields have no picture in either set.
        Assert.Contains("settings/dialog.png", stale);
    }
}
