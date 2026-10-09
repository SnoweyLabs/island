using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// Guards of the browser add-on (EVALS I3 and WORK-ORDER-8 section 7), on its source and on the contents of the zip that goes to the Chrome Web Store.
/// The add-on's own tests (extension/tests, run with node) hold the same rules; these hold them where verify can see them and on the zip.
/// </summary>
public class ExtensionGuardTests
{
    private static readonly string[] MediaMatches =
    [
        "https://www.youtube.com/*", "https://music.youtube.com/*", "https://www.twitch.tv/*", "https://soundcloud.com/*", "https://open.spotify.com/*",
    ];

    private static readonly string[] IconServices = ["s2/favicons", "favicon.ico", "icons.duckduckgo", "icon.horse", "besticon", "faviconkit", "favicone", "clearbit", "gstatic.com", "faviconV2"];

    // The same rules as extension/tests/guard.js, which the add-on's own tests apply. executeScript was in this list as a bare word; WORK-ORDER-9 section 1
    // needs one call of it, to put the add-on's own files into a tab that was already open, so the blanket ban became the narrow rule of
    // Executes_Script_Only_As_Files_Of_The_Addon (the full form, in background.js, naming `files: entry.js`). The other patterns became stricter
    // (the words eval and Function themselves, the constructor chain, userScripts, registerContentScripts) after the attack on it (review/attack-wo9.md, G1 to G9).
    private static readonly (Regex Pattern, string Name)[] DynamicCode =
    [
        (new(@"\beval\b", RegexOptions.Compiled), "eval"),
        (new(@"\bFunction\b", RegexOptions.Compiled), "the Function constructor"),
        (new(@"\bconstructor\s*\.\s*constructor\b", RegexOptions.Compiled), "the constructor chain"),
        (new(@"\b(?:setTimeout|setInterval)\s*\(\s*['""`]", RegexOptions.Compiled), "a string given to a timer"),
        (new(@"\bdocument\.write\s*\(", RegexOptions.Compiled), "document.write"),
        (new(@"\bimport\s*\(", RegexOptions.Compiled), "dynamic import"),
        (new(@"\bWebAssembly\b", RegexOptions.Compiled), "WebAssembly"),
        (new(@"\buserScripts\b", RegexOptions.Compiled), "userScripts"),
        (new(@"\bregisterContentScripts\b", RegexOptions.Compiled), "registerContentScripts"),
    ];

    private static string ExtensionFolder => Path.Combine(RepoPaths.Root, "extension");

    private static string ZipPath => Path.Combine(RepoPaths.Root, "ship", "addon", "island-addon.zip");

