using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Island.Core;
using Xunit.Abstractions;
using static Island.Attack7B.Tests.Support;

namespace Island.Attack7B.Tests;

/// <summary>Island.Notify as a child process, on invented pipe names only.</summary>
public class NotifyAttackTests(ITestOutputHelper output)
{
    private static void AssertSilentZero(NotifyRun run)
    {
        Assert.False(run.TimedOut, "Island.Notify did not end in time");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.Equal("", run.Error);
    }

    // ---- stdin ----------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Nothing_On_Stdin_Closed_At_Once_Exits_Zero_Quickly_And_Sends_Nothing()
    {
        using var island = new Collector();
        var run = RunNotify([], [island.Name]);
        AssertSilentZero(run);
        Assert.Null(island.Next(400));
        output.WriteLine($"empty stdin: {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(1.5), run.Elapsed.ToString());
    }

    [Fact]
    public void Holds_Stdin_That_Never_Closes_Ends_After_About_Two_Seconds_With_Zero()
    {
        using var island = new Collector();
        var run = RunNotify(Utf8("{\"hook_event_name\":"), [island.Name], closeStdin: false, waitMs: 8000);
        AssertSilentZero(run);
        output.WriteLine($"stdin never closed: {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(3.5), run.Elapsed.ToString());
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void Holds_No_Stdin_Bytes_And_Never_Closed_Also_Ends()
    {
        using var island = new Collector();
        var run = RunNotify([], [island.Name], closeStdin: false, waitMs: 8000);
        AssertSilentZero(run);
        output.WriteLine($"stdin silent and open: {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(3.5));
    }

    [Theory]
    [InlineData(65537)]
    [InlineData(1024 * 1024)]
    public void Holds_Input_Past_The_Limit_Is_Ignored_Quickly(int size)
    {
        using var island = new Collector();
        // padding BEFORE the fields that matter, so the cut at 64 KB leaves nothing usable
        var json = "{\"pad\":\"" + new string('a', size) + "\",\"cwd\":\"x/island\",\"hook_event_name\":\"Stop\"}";
        var run = RunNotify(Utf8(json), [island.Name]);
        AssertSilentZero(run);
        Assert.Null(island.Next(400));
        output.WriteLine($"{size} byte padding: {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(2.5), run.Elapsed.ToString());
    }

    [Fact]
    public void Holds_Hundred_Megabytes_Of_Stdin_Ends_Quickly_With_Zero()
    {
        using var island = new Collector();
        var run = RunNotifyStreaming(100L * 1024 * 1024, [island.Name]);
        AssertSilentZero(run);
        output.WriteLine($"100 MB of blanks: {run.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(5), run.Elapsed.ToString());
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void Defect_A_Stop_Whose_Long_Message_Passes_64_KB_Is_Dropped_Although_Its_Event_And_Folder_Came_First()
    {
        // The hooks page's own order: session_id, cwd, hook_event_name, ... last_assistant_message (the agent's whole last answer).
        using var island = new Collector();
        var json = "{\"session_id\":\"alpha\",\"cwd\":\"x/island\",\"hook_event_name\":\"Stop\",\"last_assistant_message\":\""
                   + new string('a', 70_000) + "\"}";
        var run = RunNotify(Utf8(json), [island.Name]);
        AssertSilentZero(run);
        // A notice should arrive: the three fields that matter are in the first 100 bytes. Today the 64 KB cut makes the JSON invalid, so nothing arrives.
        Assert.NotNull(island.Next(1500));
    }

    // ---- input shapes ---------------------------------------------------------------------------------------

    public static TheoryData<string, byte[]> Rejected() => new()
    {
        { "broken json", Utf8("{\"hook_event_name\":\"Stop\",") },
        { "not json", Utf8("Stop") },
        { "array root", Utf8("[\"Stop\"]") },
        { "string root", Utf8("\"Stop\"") },
        { "null root", Utf8("null") },
        { "event is number", Utf8("{\"hook_event_name\":5,\"cwd\":\"x/island\"}") },
        { "event is array", Utf8("{\"hook_event_name\":[\"Stop\"],\"cwd\":\"x/island\"}") },
        { "event is object", Utf8("{\"hook_event_name\":{\"a\":\"Stop\"},\"cwd\":\"x/island\"}") },
        { "event is null", Utf8("{\"hook_event_name\":null,\"cwd\":\"x/island\"}") },
        { "event empty", Utf8("{\"hook_event_name\":\"\",\"cwd\":\"x/island\"}") },
        { "event lowercase", Utf8("{\"hook_event_name\":\"stop\",\"cwd\":\"x/island\"}") },
        { "event with trailing blank", Utf8("{\"hook_event_name\":\"Stop \",\"cwd\":\"x/island\"}") },
        { "cwd is number", Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":5}") },
        { "cwd is object", Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":{\"a\":1}}") },
        { "kind is number on notification", Utf8("{\"hook_event_name\":\"Notification\",\"notification_type\":5,\"cwd\":\"x/island\"}") },
        { "notification without kind", Utf8("{\"hook_event_name\":\"Notification\",\"cwd\":\"x/island\"}") },
        { "notification kind case", Utf8("{\"hook_event_name\":\"Notification\",\"notification_type\":\"Permission_Prompt\",\"cwd\":\"x/island\"}") },
        { "deep nesting 40", Utf8("{\"hook_event_name\":\"Stop\",\"x\":" + new string('[', 40) + new string(']', 40) + "}") },
        { "utf-16 le text", Encoding.Unicode.GetBytes("{\"hook_event_name\":\"Stop\",\"cwd\":\"x/island\"}") },
        { "only zero bytes", new byte[200] },
        { "only whitespace", Utf8("   \r\n\t  ") },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Holds_Bad_Input_Is_Ignored_Silently_Exit_Zero(string label, byte[] input)
    {
        using var island = new Collector();
        var run = RunNotify(input, [island.Name]);
        AssertSilentZero(run);
        Assert.Null(island.Next(300));
        Assert.True(run.Elapsed < TimeSpan.FromSeconds(2), label + ": " + run.Elapsed);
    }

    [Theory]
    [InlineData(0xC3, 0x28)]
    [InlineData(0x80, 0x80)]
    [InlineData(0xFF, 0xFE)]
    [InlineData(0xED, 0xA0)]
    public void Holds_Invalid_Utf8_In_The_Folder_Exits_Zero_Silently_And_Never_Leaks_The_Bytes(int a, int b)
    {
        using var island = new Collector();
        var run = RunNotify([.. Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":\"x/is"), (byte)a, (byte)b, .. Utf8("land\"}")], [island.Name]);
        AssertSilentZero(run);
        var n = island.Next(800); // either dropped or shown without a name: both are fine, a crash is not
        output.WriteLine($"{a:X2} {b:X2}: " + (n is null ? "dropped" : "delivered as '" + n.ProjectName + "'"));
        if (n is not null) Assert.DoesNotContain('�', n.ProjectName);
    }

    [Fact]
    public void Holds_A_Bom_Before_Valid_Json_Is_Skipped_And_The_Notice_Arrives()
    {
        using var island = new Collector();
        var run = RunNotify([0xEF, 0xBB, 0xBF, .. Utf8(StopJson(@"C:\work\island"))], [island.Name]);
        AssertSilentZero(run);
        Assert.Equal("island", island.Next(3000)?.ProjectName);
    }

    [Fact]
    public void Holds_A_Lone_Surrogate_Escape_In_The_Folder_Still_Gives_A_Notice()
    {
        using var island = new Collector();
        var run = RunNotify(Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":\"C:\\\\work\\\\isl\\ud800and\"}"), [island.Name]);
        AssertSilentZero(run);
        var n = island.Next(3000);
        Assert.NotNull(n);
        Assert.Equal("island", n.ProjectName);
    }

    [Fact]
    public void Holds_Unknown_Extra_Fields_And_Escapes_Do_Not_Matter()
    {
        using var island = new Collector();
        var run = RunNotify(Utf8("{\"a\":[1,2,{\"b\":null}],\"hook\\u005fevent_name\":\"Stop\",\"cwd\":\"C:\\\\Pr\\u00f6j\\\\island\"}"), [island.Name]);
        AssertSilentZero(run);
        Assert.Equal("island", island.Next(3000)?.ProjectName);
    }

    // ---- hostile arguments ----------------------------------------------------------------------------------

    [Fact]
    public void Holds_Names_That_Are_Not_Plain_Never_Reach_Any_Pipe_Not_Even_One_That_Exists()
    {
        using var island = new Collector();
        string[][] bad =
        [
            [@"\\.\pipe\" + island.Name],
            [@"\\.\pipe\x"],
            [@"..\" + island.Name],
            [@"pipe\" + island.Name],
            ["pipe/" + island.Name],
            [island.Name + @"\..\" + island.Name],
            [""],
            [" " + island.Name],
            [island.Name + " "],
            [island.Name + "\n"],
            [new string('a', 129)],
            [new string('a', 10_000)],
            [island.Name.Replace('.', '\u2024')],
            ["\u0130" + island.Name],
        ];
        foreach (var args in bad)
        {
            var run = RunNotify(Utf8(StopJson("x/island")), args);
            AssertSilentZero(run);
            Assert.True(run.Elapsed < TimeSpan.FromSeconds(2), args[0].Length + ": " + run.Elapsed);
        }

        Assert.Null(island.Next(500)); // none of them reached the pipe that existed
    }

    [Fact]
    public void Holds_Odd_But_Plain_Names_Exit_Zero_Quickly()
    {
        foreach (var name in new[] { "..", ".", "...", "-", "_", "CON", "NUL", "anonymous", "a..b", new string('a', 128) })
        {
            var run = RunNotify(Utf8(StopJson("x/island")), [name]);
            AssertSilentZero(run);
            output.WriteLine($"name '{(name.Length > 20 ? name[..20] + "..." : name)}': {run.Elapsed.TotalMilliseconds:F0} ms");
            Assert.True(run.Elapsed < TimeSpan.FromSeconds(1.5), name + ": " + run.Elapsed);
        }
    }

    [Fact]
    public void Holds_Extra_Arguments_Are_Ignored_And_The_First_Still_Works()
    {
        using var island = new Collector();
        var run = RunNotify(Utf8(StopJson("x/island")), [island.Name, "--x", @"\\.\pipe\other", ""]);
        AssertSilentZero(run);
        Assert.Equal("island", island.Next(3000)?.ProjectName);
    }

    // ---- no file named by input is opened -------------------------------------------------------------------

    [Fact]
    public void Holds_Paths_Named_In_The_Input_Are_Never_Opened()
    {
        // The canary is a pipe whose name the input names in every field that holds a path: an open of it would connect.
        var canaryName = NewName();
        using var canary = new NamedPipeServerStream(canaryName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = canary.WaitForConnectionAsync();
        using var island = new Collector();
        var path = JsonSerializer.Serialize(@"\\.\pipe\" + canaryName);
        var json = "{\"session_id\":\"alpha\",\"transcript_path\":" + path + ",\"cwd\":" + path
                   + ",\"hook_event_name\":\"Stop\",\"notification_type\":\"x\",\"file\":" + path + "}";
        var run = RunNotify(Utf8(json), [island.Name]);
        AssertSilentZero(run);
        Assert.NotNull(island.Next(3000)); // it did its job
        Assert.False(connected.Wait(700), "Island.Notify opened a path its input named");
    }

    // ---- many at once ---------------------------------------------------------------------------------------

    private sealed record Many(int Started, int ExitZero, int Silent, int TimedOut, double MaxMs, double MedianMs, double TotalMs);

    private static Many RunMany(int count, string pipe, Func<int, string> json, int totalWaitMs = 60_000)
    {
        var clock = Stopwatch.StartNew();
        var procs = new List<(Process P, Task<string> O, Task<string> E)>();
        for (var i = 0; i < count; i++)
        {
            var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Island.Notify.exe"))
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            psi.ArgumentList.Add(pipe);
            var p = Process.Start(psi)!;
            procs.Add((p, p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync()));
        }

        for (var i = 0; i < count; i++)
        {
            try
            {
                procs[i].P.StandardInput.BaseStream.Write(Utf8(json(i)));
                procs[i].P.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }

        var times = new List<double>();
        int zero = 0, silent = 0, timedOut = 0;
        foreach (var (p, o, e) in procs)
        {
            var left = (int)Math.Max(100, totalWaitMs - clock.ElapsedMilliseconds);
            if (!p.WaitForExit(left))
            {
                timedOut++;
                p.Kill(entireProcessTree: true);
                p.WaitForExit();
                continue;
            }

            times.Add((p.ExitTime - p.StartTime).TotalMilliseconds);
            if (p.ExitCode == 0) zero++;
            if (o.Result == "" && e.Result == "") silent++;
        }

        foreach (var (p, _, _) in procs) p.Dispose();
        times.Sort();
        return new Many(count, zero, silent, timedOut, times.Count == 0 ? 0 : times[^1], times.Count == 0 ? 0 : times[times.Count / 2], clock.Elapsed.TotalMilliseconds);
    }

    [Fact]
    public void Holds_A_Hundred_At_Once_With_Nobody_Listening_All_Zero_Silent_And_Bounded()
    {
        var before = LiveNotifyProcesses();
        var r = RunMany(100, NewName(), i => StopJson("x/island" + i));
        output.WriteLine($"100 without a listener: {r}");
        Assert.Equal(100, r.ExitZero);
        Assert.Equal(100, r.Silent);
        Assert.Equal(0, r.TimedOut);
        Assert.True(r.TotalMs < 30_000, r.TotalMs.ToString("F0"));
        Assert.Equal(before, LiveNotifyProcesses());
    }

    [Fact]
    public void Holds_A_Hundred_At_Once_With_A_Listener_All_Zero_Silent_And_Bounded()
    {
        var before = LiveNotifyProcesses();
        using var island = new Collector();
        var r = RunMany(100, island.Name, i => StopJson("x/island" + i));
        var got = island.TakeAll(100, 3000);
        output.WriteLine($"100 with a listener: {r}; notices delivered {got.Count}");
        Assert.Equal(100, r.ExitZero);
        Assert.Equal(100, r.Silent);
        Assert.Equal(0, r.TimedOut);
        Assert.Equal(before, LiveNotifyProcesses());
    }

    [Fact]
    public void Defect_A_Burst_Of_A_Hundred_Hooks_Loses_Notices_Because_Notify_Gives_Up_After_300_Ms()
    {
        // PROBABILISTIC (lost 0 of 100 in one run, 5 of 100 in another; three rounds make a loss likely). The deterministic form of the same
        // cause is Defect_Sixteen_Idle_Same_User_Connections_Make_A_Real_Hook_Lose_Its_Notice.
        var lost = 0;
        for (var round = 0; round < 3; round++)
        {
            using var island = new Collector();
            RunMany(100, island.Name, i => StopJson("x/island" + i));
            lost += 100 - island.TakeAll(100, 4000).Count;
        }

        output.WriteLine($"lost over 3 bursts of 100: {lost}");
        Assert.Equal(0, lost);
    }

    [Fact]
    public void Defect_Sixteen_Idle_Same_User_Connections_Make_A_Real_Hook_Lose_Its_Notice()
    {
        // 16 acceptors; each idle connection holds one for the 1.5 s read limit; Notify waits only 300 ms for a free instance.
        using var island = new Collector();
        var idle = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < 16; i++) idle.Add(Connect(island.Name, 3000));
            var run = RunNotify(Utf8(StopJson("x/island")), [island.Name]);
            AssertSilentZero(run);
            Assert.NotNull(island.Next(1200)); // expected: heard. Today: lost, Notify has long gone
        }
        finally
        {
            foreach (var c in idle) c.Dispose();
        }
    }
}
