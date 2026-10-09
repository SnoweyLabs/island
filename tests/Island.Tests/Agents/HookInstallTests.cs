using System.Text.Json.Nodes;
using Island.Core;

namespace Island.Tests;

public class HookInstallTests
{
    private const string NotifyPath = @"C:\Apps\Alpha\notify\Island.Notify.exe";

    private const string Existing = """
        {
          "model": "alpha",
          "numbers": [1, 2.50, 12345678901234567890],
          "note": "naïve café 日本",
          "hooks": {
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "check.exe" } ] } ],
            "Stop": [ { "hooks": [ { "type": "command", "command": "beta.exe", "args": ["--x"] } ] } ],
            "Custom": 7
          },
          "zeta": true
        }
        """;

    private static JsonNode Parse(string text) => JsonNode.Parse(text)!;

    private static JsonArray Groups(string text, string eventName) => (JsonArray)Parse(text)["hooks"]![eventName]!;

    [Fact]
    public void Existing_Hooks_Are_Kept()
    {
        var edit = HookInstaller.Connect(Existing, NotifyPath);

        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
        var root = Parse(edit.Text).AsObject();
        Assert.Equal(["model", "numbers", "note", "hooks", "zeta"], root.Select(p => p.Key));
        Assert.Contains("naïve café 日本", edit.Text); // not turned into escapes
        Assert.Contains("12345678901234567890", edit.Text); // numbers keep their text
        var hooks = root["hooks"]!.AsObject();
        Assert.Equal(["PreToolUse", "Stop", "Custom", "SessionStart", "UserPromptSubmit", "PostToolUse", "SessionEnd", "Notification"], hooks.Select(p => p.Key)); // theirs first, then the events of today's set that were missing
        Assert.True(JsonNode.DeepEquals(Parse(Existing)["hooks"]!["PreToolUse"], hooks["PreToolUse"]));
        Assert.Equal(7, (int)hooks["Custom"]!);

        var stop = Groups(edit.Text, "Stop");
        Assert.Equal(2, stop.Count);
        Assert.Equal("beta.exe", (string)stop[0]!["hooks"]![0]!["command"]!); // theirs first, untouched
        var ours = stop[1]!["hooks"]![0]!;
        Assert.Equal("command", (string)ours["type"]!);
        Assert.Equal(NotifyPath, (string)ours["command"]!);
        Assert.Equal(["--agent", "claude"], ((JsonArray)ours["args"]!).Select(a => (string)a!));
        Assert.True((bool)ours["async"]!);
        Assert.Null(stop[1]!["matcher"]); // Stop takes no matcher

        var note = Groups(edit.Text, "Notification");
        Assert.Equal(2, note.Count); // the two kinds of Notification, each in a group of its own with its matcher
        Assert.Equal("permission_prompt", (string)note[0]!["matcher"]!);
        Assert.Equal("idle_prompt", (string)note[1]!["matcher"]!);
        Assert.Equal(NotifyPath, (string)note[0]!["hooks"]![0]!["command"]!);
    }

    [Fact]
    public void Installing_Twice_Adds_Nothing()
    {
        var first = HookInstaller.Connect(Existing, NotifyPath);
        var second = HookInstaller.Connect(first.Text, NotifyPath);

        Assert.False(second.Changed);
        Assert.Null(second.Reason);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(2, Groups(second.Text, "Stop").Count);
        Assert.True(HookInstaller.IsConnected(second.Text));
    }

    [Fact]
    public void Disconnect_Removes_Only_Ours()
    {
        // Ours sits beside someone else's handler in one group, and a lookalike program name stays.
        const string mixed = """
            {
              "hooks": {
                "Stop": [
                  { "hooks": [ { "type": "command", "command": "beta.exe" }, { "type": "command", "command": "C:\\Other\\Island.Notify.exe", "args": [], "async": true } ] },
                  { "hooks": [ { "type": "command", "command": "C:\\Other\\Island.Notify.Smoke.exe", "args": [] } ] }
                ],
                "Notification": [ { "matcher": "permission_prompt", "hooks": [ { "type": "command", "command": "\"C:\\Program Files\\Alpha\\island.notify.EXE\" --a" } ] } ],
                "SessionStart": [ { "hooks": [ { "type": "command", "command": "gamma.exe" } ] } ]
              },
              "keep": 1
            }
            """;

        var edit = HookInstaller.Disconnect(mixed);

        Assert.True(edit.Changed);
        var after = Parse(edit.Text);
        Assert.Equal(1, (int)after["keep"]!);
        var stop = (JsonArray)after["hooks"]!["Stop"]!;
        Assert.Equal(2, stop.Count);
        Assert.Single((JsonArray)stop[0]!["hooks"]!);
        Assert.Equal("beta.exe", (string)stop[0]!["hooks"]![0]!["command"]!);
        Assert.Contains("Island.Notify.Smoke.exe", edit.Text);
        Assert.Null(after["hooks"]!["Notification"]); // its only entry was ours
        Assert.NotNull(after["hooks"]!["SessionStart"]);
        Assert.False(HookInstaller.IsConnected(edit.Text));
    }

