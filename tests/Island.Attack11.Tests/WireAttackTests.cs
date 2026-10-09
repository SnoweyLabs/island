using System.Text;
using Island.Agents;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Attack11.Tests;

/// <summary>The version-2 message and the pipe attacked from outside: out of shape, too long, of version 3, and sent to a real pipe of an invented name.</summary>
public class WireAttackTests
{
    private const string Good = "{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s1\",\"t\":5,\"f\":\"Q:\\\\Invented\\\\Alpha\",\"c\":[1,2]}";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    // ---- Shape ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Good_Message_Is_Taken()
    {
        var m = SessionWire.TryParse(Good);
        Assert.NotNull(m);
        Assert.Equal("claude", m.Helper);
        Assert.Equal("s1", m.SessionId);
        Assert.Equal(5, m.Time);
        Assert.Equal([1, 2], m.Chain);
    }

    [Theory]
    [InlineData("{\"v\":3,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":0,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1],\"x\":1}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\"}")]
    [InlineData("{\"v\":2,\"a\":1,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\"}")] // a wrong-typed first "a" must not let a second one through
    [InlineData("{\"v\":2,\"a\":null,\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":null,\"t\":5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":-1,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":1.5,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":\"5\",\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":99999999999999999999,\"f\":\"\",\"c\":[1]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[0]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[-4]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1.5]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[[1]]}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":{\"0\":1}}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]}")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"v\"")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]} {\"v\":2}")]
    [InlineData("{\"v\":2,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]},")]
    public void Holds_Anything_Out_Of_Shape_Is_Refused(string text)
    {
        Assert.Null(SessionWire.TryParse(text));
        Assert.Null(SessionWire.TryParseAny(Utf8(text)));
    }

    [Fact]
    public void Holds_Version_One_Is_Still_Taken_As_Claude_Code_Without_A_Session_Id()
    {
        var m = SessionWire.TryParseAny(AgentWire.Encode("Stop", "", @"Q:\Invented\Alpha", [4, 5]));
        Assert.NotNull(m);
        Assert.Equal("claude", m.Helper);
        Assert.Equal("", m.SessionId);
        Assert.Null(m.Time);
        Assert.Null(SessionWire.TryParse(AgentWire.Encode("Stop", "", "x", [4]))); // the version-two parser alone refuses version one
    }

    [Fact]
    public void Holds_A_Version_Three_Message_Is_Refused_By_Both_Parsers_And_By_The_Old_One()
    {
        var three = Utf8("{\"v\":3,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}");
        Assert.Null(SessionWire.TryParseAny(three));
        Assert.Null(AgentWire.TryParse(three));
    }

    // ---- Length ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Limit_Of_Four_Thousand_And_Ninety_Six_Bytes_To_The_Byte()
    {
        var at = Good + new string(' ', 4096 - Utf8(Good).Length);
        Assert.Equal(4096, Utf8(at).Length);
        Assert.NotNull(SessionWire.TryParse(Utf8(at)));
        Assert.Null(SessionWire.TryParse(Utf8(at + " ")));
    }

    [Fact]
    public void Holds_A_Megabyte_Is_Refused_At_Once_Whatever_Its_Shape()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(SessionWire.TryParse(new string('x', 1_000_000)));
        Assert.Null(SessionWire.TryParse("{\"v\":2,\"s\":\"" + new string('x', 1_000_000) + "\"}"));
        Assert.Null(SessionWire.TryParseAny(new byte[1_000_000]));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), clock.Elapsed.ToString());
    }

    [Fact]
    public void Holds_Every_Text_Over_Its_Limit_Is_Refused_And_At_The_Limit_Taken()
    {
        string Wire(string a = "claude", string e = "Stop", string k = "", string s = "s", string f = "") =>
            "{\"v\":2,\"a\":\"" + a + "\",\"e\":\"" + e + "\",\"k\":\"" + k + "\",\"s\":\"" + s + "\",\"t\":5,\"f\":\"" + f + "\",\"c\":[1]}";

        Assert.NotNull(SessionWire.TryParse(Wire(a: new string('a', SessionLimits.MaxHelperChars))));
        Assert.Null(SessionWire.TryParse(Wire(a: new string('a', SessionLimits.MaxHelperChars + 1))));
        Assert.NotNull(SessionWire.TryParse(Wire(e: new string('e', SessionLimits.MaxEventChars))));
        Assert.Null(SessionWire.TryParse(Wire(e: new string('e', SessionLimits.MaxEventChars + 1))));
        Assert.NotNull(SessionWire.TryParse(Wire(k: new string('k', SessionLimits.MaxKindChars))));
        Assert.Null(SessionWire.TryParse(Wire(k: new string('k', SessionLimits.MaxKindChars + 1))));
        Assert.NotNull(SessionWire.TryParse(Wire(s: new string('s', SessionLimits.MaxSessionIdChars))));
        Assert.Null(SessionWire.TryParse(Wire(s: new string('s', SessionLimits.MaxSessionIdChars + 1))));
        Assert.NotNull(SessionWire.TryParse(Wire(f: new string('f', SessionLimits.MaxFolderChars))));
        Assert.Null(SessionWire.TryParse(Wire(f: new string('f', SessionLimits.MaxFolderChars + 1))));
    }

    [Fact]
    public void Holds_The_Encoder_Never_Writes_More_Than_The_Limit_And_What_It_Writes_Is_Taken_Back()
    {
        var rng = new Random(11);
        for (var n = 0; n < 300; n++)
        {
            string Noise(int max) => new string([.. Enumerable.Range(0, rng.Next(0, max)).Select(_ => (char)rng.Next(0, 0xFFFF))]);
            var bytes = SessionWire.Encode(Noise(500), Noise(500), Noise(500), Noise(500), rng.NextInt64(long.MinValue, long.MaxValue), Noise(2000), [.. Enumerable.Range(-5, rng.Next(0, 40))]);
            Assert.True(bytes.Length <= SessionLimits.MaxMessageBytes);
            Assert.NotNull(SessionWire.TryParse(bytes.AsSpan(0, bytes.Length - 1))); // without the newline
        }
    }

    [Fact]
    public void Holds_Random_Bytes_Never_Throw_From_Either_Parser()
    {
        var rng = new Random(12);
        for (var n = 0; n < 3000; n++)
        {
            var bytes = new byte[rng.Next(0, 600)];
            rng.NextBytes(bytes);
            Assert.Null(Record.Exception(() => SessionWire.TryParseAny(bytes)));
        }
    }

    // ---- A real pipe ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Pipe_Raises_Only_What_Is_Exactly_Version_One_Or_Two_And_Survives_The_Rest()
    {
        using var island = new PipeCollector();
        Pipe.Send(island.Name, Utf8("{\"v\":3,\"a\":\"claude\",\"e\":\"Stop\",\"k\":\"\",\"s\":\"s\",\"t\":5,\"f\":\"\",\"c\":[1]}\n"));
        Pipe.Send(island.Name, new byte[1_000_000]);
        Pipe.Send(island.Name, Utf8(new string('{', 5000) + "\n"));
        Pipe.Send(island.Name, [0xFF, 0xFE, 0x00, 0x01, (byte)'\n']);
        Assert.Null(island.Next(400));

        Pipe.Send(island.Name, Utf8(Good + "\n"));
        var m = island.Next(5000);
        Assert.NotNull(m);
        Assert.Equal("s1", m.SessionId);
    }

    [Fact]
    public void Holds_A_Session_Id_Of_Path_Characters_Arrives_As_Plain_Text()
    {
        using var island = new PipeCollector();
        foreach (var id in new[] { @"..\\..\\x", "../../x", @"C:\\Windows", "CON" })
            Pipe.Send(island.Name, Utf8(Good.Replace("\"s1\"", "\"" + id + "\"") + "\n"));

        var seen = new List<string>();
        for (var i = 0; i < 4; i++) seen.Add(island.Next(5000)?.SessionId ?? "<none>");
        Assert.DoesNotContain("<none>", seen);
        Assert.All(seen, id => Assert.True(id.Length <= SessionLimits.MaxSessionIdChars));
    }

    [Fact]
    public void Holds_Two_Hundred_Messages_From_Twenty_Threads_All_Arrive()
    {
        using var island = new PipeCollector();
        var threads = Enumerable.Range(0, 20).Select(t => new Thread(() =>
        {
            for (var i = 0; i < 10; i++) Pipe.Send(island.Name, Utf8(Good.Replace("\"s1\"", $"\"t{t}m{i}\"") + "\n"));
        })).ToList();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) Assert.True(t.Join(TimeSpan.FromSeconds(60)));
        var ids = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var m = island.Next(5000);
            Assert.NotNull(m);
            ids.Add(m.SessionId);
        }

        Assert.Equal(200, ids.Count);
    }

    [Fact]
    public void Holds_A_Handler_That_Throws_Does_Not_Stop_The_Server()
    {
        var name = "island-attack11-" + Guid.NewGuid().ToString("N");
        using var server = new AgentPipeServer(name);
        var calls = 0;
        server.MessageReceived += _ => { Interlocked.Increment(ref calls); throw new InvalidOperationException("the handler failed"); };
        Assert.True(server.Start());
        Pipe.Send(name, Utf8(Good + "\n"));
        Pipe.Send(name, Utf8(Good + "\n"));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void Holds_The_Notice_Of_A_Version_Two_Message_Follows_The_Table_Not_The_Event_Alone()
    {
        // Stop of Claude Code raises the notice; the same word from a helper whose table has no row for it does not; Codex's PermissionRequest does.
        Assert.NotNull(AgentNotice.From(SessionWire.TryParse(Good)!, AgentSignalTables.All));
        Assert.Null(AgentNotice.From(SessionWire.TryParse(Good.Replace("\"claude\"", "\"gemini\""))!, AgentSignalTables.All));
        Assert.Null(AgentNotice.From(SessionWire.TryParse(Good.Replace("\"Stop\"", "\"StopFailure\""))!, AgentSignalTables.All));
        Assert.Null(AgentNotice.From(SessionWire.TryParse(Good.Replace("\"Stop\"", "\"UserPromptSubmit\""))!, AgentSignalTables.All));
        Assert.NotNull(AgentNotice.From(SessionWire.TryParse(Good.Replace("\"claude\"", "\"codex\"").Replace("\"Stop\"", "\"PermissionRequest\"").Replace("\"k\":\"\"", "\"k\":\"Bash\""))!, AgentSignalTables.All));
        Assert.Null(AgentNotice.From(SessionWire.TryParse(Good.Replace("\"claude\"", "\"codex\"").Replace("\"Stop\"", "\"Interrupt\""))!, AgentSignalTables.All));
    }
}
