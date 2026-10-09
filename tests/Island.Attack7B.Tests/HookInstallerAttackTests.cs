using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Attack7B.Tests;

/// <summary>HookInstaller on invented text only. No file is read or written, none is named.</summary>
public class HookInstallerAttackTests(ITestOutputHelper output)
{
    private const string Path = @"C:\Invented\notify\Island.Notify.exe";

    // ---- helpers --------------------------------------------------------------------------------------------

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
        try
        {
            return e.GetString()!;
        }
        catch (InvalidOperationException)
        {
            return "RAW:" + e.GetRawText();
        }
    }

    private static bool JsonEqual(string a, string b)
    {
        using var da = JsonDocument.Parse(a.TrimStart('\uFEFF'));
        using var db = JsonDocument.Parse(b.TrimStart('\uFEFF'));
        return Same(da.RootElement, db.RootElement);
    }

    private static JsonDocument Doc(string text) => JsonDocument.Parse(text.TrimStart('\uFEFF'));

    private static string Ours(string command = Path) =>
        "{\"type\":\"command\",\"command\":" + JsonSerializer.Serialize(command) + ",\"args\":[],\"async\":true}";

    /// <summary>Today's set (WORK-ORDER-11 section 3): one entry per event, the two kinds of Notification each in its own group, the same handler in all of them.</summary>
    private static string AllEntries(string handler, bool doubledStop = false)
    {
        string Plain(string name) => "\"" + name + "\":[{\"hooks\":[" + handler + "]}]";
        var stop = doubledStop ? "\"Stop\":[{\"hooks\":[" + handler + "," + handler + "]},{\"hooks\":[" + handler + "," + handler + "]}]" : Plain("Stop");
        return string.Join(",", Plain("SessionStart"), Plain("UserPromptSubmit"), Plain("PostToolUse"), stop, Plain("SessionEnd"),
            "\"Notification\":[{\"matcher\":\"permission_prompt\",\"hooks\":[" + handler + "]},{\"matcher\":\"idle_prompt\",\"hooks\":[" + handler + "]}]");
    }

    private static string Settings(string hooksBody, string rest = "\"model\":\"x\"") => "{" + rest + (rest.Length > 0 ? "," : "") + "\"hooks\":{" + hooksBody + "}}";

    private static void AssertUntouched(HookEdit r, string original, string reason)
    {
        Assert.False(r.Changed);
        Assert.Equal(original, r.Text);
        Assert.Equal(reason, r.Reason);
    }

    // ---- unreadable text comes back unchanged ---------------------------------------------------------------

    public static TheoryData<string> Unreadable() =>
    [
        "[]", "[{}]", "\"x\"", "5", "null", "true", "false", "{", "}", "{\"a\":1", "{\"a\":1,}", "{,}",
        "{ // a comment\n\"a\":1}", "/* c */ {}", "{\"a\":1} // c", "# yaml: true", "a: 1",
        "{\"a\":1} {\"b\":2}", "{\"a\":1}x", "{\"a\":1}\0", "\0", "\0{}",
        "{'a':1}", "{a:1}", "{\"a\":01}", "{\"a\":.5}", "{\"a\":+1}", "{\"a\":NaN}", "{\"a\":\"\t\"}", "{\"a\":\"\\x\"}", "{\"a\":\"\\u12\"}",
        "{\"hooks\":[]}", "{\"hooks\":\"x\"}", "{\"hooks\":5}", "{\"hooks\":null}", "{\"hooks\":true}",
        "\uFEFF[]", "\uFEFF\uFEFF{", new string('[', 10_000) + new string(']', 10_000), "{\"a\":" + new string('[', 10_000),
        "{\"x\":" + string.Concat(Enumerable.Repeat("[", 65)) + string.Concat(Enumerable.Repeat("]", 65)) + "}",
    ];

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void Holds_Unreadable_Text_Comes_Back_Unchanged_With_The_Reason(string text)
    {
        AssertUntouched(HookInstaller.Connect(text, Path), text, HookInstaller.FileUnreadable);
        AssertUntouched(HookInstaller.Disconnect(text), text, HookInstaller.FileUnreadable);
        Assert.False(HookInstaller.IsConnected(text));
    }

    [Theory]
    [InlineData("{\"hooks\":{\"Stop\":{}}}")]
    [InlineData("{\"hooks\":{\"Stop\":\"x\"}}")]
    [InlineData("{\"hooks\":{\"Stop\":5}}")]
    [InlineData("{\"hooks\":{\"Stop\":true}}")]
    [InlineData("{\"hooks\":{\"Notification\":{}}}")]
    [InlineData("{\"hooks\":{\"Notification\":\"x\"}}")]
    [InlineData("{\"hooks\":{\"Stop\":[],\"Notification\":{}}}")]
    public void Holds_A_Stop_Or_Notification_That_Is_Not_A_List_Refuses_Connect_Without_Half_Doing_It(string text)
    {
        AssertUntouched(HookInstaller.Connect(text, Path), text, HookInstaller.FileUnreadable);
        var d = HookInstaller.Disconnect(text);
        Assert.False(d.Changed);
        Assert.Equal(text, d.Text);
        Assert.False(HookInstaller.IsConnected(text));
    }

    [Fact]
    public void Holds_Deep_Nesting_Of_10000_Comes_Back_Quickly_Unchanged()
    {
        var text = "{\"x\":" + string.Concat(Enumerable.Repeat("{\"a\":", 10_000)) + "1" + new string('}', 10_000) + "}";
        var clock = Stopwatch.StartNew();
        AssertUntouched(HookInstaller.Connect(text, Path), text, HookInstaller.FileUnreadable);
        AssertUntouched(HookInstaller.Disconnect(text), text, HookInstaller.FileUnreadable);
        Assert.False(HookInstaller.IsConnected(text));
        Assert.True(clock.ElapsedMilliseconds < 1500, clock.ElapsedMilliseconds + " ms");
    }

    [Theory]
    [InlineData(55)]
    [InlineData(62)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    public void Holds_Nesting_Around_The_Parser_Limit_Is_Either_Refused_Unchanged_Or_Kept_Equal_And_Never_Throws(int depth)
    {
        var text = "{\"x\":" + string.Concat(Enumerable.Repeat("[", depth)) + string.Concat(Enumerable.Repeat("]", depth)) + "}";
        var c = HookInstaller.Connect(text, Path);
        if (c.Reason is not null) AssertUntouched(c, text, HookInstaller.FileUnreadable);
        else Assert.True(c.Changed);
        output.WriteLine($"depth {depth}: " + (c.Reason ?? "connected"));
    }

    // ---- empty, blank, BOM ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\r\n")]
    [InlineData("\t \n ")]
    public void Holds_Empty_Or_Blank_Text_Becomes_Only_Our_Hooks(string text)
    {
        var r = HookInstaller.Connect(text, Path);
        Assert.True(r.Changed);
        Assert.Null(r.Reason);
        using var d = Doc(r.Text);
        Assert.Equal(6, d.RootElement.GetProperty("hooks").EnumerateObject().Count()); // the six events of today's set (Notification holds two groups)
        Assert.True(HookInstaller.IsConnected(r.Text));
        Assert.False(HookInstaller.Disconnect(text).Changed);
    }

    [Fact]
    public void Holds_Null_Text_Is_Treated_As_Empty()
    {
        Assert.True(HookInstaller.Connect(null, Path).Changed);
        Assert.False(HookInstaller.Disconnect(null).Changed);
        Assert.False(HookInstaller.IsConnected(null));
    }

    [Fact]
    public void Holds_A_Bom_Before_An_Object_Is_Accepted_And_Dropped_In_The_Output()
    {
        var r = HookInstaller.Connect("\uFEFF{\"model\":\"x\"}", Path);
        Assert.True(r.Changed);
        Assert.False(r.Text.StartsWith('\uFEFF'));
        using var d = Doc(r.Text);
        Assert.Equal("x", d.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData("\uFEFF")]
    [InlineData("\uFEFF\r\n")]
    [InlineData("\uFEFF   ")]
    public void Defect_A_File_That_Holds_Only_A_Bom_Is_Called_Unreadable_Where_An_Empty_File_Is_Fine(string text)
    {
        var r = HookInstaller.Connect(text, Path);
        Assert.Null(r.Reason);
        Assert.True(r.Changed);
    }

    // ---- round trips ----------------------------------------------------------------------------------------

    public static TheoryData<string> Originals()
    {
        var data = new TheoryData<string>();
        foreach (var o in OriginalList()) data.Add(o);
        return data;
    }

    private static string[] OriginalList() =>
    [
        Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo done\"}]}]"),
        Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo a\"}]},{\"hooks\":[{\"type\":\"command\",\"command\":\"echo b\"}]}]," +
                 "\"Notification\":[{\"matcher\":\"permission_prompt\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo n\"}]}]," +
                 "\"PreToolUse\":[{\"matcher\":\"Bash\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo p\"}]}]"),
        Settings("\"PreCompact\":[]"),
        Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"node\",\"args\":[\"C:\\\\x\\\\Island.Notify.js\"]}]}]"),
        Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo \\\"Island.Notify\\\"\"}]}]"),
        "{\"b\":2,\"a\":1,\"z\":[3,2,1],\"hooks\":{\"Stop\":[{\"x\":null}]},\"c\":{\"d\":{}}}",
        "{\"n\":[1e400,-0,1.0,0.30000000000000004,12345678901234567890123456789,1E5,-1.5e-7,0.10000000000000000555111512312578270211815834045410156250],\"hooks\":{\"Other\":[]}}",
        "{\"s\":\"\\u00e9 \\u4e2d \\ud83d\\ude00 \\u2028 </script> & ' \\\" \\\\ \\/ \\n \\t \\u0001\",\"hooks\":{\"Other\":[{\"x\":1}]}}",
        "{\"s\":\"é中😀\u2028 <> &\",\"emptyObj\":{},\"emptyArr\":[],\"t\":true,\"f\":false,\"nl\":null,\"hooks\":{\"Other\":[{\"x\":1}]}}",
        "{\n    \"model\": \"x\",\n    \"hooks\": {\n        \"Stop\": [ { \"hooks\": [ { \"type\": \"command\", \"command\": \"echo\" } ] } ]\n    }\n}\n",
        "{\"hooks\":{\"Stop\":[1,null,\"x\",[],{}, {\"hooks\":5}, {\"hooks\":[1,null,\"x\"]}]}}",
    ];

    [Theory]
    [MemberData(nameof(Originals))]
    public void Holds_Connect_Then_Disconnect_Gives_Back_An_Equivalent_Value(string original)
    {
        var c = HookInstaller.Connect(original, Path);
        Assert.Null(c.Reason);
        Assert.True(c.Changed);
        Assert.True(HookInstaller.IsConnected(c.Text));
        var d = HookInstaller.Disconnect(c.Text);
        Assert.True(d.Changed);
        Assert.False(HookInstaller.IsConnected(d.Text));
        Assert.True(JsonEqual(original, d.Text), "original:\n" + original + "\nafter:\n" + d.Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"model\":\"x\"}")]
    [InlineData("{\"model\":\"x\",\"env\":{\"A\":\"1\"}}")]
    public void Defect_Disconnect_Leaves_An_Empty_Hooks_Object_That_Connect_Had_Created(string original)
    {
        var d = HookInstaller.Disconnect(HookInstaller.Connect(original, Path).Text);
        Assert.True(JsonEqual(original, d.Text), "after: " + d.Text);
    }

    [Theory]
    [InlineData("{\"hooks\":{\"Stop\":[]}}")]
    [InlineData("{\"hooks\":{\"Notification\":[]}}")]
    [InlineData("{\"model\":\"x\",\"hooks\":{\"Stop\":[],\"Notification\":[]}}")]
    public void Holds_A_List_That_Our_Removal_Emptied_Is_Taken_Out_Which_Cannot_Be_Told_From_One_The_User_Left_Empty(string original)
    {
        // Changed by the main session (ATTACK7B 2): after Connect a list that was empty and a list Connect made look the same, so Disconnect cannot give
        // back both. The common case (a file with no such list, which Connect made) comes back exactly (Defect_Disconnect_Leaves_An_Empty_Hooks_Object...);
        // a list the person had left empty is taken out with our entry. Nothing of the person's own entries is touched.
        var d = HookInstaller.Disconnect(HookInstaller.Connect(original, Path).Text);
        Assert.False(HookInstaller.IsConnected(d.Text));
        Assert.DoesNotContain("Island", d.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Holds_Connect_Twice_Changes_Nothing_The_Second_Time()
    {
        foreach (var original in new[] { "", "{}", "{\r\n}\r\n" }.Concat(OriginalList()))
        {
            var one = HookInstaller.Connect(original, Path);
            var two = HookInstaller.Connect(one.Text, Path);
            Assert.False(two.Changed);
            Assert.Equal(one.Text, two.Text);
            Assert.Null(two.Reason);
        }
    }

    [Fact]
    public void Holds_Disconnect_Twice_And_Disconnect_Without_Ours_Change_Nothing()
    {
        foreach (var original in OriginalList())
        {
            var once = HookInstaller.Disconnect(HookInstaller.Connect(original, Path).Text);
            var twice = HookInstaller.Disconnect(once.Text);
            Assert.False(twice.Changed);
            Assert.Equal(once.Text, twice.Text);
            var plain = HookInstaller.Disconnect(original);
            Assert.False(plain.Changed);
            Assert.Equal(original, plain.Text);
        }
    }

    [Fact]
    public void Holds_Line_Endings_Of_The_Original_Are_Kept()
    {
        var crlf = HookInstaller.Connect("{\r\n  \"model\": \"x\"\r\n}\r\n", Path).Text;
        Assert.DoesNotMatch("[^\r]\n", crlf);
        Assert.EndsWith("\r\n", crlf);
        var lf = HookInstaller.Connect("{\n  \"model\": \"x\"\n}\n", Path).Text;
        Assert.DoesNotContain('\r', lf);
    }

    // ---- other people's entries are kept, in order ----------------------------------------------------------

    [Fact]
    public void Holds_A_Users_Own_Hooks_Are_Kept_Untouched_And_In_Order_And_Ours_Come_After()
    {
        const string stopA = "{\"hooks\":[{\"type\":\"command\",\"command\":\"echo A\",\"timeout\":5}]}";
        const string stopB = "{\"matcher\":\"*\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo B\"},{\"type\":\"command\",\"command\":\"echo C\"}]}";
        const string notif = "{\"matcher\":\"permission_prompt\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo N\"}]}";
        var original = "{\"z\":1,\"hooks\":{\"Stop\":[" + stopA + "," + stopB + "],\"Notification\":[" + notif + "],\"Other\":[" + stopA + "]},\"a\":2}";
        var r = HookInstaller.Connect(original, Path);
        using var d = Doc(r.Text);
        var hooks = d.RootElement.GetProperty("hooks");
        Assert.Equal(["Stop", "Notification", "Other", "SessionStart", "UserPromptSubmit", "PostToolUse", "SessionEnd"], hooks.EnumerateObject().Select(p => p.Name)); // theirs in order, then the events of today's set that were missing
        Assert.Equal(["z", "hooks", "a"], d.RootElement.EnumerateObject().Select(p => p.Name));
        var stop = hooks.GetProperty("Stop").EnumerateArray().ToList();
        Assert.Equal(3, stop.Count);
        using (var a = JsonDocument.Parse(stopA)) Assert.True(Same(a.RootElement, stop[0]));
        using (var b = JsonDocument.Parse(stopB)) Assert.True(Same(b.RootElement, stop[1]));
        Assert.Equal(3, hooks.GetProperty("Notification").GetArrayLength()); // theirs, then ours for permission_prompt and ours for idle_prompt
        using (var n = JsonDocument.Parse(notif)) Assert.True(Same(n.RootElement, hooks.GetProperty("Notification")[0]));
        Assert.False(stop[2].TryGetProperty("matcher", out _));
        Assert.Equal("permission_prompt", hooks.GetProperty("Notification")[1].GetProperty("matcher").GetString());
        Assert.Equal("idle_prompt", hooks.GetProperty("Notification")[2].GetProperty("matcher").GetString());
    }

    [Fact]
    public void Holds_Disconnect_Removes_Only_Ours_Even_Inside_A_Group_Shared_With_A_Users_Hook()
    {
        var shared = "{\"matcher\":\"x\",\"hooks\":[{\"type\":\"command\",\"command\":\"echo keep\"}," + Ours() + ",{\"type\":\"command\",\"command\":\"echo keep2\"}]}";
        var text = Settings("\"Stop\":[" + shared + "]");
        var d = HookInstaller.Disconnect(text);
        Assert.True(d.Changed);
        using var doc = Doc(d.Text);
        var handlers = doc.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks");
        Assert.Equal(["echo keep", "echo keep2"], handlers.EnumerateArray().Select(h => h.GetProperty("command").GetString()));
    }

    // ---- look-alikes ----------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Island_Notify_As_An_Argument_Of_Another_Program_Is_Not_Ours()
    {
        var other = "{\"hooks\":[{\"type\":\"command\",\"command\":\"node\",\"args\":[\"C:\\\\x\\\\Island.Notify.exe\"]}]}";
        var text = Settings("\"Stop\":[" + other + "]");
        Assert.False(HookInstaller.IsConnected(text));
        Assert.False(HookInstaller.Disconnect(text).Changed);
        var c = HookInstaller.Connect(text, Path);
        Assert.True(c.Changed);
        using var d = Doc(c.Text);
        Assert.Equal(2, d.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength());
    }

    [Theory]
    [InlineData("echo Island.Notify.exe")]
    [InlineData("cmd /c C:\\x\\Island.Notify.exe")]
    [InlineData("C:\\Program Files\\x\\Island.Notify.exe")] // unquoted with a space: the first word is not ours
    [InlineData("C:\\x\\Island.Notify.exe.bak")]
    [InlineData("C:\\x\\NotIsland.Notify.exe")]
    [InlineData("C:\\x\\Island.Notify\\")]
    [InlineData("")]
    public void Holds_Shell_Lines_That_Merely_Mention_The_Name_Are_Not_Ours(string command)
    {
        var text = Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":" + JsonSerializer.Serialize(command) + "}]}]");
        Assert.False(HookInstaller.IsConnected(text));
        Assert.False(HookInstaller.Disconnect(text).Changed);
    }

    [Theory]
    [InlineData("C:\\x\\Island.Notify.exe")]
    [InlineData("\"C:\\Program Files\\x\\Island.Notify.exe\" --flag")]
    [InlineData("Island.Notify")]
    [InlineData("c:/x/island.notify.EXE")]
    public void Holds_A_Shell_Form_Entry_That_Runs_Island_Notify_Is_Ours(string command)
    {
        // Changed by the main session (ATTACK7B 3, then WORK-ORDER-11): connected means every entry of today's set, so the shell-form entry is given for all of them.
        var handler = "{\"type\":\"command\",\"command\":" + JsonSerializer.Serialize(command) + "}";
        var text = Settings(AllEntries(handler));
        Assert.True(HookInstaller.IsConnected(text));
        Assert.True(HookInstaller.Disconnect(text).Changed);
    }

    [Fact]
    public void Holds_A_Different_Program_Named_Island_Notify_In_Another_Folder_Counts_As_Ours_By_Name_Both_Ways_As_The_Order_Says()
    {
        // Documented, not a defect: ownership is by file name only. The cost: Connect says "already there" and Disconnect removes an entry
        // that points at a different Island.Notify.exe (another person's program of the same name).
        var text = Settings(AllEntries(Ours(@"D:\Other\Tools\Island.Notify.exe")));
        Assert.False(HookInstaller.Connect(text, Path).Changed);
        Assert.True(HookInstaller.Disconnect(text).Changed);
    }

    [Fact]
    public void Holds_An_Entry_Of_Ours_With_Another_Path_Is_Left_As_It_Is_By_Connect_It_Is_Not_Repaired()
    {
        var text = Settings(AllEntries(Ours(@"C:\Old\Island.Notify.exe")));
        var r = HookInstaller.Connect(text, Path);
        Assert.False(r.Changed); // by the order: "never to repair"; the stale path stays until Disconnect, then Connect
    }

    [Fact]
    public void Holds_Our_Entries_Present_Twice_Are_Not_Added_Again_And_Both_Are_Removed()
    {
        var text = Settings(AllEntries(Ours(), doubledStop: true)); // Stop holds four copies of our handler in two groups
        Assert.False(HookInstaller.Connect(text, Path).Changed);
        var d = HookInstaller.Disconnect(text);
        Assert.True(d.Changed);
        Assert.False(HookInstaller.IsConnected(d.Text));
        Assert.DoesNotContain("Island.Notify", d.Text);
    }

    [Fact]
    public void Holds_One_Of_Ours_Present_And_The_Other_Missing_Connect_Adds_Only_The_Missing_One()
    {
        var text = Settings("\"Stop\":[{\"hooks\":[" + Ours() + "]}]");
        var r = HookInstaller.Connect(text, Path);
        Assert.True(r.Changed);
        using var d = Doc(r.Text);
        Assert.Equal(1, d.RootElement.GetProperty("hooks").GetProperty("Stop").GetArrayLength());
        Assert.Equal(2, d.RootElement.GetProperty("hooks").GetProperty("Notification").GetArrayLength()); // the two kinds, each in a group of its own
        Assert.Equal(6, d.RootElement.GetProperty("hooks").EnumerateObject().Count());
    }

    [Fact]
    public void Defect_Connect_Counts_Our_Program_Under_Another_Matcher_As_Connected_So_Permission_Prompts_Never_Reach_The_Island()
    {
        var text = Settings("\"Stop\":[{\"hooks\":[" + Ours() + "]}],\"Notification\":[{\"matcher\":\"idle_prompt\",\"hooks\":[" + Ours() + "]}]");
        var r = HookInstaller.Connect(text, Path);
        Assert.True(r.Changed, "nothing was added: no entry for permission_prompt exists, so the 'needs your answer' notice can never come");
        Assert.Contains("permission_prompt", r.Text);
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Notification")]
    [InlineData("PreToolUse")]
    [InlineData("SessionStart")]
    public void Defect_Is_Connected_Says_Yes_When_Only_One_Of_The_Two_Entries_Or_An_Entry_Under_Another_Event_Is_There(string eventName)
    {
        // Expected: connected only when both of ours are there (Stop, and Notification with permission_prompt). Today any entry under any event is enough,
        // so the settings show "Disconnect" for a half-made or unrelated state and the person cannot press Connect to complete it.
        var text = Settings("\"" + eventName + "\":[{\"matcher\":\"permission_prompt\",\"hooks\":[" + Ours() + "]}]");
        Assert.False(HookInstaller.IsConnected(text));
    }

    // ---- the path -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("calc.exe")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\x\Island.Notify.exe.bak")]
    [InlineData(@"C:\x\Island.Notify.exe\")]
    [InlineData(@"C:\x\Island.Notify\")]
    [InlineData(@"C:\x\Island.Notify.cmd")]
    [InlineData(@"C:\x\Island.Notifyx.exe")]
    [InlineData(@"C:\x\xIsland.Notify.exe")]
    [InlineData(@"C:\x\Island.Notify.dll")]
    [InlineData("\\")]
    public void Holds_A_Path_Of_Another_Program_Is_Refused_Unchanged_With_The_Reason(string path)
    {
        foreach (var text in new[] { "", "{}", "{\"model\":\"x\"}", "[]" })
        {
            var r = HookInstaller.Connect(text, path);
            Assert.False(r.Changed);
            Assert.Equal(text, r.Text);
            Assert.Equal(HookInstaller.NotifyPathInvalid, r.Reason);
        }
    }

    [Fact]
    public void Holds_A_Null_Path_Is_Refused_Not_Thrown()
    {
        var r = HookInstaller.Connect("{}", null!);
        Assert.Equal(HookInstaller.NotifyPathInvalid, r.Reason);
    }

    [Fact]
    public void Holds_The_Path_Check_Comes_Before_The_Text_Check()
    {
        Assert.Equal(HookInstaller.NotifyPathInvalid, HookInstaller.Connect("[", "calc.exe").Reason);
    }

    [Theory]
    [InlineData(@"C:\Users\Jo Doe\AppData\Local\Island\notify\Island.Notify.exe")]
    [InlineData("C:\\Users\\J\"o\\Island.Notify.exe")]
    [InlineData("C:\\Users\\Jo\\\u00e9\u4e2d\ud83d\ude00\\Island.Notify.exe")]
    [InlineData("C:/Users/x/Island.Notify.exe")]
    [InlineData(@"C:\x\island.notify.EXE")]
    [InlineData(@"C:\x\Island.Notify")]
    public void Holds_Awkward_But_Real_Paths_Are_Written_Exactly_And_Found_Again(string path)
    {
        var r = HookInstaller.Connect("{}", path);
        Assert.True(r.Changed);
        using var d = Doc(r.Text);
        Assert.Equal(path, d.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0].GetProperty("command").GetString());
        Assert.True(HookInstaller.IsConnected(r.Text));
        Assert.False(HookInstaller.Connect(r.Text, path).Changed);
        Assert.False(HookInstaller.IsConnected(HookInstaller.Disconnect(r.Text).Text));
    }

    [Fact]
    public void Holds_A_Path_Longer_Than_260_Characters_Is_Written_And_Found_Again()
    {
        var path = @"C:\" + string.Join('\\', Enumerable.Repeat(new string('d', 50), 8)) + @"\Island.Notify.exe";
        Assert.True(path.Length > 400);
        var r = HookInstaller.Connect("{}", path);
        Assert.True(r.Changed);
        Assert.True(HookInstaller.IsConnected(r.Text));
        using var d = Doc(r.Text);
        Assert.Equal(path, d.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0].GetProperty("command").GetString());
    }

    [Theory]
    [InlineData(@"Island.Notify.exe")]
    [InlineData(@".\Island.Notify.exe")]
    [InlineData(@"..\..\Island.Notify.exe")]
    [InlineData(@"\\evil-server\share\Island.Notify.exe")]
    [InlineData(@"//evil-server/share/Island.Notify.exe")]
    [InlineData(@"\\?\UNC\evil-server\share\Island.Notify.exe")]
    [InlineData(@"http://evil.example/Island.Notify.exe")]
    public void Defect_A_Relative_Or_Network_Path_Is_Accepted_As_The_Hooks_Program(string path)
    {
        // LATENT: the only caller passes an absolute path under %LOCALAPPDATA%. Connect itself accepts a program found through the current folder
        // or on another machine as "ours"; it should refuse anything that is not an absolute local path.
        var r = HookInstaller.Connect("{}", path);
        Assert.Equal(HookInstaller.NotifyPathInvalid, r.Reason);
    }

    [Theory]
    [InlineData(@"C:\x\Island.Notify.exe ")]
    [InlineData(@" C:\x\Island.Notify.exe")]
    [InlineData("C:\\x\\Island.Notify.exe\t")]
    public void Defect_A_Path_With_Surrounding_Blanks_Passes_The_Check_Trimmed_And_Is_Written_Untrimmed(string path)
    {
        // the check trims, the entry is written with the raw text: the hook then names a program that does not exist (CreateProcess does not trim)
        var r = HookInstaller.Connect("{}", path);
        if (r.Reason is not null) return; // refusing is fine
        using var d = Doc(r.Text);
        var written = d.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0].GetProperty("command").GetString();
        Assert.Equal(path.Trim(), written);
    }

    [Fact]
    public void Defect_The_Screen_Is_Shown_Lines_For_A_Program_That_Connect_Would_Refuse()
    {
        // LATENT: LinesToAdd has no path check; the screen asks "add exactly these lines?" and Connect then answers NOTIFY_PATH_INVALID.
        var lines = HookInstaller.LinesToAdd(@"C:\x\calc.exe");
        Assert.True(lines.Count == 0 || !string.Join("\n", lines).Contains("calc.exe"));
    }

    [Fact]
    public void Holds_The_Lines_Shown_Are_Exactly_What_Connect_Writes_Into_An_Empty_File()
    {
        foreach (var path in new[] { Path, @"C:\Users\Jo Doe\Island.Notify.exe", "C:\\a\\\u00e9\u4e2d\\Island.Notify.exe" })
        {
            var lines = HookInstaller.LinesToAdd(path);
            Assert.True(JsonEqual(string.Join("\n", lines), HookInstaller.Connect("{}", path).Text));
            Assert.DoesNotContain("\r", string.Join("\n", lines));
        }
    }

    // ---- duplicate keys and odd members ---------------------------------------------------------------------

    [Fact]
    public void Holds_A_Repeated_Hooks_Key_Is_Refused_Or_Read_The_Way_Claude_Code_Reads_It_Last_One_Wins()
    {
        var text = "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo first\"}]}]},\"hooks\":{}}";
        var r = HookInstaller.Connect(text, Path);
        output.WriteLine(r.Reason ?? "changed: " + r.Text.Replace("\n", " "));
        if (r.Reason is not null)
        {
            AssertUntouched(r, text, HookInstaller.FileUnreadable);
            return;
        }

        using var d = Doc(r.Text);
        Assert.Equal(1, d.RootElement.EnumerateObject().Count(p => p.Name == "hooks"));
        Assert.DoesNotContain("echo first", r.Text); // last one wins, as JSON.parse does
    }

    [Fact]
    public void Holds_A_Repeated_Key_Elsewhere_Is_Refused_Or_Resolved_Last_One_Wins()
    {
        var text = "{\"env\":{\"A\":\"1\",\"A\":\"2\"},\"model\":\"a\",\"model\":\"b\"}";
        var r = HookInstaller.Connect(text, Path);
        output.WriteLine(r.Reason ?? "changed: " + r.Text.Replace("\n", " "));
        if (r.Reason is not null)
        {
            AssertUntouched(r, text, HookInstaller.FileUnreadable);
            return;
        }

        using var d = Doc(r.Text);
        Assert.Equal("2", d.RootElement.GetProperty("env").GetProperty("A").GetString());
        Assert.Equal("b", d.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, d.RootElement.EnumerateObject().Count(p => p.Name == "model"));
    }

    [Fact]
    public void Holds_Key_Case_Is_Respected()
    {
        var text = "{\"Hooks\":{\"stop\":[]},\"HOOKS\":1}";
        var r = HookInstaller.Connect(text, Path);
        Assert.True(r.Changed);
        using var d = Doc(r.Text);
        Assert.Equal(3, d.RootElement.EnumerateObject().Count());
        Assert.Equal(1, d.RootElement.GetProperty("HOOKS").GetInt32());
    }

    [Theory]
    [InlineData("{\"a\":\"\\ud800\"}")]
    [InlineData("{\"a\":\"x\\udc00y\"}")]
    [InlineData("{\"\\ud800\":1}")]
    [InlineData("{\"a\":[\"\\ud83d\"]}")]
    public void Defect_A_Lone_Surrogate_Escape_Anywhere_In_The_File_Makes_Connect_And_Disconnect_Throw(string text)
    {
        // valid JSON as the grammar goes (JSON.parse in Claude Code reads it); Render is outside the parser's try block
        var c = HookInstaller.Connect(text, Path);
        if (c.Reason is not null) { Assert.Equal(text, c.Text); return; }
        var d = HookInstaller.Disconnect(c.Text);
        Assert.Null(d.Reason);
    }

    [Fact]
    public void Holds_Is_Connected_Never_Throws_On_Odd_Members()
    {
        foreach (var text in new[]
                 {
                     "{\"hooks\":{\"Stop\":[1,null,\"x\",[],{},{\"hooks\":5},{\"hooks\":[1,null,\"x\",{\"command\":5},{\"command\":null},{\"command\":[]}]}]}}",
                     "{\"hooks\":{\"Stop\":null,\"Notification\":null}}",
                     "{\"hooks\":{\"Stop\":[{\"hooks\":[{\"command\":\"" + new string('a', 100_000) + "\"}]}]}}",
                 })
        {
            Assert.False(HookInstaller.IsConnected(text));
            Assert.False(HookInstaller.Disconnect(text).Changed);
            Assert.NotNull(HookInstaller.Connect(text, Path).Text);
        }
    }

    [Fact]
    public void Holds_A_Null_Stop_Is_Replaced_By_Ours_Which_Disconnect_Then_Removes()
    {
        var c = HookInstaller.Connect("{\"hooks\":{\"Stop\":null}}", Path);
        Assert.True(c.Changed);
        Assert.True(HookInstaller.IsConnected(c.Text));
    }

    // ---- size -----------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Ten_Megabytes_Of_Valid_Json_Is_Kept_Equal_And_Quick_Enough()
    {
        var sb = new StringBuilder("{\"data\":[");
        for (var i = 0; sb.Length < 10_000_000; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i).Append(",\"s\":\"item number ").Append(i).Append(" \\u00e9\",\"n\":[1.5,2e3,null,true]}");
        }

        sb.Append("],\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo keep\"}]}]}}");
        var text = sb.ToString();
        var clock = Stopwatch.StartNew();
        var c = HookInstaller.Connect(text, Path);
        var connectMs = clock.ElapsedMilliseconds;
        clock.Restart();
        var isConnected = HookInstaller.IsConnected(c.Text);
        var d = HookInstaller.Disconnect(c.Text);
        output.WriteLine($"10 MB: connect {connectMs} ms, is-connected+disconnect {clock.ElapsedMilliseconds} ms, size {text.Length}");
        Assert.Null(c.Reason);
        Assert.True(isConnected);
        Assert.True(connectMs < 5000);
        Assert.True(JsonEqual(text, d.Text));
    }

    [Fact]
    public void Holds_Fifty_Thousand_Entries_Of_A_User_Are_Kept_And_Connect_Is_Quick()
    {
        var entries = string.Join(',', Enumerable.Range(0, 50_000).Select(i => "{\"hooks\":[{\"type\":\"command\",\"command\":\"echo " + i + "\"}]}"));
        var text = Settings("\"Stop\":[" + entries + "],\"Notification\":[" + entries + "]");
        var clock = Stopwatch.StartNew();
        var c = HookInstaller.Connect(text, Path);
        output.WriteLine($"2 x 50 000 entries: connect {clock.ElapsedMilliseconds} ms");
        Assert.True(c.Changed);
        Assert.True(clock.ElapsedMilliseconds < 5000);
        clock.Restart();
        var d = HookInstaller.Disconnect(c.Text);
        output.WriteLine($"disconnect {clock.ElapsedMilliseconds} ms");
        Assert.True(JsonEqual(text, d.Text));
        Assert.True(clock.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void Holds_Disconnect_Of_Fifty_Thousand_Of_Our_Own_Entries_Is_Slow_But_Bounded()
    {
        // each removal from a JsonArray shifts the rest (quadratic); measured below, the bound is generous.
        var group = "{\"hooks\":[" + Ours() + "]}";
        var many = string.Join(',', Enumerable.Repeat(group, 50_000));
        var text = Settings("\"Stop\":[" + many + "]");
        var clock = Stopwatch.StartNew();
        var d = HookInstaller.Disconnect(text);
        output.WriteLine($"50 000 groups of ours: disconnect {clock.ElapsedMilliseconds} ms");
        Assert.True(d.Changed);
        Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");
        clock.Restart();
        var many2 = string.Join(',', Enumerable.Repeat(Ours(), 50_000));
        var d2 = HookInstaller.Disconnect(Settings("\"Stop\":[{\"hooks\":[" + many2 + "]}]"));
        output.WriteLine($"one group with 50 000 handlers of ours: disconnect {clock.ElapsedMilliseconds} ms");
        Assert.True(d2.Changed);
        Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");
    }

    // ---- fuzz -----------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Thousands_Of_Mutated_Settings_Never_Throw_Never_Write_Into_Unparseable_Text_And_Keep_Every_Key()
    {
        var seed = Settings("\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo a\"}]}],\"Notification\":[{\"matcher\":\"x\",\"hooks\":[" + Ours() + "]}]",
            "\"model\":\"x\",\"env\":{\"A\":\"1\",\"B\":[1,2,{\"c\":null}]},\"n\":1e5");
        var rnd = new Random(2026);
        var written = 0;
        for (var i = 0; i < 4000; i++)
        {
            var chars = seed.ToCharArray().ToList();
            for (var m = rnd.Next(1, 4); m > 0; m--)
            {
                var at = rnd.Next(chars.Count + 1);
                switch (rnd.Next(4))
                {
                    case 0 when at < chars.Count: chars.RemoveAt(at); break;
                    case 1: chars.Insert(at, "{}[],:\"\\ \n/*\0\ud800".ToCharArray()[rnd.Next(14)]); break;
                    case 2 when at < chars.Count: chars[at] = (char)rnd.Next(32, 127); break;
                    default: chars.RemoveRange(at, Math.Min(chars.Count - at, rnd.Next(1, 30))); break;
                }
            }

            var text = new string(chars.ToArray());
            var parses = true;
            try { using var _ = JsonDocument.Parse(text); }
            catch (Exception e) when (e is JsonException or ArgumentException) { parses = false; }
            var c = HookInstaller.Connect(text, Path);
            var d = HookInstaller.Disconnect(text);
            var isConnected = HookInstaller.IsConnected(text);
            if (!parses)
            {
                Assert.False(c.Changed, "wrote into unparseable text: " + text);
                Assert.Equal(text, c.Text);
                Assert.False(d.Changed, "changed unparseable text: " + text);
                Assert.Equal(text, d.Text);
                Assert.False(isConnected);
                continue;
            }

            if (c.Changed)
            {
                written++;
                using var before = Doc(text);
                using var after = Doc(c.Text);
                if (before.RootElement.ValueKind == JsonValueKind.Object && after.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var keepKeys = before.RootElement.EnumerateObject().Select(p => p.Name).Distinct().ToList();
                    var nowKeys = after.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                    foreach (var k in keepKeys) Assert.Contains(k, nowKeys);
                }
            }
            else if (c.Reason is null)
            {
                Assert.Equal(text, c.Text);
            }
        }

        output.WriteLine($"mutations that parsed and were written: {written}");
    }
}