    [Fact]
    public void Disconnect_After_Connect_Gives_Back_The_Original_Meaning()
    {
        var connected = HookInstaller.Connect(Existing, NotifyPath);
        var back = HookInstaller.Disconnect(connected.Text);

        Assert.True(back.Changed);
        Assert.True(JsonNode.DeepEquals(Parse(Existing), Parse(back.Text)));
        Assert.False(HookInstaller.Disconnect(back.Text).Changed);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    [InlineData("""{"hooks": []}""")]
    [InlineData("""{"hooks": null}""")]
    [InlineData("""{"a": 1, "a": 2}""")]
    [InlineData("{ // a comment\n \"a\": 1 }")]
    [InlineData("""{"a": 1} trailing""")]
    public void Unreadable_Text_Is_Returned_Unchanged_With_A_Reason(string text)
    {
        var connect = HookInstaller.Connect(text, NotifyPath);
        var disconnect = HookInstaller.Disconnect(text);

        Assert.Equal(text, connect.Text);
        Assert.False(connect.Changed);
        Assert.Equal("HOOKS_FILE_UNREADABLE", connect.Reason);
        Assert.Equal(text, disconnect.Text);
        Assert.False(disconnect.Changed);
        Assert.Equal("HOOKS_FILE_UNREADABLE", disconnect.Reason);
        Assert.False(HookInstaller.IsConnected(text));
    }

    [Theory]
    [InlineData("""{"hooks": {"Stop": [], "Notification": {}}}""")]
    [InlineData("""{"hooks": {"Stop": {}}}""")]
    [InlineData("""{"hooks": {"Notification": "x"}}""")]
    public void An_Event_That_Is_Not_A_List_Makes_Connecting_Refuse_And_Leaves_Nothing_Half_Done(string text)
    {
        var edit = HookInstaller.Connect(text, NotifyPath);

        Assert.Equal(text, edit.Text);
        Assert.False(edit.Changed);
        Assert.Equal(HookInstaller.FileUnreadable, edit.Reason);
        Assert.Equal(text, HookInstaller.Disconnect(text).Text); // nothing of ours to remove, nothing touched
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Empty_Text_Becomes_Only_Our_Hooks(string text)
    {
        var edit = HookInstaller.Connect(text, NotifyPath);

        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
        var root = Parse(edit.Text).AsObject();
        Assert.Equal(["hooks"], root.Select(p => p.Key));
        Assert.Equal(["SessionStart", "UserPromptSubmit", "PostToolUse", "Stop", "SessionEnd", "Notification"], root["hooks"]!.AsObject().Select(p => p.Key));
        Assert.Equal(text, HookInstaller.Disconnect(text).Text);
    }

    [Fact]
    public void An_Object_With_No_Hooks_Gets_Them_Beside_Its_Other_Keys()
    {
        var edit = HookInstaller.Connect("""{"theme": "dark"}""", NotifyPath);

        var root = Parse(edit.Text).AsObject();
        Assert.Equal(["theme", "hooks"], root.Select(p => p.Key));
    }

    [Fact]
    public void A_Path_That_Is_Not_Our_Program_Is_Refused()
    {
        var edit = HookInstaller.Connect("{}", @"C:\Apps\Alpha\other.exe");

        Assert.Equal("{}", edit.Text);
        Assert.False(edit.Changed);
        Assert.Equal("NOTIFY_PATH_INVALID", edit.Reason);
    }

    [Fact]
    public void Line_Endings_Of_The_File_Are_Kept()
    {
        var crlf = "{\r\n  \"a\": 1\r\n}\r\n";

        var edit = HookInstaller.Connect(crlf, NotifyPath);

        Assert.DoesNotContain("\n", edit.Text.Replace("\r\n", ""));
        Assert.Contains("\r\n", edit.Text);
    }

    [Fact]
    public void A_Byte_Order_Mark_Does_Not_Make_The_Text_Unreadable()
    {
        var edit = HookInstaller.Connect(((char)0xFEFF) + "{}", NotifyPath);

        Assert.True(edit.Changed);
        Assert.Null(edit.Reason);
    }

    [Fact]
    public void The_Lines_To_Show_Are_The_Entries_Written()
    {
        var lines = HookInstaller.LinesToAdd(NotifyPath);
        var shown = string.Join("\n", lines);

        Assert.Contains("\"permission_prompt\"", shown);
        Assert.Contains("\"async\": true", shown);
        Assert.True(JsonNode.DeepEquals(Parse(shown)["hooks"], Parse(HookInstaller.Connect("{}", NotifyPath).Text)["hooks"]));
    }

    // What the connection made before WORK-ORDER-11 wrote: Stop, and Notification for permission_prompt, started with no arguments.
    private const string OldEntries = """
        { "hooks": {
          "Stop": [ { "hooks": [ { "type": "command", "command": "C:\\Apps\\Alpha\\notify\\Island.Notify.exe", "args": [], "async": true } ] } ],
          "Notification": [ { "matcher": "permission_prompt", "hooks": [ { "type": "command", "command": "C:\\Apps\\Alpha\\notify\\Island.Notify.exe", "args": [], "async": true } ] } ]
        } }
        """;

    [Fact]
    public void Old_Entries_Read_As_Connected_Older()
    {
        Assert.Equal(AgentConnection.ConnectedOlder, HookInstaller.StateOf(OldEntries));
        Assert.False(HookInstaller.IsConnected(OldEntries));
        Assert.Equal(AgentConnection.NotConnected, HookInstaller.StateOf(""));
        Assert.Equal(AgentConnection.NotConnected, HookInstaller.StateOf(Existing)); // someone else's hooks only
        Assert.Equal(AgentConnection.Unreadable, HookInstaller.StateOf("{ not json"));
        Assert.Equal(AgentConnection.Connected, HookInstaller.StateOf(HookInstaller.Connect("", NotifyPath).Text));
        Assert.Equal(AgentConnection.Connected, HookInstaller.StateOf(HookInstaller.Connect(OldEntries, NotifyPath).Text));
    }

    [Fact]
    public void Update_Adds_Only_What_Is_Missing()
    {
        var update = HookInstaller.Connect(OldEntries, NotifyPath);

        Assert.True(update.Changed);
        var root = Parse(update.Text);
        // The two old groups are exactly as they were (their arguments are left alone: nothing is repaired), and only the missing ones were added.
        Assert.Single(Groups(update.Text, "Stop"));
        Assert.Empty((JsonArray)Groups(update.Text, "Stop")[0]!["hooks"]![0]!["args"]!);
        var note = Groups(update.Text, "Notification");
        Assert.Equal(2, note.Count);
        Assert.Equal("permission_prompt", (string)note[0]!["matcher"]!);
        Assert.Equal("idle_prompt", (string)note[1]!["matcher"]!);
        Assert.Equal(["Stop", "Notification", "SessionStart", "UserPromptSubmit", "PostToolUse", "SessionEnd"], root["hooks"]!.AsObject().Select(p => p.Key));
        Assert.Equal(AgentConnection.Connected, HookInstaller.StateOf(update.Text));
        Assert.False(HookInstaller.Connect(update.Text, NotifyPath).Changed); // and nothing is added twice

        // The question shows every line of today's set, one list, never a shortened one.
        var lines = HookInstaller.LinesToAdd(NotifyPath);
        foreach (var name in HookInstaller.WrittenEvents) Assert.Contains(lines, l => l.Contains($"\"{name}\""));
        Assert.Equal(1, lines.Count(l => l.Contains("idle_prompt")));
        Assert.Contains(lines, l => l.Contains("\"--agent\""));
    }

    [Fact]
    public void Disconnect_Removes_Old_And_New_Entries_Alike()
    {
        // A file with the old entries, and a file made by today's Connect, and one with both (an update interrupted halfway): all come back clean.
        foreach (var text in new[] { OldEntries, HookInstaller.Connect("", NotifyPath).Text, HookInstaller.Connect(OldEntries, NotifyPath).Text, HookInstaller.Connect(Existing, NotifyPath).Text })
        {
            var edit = HookInstaller.Disconnect(text);
            Assert.True(edit.Changed);
            Assert.DoesNotContain("Island.Notify", edit.Text);
            Assert.Equal(AgentConnection.NotConnected, HookInstaller.StateOf(edit.Text));
        }

        // Theirs stay, in order.
        var back = Parse(HookInstaller.Disconnect(HookInstaller.Connect(Existing, NotifyPath).Text).Text);
        Assert.True(JsonNode.DeepEquals(Parse(Existing), back));
    }
}
