using System.Text;
using Island.Core;
using Xunit.Abstractions;
using static Island.Attack11.Tests.NotifyExe;

namespace Island.Attack11.Tests;

/// <summary>Island.Notify's argument parser, its reader of the hook's input and the built program itself, attacked with every wrong argument. Invented pipe names, example inputs.</summary>
public class NotifyAttackTests(ITestOutputHelper output)
{
    // ---- NotifyArguments, in process --------------------------------------------------------------------------------------------------------------------

    private static NotifyArguments? Parse(params string[] args) => NotifyArguments.Parse(args);

    [Fact]
    public void Holds_No_Arguments_Is_Claude_Code_With_The_Real_Pipe()
    {
        var a = Parse();
        Assert.NotNull(a);
        Assert.Equal("claude", a.Agent);
        Assert.Null(a.Event);
        Assert.Null(a.PipeName);
    }

    [Fact]
    public void Holds_The_Good_Forms_In_Any_Order()
    {
        Assert.Equal(new NotifyArguments("codex", "Stop", "pipe1"), Parse("--agent", "codex", "--event", "Stop", "pipe1"));
        Assert.Equal(new NotifyArguments("codex", "Stop", "pipe1"), Parse("pipe1", "--event", "Stop", "--agent", "codex"));
        Assert.Equal(new NotifyArguments("codex", "Stop", "pipe1"), Parse("--event", "Stop", "pipe1", "--agent", "CODEX"));
        Assert.Equal(new NotifyArguments("claude", "Stop", null), Parse("--event", "Stop"));
        Assert.Equal(new NotifyArguments("a-b_c9", null, null), Parse("--agent", "A-B_c9"));
    }

