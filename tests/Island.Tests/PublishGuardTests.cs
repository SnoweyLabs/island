using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Island.Tests;

/// <summary>
/// The sweep of WORK-ORDER-14 section 1, before the code is made public: over every file git holds, nothing that looks like a key, a token or a password,
/// no trace of this computer or its person (the scanner of WORK-ORDER-8), and none of the workshop's leftovers. In a copy with no git of its own (the
/// public copy before it is published), every file under the root is what would be published, and that is what is swept.
/// </summary>
public class PublishGuardTests(ITestOutputHelper output)
{
    // The add-on's manifest carries a public key, which every unpacked add-on carries so that its id is the same on every computer: allowed, by name.
    private const string AllowedPublicKeyFile = "extension/manifest.json";

    // Built from pieces so that this file does not hold the very patterns it looks for.
    private static readonly (string Kind, Regex Pattern)[] SecretPatterns =
    [
        ("a private key block", new Regex("-----BEGIN [A-Z ]*" + "PRIVATE KEY-----")),
        ("a GitHub token", new Regex(@"\b(gh" + @"[pousr]_[A-Za-z0-9]{30,}|github" + @"_pat_[A-Za-z0-9_]{20,})")),
        ("a secret API key", new Regex(@"\bs" + @"k-[A-Za-z0-9_\-]{20,}")),
        ("a cloud access key", new Regex(@"\bAK" + @"IA[0-9A-Z]{16}\b")),
        ("a chat-service token", new Regex(@"\bxo" + @"x[abprs]-[A-Za-z0-9\-]{10,}")),
        ("a password with a value", new Regex(@"(?i)\bpass" + @"(word|wd)[""']?\s*[=:]\s*[""']?[^\s""'<>;,)]{3,}")),
    ];

    // Files that only make sense in the workshop on this laptop.
    private static readonly (string Kind, Regex Pattern)[] LeftoverPatterns =
    [
        ("a built folder", new Regex(@"(^|/)(dist|bin|obj|\.vs|\.worktrees|TestResults)/", RegexOptions.IgnoreCase)),
        ("the self-test's screen lock", new Regex(@"(^|/)\.screen-lock$", RegexOptions.IgnoreCase)),
        ("a log or a test result", new Regex(@"\.(log|trx|coverage)$", RegexOptions.IgnoreCase)),
        ("an environment file", new Regex(@"(^|/)\.env(\.[^/]*)?$", RegexOptions.IgnoreCase)),
        ("a per-user editor file", new Regex(@"\.(user|suo)$", RegexOptions.IgnoreCase)),
        ("a script of the workshop's own copies under dist", new Regex(@"^(run|stop|copy-next)\.cmd$", RegexOptions.IgnoreCase)),
    ];

    [Fact]
    public void Nothing_Published_Looks_Like_A_Key_A_Token_Or_A_Password()
    {
        var files = PublishedFiles();
        var findings = files.SelectMany(f => SecretsIn(f, File.ReadAllBytes(Path.Combine(RepoPaths.Root, f)))).ToList();
        output.WriteLine($"{files.Count} files swept for secrets");
        Assert.True(files.Count > 0);
        Assert.True(findings.Count == 0, "Something that looks like a secret:\n" + string.Join("\n", findings));
    }

    [Fact]
    public void Nothing_Published_Carries_This_Computer_Or_Its_Person()
    {
        var files = PublishedFiles();
        // Dan's name may appear in the public copy (his decision of 7 October, WORK-ORDER-14 "WHAT CHANGES"): the words of the account's full name are not
        // looked for here. Everything else of this computer is. The guard of ship/ (ShipGuardTests) still looks for them there.
        var scanner = TraceScanner.ForThisComputerWithoutTheName();
        var findings = files.SelectMany(f => scanner.ScanFile(Path.Combine(RepoPaths.Root, f), f)).ToList();
        output.WriteLine($"{scanner.FilesScanned} files swept for traces (archives counted by their entries)");
        Assert.True(scanner.FilesScanned > 0);
        Assert.True(findings.Count == 0, "Traces of this computer or its person:\n" + string.Join("\n", findings));
    }

