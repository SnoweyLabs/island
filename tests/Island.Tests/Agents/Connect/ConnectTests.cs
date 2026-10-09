using System.Text.Json.Nodes;
using Island.Core;
using Island.Core.Agents.Connect;

namespace Island.Tests.Agents.Connect;

public class ConnectTests
{
    private const string NotifyPath = @"Q:\Invented\Alpha\notify\Island.Notify.exe";
    private const string OurCommand = "\"" + NotifyPath + "\" --agent codex";
    private static readonly string[] Written = ["SessionStart", "UserPromptSubmit", "PermissionRequest", "PostToolUse", "Stop", "Interrupt"];

    // Someone else's file: foreign hooks (one in an event of ours), unrelated settings, odd numbers and text that must come back as they were.
    private const string Existing = """
        {
          "description": "Alpha's hooks",
          "numbers": [1, 2.50, 12345678901234567890],
          "note": "naïve café 日本",
          "hooks": {
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "check.exe" } ] } ],
            "Stop": [ { "hooks": [ { "type": "command", "command": "beta.exe --x", "timeout": 30 } ] } ],
            "Custom": 7
          },
          "zeta": true
        }
        """;

    private static JsonNode Parse(string text) => JsonNode.Parse(text)!;

    private static JsonArray Groups(string text, string eventName) => (JsonArray)Parse(text)["hooks"]![eventName]!;

    private static int OursIn(string text) =>
        Parse(text)["hooks"]!.AsObject().Select(p => p.Value).OfType<JsonArray>().Sum(list => list.Sum(g => ((JsonArray)g!["hooks"]!).Count(h => ((string)h!["command"]!).Contains("Island.Notify"))));

    [Fact]
    public void Codex_Connect_Adds_Its_Entries_Once()
    {
        var first = CodexHooks.Connect(Existing, NotifyPath);
        var second = CodexHooks.Connect(first.Text, NotifyPath);
        var fromNothing = CodexHooks.Connect("", NotifyPath);
        var fromNull = CodexHooks.Connect(null, NotifyPath);

        Assert.True(first.Changed);
        Assert.Null(first.Reason);
        Assert.False(second.Changed); // nothing is added twice
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(Written.Length, OursIn(first.Text));
        foreach (var name in Written)
        {
            var ours = Groups(first.Text, name).Select(g => (JsonObject)((JsonArray)g!["hooks"]!)[0]!).Last();
            Assert.Equal("command", (string)ours["type"]!);
            Assert.Equal(OurCommand, (string)ours["command"]!); // the shell form: the path in double quotes, then --agent codex
            Assert.True((bool)ours["async"]!); // in the background: Codex never waits
            Assert.Null(ours["args"]); // the page gives no args field for Codex
        }

        Assert.Equal(2, Groups(first.Text, "Stop").Count); // beside the person's own Stop hook, theirs first
        Assert.DoesNotContain("SessionEnd", first.Text); // always synchronous: no entry
        Assert.Equal(CodexHooks.Connect("", NotifyPath).Text, fromNull.Text);
        Assert.Equal(Written, Parse(fromNothing.Text)["hooks"]!.AsObject().Select(p => p.Key));
        Assert.Equal(ConnectState.Connected, CodexHooks.State(first.Text));
        Assert.Equal(ConnectState.Connected, CodexHooks.State(CodexHooks.Connect(first.Text, NotifyPath).Text));
    }

    [Fact]
    public void Codex_Connect_Through_The_Alias_Writes_The_Alias_Quoted()
    {
        var edit = CodexHooks.Connect("", PackageNames.NotifyAlias, viaAlias: true);

        Assert.True(edit.Changed);
        Assert.Equal($"\"{PackageNames.NotifyAlias}\" --agent codex", (string)Groups(edit.Text, "Stop")[0]!["hooks"]![0]!["command"]!);
        Assert.Equal(ConnectState.Connected, CodexHooks.State(edit.Text));
        Assert.Equal("NOTIFY_PATH_INVALID", CodexHooks.Connect("", NotifyPath, viaAlias: true).Reason); // a path is not the alias
    }

    [Theory]
    [InlineData(@"Island.Notify.exe")] // relative
    [InlineData(@"\\server\share\Island.Notify.exe")] // network
    [InlineData(@" Q:\Invented\Island.Notify.exe")] // blanks round it
    [InlineData(@"Q:\Invented\Other.exe")] // not our program
    [InlineData("Q:\\Invented\\a\"b\\Island.Notify.exe")] // a quote would end the quoting
    [InlineData(@"Q:\Invented\%PATH%\Island.Notify.exe")] // a shell would expand it
    [InlineData(@"Q:\Invented\$x\Island.Notify.exe")]
    [InlineData("Q:\\Invented\\a`b\\Island.Notify.exe")]
    [InlineData("Q:\\Invented\\a\nb\\Island.Notify.exe")]
    public void Codex_Connect_Refuses_A_Command_It_Cannot_Quote_Safely(string command)
    {
        var edit = CodexHooks.Connect(Existing, command);

        Assert.False(edit.Changed);
        Assert.Equal("NOTIFY_PATH_INVALID", edit.Reason);
        Assert.Equal(Existing, edit.Text);
        Assert.Empty(CodexHooks.LinesToAdd(command));
    }

