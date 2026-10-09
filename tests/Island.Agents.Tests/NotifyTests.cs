using System.Text.Json;
using Island.Core;
using Xunit.Abstractions;
using static Island.Agents.Tests.PipeTestSupport;

namespace Island.Agents.Tests;

public class NotifyTests(ITestOutputHelper output)
{
    [Fact]
    public void Stop_Reaches_The_Island_As_Finished_For_The_Project()
    {
        using var island = new Collector();

        var run = RunNotify(Utf8(StopJson(IslandFolder())), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal("", run.Error);
        var notice = island.Next(5000);
        Assert.NotNull(notice);
        Assert.Equal("island", notice.ProjectName);
        Assert.Equal(AgentSignal.Finished, notice.Signal);
        Assert.Equal("Agent finished — waiting for you", notice.Line);
        // The chain starts with the program that started Island.Notify, which here is this test's own process.
        Assert.NotEmpty(notice.Chain);
        Assert.Equal(Environment.ProcessId, notice.Chain[0]);
        output.WriteLine($"notify ran in {run.Elapsed.TotalMilliseconds:F0} ms, chain length {notice.Chain.Count}");
    }

    [Fact]
    public void A_Permission_Prompt_Reaches_The_Island_As_Needs_Your_Answer()
    {
        using var island = new Collector();

        var run = RunNotify(Utf8(NotificationJson(IslandFolder(), "permission_prompt")), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        var notice = island.Next(5000);
        Assert.NotNull(notice);
        Assert.Equal(AgentSignal.NeedsYourAnswer, notice.Signal);
        Assert.Equal("island", notice.ProjectName);
    }

    [Theory]
    [InlineData("idle_prompt")]
    [InlineData("auth_success")]
    [InlineData("elicitation_dialog")]
    public void Other_Notifications_Send_Nothing(string kind)
    {
        using var island = new Collector();

        var run = RunNotify(Utf8(NotificationJson(IslandFolder(), kind)), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        Assert.Null(island.Next(500));
    }

    [Fact]
    public void With_Nobody_Listening_It_Exits_Zero_Quickly_And_Prints_Nothing()
    {
        var run = RunNotify(Utf8(StopJson(IslandFolder())), [NewName()]);

        output.WriteLine($"no listener: exit {run.ExitCode} after {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal("", run.Error);
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(5), $"took {run.Elapsed}");
    }

    public static TheoryData<string, byte[]> BadInputs() => new()
    {
        { "empty", [] },
        { "whitespace", Utf8("  \n ") },
        { "garbage text", Utf8("this is not json") },
        { "truncated json", Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":") },
        { "binary", [0, 1, 2, 0xFF, 0xFE, 0x80, 0xC3, 0x28, 0, 0] },
        { "array", Utf8("[1,2,3]") },
        { "wrong types", Utf8("{\"hook_event_name\":5,\"cwd\":[]}") },
        { "oversized, fields after the limit", Utf8("{\"pad\":\"" + new string('x', 200_000) + "\",\"hook_event_name\":\"Stop\",\"cwd\":\"x\"}") }, // changed with ATTACK7B 4: fields that come first, inside the 64 KB, are read (see Holds_A_Long_Message_After_The_Fields...)
        { "deep nesting", Utf8("{\"hook_event_name\":\"Stop\",\"x\":" + new string('[', 4000) + new string(']', 4000) + "}") },
    };

    [Theory]
    [MemberData(nameof(BadInputs))]
    public void Bad_Input_Exits_Zero_Prints_Nothing_And_Tells_Nobody(string label, byte[] input)
    {
        using var island = new Collector();

        var run = RunNotify(input, [island.Name]);

        Assert.False(run.TimedOut, label);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal("", run.Error);
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void An_Input_That_Never_Ends_Does_Not_Hang_It()
    {
        using var island = new Collector();

        var run = RunNotify(Utf8("{\"hook_event_name\":\"Stop\""), [island.Name], closeStdin: false);

        output.WriteLine($"endless input: exit {run.ExitCode} after {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.Null(island.Next(300));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData(@"..\x")]
    [InlineData("a/b")]
    public void A_Pipe_Name_That_Is_Not_Plain_Is_Never_Replaced_By_The_Real_One(string name)
    {
        var run = RunNotify(Utf8(StopJson(IslandFolder())), [name]);

        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
    }

    [Fact]
    public void A_Lone_Surrogate_In_The_Folder_Still_Gets_Through()
    {
        using var island = new Collector();
        // JSON text with an escaped lone surrogate, as a hook can produce for a broken folder name.
        var json = "{\"hook_event_name\":\"Stop\",\"cwd\":\"C:\\\\work\\\\isl\\ud800and\"}";

        var run = RunNotify(Utf8(json), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        var notice = island.Next(5000);
        Assert.NotNull(notice);
        Assert.Equal("island", notice.ProjectName);
    }

    [Fact]
    public void A_Folder_With_A_Trailing_Slash_And_A_Very_Long_Path_Gives_The_Name()
    {
        using var island = new Collector();
        var longPath = "C:/" + string.Join("/", Enumerable.Repeat(new string('d', 200), 30)) + "/island/";

        var run = RunNotify(Utf8(StopJson(longPath)), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("island", island.Next(5000)?.ProjectName);
    }

    [Fact]
    public void Without_An_Agent_Argument_It_Is_Claude_Code()
    {
        using var island = new MessageCollector();
        var before = Environment.TickCount64;

        var run = RunNotify(Utf8(StopJson(IslandFolder())), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
        var message = island.Next(5000);
        Assert.NotNull(message);
        Assert.Equal("claude", message.Helper);
        Assert.Equal("Stop", message.Event);
        Assert.Equal("abc123", message.SessionId);
        Assert.Equal(Environment.ProcessId, message.Chain[0]);
        // A version-2 message: the time is the moment this program was created on the system's steady counter, never later than now and not long before the run.
        Assert.NotNull(message.Time);
        Assert.InRange(message.Time!.Value, before - 5000, Environment.TickCount64);
    }

    [Fact]
    public void A_Cut_Input_Still_Yields_The_Session_Id()
    {
        using var island = new MessageCollector();
        // The fields come first and the helper's whole last answer after them: the input is cut at 64 KB, inside a string.
        var json = "{\"session_id\":\"cut-session\",\"hook_event_name\":\"Stop\",\"cwd\":\"" + IslandFolder().Replace("\\", "\\\\") + "\",\"last_assistant_message\":\"" + new string('x', 200_000) + "\"}";

        var run = RunNotify(Utf8(json), [island.Name]);

        Assert.Equal(0, run.ExitCode);
        var message = island.Next(5000);
        Assert.NotNull(message);
        Assert.Equal("cut-session", message.SessionId);
        Assert.Equal("Stop", message.Event);
    }

    [Theory]
    [InlineData("-x")]
    [InlineData("--agent")]
    [InlineData("--event")]
    [InlineData("--unknown")]
    [InlineData("-")]
    public void An_Argument_That_Begins_With_A_Hyphen_Is_Never_A_Pipe_Name(string argument)
    {
        using var island = new MessageCollector();

        var run = RunNotify(Utf8(StopJson(IslandFolder())), [argument, island.Name]);

        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
        Assert.Null(island.Next(400)); // nothing was sent, to the pipe that was named or to any other
    }

    [Fact]
    public void Codex_Example_Input_Becomes_A_Version_Two_Message()
    {
        using var island = new MessageCollector();
        var codexStop = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["session_id"] = "codex-session-1",
            ["turn_id"] = "turn-1",
            ["cwd"] = IslandFolder(),
            ["hook_event_name"] = "Stop",
            ["model"] = "invented-model",
        });
        var codexAsk = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["session_id"] = "codex-session-1",
            ["cwd"] = IslandFolder(),
            ["hook_event_name"] = "PermissionRequest",
            ["tool_name"] = "Bash",
        });

        Assert.Equal(0, RunNotify(Utf8(codexStop), ["--agent", "codex", island.Name]).ExitCode);
        var finished = island.Next(5000);
        Assert.NotNull(finished);
        Assert.Equal("codex", finished.Helper);
        Assert.Equal("Stop", finished.Event);
        Assert.Equal("codex-session-1", finished.SessionId);

        Assert.Equal(0, RunNotify(Utf8(codexAsk), ["--agent", "codex", island.Name]).ExitCode);
        var asked = island.Next(5000);
        Assert.NotNull(asked);
        Assert.Equal("PermissionRequest", asked.Event);
        Assert.Equal("Bash", asked.Kind); // the kind of a permission request is the tool's name
    }

    [Fact]
    public void An_Event_The_Table_Does_Not_Know_Sends_Nothing_And_An_Agent_It_Does_Not_Know_Sends_Nothing()
    {
        using var island = new MessageCollector();
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { ["session_id"] = "s", ["cwd"] = IslandFolder(), ["hook_event_name"] = "PreToolUse", ["tool_name"] = "Bash" });

        Assert.Equal(0, RunNotify(Utf8(json), [island.Name]).ExitCode);
        Assert.Equal(0, RunNotify(Utf8(StopJson(IslandFolder())), ["--agent", "nobody", island.Name]).ExitCode);

        Assert.Null(island.Next(500));
    }
}
