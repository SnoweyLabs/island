using System.Text;
using Island.Core;

namespace Island.Tests;

public class AgentEventTests
{
    // The shapes are those of the examples on the Claude Code hooks page (read 6 Oct 2026), with an invented folder.
    private const string StopJson =
        """{"session_id":"abc123","prompt_id":"p1","transcript_path":"t.jsonl","cwd":"C:\\work\\island","permission_mode":"default","hook_event_name":"Stop","last_assistant_message":"Done.","tool_use_count":5,"turn_number":3}""";

    private static string Notification(string kind) =>
        $$"""{"session_id":"abc123","cwd":"C:\\work\\island","hook_event_name":"Notification","notification_type":"{{kind}}","message":"m"}""";

    private static AgentSignal? SignalOf(string json)
    {
        var input = HookInputReader.Read(json);
        return input is null ? null : AgentSignals.Classify(input.Event, input.NotificationType);
    }

    [Fact]
    public void Stop_Means_Finished()
    {
        var input = HookInputReader.Read(StopJson);

        Assert.NotNull(input);
        Assert.Equal("Stop", input.Event);
        Assert.Equal(@"C:\work\island", input.Folder);
        Assert.Equal(AgentSignal.Finished, SignalOf(StopJson));
        Assert.Equal("Agent finished — waiting for you", AgentSignals.Text(AgentSignal.Finished));
    }

    [Fact]
    public void Permission_Prompt_Means_Needs_Your_Answer()
    {
        Assert.Equal(AgentSignal.NeedsYourAnswer, SignalOf(Notification("permission_prompt")));
        Assert.Equal("Agent needs your answer", AgentSignals.Text(AgentSignal.NeedsYourAnswer));
    }

    [Theory]
    [InlineData("idle_prompt")]
    [InlineData("auth_success")]
    [InlineData("elicitation_dialog")]
    [InlineData("agent_completed")]
    [InlineData("Permission_Prompt")]
    [InlineData("")]
    public void Everything_Else_Is_Ignored(string kind)
    {
        Assert.Null(SignalOf(Notification(kind)));
    }

    [Theory]
    [InlineData("SubagentStop")]
    [InlineData("StopFailure")]
    [InlineData("SessionEnd")]
    [InlineData("TaskCompleted")]
    [InlineData("stop")]
    [InlineData("PreToolUse")]
    public void Other_Events_Are_Ignored(string hookEvent)
    {
        Assert.Null(SignalOf($$"""{"hook_event_name":"{{hookEvent}}","cwd":"x","notification_type":"permission_prompt"}"""));
    }