    [Fact]
    public void Codex_Disconnect_Removes_Only_What_Runs_Island_Notify()
    {
        // Ours in four forms (shell form quoted, shell form unquoted, PowerShell call form, exec form with args), beside someone else's handler in one group;
        // lookalikes and other programs that merely mention Island.Notify or --agent codex stay.
        const string mixed = """
            {
              "hooks": {
                "Stop": [
                  { "hooks": [ { "type": "command", "command": "\"Q:\\Invented\\Alpha\\notify\\Island.Notify.exe\" --agent codex", "async": true }, { "type": "command", "command": "beta.exe" } ] },
                  { "hooks": [ { "type": "command", "command": "Island.Notify2.exe --agent codex" } ] }
                ],
                "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "Q:\\Invented\\Island.Notify.exe --agent codex" } ] } ],
                "PostToolUse": [ { "hooks": [ { "type": "command", "command": "& \"Q:\\Invented\\Island.Notify.exe\" --agent codex" } ] } ],
                "Interrupt": [ { "hooks": [ { "type": "command", "command": "Q:\\Invented\\Island.Notify.exe", "args": ["--agent", "codex"] } ] } ],
                "PermissionRequest": [ { "hooks": [ { "type": "command", "command": "python Q:\\Invented\\Island.Notify.exe --agent codex" } ] } ],
                "SessionStart": [ { "hooks": [ { "type": "command", "command": "gamma.exe --agent codex" } ] } ]
              }
            }
            """;

        var edit = CodexHooks.Disconnect(mixed);

        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
        var hooks = Parse(edit.Text)["hooks"]!.AsObject();
        Assert.Equal(["Stop", "PermissionRequest", "SessionStart"], hooks.Select(p => p.Key)); // lists we emptied are gone, the others stay in place
        var stop = (JsonArray)hooks["Stop"]!;
        Assert.Equal(2, stop.Count);
        Assert.Equal(["beta.exe"], ((JsonArray)stop[0]!["hooks"]!).Select(h => (string)h!["command"]!));
        Assert.Equal("Island.Notify2.exe --agent codex", (string)stop[1]!["hooks"]![0]!["command"]!);
        Assert.Equal("python Q:\\Invented\\Island.Notify.exe --agent codex", (string)hooks["PermissionRequest"]![0]!["hooks"]![0]!["command"]!);
        Assert.Equal(ConnectState.NotConnected, CodexHooks.State(edit.Text));
        Assert.False(CodexHooks.Disconnect(edit.Text).Changed); // nothing left to take out
    }

    [Fact]
    public void Codex_Disconnect_Takes_Out_The_Hooks_Object_It_Made_And_Leaves_The_Rest()
    {
        var connected = CodexHooks.Connect("""{ "description": "Alpha" }""", NotifyPath);
        var removed = CodexHooks.Disconnect(connected.Text);

        Assert.True(JsonNode.DeepEquals(Parse("""{ "description": "Alpha" }"""), Parse(removed.Text)));
        Assert.Empty(Parse(CodexHooks.Disconnect(CodexHooks.Connect("", NotifyPath).Text).Text).AsObject()); // an empty object, no hooks key
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{ \"hooks\": [] }")]
    [InlineData("{ \"hooks\": 7 }")]
    [InlineData("{ \"a\": 1, \"a\": 2 }")] // a repeated property name
    [InlineData("{ // a comment\n \"hooks\": {} }")]
    [InlineData("{ \"hooks\": {}, }")] // a trailing comma
    [InlineData("{ \"hooks\": { \"Stop\": { \"hooks\": [] } } }")] // an event that is not a list
    [InlineData("{ \"hooks\": { \"Interrupt\": \"x\" } }")]
    [InlineData("[hooks]\nStop = 1")] // the other file Codex reads, which is not JSON
    public void Codex_An_Unreadable_File_Is_Left_Alone(string text)
    {
        var connect = CodexHooks.Connect(text, NotifyPath);
        var disconnect = CodexHooks.Disconnect(text);

        Assert.False(connect.Changed);
        Assert.Equal(text, connect.Text);
        Assert.Equal("HOOKS_FILE_UNREADABLE", connect.Reason);
        Assert.Equal(HookInstaller.FileUnreadable, connect.Reason);
        Assert.Equal(text, disconnect.Text);
        Assert.False(disconnect.Changed);
        // Disconnect has nothing to say about an event that is not a list unless it is one it must read; the file is never written either way.
        Assert.True(disconnect.Reason is null or "HOOKS_FILE_UNREADABLE");
        Assert.NotEqual(ConnectState.Connected, CodexHooks.State(text));
    }