    /// <summary>What the browser loads: every file of the add-on that is not a test or a document. Name (with /) and text for scripts and the manifest.</summary>
    private static Dictionary<string, string> SourceTexts()
    {
        var files = new Dictionary<string, string>();
        foreach (var path in Directory.EnumerateFiles(ExtensionFolder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(ExtensionFolder, path).Replace('\\', '/');
            if (name.StartsWith("tests/", StringComparison.Ordinal) || !(name.EndsWith(".js", StringComparison.Ordinal) || name == "manifest.json")) continue;
            files[name] = File.ReadAllText(path);
        }

        return files;
    }

    private static Dictionary<string, string> ZipTexts()
    {
        var files = new Dictionary<string, string>();
        using var zip = ZipFile.OpenRead(ZipPath);
        foreach (var entry in zip.Entries)
        {
            if (!(entry.FullName.EndsWith(".js", StringComparison.Ordinal) || entry.FullName == "manifest.json")) continue;
            using var reader = new StreamReader(entry.Open());
            files[entry.FullName] = reader.ReadToEnd();
        }

        return files;
    }

    private static IEnumerable<object[]> Sources()
    {
        yield return ["the source", SourceTexts()];
        if (File.Exists(ZipPath)) yield return ["the zip", ZipTexts()];
    }

    public static TheoryData<string, Dictionary<string, string>> Both()
    {
        var data = new TheoryData<string, Dictionary<string, string>>();
        foreach (var s in Sources()) data.Add((string)s[0], (Dictionary<string, string>)s[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void No_Remote_Fetch_And_No_Icon_Service(string where, Dictionary<string, string> files)
    {
        Assert.Contains("background.js", files.Keys);
        foreach (var (name, text) in files.Where(f => f.Key.EndsWith(".js", StringComparison.Ordinal)))
        {
            foreach (Match m in Regex.Matches(text, @"\b(?:https?|wss?|ftp)://[^\s'""`]*"))
                Assert.True(m.Value.StartsWith("ws://127.0.0.1:", StringComparison.Ordinal), $"{where}: {name}: an address that is not this computer's");
            foreach (var service in IconServices)
                Assert.DoesNotContain(service, text, StringComparison.OrdinalIgnoreCase);
        }

        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        var root = manifest.RootElement;
        // WORK-ORDER-9 section 1: this computer and the five media sites, nothing else.
        Assert.Equal(new[] { "http://127.0.0.1/*" }.Concat(MediaMatches).Order().ToArray(), root.GetProperty("host_permissions").EnumerateArray().Select(e => e.GetString()!).Order().ToArray());
        foreach (var script in root.GetProperty("content_scripts").EnumerateArray())
            Assert.All(script.GetProperty("matches").EnumerateArray().Select(e => e.GetString()!), match => Assert.Contains(match, MediaMatches));
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void No_Eval_Or_Dynamic_Code(string where, Dictionary<string, string> files)
    {
        foreach (var (name, text) in files.Where(f => f.Key.EndsWith(".js", StringComparison.Ordinal)))
        {
            var code = Regex.Replace(Regex.Replace(text, @"/\*[\s\S]*?\*/", ""), @"^\s*//.*$", "", RegexOptions.Multiline);
            foreach (var (pattern, what) in DynamicCode) Assert.False(pattern.IsMatch(code), $"{where}: {name}: {what}");
            foreach (Match m in Regex.Matches(code, @"importScripts\(([^)]*)\)"))
                foreach (var arg in m.Groups[1].Value.Split(',').Select(a => a.Trim()))
                    Assert.Matches(@"^'[A-Za-z0-9_\-/]+\.js'$", arg);
        }

        Assert.DoesNotContain("unsafe-eval", files["manifest.json"], StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Executes_Script_Only_As_Files_Of_The_Addon(string where, Dictionary<string, string> files)
    {
        var calls = 0;
        foreach (var (name, text) in files.Where(f => f.Key.EndsWith(".js", StringComparison.Ordinal)))
        {
            var code = Regex.Replace(Regex.Replace(text, @"/\*[\s\S]*?\*/", ""), @"^\s*//.*$", "", RegexOptions.Multiline);
            var rest = code.Replace("chrome.scripting.executeScript", "");

            // The words `scripting` and `executeScript` may appear only inside the one full form: no alias, no destructuring, no computed name.
            Assert.False(Regex.IsMatch(rest, @"\bscripting\b"), $"{where}: {name}: scripting reached any way but chrome.scripting.executeScript");
            Assert.DoesNotContain("executeScript", rest, StringComparison.Ordinal);

            foreach (Match m in Regex.Matches(code, @"\bchrome\.scripting\.executeScript\b"))
            {
                calls++;
                Assert.True(name == "background.js", $"{where}: {name}: executeScript outside the service worker");
                var argument = CallArgument(code, m.Index + m.Length);
                Assert.Matches(@"\bfiles\s*:\s*entry\.js\b", argument); // the files the manifest's content_scripts list
                Assert.False(argument.Contains("...", StringComparison.Ordinal) || Regex.IsMatch(argument, @"\b(func|function|args|code)\b|=>|`"), $"{where}: {name}: executeScript with something other than files");
                Assert.Matches(@"for\s*\(\s*const\s+entry\s+of\s+chrome\.runtime\.getManifest\(\)\.content_scripts\b", code);
            }
        }

        Assert.True(calls >= 1, "the guard must see the one call");
    }

    /// <summary>The text between the parentheses of the call whose "(" is at <paramref name="from"/>, balanced.</summary>
    private static string CallArgument(string text, int from)
    {
        var depth = 0;
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[(from + 1)..i];
        }

        throw new InvalidOperationException("an unclosed call");
    }

    [Fact]
    public void The_Zip_Holds_The_Addon_Without_Its_Tests_Its_Protocol_And_Its_Readme()
    {
        if (!File.Exists(ZipPath)) return;
        using var zip = ZipFile.OpenRead(ZipPath);
        var names = zip.Entries.Select(e => e.FullName).Order().ToList();

        Assert.DoesNotContain(names, n => n.StartsWith("tests/", StringComparison.Ordinal) || n.Contains('\\', StringComparison.Ordinal));
        Assert.DoesNotContain("PROTOCOL.md", names);
        Assert.DoesNotContain("README.md", names);
        Assert.DoesNotContain(names, n => n.EndsWith(".pem", StringComparison.Ordinal));

        var expected = Directory.EnumerateFiles(ExtensionFolder, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(ExtensionFolder, p).Replace('\\', '/'))
            .Where(n => !n.StartsWith("tests/", StringComparison.Ordinal) && !n.StartsWith("node_modules/", StringComparison.Ordinal) && n is not ("PROTOCOL.md" or "README.md"))
            .Order().ToList();
        Assert.Equal(expected, names);

        // The manifest in the zip is the one in the repository, byte for byte; each file the zip holds is the file of the repository.
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            Assert.Equal(File.ReadAllBytes(Path.Combine(ExtensionFolder, entry.FullName)), memory.ToArray());
        }
    }

    [Fact]
    public void The_Manifest_Asks_For_Only_The_Permissions_Some_Code_Uses_And_Carries_The_Addons_Own_Version()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ExtensionFolder, "manifest.json")));
        var root = manifest.RootElement;
        Assert.Equal("1.1.0", root.GetProperty("version").GetString()); // the add-on's own version, apart from the app's (WORK-ORDER-8 section 1)

        var code = string.Join("\n", SourceTexts().Where(f => f.Key.EndsWith(".js", StringComparison.Ordinal)).Select(f => f.Value));
        var evidence = new Dictionary<string, string>
        {
            ["tabs"] = "chrome.tabs.",
            ["favicon"] = "/_favicon/",
            ["alarms"] = "chrome.alarms.",
            ["storage"] = "chrome.storage.",
            ["scripting"] = "chrome.scripting.",
        };
        var asked = root.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).Order().ToList();
        Assert.Equal(evidence.Keys.Order(), asked);
        foreach (var permission in asked) Assert.Contains(evidence[permission], code, StringComparison.Ordinal); // a permission that no code uses would be removed

        // Every icon the manifest names is there.
        foreach (var icon in root.GetProperty("icons").EnumerateObject())
            Assert.True(File.Exists(Path.Combine(ExtensionFolder, icon.Value.GetString()!)), icon.Name);
    }
}
