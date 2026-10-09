using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;
using Island.Core;

namespace Island.Tests;

/// <summary>
/// Proofs for the texts and pictures of <c>ship/</c> that the coverage table (WORK-ORDER-12 section 5) found had none: a store text says only what is true of the code, and every picture has
/// the size its name or the store's page gives.
/// </summary>
public class StoreProofTests
{
    private static string Read(params string[] parts) => File.ReadAllText(RepoPaths.File(["ship", .. parts]));

    private static string Bullet(string text, string heading)
    {
        var at = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(at >= 0, heading);
        var end = text.IndexOf("\n- ", at, StringComparison.Ordinal);
        return end < 0 ? text[at..] : text[at..end];
    }

    // The words of the privacy bullet for each key of settings.json; a key that is written and has no entry here makes the first test red, so a new setting cannot be added without its sentence.
    private static readonly Dictionary<string, string> Words = new()
    {
        ["schema"] = "which version of the file it is",
        ["hotkeys"] = "your main key and the keys you gave to pages",
        ["idleSeconds"] = "how long the island waits before it leaves",
        ["glass"] = "the glass",
        ["pickKeys"] = "things and scenes",
        ["mode"] = "the mode",
        ["showPill"] = "the small pill",
        ["movingLight"] = "moving light",
        ["noticeSeconds"] = "how long the notice stays",
        ["neverOver"] = "never appears over",
        ["startWithWindows"] = "Start with Windows",
    };

    [Fact]
    public void The_Privacy_Text_Says_Everything_The_Settings_File_Holds()
    {
        using var doc = JsonDocument.Parse(Settings.Defaults.ToJson());
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        var bullet = Bullet(Read("store", "privacy.md"), "- **settings.json**");

        Assert.Empty(keys.Where(k => !Words.ContainsKey(k)).Select(k => $"settings.json now holds \"{k}\" and the privacy text has no sentence for it"));
        foreach (var key in keys) Assert.Contains(Words[key], bullet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Privacy_Text_Says_What_Was_Added_By_Hand_And_What_The_Terminals_Page_Reads()
    {
        var privacy = Read("store", "privacy.md");
        // WORK-ORDER-10: the picks file holds the location of anything added by hand, with the user folder written as a variable.
        var picks = Bullet(privacy, "- **picks.json**");
        Assert.Contains("by hand", picks, StringComparison.Ordinal);
        Assert.Contains("%USERPROFILE%", picks, StringComparison.Ordinal);
        // WORK-ORDER-11: the Terminals page reads the running programs and the terminal windows' titles in memory; connecting a helper writes into that helper's own settings file.
        Assert.Contains("list of running programs", privacy, StringComparison.Ordinal);
        Assert.Contains("titles of terminal windows", privacy, StringComparison.Ordinal);
        Assert.Contains("that helper's own settings file", privacy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Justification_Of_Editing_A_Helper_Settings_File_Names_Both_Helpers_And_Hard_Codes_No_Count()
    {
        var text = Read("store", "full-rights-justification.md");
        var line = text[(text.IndexOf("### EditAgentSettings", StringComparison.Ordinal) + "### EditAgentSettings".Length)..].TrimStart().Split('\n')[0];
        Assert.Contains("Claude Code", line, StringComparison.Ordinal);
        Assert.Contains("Codex", line, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(?:two|three|four|five|six|seven|eight|\d+) (?:small )?hook entries", line); // the number of entries is shown to the person first and has changed before
        Assert.Contains("copy of the file is saved beside it first", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Permission_Of_The_Addons_Manifest_Has_Its_Sentence()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(RepoPaths.File("extension", "manifest.json")));
        var permissions = Read("addon", "permissions.md");
        foreach (var name in manifest.RootElement.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!))
            Assert.Contains($"- **{name}** —", permissions, StringComparison.Ordinal);

        var hosts = manifest.RootElement.GetProperty("host_permissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        foreach (var host in hosts.Where(h => !h.StartsWith("http://127.0.0.1", StringComparison.Ordinal)))
            Assert.Contains(new Uri(host.Replace("/*", "/")).Host, permissions, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", permissions, StringComparison.Ordinal);
    }

    private static (int W, int H) PngSize(string path)
    {
        var head = new byte[24];
        using (var stream = File.OpenRead(path)) Assert.Equal(24, stream.Read(head, 0, 24));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], head[..8]);
        return (BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20)));
    }

    [Fact]
    public void Every_Picture_Of_The_Package_Has_The_Size_Its_Name_Gives()
    {
        var wrong = new List<string>();
        var seen = 0;
        foreach (var file in Directory.EnumerateFiles(RepoPaths.File("ship", "package", "Assets"), "*.png"))
        {
            var name = Path.GetFileName(file);
            var baseSize = name.StartsWith("Square44x44Logo", StringComparison.Ordinal) ? 44.0 : name.StartsWith("Square150x150Logo", StringComparison.Ordinal) ? 150.0 : name.StartsWith("StoreLogo", StringComparison.Ordinal) ? 50.0 : 0;
            if (baseSize == 0) continue;
            var target = Regex.Match(name, @"targetsize-(\d+)");
            var scale = Regex.Match(name, @"scale-(\d+)");
            var expected = target.Success ? int.Parse(target.Groups[1].Value) : (int)Math.Ceiling(baseSize * (scale.Success ? int.Parse(scale.Groups[1].Value) : 100) / 100.0);
            var (w, h) = PngSize(file);
            seen++;
            if (w != expected || h != expected) wrong.Add($"{name}: {w}x{h}, its name says {expected}x{expected}");
        }

        Assert.True(seen > 30, $"only {seen} package pictures found");
        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_Picture_Of_The_Listings_Has_The_Size_The_Store_Asks_For()
    {
        var wrong = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoPaths.File("ship", "addon", "images"), "*.png"))
        {
            var m = Regex.Match(Path.GetFileName(file), @"(\d+)x(\d+)\.png$");
            Assert.True(m.Success, Path.GetFileName(file));
            var (w, h) = PngSize(file);
            if (w != int.Parse(m.Groups[1].Value) || h != int.Parse(m.Groups[2].Value)) wrong.Add($"{Path.GetFileName(file)}: {w}x{h}");
        }

        var tile = PngSize(RepoPaths.File("ship", "store", "images", "app-tile-300x300.png"));
        if (tile != (300, 300)) wrong.Add($"app-tile-300x300.png: {tile}");
        foreach (var file in Directory.EnumerateFiles(RepoPaths.File("ship", "store", "screenshots"), "*.png"))
        {
            var (w, h) = PngSize(file);
            if (w < 1366 || h < 768) wrong.Add($"{Path.GetFileName(file)}: {w}x{h} is under 1366x768"); // the Store's own minimum for a desktop screenshot
        }

        Assert.Empty(wrong);
    }
}