    [Fact]
    public void A_Message_Becomes_A_Notice_Only_For_The_Two_Meanings()
    {
        var stop = AgentNotice.From(new AgentMessage("Stop", "", @"C:\work\island\", [4242, 7]));
        var idle = AgentNotice.From(new AgentMessage("Notification", "idle_prompt", @"C:\work\island", [4242]));
        var nameless = AgentNotice.From(new AgentMessage("Stop", "", "", []));

        Assert.NotNull(stop);
        Assert.Equal("island", stop.ProjectName);
        Assert.Equal(AgentSignal.Finished, stop.Signal);
        Assert.Equal([4242, 7], stop.Chain);
        Assert.Null(idle);
        Assert.Equal(ProjectName.Unknown, nameless!.ProjectName);
    }

    [Theory]
    [InlineData(@"C:\work\island", "island")]
    [InlineData("C:/work/island", "island")]
    [InlineData(@"C:\work\island\", "island")]
    [InlineData(@"C:\work\island\\\", "island")]
    [InlineData("/home/dev/island/", "island")]
    [InlineData(@"C:\work/mixed\island", "island")]
    [InlineData(@"\\server\share\island", "island")]
    [InlineData(@"C:\", "C:")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("island", "island")]
    [InlineData(@"C:\work\  spaced name  ", "spaced name")]
    public void Project_Name_Is_The_Last_Folder_Cut_And_Cleaned(string? folder, string expected)
    {
        Assert.Equal(expected, ProjectName.From(folder));
    }

    [Fact]
    public void Project_Name_Is_Cut_To_Forty_Characters()
    {
        var name = ProjectName.From(@"C:\work\" + new string('a', 200));

        Assert.Equal(40, name.Length);
    }

    [Fact]
    public void Project_Name_Never_Ends_In_Half_A_Surrogate_Pair()
    {
        // 39 letters, then one emoji (two chars) that straddles the cut at 40.
        var folder = @"C:\work\" + new string('a', 39) + char.ConvertFromUtf32(0x1F600) + "tail";

        var name = ProjectName.From(folder);

        Assert.Equal(new string('a', 39), name);
        Assert.All(name, c => Assert.False(char.IsSurrogate(c)));
    }

    [Fact]
    public void Project_Name_Has_No_Control_Or_Direction_Characters()
    {
        var bidi = new string([(char)0x202E, (char)0x200F, (char)0x2066, (char)0x2069, (char)0x061C, (char)0x200E, (char)0x202A]);
        var folder = @"C:\work\is" + bidi + "la" + (char)7 + "nd" + (char)0x2028 + (char)0x85 + (char)0;

        Assert.Equal("island", ProjectName.From(folder));
    }

    [Fact]
    public void A_Lone_Surrogate_In_The_Folder_Is_Dropped_And_A_Pair_Is_Kept()
    {
        var lone = ((char)0xD800).ToString();
        var pair = char.ConvertFromUtf32(0x1F600);

        Assert.Equal("ab", ProjectName.From(@"C:\x\a" + lone + "b"));
        Assert.Equal("a" + pair, ProjectName.From(@"C:\x\a" + pair + lone));
        Assert.Equal("", ProjectName.From(lone + lone));
    }

    [Fact]
    public void Broken_Or_Oversized_Input_Is_Ignored()
    {
        Assert.Null(HookInputReader.Read((string?)null));
        Assert.Null(HookInputReader.Read(""));
        Assert.Null(HookInputReader.Read("   "));
        Assert.Null(HookInputReader.Read("{"));
        Assert.Null(HookInputReader.Read("not json at all"));
        Assert.Null(HookInputReader.Read("[]"));
        Assert.Null(HookInputReader.Read("42"));
        Assert.Null(HookInputReader.Read("null"));
        Assert.Null(HookInputReader.Read("{}"));
        Assert.Null(HookInputReader.Read("""{"hook_event_name": 5}"""));
        Assert.Null(HookInputReader.Read("""{"hook_event_name": ""}"""));
        Assert.Null(HookInputReader.Read("""{"hook_event_name": "Stop", "cwd": 7}"""));
        Assert.Null(HookInputReader.Read("""{"hook_event_name": "Notification", "notification_type": ["x"]}"""));
        Assert.Null(HookInputReader.Read(StopJson.Replace("\"cwd\"", "\"cwd\":{")));
        Assert.Null(HookInputReader.Read(new byte[] { 0xFF, 0xFE, 0x00, 0x80, 0xC3 }));
        Assert.Null(HookInputReader.Read(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void A_Lone_Surrogate_Escape_In_The_Folder_Does_Not_Lose_The_Notice()
    {
        // JSON text, as a hook writes a path with an unpaired surrogate: the escape \ud800 on its own, and a valid pair.
        var bs = ((char)92).ToString();
        var lone = "{\"hook_event_name\":\"Stop\",\"cwd\":\"C:" + bs + bs + "x" + bs + bs + "isl" + bs + "ud800and\"}";
        var pair = "{\"hook_event_name\":\"Stop\",\"cwd\":\"C:" + bs + bs + "x" + bs + bs + "a" + bs + "ud83d" + bs + "ude00b\"}";
        var loneLow = "{\"hook_event_name\":\"Stop\",\"cwd\":\"a" + bs + "udc00b\"}";

        Assert.Equal(@"C:\x\island", HookInputReader.Read(lone)!.Folder);
        Assert.Equal(@"C:\x\a" + char.ConvertFromUtf32(0x1F600) + "b", HookInputReader.Read(pair)!.Folder);
        Assert.Equal("ab", HookInputReader.Read(loneLow)!.Folder);
        Assert.Equal("island", ProjectName.From(HookInputReader.Read(lone)!.Folder));
    }

    [Fact]
    public void Input_Over_Sixty_Four_KB_Is_Ignored_And_Exactly_At_The_Limit_Is_Read()
    {
        var body = StopJson[..^1] + ",\"pad\":\"";
        var padLength = AgentPipe.StdinLimitBytes - body.Length - 2;
        var atLimit = body + new string('x', padLength) + "\"}";
        Assert.Equal(AgentPipe.StdinLimitBytes, atLimit.Length);

        Assert.NotNull(HookInputReader.Read(atLimit));
        Assert.Null(HookInputReader.Read(atLimit + " "));
        Assert.Null(HookInputReader.Read(Encoding.UTF8.GetBytes(atLimit + " ")));
    }

    [Fact]
    public void Deeply_Nested_Input_Is_Ignored_Without_Throwing()
    {
        var nested = new string('[', 5000) + new string(']', 5000);

        Assert.Null(HookInputReader.Read("{\"hook_event_name\":\"Stop\",\"x\":" + nested + "}"));
    }

    [Fact]
    public void A_Byte_Order_Mark_Is_Tolerated()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(StopJson)).ToArray();

        Assert.NotNull(HookInputReader.Read(bytes));
    }

    [Fact]
    public void The_Message_Round_Trips_And_Is_Bounded()
    {
        var folder = @"C:\" + new string('é', 1000) + @"\island";
        var bytes = AgentWire.Encode("Stop", "", folder, Enumerable.Range(1, 40).ToList());

        Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        var message = AgentWire.TryParse(bytes);
        Assert.NotNull(message);
        Assert.Equal("Stop", message.Event);
        Assert.Equal(AgentPipe.MaxChain, message.Chain.Count);
        Assert.Equal("island", ProjectName.From(message.Folder));
        Assert.True(message.Folder.Length <= AgentPipe.MaxFolderChars);
    }

    [Fact]
    public void Encoding_Survives_A_Lone_Surrogate_And_Control_Text()
    {
        var folder = @"C:\x\bad" + (char)0xD800 + (char)0xDC00 + (char)0xDC00 + (char)0xD83D + (char)7;

        var message = AgentWire.TryParse(AgentWire.Encode("Stop\n", "k" + (char)0, folder, [3, -1, 0, 5]));

        Assert.NotNull(message);
        Assert.Equal("Stop", message.Event);
        Assert.Equal("k", message.Kind);
        Assert.Equal([3, 5], message.Chain);
        Assert.Equal(@"C:\x\bad" + char.ConvertFromUtf32(0x10000), message.Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x"}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[1],"extra":1}""")]
    [InlineData("""{"v":2,"e":"Stop","k":"","f":"x","c":[1]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[1],"c":[2]}""")]
    [InlineData("""{"v":1,"e":"Stop","e":"Stop","k":"","f":"x","c":[1]}""")]
    [InlineData("""{"v":1,"e":5,"k":"","f":"x","c":[1]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":["1"]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[0]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[-4]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[1.5]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[99999999999]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":{"a":1}}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[[1]]}""")]
    [InlineData("""{"v":1,"e":"Stop","k":"","f":"x","c":[1]} {"v":1}""")]
    [InlineData("not json")]
    public void Anything_But_The_Exact_Shape_Is_Ignored(string text)
    {
        Assert.Null(AgentWire.TryParse(text));
    }

    [Fact]
    public void A_Message_Over_The_Size_Limit_Or_Of_Binary_Is_Ignored()
    {
        var big = "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"" + new string('x', AgentPipe.MaxMessageBytes) + "\",\"c\":[1]}";

        Assert.Null(AgentWire.TryParse(big));
        Assert.Null(AgentWire.TryParse(new byte[] { 0, 1, 2, 0xFF, 0xFE, (byte)'{' }));
        Assert.Null(AgentWire.TryParse(new byte[AgentPipe.MaxMessageBytes + 1]));
        Assert.Null(AgentWire.TryParse(ReadOnlySpan<byte>.Empty));
        Assert.Null(AgentWire.TryParse("{\"v\":1,\"e\":\"" + new string('x', AgentPipe.MaxEventChars + 1) + "\",\"k\":\"\",\"f\":\"\",\"c\":[]}"));
    }

    [Fact]
    public void A_Chain_Is_Built_Nearest_First_And_Stops_At_Loops_Gaps_And_The_Limit()
    {
        var parents = new Dictionary<int, int> { [10] = 20, [20] = 30, [30] = 40, [40] = 0 };
        Assert.Equal([20, 30, 40], ProcessChain.Build(10, parents));

        var loop = new Dictionary<int, int> { [1] = 2, [2] = 3, [3] = 2 };
        Assert.Equal([2, 3], ProcessChain.Build(1, loop));

        var self = new Dictionary<int, int> { [5] = 5 };
        Assert.Empty(ProcessChain.Build(5, self));

        Assert.Empty(ProcessChain.Build(99, parents));

        var deep = Enumerable.Range(1, 100).ToDictionary(i => i, i => i + 1);
        Assert.Equal(AgentPipe.MaxChain, ProcessChain.Build(1, deep).Count);
    }

    [Theory]
    [InlineData("island.agents", true)]
    [InlineData("a-b_c.1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("a b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("a/b", false)]
    [InlineData("..", true)]
    public void A_Pipe_Name_Argument_Must_Be_Plain(string? name, bool valid)
    {
        Assert.Equal(valid, AgentPipe.IsValidName(name));
    }
}
