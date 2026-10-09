using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Island.Agents;
using Island.Core;
using Xunit.Abstractions;
using static Island.Agents.Tests.PipeTestSupport;

namespace Island.Agents.Tests;

public class PipeServerTests(ITestOutputHelper output)
{
    [Fact]
    public void A_Valid_Message_Becomes_A_Notice()
    {
        using var island = new Collector();

        Send(island.Name, ValidMessage(@"C:\work\island\", [4242, 7]));

        var notice = island.Next(3000);
        Assert.NotNull(notice);
        Assert.Equal("island", notice.ProjectName);
        Assert.Equal([4242, 7], notice.Chain);
    }

    [Fact]
    public void A_Message_Without_A_Newline_Is_Taken_When_The_Client_Closes()
    {
        using var island = new Collector();
        var bytes = ValidMessage();

        SendRude(island.Name, bytes[..^1]);

        Assert.NotNull(island.Next(10000));
    }

    [Fact]
    public void Only_The_First_Message_Of_A_Connection_Counts()
    {
        using var island = new Collector();
        var one = ValidMessage("a/island");
        var two = ValidMessage("a/other");

        Send(island.Name, [.. one, .. two]);

        Assert.Equal("island", island.Next(3000)?.ProjectName);
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void An_Event_That_Is_Ignored_Raises_Nothing()
    {
        using var island = new Collector();

        Send(island.Name, AgentWire.Encode("Notification", "idle_prompt", "x/island", [1]));

        Assert.Null(island.Next(500));
    }

    public static TheoryData<string, byte[]> Garbage() => new()
    {
        { "binary", [0, 1, 2, 0xFF, 0xFE, 0x80, 0xC3, 0x28, (byte)'\n'] },
        { "empty line", [(byte)'\n'] },
        { "half json", Utf8("{\"v\":1,\"e\":\"Stop\"\n") },
        { "other shape", Utf8("{\"hook_event_name\":\"Stop\",\"cwd\":\"x\"}\n") },
        { "extra property", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1],\"z\":0}\n") },
        { "huge, no newline", new byte[100_000] },
        { "huge text line", [.. Utf8(new string('x', 50_000)), (byte)'\n'] },
        { "just under the size but wrong", [.. Utf8("{\"v\":1" + new string(' ', 4000)), (byte)'\n'] },
        { "nothing", [] },
    };

    [Theory]
    [MemberData(nameof(Garbage))]
    public void Garbage_Is_Ignored_And_The_Server_Goes_On(string label, byte[] bytes)
    {
        using var island = new Collector();

        try
        {
            Send(island.Name, bytes);
        }
        catch (IOException)
        {
            // the server may have closed on an oversized stream; that is allowed
        }

        Assert.Null(island.Next(300));
        Send(island.Name, ValidMessage());
        Assert.NotNull(island.Next(3000));
        Assert.True(island.Server.IsListening, label);
    }

    [Fact]
    public void A_Client_That_Never_Writes_Does_Not_Block_The_Others()
    {
        using var island = new Collector();
        using var idle = Connect(island.Name);

        var started = Environment.TickCount64;
        Send(island.Name, ValidMessage("a/island"));

        Assert.NotNull(island.Next(3000));
        Assert.True(Environment.TickCount64 - started < AgentPipe.ReadTimeoutMs, "the idle client held the others up");
    }

    [Fact]
    public void Idle_Clients_Past_The_Limit_Do_Not_Starve_The_Island_For_Long()
    {
        using var island = new Collector();
        var idle = Enumerable.Range(0, 20).Select(_ => Connect(island.Name)).ToList();
        try
        {
            // Every idle client holds a place only until the read limit; then a real message gets through.
            Send(island.Name, ValidMessage("a/island"), ms: 10_000);

            Assert.NotNull(island.Next(10_000));
        }
        finally
        {
            idle.ForEach(c => c.Dispose());
        }
    }

    [Fact]
    public void A_Message_That_Trickles_In_Too_Slowly_Is_Ignored()
    {
        using var island = new Collector();
        var bytes = ValidMessage();
        using (var client = Connect(island.Name))
        {
            client.Write(bytes[..10]);
            client.Flush();
            Thread.Sleep(AgentPipe.ReadTimeoutMs + 700);
            try
            {
                client.Write(bytes[10..]);
                client.Flush();
            }
            catch (IOException)
            {
                // the server closed its end after the limit
            }
        }

        Assert.Null(island.Next(500));
        Send(island.Name, ValidMessage());
        Assert.NotNull(island.Next(3000));
    }

    [Fact]
    public void A_Hundred_Clients_At_Once_Are_All_Heard()
    {
        using var island = new Collector();
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        // Own threads, not the pool: the server's work runs on the pool of this same process and must not be starved by the clients.
        var go = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, 100).Select(i => new Thread(() =>
        {
            go.Wait();
            try
            {
                Send(island.Name, ValidMessage($"a/p{i}", [i + 1]), ms: 20_000);
            }
            catch (Exception e)
            {
                errors.Add(e.GetType().Name);
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        go.Set();
        threads.ForEach(t => t.Join());
        var heard = island.TakeAll(100, 20_000);
        clock.Stop();

        output.WriteLine($"100 clients: {heard.Count} heard, {errors.Count} client errors, {clock.ElapsedMilliseconds} ms");
        Assert.Empty(errors);
        Assert.Equal(100, heard.Count);
        Assert.Equal(100, heard.Select(n => n.ProjectName).Distinct().Count());
    }

    [Fact]
    public void Rude_Clients_That_Close_At_Once_Cannot_Hurt_The_Server()
    {
        using var island = new Collector();
        var threads = Enumerable.Range(0, 50).Select(i => new Thread(() =>
        {
            try
            {
                SendRude(island.Name, ValidMessage($"a/p{i}"), ms: 10_000);
            }
            catch (IOException)
            {
                // allowed
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        island.TakeAll(50, 3000); // some may be lost, which is the cost of closing first; none may break anything
        Send(island.Name, ValidMessage("a/after"), ms: 10_000);
        Assert.Equal("after", island.TakeAll(1, 3000).LastOrDefault()?.ProjectName);
        Assert.True(island.Server.IsListening);
    }

    [Fact]
    public void A_Handler_That_Throws_Does_Not_Stop_The_Server()
    {
        var name = NewName();
        using var server = new AgentPipeServer(name);
        var calls = 0;
        server.NoticeReceived += _ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("a broken handler");
        };
        Assert.True(server.Start());

        Send(name, ValidMessage());
        Send(name, ValidMessage());
        SpinWait.SpinUntil(() => Volatile.Read(ref calls) >= 2, 3000);

        Assert.Equal(2, Volatile.Read(ref calls));
        Assert.True(server.IsListening);
    }

    [Fact]
    public void Dispose_Stops_Cleanly_Even_With_A_Client_Waiting()
    {
        var island = new Collector();
        var idle = Connect(island.Name);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        island.Dispose();
        island.Dispose(); // twice is harmless
        clock.Stop();
        idle.Dispose();

        output.WriteLine($"dispose took {clock.ElapsedMilliseconds} ms");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3));
        Assert.False(island.Server.IsListening);
        Assert.ThrowsAny<Exception>(() => Connect(island.Name, 300));
        Assert.False(island.Server.Start()); // a stopped server does not start again
    }

    [Fact]
    public void Nothing_Arrives_After_Dispose()
    {
        var island = new Collector();
        Send(island.Name, ValidMessage());
        Assert.NotNull(island.Next(3000));

        island.Dispose();

        Assert.Equal(0, island.Count);
    }

    [Fact]
    public void A_Name_That_Is_Already_Taken_Is_Reported_Not_Thrown()
    {
        using var island = new Collector();
        using var second = new AgentPipeServer(island.Name);

        Assert.False(second.Start());
        Assert.False(second.IsListening);
        Send(island.Name, ValidMessage()); // the first is untouched
        Assert.NotNull(island.Next(3000));
    }

    [Fact]
    public void Start_Twice_Changes_Nothing()
    {
        using var island = new Collector();

        Assert.True(island.Server.Start());
        Send(island.Name, ValidMessage());
        Assert.NotNull(island.Next(3000));
        Assert.Null(island.Next(300));
    }

    [Fact]
    public void The_Pipe_Is_Open_To_The_Current_User_Only_And_Not_Over_The_Network()
    {
        var security = AgentPipeSecurity.ForCurrentUser();
        var me = WindowsIdentity.GetCurrent().User!;
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

        var allowed = rules.Where(r => r.AccessControlType == AccessControlType.Allow).ToList();
        var denied = rules.Where(r => r.AccessControlType == AccessControlType.Deny).ToList();
        Assert.Single(allowed);
        Assert.Equal(me, allowed[0].IdentityReference);
        Assert.Single(denied);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), denied[0].IdentityReference);
        Assert.DoesNotContain(rules, r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.WorldSid, null));
    }

    [Fact]
    public void The_Running_Pipe_Carries_That_Security()
    {
        using var island = new Collector();
        using var client = new NamedPipeClientStream(
            ".", island.Name, PipeAccessRights.ReadPermissions | PipeAccessRights.WriteData, PipeOptions.None,
            System.Security.Principal.TokenImpersonationLevel.None, HandleInheritability.None);
        client.Connect(3000);

        var rules = client.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

        var me = WindowsIdentity.GetCurrent().User!;
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference == me);
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Deny
                                    && r.IdentityReference == new SecurityIdentifier(WellKnownSidType.NetworkSid, null));
        Assert.DoesNotContain(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference != me);
    }
}
