using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Core.Agents.Connect;

namespace Island.Attack11.Tests;

/// <summary>
/// The two installers, Claude Code's HookInstaller and Codex's CodexHooks, on text only: an empty file, a file with a byte-order mark, someone else's hooks,
/// entries written by an older island, and files that cannot be read. No file is read or written, none is named. Invented commands and paths only.
/// </summary>
public class InstallerAttackTests
{
    private const string Notify = @"C:\Invented\notify\Island.Notify.exe";

    // Both installers behind one shape, so each attack runs on both.
    public sealed record Installer(string Name, Func<string?, HookEdit> Connect, Func<string?, HookEdit> Disconnect, Func<string?, string> State)
    {
        public override string ToString() => Name;
    }

    private static readonly Installer Claude = new("claude", t => HookInstaller.Connect(t, Notify), HookInstaller.Disconnect, t => HookInstaller.StateOf(t).ToString());
    private static readonly Installer Codex = new("codex", t => CodexHooks.Connect(t, Notify), CodexHooks.Disconnect, t => CodexHooks.State(t).ToString());

    public static TheoryData<string> Both() => new() { "claude", "codex" };

    private static Installer Pick(string name) => name == "claude" ? Claude : Codex;

    // ---- Comparing text as JSON ----------------------------------------------------------------------------------------------------------------------