    [Fact]
    public void Codex_An_Unreadable_File_Is_Reported_As_Unreadable_By_State()
    {
        Assert.Equal(ConnectState.Unreadable, CodexHooks.State("{ not json"));
        Assert.Equal(ConnectState.Unreadable, CodexHooks.State("[]"));
        Assert.Equal(ConnectState.Unreadable, CodexHooks.State("{ \"hooks\": 7 }"));
    }

    [Fact]
    public void Codex_Everything_Else_In_The_File_Is_Kept_In_Its_Order()
    {
        var edit = CodexHooks.Connect(Existing, NotifyPath);

        var root = Parse(edit.Text).AsObject();
        Assert.Equal(["description", "numbers", "note", "hooks", "zeta"], root.Select(p => p.Key));
        Assert.Contains("naïve café 日本", edit.Text); // not turned into escapes
        Assert.Contains("12345678901234567890", edit.Text); // numbers keep their text
        Assert.Contains("2.50", edit.Text);
        var hooks = root["hooks"]!.AsObject();
        Assert.Equal(["PreToolUse", "Stop", "Custom", .. Written.Where(w => w != "Stop")], hooks.Select(p => p.Key)); // theirs keep their places, ours go after
        Assert.True(JsonNode.DeepEquals(Parse(Existing)["hooks"]!["PreToolUse"], hooks["PreToolUse"]));
        Assert.Equal(7, (int)hooks["Custom"]!);
        var stop = (JsonArray)hooks["Stop"]!;
        Assert.True(JsonNode.DeepEquals(Parse(Existing)["hooks"]!["Stop"]![0], stop[0])); // theirs first, untouched
        Assert.True((bool)root["zeta"]!);

        // Taking ours out again gives their file back, in its order.
        var back = CodexHooks.Disconnect(edit.Text);
        Assert.True(JsonNode.DeepEquals(Parse(Existing), Parse(back.Text)));
        Assert.Equal(["description", "numbers", "note", "hooks", "zeta"], Parse(back.Text).AsObject().Select(p => p.Key));
        Assert.Equal(["PreToolUse", "Stop", "Custom"], Parse(back.Text)["hooks"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Codex_A_File_With_A_Byte_Order_Mark_Or_Nothing_Is_Handled()
    {
        var bom = ((char)0xFEFF).ToString();

        Assert.Equal(ConnectState.NotConnected, CodexHooks.State(bom));
        Assert.Equal(ConnectState.NotConnected, CodexHooks.State("  \n"));
        Assert.True(CodexHooks.Connect(bom, NotifyPath).Changed);
        Assert.False(CodexHooks.Disconnect(bom).Changed);
        Assert.Null(CodexHooks.Disconnect(bom).Reason);
        var withMark = CodexHooks.Connect(bom + Existing, NotifyPath);
        Assert.True(withMark.Changed);
        Assert.Equal(Written.Length, OursIn(withMark.Text));
        Assert.True(JsonNode.DeepEquals(Parse(Existing), Parse(CodexHooks.Disconnect(bom + withMark.Text).Text)));
        Assert.False(CodexHooks.Disconnect(null).Changed);
    }

    [Fact]
    public void Codex_Line_Endings_Are_Kept()
    {
        var lf = Existing.Replace("\r\n", "\n"); // the source file's own line endings do not matter
        var crlf = lf.Replace("\n", "\r\n");

        var edit = CodexHooks.Connect(crlf, NotifyPath);

        Assert.DoesNotContain("\n", edit.Text.Replace("\r\n", ""));
        Assert.EndsWith("\r\n", edit.Text);
        Assert.DoesNotContain("\r\n", CodexHooks.Connect(lf, NotifyPath).Text);
    }

    [Fact]
    public void Codex_State_Says_Older_When_Only_Some_Entries_Are_There()
    {
        var full = CodexHooks.Connect(Existing, NotifyPath).Text;
        var withoutStop = CodexHooks.Disconnect(full); // all of ours out
        var one = Parse(Existing);
        one["hooks"]!["Stop"]!.AsArray().Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = OurCommand }) });
        var someText = one.ToJsonString();
        var anotherEvent = Parse(Existing);
        anotherEvent["hooks"]!["SessionEnd"] = Parse("""[ { "hooks": [ { "type": "command", "command": "Q:\\Invented\\Island.Notify.exe --agent codex" } ] } ]""");