    [Fact]
    public void None_Of_The_Workshops_Leftovers_Is_Published()
    {
        var findings = PublishedFiles().SelectMany(LeftoversIn).ToList();
        Assert.True(findings.Count == 0, "Workshop leftovers:\n" + string.Join("\n", findings));
    }

    [Fact]
    public void The_Sweep_Finds_A_Planted_Secret_And_Leftover_And_Allows_The_Add_Ons_Public_Key()
    {
        // Invented values, assembled here so that no file holds them.
        var plants = new (string Kind, string Text)[]
        {
            ("a private key block", "-----BEGIN RSA " + "PRIVATE KEY-----\nabc"),
            ("a private key block", "-----BEGIN " + "PRIVATE KEY-----"),
            ("a GitHub token", "token gh" + "p_" + new string('a', 36)),
            ("a GitHub token", "github" + "_pat_" + new string('B', 30)),
            ("a secret API key", "key=s" + "k-" + new string('c', 40)),
            ("a cloud access key", "AK" + "IA" + "ABCDEFGHIJKLMNOP"),
            ("a chat-service token", "xo" + "xb-" + "1234567890-abcdef"),
            ("a password with a value", "pass" + "word=hunter22"),
            ("a password with a value", "\"Pass" + "word\": \"letmein\""),
        };
        foreach (var (kind, text) in plants)
            Assert.Contains(SecretsIn("a.txt", Encoding.UTF8.GetBytes(text)), f => f.EndsWith(kind, StringComparison.Ordinal));

        // A text that only talks about keys and passwords is not a secret.
        Assert.Empty(SecretsIn("a.md", Encoding.UTF8.GetBytes("No key, token or password is read, written or printed. The password field is empty.")));
        // The add-on's public key is allowed in its manifest, and in that file only.
        var manifestKey = "\"key\": \"MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA" + new string('x', 40) + "\"";
        Assert.Empty(SecretsIn(AllowedPublicKeyFile, Encoding.UTF8.GetBytes(manifestKey)));
        Assert.Contains(SecretsIn(AllowedPublicKeyFile, Encoding.UTF8.GetBytes(plants[2].Text)), f => f.EndsWith("a GitHub token", StringComparison.Ordinal));

        foreach (var path in new[] { "dist/Island/Island.App.exe", "src/Island.App/bin/Release/x.dll", ".screen-lock", "review/run.log", "tests/TestResults/a.trx", ".env", "a/.env.local", "Island.sln.user", "run.cmd", "stop.cmd", "copy-next.cmd" })
            Assert.NotEmpty(LeftoversIn(path));
        foreach (var path in new[] { "src/Island.App/App.cs", "review/selftest.json", "ship/installer/island.iss", "README.md", "src/Island.Core/Environment.cs", "tools/BlurProbe/Program.cs" })
            Assert.Empty(LeftoversIn(path));
    }

    private static IEnumerable<string> SecretsIn(string name, byte[] bytes)
    {
        // Latin-1 keeps one character per byte, so a pattern is found in any file, text or not.
        var text = Encoding.Latin1.GetString(bytes);
        foreach (var (kind, pattern) in SecretPatterns)
        {
            if (!pattern.IsMatch(text)) continue;
            yield return $"{name}: {kind}";
        }
    }

    private static IEnumerable<string> LeftoversIn(string name) =>
        LeftoverPatterns.Where(p => p.Pattern.IsMatch(name)).Select(p => $"{name}: {p.Kind}");

    /// <summary>
    /// The files that would be published, relative to the root with '/' between parts: what git holds when the root has a git of its own; otherwise every
    /// file under the root but build output and git's own folder (the public copy before it is published).
    /// </summary>
    internal static List<string> PublishedFiles()
    {
        var root = RepoPaths.Root;
        if (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))) return GitFiles(root);

        string[] skipped = ["bin", "obj", ".vs", ".git"];
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').SkipLast(1).Any(part => skipped.Contains(part, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> GitFiles(string root)
    {
        var start = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git could not be started");
        var listed = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.True(git.ExitCode == 0, "git ls-files failed: the sweep needs git when the folder has a git of its own");
        // A file git holds but that is not on disk (deleted, not yet committed) is not swept.
        return [.. listed.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(f => File.Exists(Path.Combine(root, f)))];
    }
}
