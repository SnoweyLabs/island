using System.Text;
using System.Text.Json.Nodes;
using Island.Core;
using Island.Core.Agents.Connect;

namespace Island.Redteam.Code.Tests;

/// <summary>WORK-ORDER-12 section 4, CODE: the text edits made into other programs' settings (HookInstaller for Claude Code, CodexHooks for Codex): files that are not JSON, a byte-order mark, huge, deep, odd shapes. Text in, text out: no file is touched.</summary>
public class InstallerEdgeTests
{
    private const string Notify = "C:\\Users\\Invented\\AppData\\Local\\Island\\notify\\Island.Notify.exe";

    private static readonly string[] Texts =
    [
        "", " ", "\uFEFF", "{}", "[]", "null", "7", "\"x\"", "{", "{\"hooks\": null}", "{\"hooks\": []}", "{\"hooks\": {}}", "{\"hooks\": {\"Stop\": null}}", "{\"hooks\": {\"Stop\": {}}}",
        "{\"hooks\": {\"Stop\": [null, 1, \"x\", [], {\"hooks\": null}, {\"hooks\": [null, 3]}]}}", "{\"hooks\": {\"Stop\": []}, \"hooks\": {}}", "// c\n{}", "{\"a\":1,}", "{\"env\": {\"TOKEN\": \"invented\"}, \"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"echo\"}]}]}}",
        "\uFEFF{\"model\": \"x\"}", "{\"model\": \"x\"}\r\n", "{\"s\": \"\\ud800\"}", new string('[', 2000) + new string(']', 2000),
        "{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"\\\"C:\\\\x\\\\Island.Notify.exe\\\" --agent claude\"}]}]}}",
    ];

    [Fact]
    public void Both_Installers_Give_A_Plain_Answer_For_Every_Odd_Text_And_Unreadable_Text_Comes_Back_Unchanged()
    {
        foreach (var text in Texts)
        {
            foreach (var edit in new[] { HookInstaller.Connect(text, Notify), HookInstaller.Disconnect(text), CodexHooks.Connect(text, Notify), CodexHooks.Disconnect(text) })
            {
                if (edit.Reason is not null) Assert.Equal((text, false), (edit.Text, edit.Changed));
                if (edit.Changed) Assert.NotNull(JsonNode.Parse(edit.Text.TrimStart('\uFEFF')));
            }

            Assert.Null(Record.Exception(() => HookInstaller.StateOf(text)));
            Assert.Null(Record.Exception(() => CodexHooks.State(text)));
        }
    }

    [Fact]
    public void Connect_Then_Disconnect_Keeps_Every_Other_Entry_Key_And_Value_Of_A_Users_File()
    {
        var user = "{\"env\": {\"TOKEN\": \"invented\"}, \"permissions\": {\"allow\": [\"Bash(ls)\"]}, \"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"echo done\"}]}], \"PreToolUse\": [{\"matcher\": \"Bash\", \"hooks\": [{\"type\": \"command\", \"command\": \"check\"}]}]}}";
        foreach (var (connect, disconnect) in new (Func<string, HookEdit>, Func<string, HookEdit>)[] { (t => HookInstaller.Connect(t, Notify), HookInstaller.Disconnect), (t => CodexHooks.Connect(t, Notify), CodexHooks.Disconnect) })
        {
            var on = connect(user);
            Assert.True(on.Changed);
            Assert.Equal(on.Text, connect(on.Text).Text); // nothing is added twice
            var off = disconnect(on.Text);
            Assert.True(off.Changed);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(user), JsonNode.Parse(off.Text)), "the user's own entries came back different");
        }
    }

    [Fact]
    public void A_Very_Large_Settings_File_Is_Edited_In_Reasonable_Time_And_Keeps_Its_Content()
    {
        var sb = new StringBuilder("{\"notes\": [");
        for (var i = 0; i < 200_000; i++) sb.Append(i == 0 ? "" : ",").Append("{\"k\":").Append(i).Append(",\"v\":\"invented text ").Append(i).Append("\"}");
        sb.Append("]}");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var on = HookInstaller.Connect(sb.ToString(), Notify);
        Assert.True(on.Changed && on.Text.Contains("invented text 199999"));
        Assert.True(HookInstaller.Disconnect(on.Text).Changed);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
    }

    [Theory]
    [InlineData("C:\\Users\\Invented\\Island.Notify.exe\\..\\evil.exe")]
    [InlineData("\\\\server\\share\\Island.Notify.exe")]
    [InlineData("relative\\Island.Notify.exe")]
    [InlineData(" C:\\x\\Island.Notify.exe")]
    [InlineData("C:\\x\\Island.Notify.exe ")]
    [InlineData("C:\\x\\Other.exe")]
    [InlineData("")]
    public void A_Command_That_Is_Not_The_Islands_Own_Notify_Is_Never_Written(string command)
    {
        // "..\\evil.exe" ends in another program's name; the others are not absolute, not local or not the program's name.
        Assert.False(HookInstaller.Connect("{}", command).Changed);
        Assert.False(CodexHooks.Connect("{}", command).Changed);
    }

    [Fact]
    public void Defect_A_Notify_Path_The_Installer_Refuses_Is_Reported_As_A_Settings_File_That_Could_Not_Be_Read()
    {
        // code-1-17 (LOW): HookInstaller.Connect and CodexHooks.Connect return Reason NOTIFY_PATH_INVALID for a command that is not an absolute local path of Island.Notify (Codex's also refuses
        // a quote, %, $, a backtick or a control character, because its entry is one shell line: a profile folder whose name holds a "%" or "$" gives such a path). OutsideAgentConnector and
        // OutsideCodexConnector turn ANY Reason into AgentRefusals.HooksFileUnreadable ("Claude Code's settings file could not be read, so Island did not connect ... Open the file, fix it"),
        // which is false for this reason: the file was never read. Expected: the refusal names the program's place (a refusal with its three parts, or the existing NOTIFY_MISSING), not the file.
        var claude = Repo.Text("src", "Island.App", "OutsideClaudeSettings.cs");
        var codex = Repo.Text("src", "Island.App", "OutsideCodexSettings.cs");
        Assert.True(claude.Contains("NotifyPathInvalid") && codex.Contains("NotifyPathInvalid"), "a refused path and an unreadable file are told as one thing");
        const string odd = @"C:\Users\Inv%ented\Island.Notify.exe";
        var reasonExists = HookInstaller.Connect("{}", odd).Reason == HookInstaller.NotifyPathInvalid || CodexHooks.Connect("{}", odd).Reason == HookInstaller.NotifyPathInvalid;
        Assert.True(reasonExists); // the reason exists in Core; the app joins it with the unreadable-file text
    }
}