    [Theory]
    [InlineData("--agent")]
    [InlineData("--event")]
    [InlineData("--agent", "")]
    [InlineData("--event", "")]
    [InlineData("--agent", "--event")]
    [InlineData("--agent", "-x")]
    [InlineData("--agent", "a b")]
    [InlineData("--agent", "a.b")]
    [InlineData("--agent", "a/b")]
    [InlineData("--agent", "a\\b")]
    [InlineData("--agent", "../x")]
    [InlineData("--agent", "a:b")]
    [InlineData("--agent", "a\u00e9b")]
    [InlineData("--agent", "a\0b")]
    [InlineData("--agent", "claude", "--agent", "codex")]
    [InlineData("--agent", "claude", "--agent", "claude")]
    [InlineData("--event", "Stop", "--event", "Stop")]
    [InlineData("--agent=claude")]
    [InlineData("--event=Stop")]
    [InlineData("--AGENT", "claude")]
    [InlineData("--Agent", "claude")]
    [InlineData("-agent", "claude")]
    [InlineData("/agent", "claude")]
    [InlineData("--")]
    [InlineData("-")]
    [InlineData("-x")]
    [InlineData("--bogus", "pipe1")]
    [InlineData("--bogus")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..\\x")]
    [InlineData("\\\\.\\pipe\\x")]
    [InlineData("-pipe")]
    [InlineData("pipe1", "--agent")]
    [InlineData("pipe1", "--event")]
    [InlineData("pipe1", "--agent", "--event", "x")]
    public void Holds_Every_Wrong_Argument_Gives_Nothing_And_Never_The_Real_Pipe(params string[] args)
    {
        Assert.Null(NotifyArguments.Parse(args));
    }

    [Fact]
    public void Holds_A_Pipe_Name_Over_The_Limit_Or_An_Agent_Over_Its_Own_Is_Refused_And_At_The_Limit_Taken()
    {
        Assert.NotNull(Parse(new string('p', 128)));
        Assert.Null(Parse(new string('p', 129)));
        Assert.Null(Parse(new string('p', 1_000_000)));
        Assert.NotNull(Parse("--agent", new string('a', NotifyArguments.MaxAgentChars)));
        Assert.Null(Parse("--agent", new string('a', NotifyArguments.MaxAgentChars + 1)));
        Assert.NotNull(Parse("--event", new string('e', NotifyArguments.MaxEventChars)));
        Assert.Null(Parse("--event", new string('e', NotifyArguments.MaxEventChars + 1)));
    }

    [Fact]
    public void Holds_A_Second_Pipe_Name_Is_Ignored_As_It_Always_Was_And_Never_Used()
    {
        var a = Parse("pipe1", "pipe2", "../x", "--bogus");
        Assert.NotNull(a);
        Assert.Equal("pipe1", a.PipeName);
    }

    [Fact]
    public void Holds_The_Agent_Argument_Is_Taken_After_The_Pipe_Name_Too()
    {
        Assert.Equal("codex", Parse("pipe1", "--agent", "codex")!.Agent);
    }

    // ---- The hook's input -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Session_Id_Is_Read_From_A_Whole_Input_And_From_One_Cut_At_The_Limit()
    {
        var head = Hook("Stop", session: "s77");
        var whole = head.TrimEnd('}') + ",\"last_assistant_message\":\"" + new string('x', 200_000) + "\"}";
        var cut = Encoding.UTF8.GetBytes(whole)[..AgentPipe.StdinLimitBytes];
        Assert.Equal("s77", HookInputReader.Read(Encoding.UTF8.GetBytes(head))!.SessionId);
        Assert.Equal("s77", HookInputReader.Read(cut)!.SessionId);
        Assert.Equal("Stop", HookInputReader.Read(cut)!.Event);
    }

    [Fact]
    public void Holds_A_Session_Id_Cut_In_The_Middle_Is_Not_Taken_As_A_Shorter_One()
    {
        var text = "{\"hook_event_name\":\"Stop\",\"cwd\":\"x\",\"pad\":\"" + new string('p', AgentPipe.StdinLimitBytes) + "\",\"session_id\":\"abcdef\"}";
        var read = HookInputReader.Read(Encoding.UTF8.GetBytes(text)[..AgentPipe.StdinLimitBytes]);
        Assert.NotNull(read);
        Assert.Equal("", read.SessionId); // the field lies beyond the cut: none, and never half of one
    }

    [Theory]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":5}", "")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":null}", "")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":[\"a\"]}", "")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"..\\\\..\\\\x\"}", "..\\..\\x")]
    [InlineData("{\"hook_event_name\":\"Stop\",\"session_id\":\"a\\u0000b\"}", "a\0b")]
    public void Holds_A_Session_Id_Of_Another_Type_Is_Empty_And_Path_Characters_Pass_As_Text(string json, string expected)
    {
        var read = HookInputReader.Read(json);
        Assert.NotNull(read);
        Assert.Equal(expected, read.SessionId);
    }

    // ---- The built program ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Good_Call_Reaches_The_Island_As_Version_Two_With_The_Agent_The_Session_And_A_Time()
    {
        using var island = new PipeCollector();
        var before = Environment.TickCount64;
        var run = Run(Utf8(Hook("UserPromptSubmit", session: "s5")), ["--agent", "claude", island.Name]);
        var after = Environment.TickCount64;
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
        var m = island.Next(5000);
        Assert.NotNull(m);
        Assert.Equal("claude", m.Helper);
        Assert.Equal("UserPromptSubmit", m.Event);
        Assert.Equal("s5", m.SessionId);
        Assert.NotNull(m.Time);
        Assert.InRange(m.Time!.Value, before - 5000, after);
        Assert.Equal(Environment.ProcessId, m.Chain[0]);
        output.WriteLine($"notify ran in {run.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void Holds_The_Old_Entry_With_No_Agent_Argument_Still_Means_Claude_Code()
    {
        using var island = new PipeCollector();
        var run = Run(Utf8(Hook("Stop")), [island.Name]);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("claude", island.Next(5000)?.Helper);
    }

    public static TheoryData<string[]> WrongCalls() => new()
    {
        new[] { "--agent" },
        new[] { "--agent", "" },
        new[] { "--agent", "a b" },
        new[] { "--agent", "a.b" },
        new[] { "--agent", "../x" },
        new[] { "--agent", "claude", "--agent", "codex" },
        new[] { "--event" },
        new[] { "--event", "Stop", "--event", "Stop" },
        new[] { "--bogus" },
        new[] { "-x" },
        new[] { "--agent=claude" },
        new[] { "" },
        new[] { "a b" },
        new[] { "a/b" },
        new[] { "..\\x" },
    };

    [Theory]
    [MemberData(nameof(WrongCalls))]
    public void Holds_Every_Wrong_Argument_Of_The_Program_Sends_Nothing_Prints_Nothing_And_Ends_With_Zero(string[] wrong)
    {
        using var island = new PipeCollector();
        // a wrong argument is followed by the invented pipe name, so that nothing at all would be a pass only because the pipe was not named
        var run = Run(Utf8(Hook("Stop")), [.. wrong, island.Name]);
        Assert.False(run.TimedOut);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void Holds_A_Wrong_Argument_After_The_Pipe_Name_Is_Ignored_As_It_Always_Was()
    {
        using var island = new PipeCollector();
        var run = Run(Utf8(Hook("Stop")), [island.Name, "--bogus", "../x"]);
        Assert.Equal(0, run.ExitCode);
        Assert.NotNull(island.Next(5000));
    }

    [Fact]
    public void Holds_An_Agent_The_Table_Does_Not_Know_And_An_Event_It_Does_Not_Know_Stay_Silent()
    {
        using var island = new PipeCollector();
        Assert.Equal(0, Run(Utf8(Hook("Stop")), ["--agent", "gemini", island.Name]).ExitCode);
        Assert.Equal(0, Run(Utf8(Hook("Stop")), ["--agent", "antigravity", island.Name]).ExitCode);
        Assert.Equal(0, Run(Utf8(Hook("PreToolUse", tool: "Bash")), ["--agent", "claude", island.Name]).ExitCode);
        Assert.Equal(0, Run(Utf8(Hook("Notification", kind: "idle_prompt")), ["--agent", "codex", island.Name]).ExitCode);
        Assert.Null(island.Next(400));
    }

    [Fact]
    public void Holds_Claude_Codes_Own_Events_Do_Not_Pass_For_Codex_And_Back()
    {
        using var island = new PipeCollector();
        Assert.Equal(0, Run(Utf8(Hook("StopFailure")), ["--agent", "codex", island.Name]).ExitCode);
        Assert.Equal(0, Run(Utf8(Hook("Interrupt")), ["--agent", "claude", island.Name]).ExitCode);
        Assert.Null(island.Next(400));
        Assert.Equal(0, Run(Utf8(Hook("Interrupt")), ["--agent", "codex", island.Name]).ExitCode);
        Assert.Equal("Interrupt", island.Next(5000)?.Event);
    }

    [Fact]
    public void Holds_A_Permission_Request_Carries_The_Tool_Name_As_Its_Kind()
    {
        using var island = new PipeCollector();
        Run(Utf8(Hook("PermissionRequest", tool: "ToolA")), ["--agent", "codex", island.Name]);
        var m = island.Next(5000);
        Assert.NotNull(m);
        Assert.Equal("ToolA", m.Kind);
    }

    [Fact]
    public void Holds_A_Hook_Input_With_A_Path_Shaped_Session_Id_Arrives_As_Text_And_Opens_No_File()
    {
        using var island = new PipeCollector();
        var run = Run(Utf8(Hook("Stop", session: "..\\\\..\\\\Windows\\\\win.ini")), [island.Name]);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output + run.Error);
        var m = island.Next(5000);
        Assert.NotNull(m);
        Assert.Equal(@"..\..\Windows\win.ini", m.SessionId);
    }

    [Fact]
    public void Holds_Nobody_Listening_Is_Quick_And_Silent_For_Every_Agent()
    {
        foreach (var agent in new[] { "claude", "codex" })
        {
            var run = Run(Utf8(Hook("Stop")), ["--agent", agent, "island-attack11-nobody-" + Guid.NewGuid().ToString("N")]);
            Assert.False(run.TimedOut);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("", run.Output + run.Error);
            Assert.True(run.Elapsed < TimeSpan.FromSeconds(8), run.Elapsed.ToString());
        }
    }

    [Fact]
    public void Defect_Notify_With_Event_Argument_And_An_Input_Without_An_Event_Name_Sends_Nothing()
    {
        // FINDING A11-07 (LATENT: Gemini and Antigravity's terminal program are blocked, so no connected helper needs it). The work order: "--event is for a helper whose input does not name its event."
        // HookInputReader.Read requires hook_event_name and returns null without it, so Island.Notify ends silently before it looks at --event. Expected: one message with event Stop.
        using var island = new PipeCollector();
        var run = Run(Utf8("{\"session_id\":\"s9\",\"cwd\":\"Q:\\\\Invented\\\\Alpha\"}"), ["--agent", "codex", "--event", "Stop", island.Name]);
        Assert.Equal(0, run.ExitCode);
        var m = island.Next(3000);
        Assert.NotNull(m);
        Assert.Equal("Stop", m.Event);
    }

    [Fact]
    public void Holds_Two_Calls_One_After_The_Other_Carry_Times_That_Do_Not_Run_Backwards_By_More_Than_A_Timer_Tick()
    {
        using var island = new PipeCollector();
        Run(Utf8(Hook("UserPromptSubmit", session: "s1")), [island.Name]);
        Run(Utf8(Hook("Stop", session: "s1")), [island.Name]);
        var first = island.Next(5000);
        var second = island.Next(5000);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second.Time >= first.Time - 20, $"{first.Time} then {second.Time}");
    }
}