    private static bool Same(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var pa = a.EnumerateObject().ToList();
                var pb = b.EnumerateObject().ToList();
                return pa.Count == pb.Count && pa.Zip(pb).All(z => z.First.Name == z.Second.Name && Same(z.First.Value, z.Second.Value));
            case JsonValueKind.Array:
                var ia = a.EnumerateArray().ToList();
                var ib = b.EnumerateArray().ToList();
                return ia.Count == ib.Count && ia.Zip(ib).All(z => Same(z.First, z.Second));
            case JsonValueKind.String:
                return Str(a) == Str(b);
            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();
            default:
                return true;
        }
    }

    private static string Str(JsonElement e)
    {
        try { return e.GetString()!; }
        catch (InvalidOperationException) { return "RAW:" + e.GetRawText(); }
    }

    private static bool JsonEqual(string a, string b)
    {
        using var da = JsonDocument.Parse(a.TrimStart((char)0xFEFF));
        using var db = JsonDocument.Parse(b.TrimStart((char)0xFEFF));
        return Same(da.RootElement, db.RootElement);
    }

    private static string Diff(string a, string b)
    {
        using var da = JsonDocument.Parse(a.TrimStart((char)0xFEFF));
        using var db = JsonDocument.Parse(b.TrimStart((char)0xFEFF));
        return Diff("$", da.RootElement, db.RootElement) ?? "(equal)";
    }

    private static string? Diff(string path, JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return $"{path}: {a.ValueKind} vs {b.ValueKind}";
        if (a.ValueKind == JsonValueKind.Object)
        {
            var pa = a.EnumerateObject().ToList();
            var pb = b.EnumerateObject().ToList();
            for (var k = 0; k < Math.Max(pa.Count, pb.Count); k++)
            {
                if (k >= pa.Count || k >= pb.Count || pa[k].Name != pb[k].Name) return $"{path}: key #{k} {(k < pa.Count ? pa[k].Name : "<none>")} vs {(k < pb.Count ? pb[k].Name : "<none>")}";
                if (Diff(path + "." + pa[k].Name, pa[k].Value, pb[k].Value) is { } d) return d;
            }

            return null;
        }

        if (a.ValueKind == JsonValueKind.Array)
        {
            var ia = a.EnumerateArray().ToList();
            var ib = b.EnumerateArray().ToList();
            for (var k = 0; k < Math.Max(ia.Count, ib.Count); k++)
            {
                if (k >= ia.Count || k >= ib.Count) return $"{path}: length {ia.Count} vs {ib.Count}";
                if (Diff($"{path}[{k}]", ia[k], ib[k]) is { } d) return d;
            }

            return null;
        }

        return Same(a, b) ? null : $"{path}: {a.GetRawText()} vs {b.GetRawText()}";
    }

    private static int OurHandlers(string text) => text.Split("Island.Notify", StringSplitOptions.None).Length - 1;

    // ---- An empty file, a byte-order mark ---------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("claude", null)]
    [InlineData("claude", "")]
    [InlineData("claude", "   \r\n\t ")]
    [InlineData("claude", "\uFEFF")]
    [InlineData("claude", "\uFEFF  \n")]
    [InlineData("codex", null)]
    [InlineData("codex", "")]
    [InlineData("codex", "   \r\n\t ")]
    [InlineData("codex", "\uFEFF")]
    [InlineData("codex", "\uFEFF  \n")]
    public void Holds_An_Empty_File_Is_Connected_From_Nothing_And_Disconnected_To_Nothing_Changed(string who, string? text)
    {
        var i = Pick(who);
        var edit = i.Connect(text);
        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
        Assert.Equal("Connected", i.State(edit.Text));
        Assert.True(OurHandlers(edit.Text) >= 2);
        var none = i.Disconnect(text);
        Assert.False(none.Changed);
        Assert.Null(none.Reason);
        Assert.Equal("NotConnected", i.State(text));
        var back = i.Disconnect(edit.Text);
        Assert.True(back.Changed);
        Assert.Equal(0, OurHandlers(back.Text));
        Assert.True(JsonEqual(back.Text, "{}"));
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_A_File_With_A_Byte_Order_Mark_Is_Read_And_Written_Back_Without_Losing_Anything(string who)
    {
        var i = Pick(who);
        var original = "\uFEFF{\"model\":\"alpha\",\"hooks\":{\"PreToolUse\":[{\"matcher\":\"Bash\",\"hooks\":[{\"type\":\"command\",\"command\":\"check.exe\"}]}]},\"z\":1}";
        var edit = i.Connect(original);
        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
        Assert.Equal("Connected", i.State(edit.Text));
        Assert.Equal("NotConnected", i.State(original));
        var back = i.Disconnect(edit.Text);
        Assert.True(JsonEqual(original, back.Text));
    }

    // ---- Someone else's hooks ---------------------------------------------------------------------------------------------------------------------------

    private const string Theirs = """
        {
          "model": "alpha", "env": { "X": "1", "Y": "<&>'" },
          "numbers": [1, 2.50, 1E5, -0, 12345678901234567890, 0.10, 1e999],
          "note": "naïve café 日本 😀 \u00e9 \/ \" \\ ",
          "hooks": {
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "check.exe", "timeout": 5 } ] } ],
            "PostToolUse": [ { "matcher": "Edit|Write", "hooks": [ { "type": "command", "command": "fmt.exe", "args": ["--fix"], "async": true } ] } ],
            "Stop": [ { "hooks": [ { "type": "command", "command": "beta.exe", "args": ["--x"] }, { "type": "http", "url": "http://example.org/x" } ] } ],
            "Notification": [ { "matcher": "permission_prompt", "hooks": [ { "type": "command", "command": "beep.exe" } ] } ],
            "Custom": 7
          },
          "zeta": [ null, true, { "a": [ ] } ]
        }
        """;

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Someone_Elses_Hooks_Are_Kept_In_Their_Order_And_Come_Back_After_A_Disconnect(string who)
    {
        var i = Pick(who);
        var edit = i.Connect(Theirs);
        Assert.Null(edit.Reason);
        Assert.True(edit.Changed);
        var after = JsonNode.Parse(edit.Text)!["hooks"]!.AsObject();
        var before = JsonNode.Parse(Theirs)!["hooks"]!;
        Assert.True(JsonNode.DeepEquals(before["PreToolUse"], after["PreToolUse"]));
        Assert.True(JsonNode.DeepEquals(before["Custom"], after["Custom"]));
        Assert.True(JsonNode.DeepEquals(before["PostToolUse"]![0], after["PostToolUse"]![0])); // theirs first, ours after
        Assert.Equal("beta.exe", (string)after["Stop"]![0]!["hooks"]![0]!["command"]!);
        Assert.Equal("beep.exe", (string)after["Notification"]![0]!["hooks"]![0]!["command"]!);
        Assert.Contains("12345678901234567890", edit.Text);
        Assert.Contains("1E5", edit.Text);
        Assert.Contains("1e999", edit.Text);
        Assert.Contains("naïve café 日本", edit.Text);
        var back = i.Disconnect(edit.Text);
        Assert.True(JsonEqual(Theirs, back.Text));
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Connect_Twice_Changes_Nothing_The_Second_Time(string who)
    {
        var i = Pick(who);
        var once = i.Connect(Theirs);
        var twice = i.Connect(once.Text);
        Assert.False(twice.Changed);
        Assert.Equal(once.Text, twice.Text);
        Assert.Null(twice.Reason);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Disconnect_Takes_Out_Only_What_Runs_Island_Notify_And_Not_Its_Lookalikes(string who)
    {
        var i = Pick(who);
        var lookalikes = new[]
        {
            "\"C:\\\\x\\\\Island.Notify2.exe\"", "\"C:\\\\x\\\\NotIsland.Notify.exe\"", "\"C:\\\\x\\\\Island.Notify.exe.bak\"", "\"C:\\\\Island.Notify\\\\run.exe\"", "\"node C:\\\\x\\\\Island.Notify.js\"",
            "\"python C:\\\\x\\\\Island.Notify.exe\"", "\"echo Island.Notify\"", "\"Island-Notify.exe\"", "\"island notify.exe\"", "\"\"", "\"   \"",
        };
        var handlers = string.Join(",", lookalikes.Select(c => "{\"type\":\"command\",\"command\":" + c + "}"));
        var original = "{\"hooks\":{\"Stop\":[{\"hooks\":[" + handlers + "]}]}}";
        var connected = i.Connect(original);
        Assert.Null(connected.Reason);
        var back = i.Disconnect(connected.Text);
        Assert.True(JsonEqual(original, back.Text), back.Text);
        Assert.False(i.Disconnect(original).Changed);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Our_Handler_Between_Two_Of_Theirs_Is_Taken_Out_And_They_Close_Ranks(string who)
    {
        var i = Pick(who);
        var withOurs = "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"a.exe\"},{\"type\":\"command\",\"command\":\"" + Notify.Replace("\\", "\\\\") + "\",\"args\":[\"--agent\",\"x\"]},{\"type\":\"command\",\"command\":\"b.exe\"}]}]}}";
        var back = i.Disconnect(withOurs);
        Assert.True(back.Changed);
        var handlers = JsonNode.Parse(back.Text)!["hooks"]!["Stop"]![0]!["hooks"]!.AsArray();
        Assert.Equal(["a.exe", "b.exe"], handlers.Select(h => (string)h!["command"]!));
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Random_Foreign_Files_Round_Trip_Exactly(string who)
    {
        var i = Pick(who);
        var rng = new Random(2026);
        for (var n = 0; n < 300; n++)
        {
            var original = RandomFile(rng);
            var edit = i.Connect(original);
            Assert.True(edit.Reason is null, $"{n}: {edit.Reason} for {original}");
            Assert.Equal("Connected", i.State(edit.Text));
            Assert.False(i.Connect(edit.Text).Changed);
            var back = i.Disconnect(edit.Text);
            Assert.True(JsonEqual(original, back.Text), $"{n}: {Diff(original, back.Text)}");
        }
    }

    // ---- Entries written by an older island ----------------------------------------------------------------------------------------------------------------

    private const string OldEntries = """
        { "hooks": {
          "Stop": [ { "hooks": [ { "type": "command", "command": "C:\\Invented\\notify\\Island.Notify.exe", "args": [], "async": true } ] } ],
          "Notification": [ { "matcher": "permission_prompt", "hooks": [ { "type": "command", "command": "C:\\Invented\\notify\\Island.Notify.exe", "args": [], "async": true } ] } ]
        } }
        """;

    [Fact]
    public void Holds_Old_Entries_Read_As_Connected_Older_Are_Updated_Only_Where_Missing_And_Removed_Alike()
    {
        Assert.Equal(AgentConnection.ConnectedOlder, HookInstaller.StateOf(OldEntries));
        var update = HookInstaller.Connect(OldEntries, Notify);
        Assert.True(update.Changed);
        Assert.Equal(AgentConnection.Connected, HookInstaller.StateOf(update.Text));
        var stop = JsonNode.Parse(update.Text)!["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop); // the old Stop entry is not repeated
        Assert.Empty(stop[0]!["hooks"]![0]!["args"]!.AsArray()); // and not repaired
        var gone = HookInstaller.Disconnect(update.Text);
        Assert.Equal(0, OurHandlers(gone.Text));
        Assert.True(JsonEqual(gone.Text, "{}"));
    }

    [Fact]
    public void Holds_Old_Entries_Under_Another_Folder_Count_As_Ours_And_Are_Not_Doubled()
    {
        var elsewhere = OldEntries.Replace("C:\\\\Invented\\\\notify", "D:\\\\Older\\\\copy");
        var update = HookInstaller.Connect(elsewhere, Notify);
        Assert.Equal(1, JsonNode.Parse(update.Text)!["hooks"]!["Stop"]!.AsArray().Count);
        Assert.Equal(2, JsonNode.Parse(update.Text)!["hooks"]!["Notification"]!.AsArray().Count); // permission_prompt (old) and idle_prompt (new)
    }

    [Fact]
    public void Holds_An_Interrupted_Update_Half_Done_Is_Finished_By_The_Next_Update()
    {
        var half = HookInstaller.Connect(OldEntries, Notify).Text;
        var node = JsonNode.Parse(half)!.AsObject();
        node["hooks"]!.AsObject().Remove("SessionEnd");
        node["hooks"]!.AsObject().Remove("PostToolUse");
        var cut = node.ToJsonString();
        Assert.Equal(AgentConnection.ConnectedOlder, HookInstaller.StateOf(cut));
        var done = HookInstaller.Connect(cut, Notify);
        Assert.Equal(AgentConnection.Connected, HookInstaller.StateOf(done.Text));
        Assert.Equal(7, OurHandlers(done.Text)); // seven entries, each once
    }

    [Fact]
    public void Holds_Codex_Older_Reads_As_Connected_Older_When_Only_Some_Events_Are_There()
    {
        var some = "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"\\\"" + Notify.Replace("\\", "\\\\") + "\\\" --agent codex\",\"async\":true}]}]}}";
        Assert.Equal(ConnectState.ConnectedOlder, CodexHooks.State(some));
        var fixedUp = CodexHooks.Connect(some, Notify);
        Assert.Equal(ConnectState.Connected, CodexHooks.State(fixedUp.Text));
        Assert.Equal(1, JsonNode.Parse(fixedUp.Text)!["hooks"]!["Stop"]!.AsArray().Count);
    }

    // ---- Files that cannot be read -----------------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, string> Unreadable() => new()
    {
        { "claude", "{ not json" }, { "claude", "[1,2]" }, { "claude", "null" }, { "claude", "5" }, { "claude", "\"x\"" },
        { "claude", "{\"hooks\":[]}" }, { "claude", "{\"hooks\":7}" }, { "claude", "{\"hooks\":null}" },
        { "claude", "{\"hooks\":{\"Stop\":{}}}" }, { "claude", "{\"hooks\":{\"Stop\":\"x\"}}" }, { "claude", "{\"hooks\":{\"Notification\":5}}" },
        { "claude", "{\"hooks\":{},\"hooks\":{}}" }, { "claude", "{\"a\":1,\"a\":2}" }, { "claude", "{\"hooks\":{\"Stop\":[]},}" },
        { "claude", "{// c\n\"a\":1}" }, { "claude", "{\"a\":1} {\"b\":2}" }, { "claude", "{\"a\":\"\\ud800\",\"hooks\":{}}" + "x" },
        { "codex", "{ not json" }, { "codex", "[1,2]" }, { "codex", "null" }, { "codex", "5" }, { "codex", "\"x\"" },
        { "codex", "{\"hooks\":[]}" }, { "codex", "{\"hooks\":7}" }, { "codex", "{\"hooks\":null}" },
        { "codex", "{\"hooks\":{\"Stop\":{}}}" }, { "codex", "{\"hooks\":{\"Stop\":\"x\"}}" }, { "codex", "{\"hooks\":{\"PostToolUse\":5}}" },
        { "codex", "{\"hooks\":{},\"hooks\":{}}" }, { "codex", "{\"a\":1,\"a\":2}" }, { "codex", "{\"hooks\":{\"Stop\":[]},}" },
        { "codex", "{// c\n\"a\":1}" }, { "codex", "{\"a\":1} {\"b\":2}" },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void Holds_A_File_That_Cannot_Be_Read_Comes_Back_Unchanged_With_The_Reason(string who, string text)
    {
        var i = Pick(who);
        var connect = i.Connect(text);
        Assert.False(connect.Changed);
        Assert.Equal(text, connect.Text);
        Assert.Equal(HookInstaller.FileUnreadable, connect.Reason);
        var disconnect = i.Disconnect(text);
        Assert.False(disconnect.Changed);
        Assert.Equal(text, disconnect.Text);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void Holds_Text_That_Is_Not_Even_Json_Reads_As_Unreadable_And_A_Wrong_Shaped_Hooks_Value_As_Not_Connected(string who)
    {
        var i = Pick(who);
        Assert.Equal("Unreadable", i.State("{ not json"));
        Assert.Equal("Unreadable", i.State("{\"hooks\":[]}"));
        Assert.Equal("NotConnected", i.State("{\"hooks\":{\"Stop\":{}}}")); // valid JSON, wrong shape: the row offers Connect and Connect refuses with the reason
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_A_Lone_Surrogate_Escape_In_Someone_Elses_Value_Leaves_The_File_As_It_Is(string who)
    {
        var i = Pick(who);
        var text = "{\"note\":\"a\\ud800b\",\"hooks\":{}}";
        var connect = i.Connect(text);
        Assert.True(connect.Reason is null ? JsonEqual(text, connect.Text) : connect.Text == text);
        Assert.DoesNotContain("Island.Notify", connect.Reason is null ? "" : connect.Text);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_Deep_Nesting_Beyond_The_Readers_Limit_Is_Refused_Not_Thrown(string who)
    {
        var i = Pick(who);
        var deep = "{\"a\":" + new string('[', 200) + new string(']', 200) + "}";
        Assert.Equal(HookInstaller.FileUnreadable, i.Connect(deep).Reason);
        var edge = "{\"a\":" + new string('[', 60) + new string(']', 60) + "}";
        var ok = i.Connect(edge);
        Assert.Null(ok.Reason);
    }

    [Theory]
    [MemberData(nameof(Both))]
    public void Holds_A_Big_File_Is_Handled_Without_Delay(string who)
    {
        var i = Pick(who);
        var sb = new StringBuilder("{\"hooks\":{\"PreToolUse\":[");
        for (var n = 0; n < 20_000; n++) sb.Append(n > 0 ? "," : "").Append("{\"matcher\":\"m").Append(n).Append("\",\"hooks\":[{\"type\":\"command\",\"command\":\"x").Append(n).Append(".exe\"}]}");
        sb.Append("]},\"big\":\"").Append(new string('x', 3_000_000)).Append("\"}");
        var text = sb.ToString();
        var clock = Stopwatch.StartNew();
        var edit = i.Connect(text);
        var back = i.Disconnect(edit.Text);
        clock.Stop();
        Assert.Null(edit.Reason);
        Assert.True(JsonEqual(text, back.Text));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    // ---- What an installer will write -----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Invented\notify\Island.Notify.exe", true)]
    [InlineData(@"C:/Invented/notify/Island.Notify.exe", true)]
    [InlineData(@"C:\Invented & Co\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented^\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented (x86)\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented\notify\Island.Notify", true)]
    [InlineData(@"relative\Island.Notify.exe", false)]
    [InlineData(@"\\server\share\Island.Notify.exe", false)]
    [InlineData(@" C:\Invented\Island.Notify.exe", false)]
    [InlineData(@"C:\Invented\Island.Notify.exe ", false)]
    [InlineData(@"C:\Invented\Other.exe", false)]
    [InlineData(@"C:\Invented\Island.Notify2.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Holds_Claude_Connect_Writes_Only_An_Absolute_Local_Path_To_Its_Own_Program(string? path, bool accepted)
    {
        var edit = HookInstaller.Connect("", path!);
        Assert.Equal(accepted, edit.Changed);
        if (!accepted) Assert.Equal(HookInstaller.NotifyPathInvalid, edit.Reason);
        Assert.Equal(accepted, HookInstaller.LinesToAdd(path!).Count > 0);
    }

    [Theory]
    [InlineData(@"C:\Invented\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented & Co\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented (x86)\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented^\notify\Island.Notify.exe", true)]
    [InlineData(@"C:\Invented\no""tify\Island.Notify.exe", false)]
    [InlineData(@"C:\Invented\%PATH%\Island.Notify.exe", false)]
    [InlineData(@"C:\Invented\$HOME\Island.Notify.exe", false)]
    [InlineData("C:\\Invented\\`x`\\Island.Notify.exe", false)]
    [InlineData("C:\\Invented\\a\nb\\Island.Notify.exe", false)]
    [InlineData("C:\\Invented\\a\0b\\Island.Notify.exe", false)]
    [InlineData(@"C:\Invented\notify\Island.Notify.exe ", false)]
    public void Holds_Codex_Connect_Refuses_A_Command_It_Cannot_Quote_Safely(string path, bool accepted)
    {
        var edit = CodexHooks.Connect("", path);
        Assert.Equal(accepted, edit.Changed);
        if (!accepted) Assert.Equal(HookInstaller.NotifyPathInvalid, edit.Reason);
        Assert.Equal(accepted, CodexHooks.LinesToAdd(path).Count > 0);
        if (accepted)
        {
            var command = (string)JsonNode.Parse(edit.Text)!["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!;
            Assert.Equal("\"" + path + "\" --agent codex", command);
        }
    }

    [Fact]
    public void Holds_The_Question_Shows_Exactly_What_Connect_Writes()
    {
        Assert.True(JsonEqual(string.Join("\n", HookInstaller.LinesToAdd(Notify)), HookInstaller.Connect("", Notify).Text));
        Assert.True(JsonEqual(string.Join("\n", CodexHooks.LinesToAdd(Notify)), CodexHooks.Connect("", Notify).Text));
    }

    [Fact]
    public void Holds_Every_Entry_Starts_The_Program_Directly_In_The_Background()
    {
        foreach (var text in new[] { HookInstaller.Connect("", Notify).Text, CodexHooks.Connect("", Notify).Text })
        {
            var hooks = JsonNode.Parse(text)!["hooks"]!.AsObject();
            foreach (var (_, groups) in hooks)
                foreach (var group in groups!.AsArray())
                    foreach (var handler in group!["hooks"]!.AsArray())
                    {
                        Assert.Equal("command", (string)handler!["type"]!);
                        Assert.True((bool)handler["async"]!);
                    }
        }
    }

    [Fact]
    public void Holds_Disconnect_Leaves_No_Hook_And_Every_Other_Key_Where_The_Person_Had_Only_Empty_Containers()
    {
        // FINDING A11-06 (LOW, cosmetic), CLOSED WITH A NOTE by the main session (adapted from Defect_Disconnect_Does_Not_Give_Back_An_Empty_List_Or_An_Empty_Hooks_Object_That_The_Person_Had,
        // whose intent was "the round trip gives back the same file"): a person's own empty list ("Stop": []) that Connect added our group to, or an empty "hooks": {}, comes back as
        // no list and no object, because once ours was added Disconnect cannot tell an empty container the person had from one it emptied, and removing the emptied ones is right for the
        // common case (a file that had no such list). Both mean "no hooks": what is kept here is that nothing of ours is left, no other key is touched and the hooks that mean something stay.
        foreach (var original in new[] { "{\"hooks\":{\"Stop\":[]}}", "{\"hooks\":{}}", "{\"a\":1,\"hooks\":{}}" })
        {
            foreach (var back in new[] { HookInstaller.Disconnect(HookInstaller.Connect(original, Notify).Text).Text, CodexHooks.Disconnect(CodexHooks.Connect(original, Notify).Text).Text })
            {
                var root = JsonNode.Parse(back)!.AsObject();
                Assert.DoesNotContain("Island.Notify", back, StringComparison.Ordinal);
                var hooks = root["hooks"]?.AsObject();
                Assert.True(hooks is null || hooks.All(p => p.Value is JsonArray { Count: 0 }), back);
                Assert.Equal(JsonNode.Parse(original)!["a"]?.ToJsonString(), root["a"]?.ToJsonString());
            }
        }
    }

    // ---- Random foreign files ---------------------------------------------------------------------------------------------------------------------------------

    private static readonly string[] NumberForms = ["1", "2.50", "1E5", "-0", "12345678901234567890", "0.10", "1e999", "3.141592653589793238", "100"];

    private static JsonNode? RandomValue(Random r, int depth)
    {
        switch (r.Next(depth > 2 ? 5 : 7))
        {
            case 0: return JsonNode.Parse(NumberForms[r.Next(NumberForms.Length)]);
            case 1: return JsonValue.Create(new[] { "naïve", "日本", "😀", "a<b>&c'", "tab\there", "", "slash/back\\", "\u00e9" }[r.Next(8)]);
            case 2: return JsonValue.Create(r.Next(2) == 0);
            case 3: return null;
            case 4: return JsonValue.Create("s" + r.Next(1000));
            case 5:
                var arr = new JsonArray();
                for (var k = r.Next(0, 4); k > 0; k--) arr.Add(RandomValue(r, depth + 1));
                return arr;
            default:
                var obj = new JsonObject();
                for (var k = r.Next(0, 4); k > 0; k--) obj["k" + r.Next(50)] = RandomValue(r, depth + 1);
                return obj;
        }
    }

    private static readonly string[] Commands = ["check.exe", "node", @"C:\Apps\x\run.cmd", "Island.Notify2.exe", "python C:\\x\\Island.Notify.exe", "\"C:\\Program Files\\x y\\tool.exe\" --flag", "echo hi && echo Island.Notify"];

    private static string RandomFile(Random r)
    {
        var root = new JsonObject();
        for (var k = r.Next(0, 6); k > 0; k--) root["key" + r.Next(30)] = RandomValue(r, 0);
        if (r.Next(5) != 0)
        {
            var hooks = new JsonObject();
            foreach (var ev in new[] { "PreToolUse", "PostToolUse", "Stop", "Notification", "SessionStart", "UserPromptSubmit", "SessionEnd", "Custom", "Interrupt", "PermissionRequest" }.OrderBy(_ => r.Next()).Take(r.Next(1, 6)))
            {
                var groups = new JsonArray();
                for (var g = r.Next(1, 3); g > 0; g--)
                {
                    var handlers = new JsonArray();
                    for (var h = r.Next(1, 3); h > 0; h--)
                    {
                        var handler = new JsonObject { ["type"] = "command", ["command"] = Commands[r.Next(Commands.Length)] };
                        // exec form (args present) names one program, never a shell line; a shell line has no args
                        if (r.Next(2) == 0 && !((string)handler["command"]!).Contains(' ')) handler["args"] = new JsonArray("--a", "b c");
                        if (r.Next(2) == 0) handler["async"] = r.Next(2) == 0;
                        if (r.Next(3) == 0) handler["timeout"] = JsonNode.Parse(NumberForms[r.Next(NumberForms.Length)]);
                        handlers.Add(handler);
                    }

                    var group = new JsonObject();
                    if (r.Next(2) == 0) group["matcher"] = new[] { "Bash", "permission_prompt", "idle_prompt", "Edit|Write", "" }[r.Next(5)];
                    group["hooks"] = handlers;
                    groups.Add(group);
                }

                hooks[ev] = groups;
            }

            root["hooks"] = hooks;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = r.Next(2) == 0 });
    }
}
