using System.Text.RegularExpressions;

namespace Island.Attack11.Tests;

/// <summary>
/// The two connectors (Claude Code's and Codex's) are WPF-side files that cannot run here; their shape is read from the source: every public way in asks the gate before anything is
/// read or written, nothing takes a path from a caller, and the place of the settings file is spelled in one private method.
/// </summary>
public class GateAttackTests
{
    private static string Source(string name) => SourceScanAttackTests.StripComments(Repo.Text("src", "Island.App", name));

    /// <summary>The text of a member: from its signature to the matching close brace (or to the end of an expression body).</summary>
    private static string Member(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "no such member: " + signature);
        var arrow = code.IndexOf("=>", start, StringComparison.Ordinal);
        var open = code.IndexOf('{', start);
        if (arrow >= 0 && (open < 0 || arrow < open)) return code[start..code.IndexOf(';', arrow)];
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[start..(i + 1)];
        }

        throw new InvalidOperationException("unbalanced: " + signature);
    }

    private static readonly Regex Touches = new(@"\b(?:ReadText|Write|ConnectOrUpdate|CopyNotify|File\.\w+|Directory\.\w+|Environment\.GetFolderPath)\s*[(.]", RegexOptions.Compiled);

    private static void AssertGateFirst(string code, string signature)
    {
        var body = Member(code, signature);
        var gate = body.IndexOf("Allowed()", StringComparison.Ordinal);
        Assert.True(gate >= 0, signature + " never asks the gate");
        var touch = Touches.Match(body, Math.Max(0, body.IndexOf('{')));
        if (touch.Success) Assert.True(gate < touch.Index, $"{signature} reaches {touch.Value} before it asks the gate");
    }

    [Theory]
    [InlineData("OutsideClaudeSettings.cs")]
    [InlineData("OutsideCodexSettings.cs")]
    public void Holds_Every_Public_Way_In_Asks_The_Gate_Before_It_Reads_Or_Writes(string file)
    {
        var code = Source(file);
        AssertGateFirst(code, "public AgentConnection State()");
        AssertGateFirst(code, "private ConnectorResult ConnectOrUpdate(bool update)");
        AssertGateFirst(code, "public ConnectorResult Disconnect()");
        // Connect and Update only forward to ConnectOrUpdate
        Assert.Contains("ConnectOrUpdate(update: false)", Member(code, "public ConnectorResult Connect()"));
        Assert.Contains("ConnectOrUpdate(update: true)", Member(code, "public ConnectorResult Update()"));
    }

    [Theory]
    [InlineData("OutsideClaudeSettings.cs")]
    [InlineData("OutsideCodexSettings.cs")]
    public void Holds_Nothing_Public_Takes_A_Path_Or_A_Text_From_A_Caller(string file)
    {
        var code = Source(file);
        var publics = Regex.Matches(code, @"public\s+[\w<>?,\[\]\s]+?\s+(\w+)\s*\(([^)]*)\)").Select(m => (Name: m.Groups[1].Value, Args: m.Groups[2].Value)).ToList();
        Assert.NotEmpty(publics);
        foreach (var (name, args) in publics)
            Assert.DoesNotMatch(@"\b(?:string|FileInfo|DirectoryInfo|Uri|Stream)\b", args); // no method names a place or hands in a text: the file knows its one place
    }

    [Theory]
    [InlineData("OutsideClaudeSettings.cs", "SettingsFile")]
    [InlineData("OutsideCodexSettings.cs", "HooksFile")]
    public void Holds_The_Place_Of_The_Settings_File_Is_Spelled_In_One_Private_Method(string file, string method)
    {
        var code = Source(file);
        Assert.Equal(1, Regex.Matches(code, @"""settings\.json""|""hooks\.json""").Count);
        Assert.Contains("private static string " + method + "()", code);
        Assert.Contains("Environment.SpecialFolder.UserProfile", Member(code, "private static string " + (file.Contains("Codex") ? "CodexFolder" : method) + "()"));
    }

    [Fact]
    public void Holds_Neither_Connector_Is_Called_At_Launch_Or_On_A_Timer_From_The_Wiring()
    {
        // the file names of the callers: only the settings screen's section and the app's own construction may name a connector; no Timer or launch path calls Connect/Update/Disconnect
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
            var name = Path.GetFileName(file);
            if (name is "OutsideClaudeSettings.cs" or "OutsideCodexSettings.cs") continue;
            var code = SourceScanAttackTests.StripComments(File.ReadAllText(file));
            if (!Regex.IsMatch(code, @"\.(?:Connect|Update|Disconnect)Agent\(|\bConnector\.(?:Connect|Update|Disconnect)\(")) continue;
            Assert.True(name is "AgentsSection.cs" or "SettingsSession.cs", $"{name} presses a connector");
        }
    }
}