        Assert.Equal(ConnectState.NotConnected, CodexHooks.State(withoutStop.Text));
        Assert.Equal(ConnectState.ConnectedOlder, CodexHooks.State(someText));
        Assert.Equal(ConnectState.ConnectedOlder, CodexHooks.State(anotherEvent.ToJsonString())); // one entry, and not for today's events
        Assert.Equal(ConnectState.Connected, CodexHooks.State(full));

        // Connect fills in only what is missing, and removal still takes out every one.
        var updated = CodexHooks.Connect(someText, NotifyPath);
        Assert.True(updated.Changed);
        Assert.Equal(ConnectState.Connected, CodexHooks.State(updated.Text));
        Assert.Equal(Written.Length, OursIn(updated.Text)); // the Stop entry already there was not doubled
    }

    [Fact]
    public void Codex_The_Question_Shows_Exactly_What_Is_Added_And_The_Trust_Sentence()
    {
        var lines = CodexHooks.LinesToAdd(NotifyPath);
        var shown = string.Join("\n", lines);
        var edit = CodexHooks.Connect("", NotifyPath);

        Assert.Equal("{", lines[0]);
        Assert.Equal("}", lines[^1]);
        Assert.True(JsonNode.DeepEquals(Parse(shown), Parse(edit.Text))); // the lines are what an empty file would hold
        Assert.Contains(OurCommand.Replace("\\", "\\\\").Replace("\"", "\\\""), shown);
        Assert.Contains("\"async\": true", shown);
        Assert.Equal(@"%USERPROFILE%\.codex\hooks.json", CodexHooks.PlaceText); // named, never expanded
        Assert.DoesNotContain("Users\\", CodexHooks.PlaceText);
        Assert.Contains("trust", CodexHooks.TrustSentence);
        Assert.Contains("/hooks", CodexHooks.TrustSentence);
        Assert.Equal(1, CodexHooks.TrustSentence.Count(c => c == '.')); // one sentence
    }

    [Fact]
    public void Codex_Over_A_Hundred_Random_Files_Ours_Go_In_Once_And_Come_Out_Leaving_Theirs()
    {
        var random = new Random(20261007);
        for (var i = 0; i < 100; i++)
        {
            var original = RandomFile(random, i % 2 == 0);
            var text = original.ToJsonString(new() { WriteIndented = i % 3 == 0 });

            var connected = CodexHooks.Connect(text, NotifyPath);
            var again = CodexHooks.Connect(connected.Text, NotifyPath);
            var back = CodexHooks.Disconnect(connected.Text);

            Assert.True(connected.Changed, $"file {i}");
            Assert.Null(connected.Reason);
            Assert.False(again.Changed, $"file {i}");
            Assert.Equal(ConnectState.Connected, CodexHooks.State(connected.Text));
            Assert.Equal(Written.Length, OursIn(connected.Text)); // each exactly once
            Assert.Equal(original.Select(p => p.Key).Where(k => k != "hooks"), Parse(connected.Text).AsObject().Select(p => p.Key).Where(k => k != "hooks"));
            Assert.True(JsonNode.DeepEquals(original, Parse(back.Text)), $"file {i}");
            Assert.Equal(original.Select(p => p.Key), Parse(back.Text).AsObject().Select(p => p.Key)); // their order
            if (original["hooks"] is JsonObject theirs)
                foreach (var (name, groups) in theirs) // their groups come first in every list, untouched
                    for (var g = 0; g < ((JsonArray)groups!).Count; g++)
                        Assert.True(JsonNode.DeepEquals(((JsonArray)groups)[g], Groups(connected.Text, name)[g]), $"file {i} {name}");
        }
    }

    private static JsonObject RandomFile(Random random, bool withHooks)
    {
        var root = new JsonObject();
        var extras = random.Next(0, 4);
        for (var e = 0; e < extras; e++) root[$"key{e}"] = random.Next(3) switch { 0 => random.Next(), 1 => (JsonNode?)$"text {random.Next()} é", _ => new JsonArray(random.Next(), true, null) };

        if (withHooks)
        {
            var hooks = new JsonObject();
            string[] pool = ["PreToolUse", "Stop", "Interrupt", "PermissionRequest", "Beta", "SessionStart", "PostToolUse", "SubagentStop"];
            foreach (var name in pool.OrderBy(_ => random.Next()).Take(random.Next(1, 5)))
            {
                var list = new JsonArray();
                for (var g = random.Next(1, 3); g > 0; g--)
                {
                    var group = new JsonObject();
                    if (random.Next(2) == 0) group["matcher"] = "Bash";
                    group["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = random.Next(2) == 0 ? "beta.exe --x" : "python C:\\Invented\\Alpha.py", ["timeout"] = random.Next(1, 9) });
                    list.Add(group);
                }

                hooks[name] = list;
            }

            root.Insert(random.Next(0, root.Count + 1), "hooks", hooks);
        }

        return root;
    }
}
