using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Island.Agents;
using Island.Core;
using Xunit.Abstractions;
using static Island.Attack7B.Tests.Support;

namespace Island.Attack7B.Tests;

/// <summary>The island's side of the pipe on invented names, with clients that behave badly.</summary>
public class PipeAttackTests(ITestOutputHelper output)
{
    /// <summary>Connect, write (a broken pipe is allowed), optionally hold the connection open, then close.</summary>
    private static void Spray(string name, byte[] bytes, int holdMs = 0, int connectMs = 5000)
    {
        using var c = Connect(name, connectMs);
        try
        {
            c.Write(bytes);
            c.Flush();
        }
        catch (IOException)
        {
        }

        if (holdMs > 0) Thread.Sleep(holdMs);
    }

    private static byte[] Random(int seed, int n)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    private static void AssertStillWorks(Collector island, string folder)
    {
        Send(island.Name, ValidMessage(folder, [4242]), 8000);
        var n = island.Next(4000);
        Assert.NotNull(n);
        Assert.Equal(folder.Split('/')[^1], n.ProjectName);
    }

    public static TheoryData<string, byte[]> Garbage()
    {
        var valid = ValidMessage("x/island");
        var data = new TheoryData<string, byte[]>
        {
            { "2 KB random", Random(1, 2048) },
            { "100 KB random", Random(2, 100_000) },
            { "5000 bytes no newline", Utf8(new string('a', 5000)) },
            { "4096 bytes of json-ish no newline", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"" + new string('a', 4096 - 33) + "\"") },
            { "valid message padded past 4096 before the newline", [.. Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}"), .. Utf8(new string(' ', 4100)), (byte)'\n'] },
            { "a million newlines", Enumerable.Repeat((byte)'\n', 1_000_000).ToArray() },
            { "newline then valid", [(byte)'\n', .. valid] },
            { "binary zero", [0] },
            { "zeros 300", new byte[300] },
            { "zero then valid", [0, .. valid] },
            { "valid then zero (no newline)", [.. valid[..^1], 0] },
            { "valid json with trailing junk", [.. valid[..^1], .. Utf8("junk"), (byte)'\n'] },
            { "bom then valid", [0xEF, 0xBB, 0xBF, .. valid] },
            { "json array", Utf8("[1,2,3]\n") },
            { "json null", Utf8("null\n") },
            { "json string", Utf8("\"Stop\"\n") },
            { "empty object", Utf8("{}\n") },
            { "valid but version 2", Utf8("{\"v\":2,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid but version string", Utf8("{\"v\":\"1\",\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid but extra property", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1],\"z\":0}\n") },
            { "valid but repeated e", Utf8("{\"v\":1,\"e\":\"Stop\",\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid but chain of 17", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]}\n") },
            { "valid but chain id 0", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[0]}\n") },
            { "valid but chain id negative", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[-5]}\n") },
            { "valid but chain id past int", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[99999999999]}\n") },
            { "valid but chain id float", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1.5]}\n") },
            { "valid but chain id string", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[\"1\"]}\n") },
            { "valid but depth 4", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[[1]]}\n") },
            { "valid but folder 261 chars", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"" + new string('a', 261) + "\",\"c\":[1]}\n") },
            { "valid but event 33 chars", Utf8("{\"v\":1,\"e\":\"" + new string('S', 33) + "\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid but lone surrogate escape", Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"a\\ud800b\",\"c\":[1]}\n") },
            { "valid shape but event Foo", Utf8("{\"v\":1,\"e\":\"Foo\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid shape Notification idle_prompt", Utf8("{\"v\":1,\"e\":\"Notification\",\"k\":\"idle_prompt\",\"f\":\"x\",\"c\":[1]}\n") },
            { "valid shape Stop with kind permission_prompt on wrong event name case", Utf8("{\"v\":1,\"e\":\"stop\",\"k\":\"permission_prompt\",\"f\":\"x\",\"c\":[1]}\n") },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Garbage))]
    public void Holds_Garbage_Delivers_Nothing_And_A_Valid_Notice_Still_Arrives_Afterwards(string label, byte[] bytes)
    {
        using var island = new Collector();
        Spray(island.Name, bytes);
        Assert.Null(island.Next(300)); // "{label}": nothing from malformed or ignored shapes
        AssertStillWorks(island, "x/island");
        Assert.Null(island.Next(200));
        _ = label;
    }

    [Fact]
    public void Holds_Half_A_Message_Then_Silence_Is_Dropped_At_The_Time_Limit_And_The_Server_Goes_On()
    {
        using var island = new Collector();
        var half = ValidMessage()[..20];
        var clock = Stopwatch.StartNew();
        using var c = Connect(island.Name);
        c.Write(half);
        c.Flush();
        Assert.Null(island.Next(2200)); // never delivered; the connection is let go after ~1.5 s
        AssertStillWorks(island, "x/island");
        output.WriteLine($"half message held {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Holds_A_Valid_Message_In_Single_Byte_Writes_Is_Delivered()
    {
        using var island = new Collector();
        using (var c = Connect(island.Name))
        {
            foreach (var b in ValidMessage("x/island"))
            {
                c.WriteByte(b);
                c.Flush();
            }

            c.ReadAsync(new byte[1]).AsTask().Wait(3000);
        }

        Assert.Equal("island", island.Next(3000)?.ProjectName);
    }

    [Fact]
    public void Holds_A_Valid_Message_Trickled_Slower_Than_The_Limit_Is_Not_Delivered()
    {
        using var island = new Collector();
        var bytes = ValidMessage("x/island");
        using (var c = Connect(island.Name))
        {
            try
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    c.WriteByte(bytes[i]);
                    c.Flush();
                    Thread.Sleep(i < bytes.Length / 2 ? 80 : 0); // first half slowly: far past 1.5 s in all
                }
            }
            catch (IOException)
            {
            }
        }

        Assert.Null(island.Next(500));
        AssertStillWorks(island, "x/island");
    }

    [Fact]
    public void Holds_Two_Messages_In_One_Connection_Give_One_Notice_The_First()
    {
        using var island = new Collector();
        Send(island.Name, [.. ValidMessage("a/first"), .. ValidMessage("a/second")]);
        Assert.Equal("first", island.Next(3000)?.ProjectName);
        Assert.Null(island.Next(400));
    }

    [Fact]
    public void Holds_A_Crlf_Ended_Message_Is_Taken()
    {
        using var island = new Collector();
        var m = ValidMessage("a/island");
        SendRude(island.Name, [.. m[..^1], (byte)'\r', (byte)'\n']);
        Assert.Equal("island", island.Next(3000)?.ProjectName);
    }

    [Fact]
    public void Holds_The_Size_Limit_Counts_The_Newline_4096_Is_Taken_4097_Is_Refused()
    {
        using var island = new Collector();
        var core = Utf8("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"a/island\",\"c\":[1]}");
        byte[] Padded(int total) => [.. core, .. Enumerable.Repeat((byte)' ', total - core.Length - 1), (byte)10];
        Spray(island.Name, Padded(4097));
        Assert.Null(island.Next(700));
        Spray(island.Name, Padded(4096));
        Assert.NotNull(island.Next(1500));
    }

    // ---- many and rude --------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Sixty_Four_Idle_Same_User_Clients_Delay_A_Valid_Message_By_An_Unpredictable_Time_But_It_Arrives_And_Nothing_Grows()
    {
        // Server stays alive, bounded in threads and memory, and delivers the valid message in the end (all asserted below). WHEN varies a lot from run to run, so no tight bound is asserted.
        using var island = new Collector();
        var threadsBefore = Process.GetCurrentProcess().Threads.Count;
        var memBefore = GC.GetTotalMemory(true);
        var idle = Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            try
            {
                using var c = Connect(island.Name, 150_000);
                Thread.Sleep(2000); // connected and silent
            }
            catch (Exception e) when (e is IOException or TimeoutException)
            {
            }
        })).ToArray();
        Thread.Sleep(300);
        var clock = Stopwatch.StartNew();
        Send(island.Name, ValidMessage("x/island"), 150_000);
        var n = island.Next(150_000);
        var took = clock.ElapsedMilliseconds;
        var threadsDuring = Process.GetCurrentProcess().Threads.Count;
        Task.WaitAll(idle, TimeSpan.FromSeconds(200));
        var memAfter = GC.GetTotalMemory(true);
        output.WriteLine($"valid message behind 64 idle clients arrived after {took} ms; threads {threadsBefore} -> {threadsDuring} (64 of those are the test own tasks); memory {memBefore / 1024} -> {memAfter / 1024} KB");
        Assert.NotNull(n);
        Assert.Equal("island", n.ProjectName);
        Assert.True(memAfter - memBefore < 20 * 1024 * 1024, "memory grew by " + (memAfter - memBefore));
        Assert.True(took < 120_000, "arrived after " + took + " ms"); // measured: 5 s to 33 s here (64 clients); 87 s and 134 s for 200 clients
    }

    [Fact]
    public void Holds_Rude_Clients_Closing_At_Odd_Moments_Cannot_Hurt_Or_Fool_The_Server()
    {
        using var island = new Collector();
        var valid = ValidMessage("x/island");
        var tasks = Enumerable.Range(0, 300).Select(i => Task.Run(() =>
        {
            try
            {
                using var c = Connect(island.Name, 20_000);
                switch (i % 6)
                {
                    case 0: break; // connect and close
                    case 1: c.Write(valid[..(valid.Length / 2)]); break; // half and close
                    case 2: c.Write(valid[..^1]); c.Flush(); break; // all but newline (valid when the client closes): counts as a notice
                    case 3: c.Write(Random(i, 5000)); break;
                    case 4: c.Write([0]); c.Flush(); c.Dispose(); break;
                    default: c.Write([(byte)'\n']); break;
                }
            }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException)
            {
            }
        })).ToArray();
        Task.WaitAll(tasks, TimeSpan.FromSeconds(60));
        var got = island.TakeAll(50, 5000);
        output.WriteLine($"rude storm: {got.Count} notices (50 messages were whole)");
        Assert.All(got, n => Assert.Equal("island", n.ProjectName));
        Assert.True(got.Count <= 50);
        AssertStillWorks(island, "x/island2");
    }

    [Fact]
    public void Holds_Two_Thousand_Garbage_Connections_Do_Not_Grow_Threads_Or_Memory_Without_Bound()
    {
        using var island = new Collector();
        var threadsBefore = Process.GetCurrentProcess().Threads.Count;
        var memBefore = GC.GetTotalMemory(true);
        Parallel.For(0, 2000, new ParallelOptions { MaxDegreeOfParallelism = 24 }, i =>
        {
            try
            {
                Spray(island.Name, Random(i, i % 2 == 0 ? 5000 : 300), 0, 30_000);
            }
            catch (TimeoutException)
            {
            }
        });
        Thread.Sleep(2000);
        var threadsAfter = Process.GetCurrentProcess().Threads.Count;
        var memAfter = GC.GetTotalMemory(true);
        output.WriteLine($"2000 garbage connections: threads {threadsBefore} -> {threadsAfter}; memory {memBefore / 1024} -> {memAfter / 1024} KB");
        Assert.True(threadsAfter - threadsBefore < 60, $"threads {threadsBefore} -> {threadsAfter}");
        Assert.True(memAfter - memBefore < 20 * 1024 * 1024);
        AssertStillWorks(island, "x/island");
    }

    // ---- lifecycle ------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Dispose_During_Traffic_Returns_Quickly_Frees_The_Name_And_Delivers_Nothing_Later()
    {
        var name = NewName();
        var server = new AgentPipeServer(name);
        var after = 0;
        var disposedAt = long.MaxValue / 2;
        server.NoticeReceived += _ =>
        {
            if (Environment.TickCount64 > Interlocked.Read(ref disposedAt) + 50) Interlocked.Increment(ref after);
        };
        Assert.True(server.Start());
        using var stop = new CancellationTokenSource();
        var senders = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
                    c.Connect(200);
                    c.Write(ValidMessage());
                    c.Flush();
                }
                catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException or UnauthorizedAccessException)
                {
                }
            }
        })).ToArray();
        Thread.Sleep(700);
        var clock = Stopwatch.StartNew();
        server.Dispose();
        Interlocked.Exchange(ref disposedAt, Environment.TickCount64);
        var took = clock.ElapsedMilliseconds;
        Thread.Sleep(500);
        stop.Cancel();
        Task.WaitAll(senders, TimeSpan.FromSeconds(10));
        output.WriteLine($"Dispose under load took {took} ms; notices after it: {after}");
        Assert.True(took < 2500, took.ToString());
        Assert.Equal(0, after);
        Assert.False(server.IsListening);
        using var again = new AgentPipeServer(name);
        Assert.True(again.Start(), "the name was not freed by Dispose");
    }

    [Fact]
    public void Holds_Dispose_Twice_And_Start_After_Dispose_Are_Harmless()
    {
        var server = new AgentPipeServer(NewName());
        Assert.True(server.Start());
        server.Dispose();
        server.Dispose();
        Assert.False(server.Start());
        Assert.False(server.IsListening);
    }

    [Fact]
    public void Holds_Start_From_Many_Threads_At_Once_Starts_One_Listener()
    {
        var name = NewName();
        using var server = new AgentPipeServer(name);
        var answers = new bool[16];
        Parallel.For(0, 16, i => answers[i] = server.Start());
        Assert.All(answers, Assert.True);
        using var other = new AgentPipeServer(name);
        Assert.False(other.Start());
    }

    [Fact]
    public void Holds_A_Second_Server_On_The_Same_Name_Is_Refused_And_Harms_Nobody()
    {
        using var first = new Collector();
        using var second = new AgentPipeServer(first.Name);
        var seenBySecond = 0;
        second.NoticeReceived += _ => Interlocked.Increment(ref seenBySecond);
        Assert.False(second.Start());
        Assert.False(second.IsListening);
        AssertStillWorks(first, "x/island");
        second.Dispose();
        AssertStillWorks(first, "x/island");
        Assert.Equal(0, seenBySecond);
        // and after the first is gone the refused one does not wake up
        first.Dispose();
        Assert.False(second.Start());
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1000)]
    public void Holds_A_Very_Long_Name_Does_Not_Make_Start_Throw(int length)
    {
        using var server = new AgentPipeServer("island.attack7b." + new string('x', length));
        var ok = server.Start();
        output.WriteLine($"name of {length + 16} chars: Start() = {ok}");
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a b")]
    public void Holds_Odd_Names_Do_Not_Make_Start_Throw(string suffix)
    {
        using var server = new AgentPipeServer("island.attack7b." + Guid.NewGuid().ToString("N") + suffix);
        var ok = server.Start();
        output.WriteLine($"suffix '{suffix}': Start() = {ok}");
    }

    [Fact]
    public void Defect_A_Handler_That_Disposes_The_Server_Stalls_For_The_Whole_Two_Second_Wait_On_Itself()
    {
        var name = NewName();
        var server = new AgentPipeServer(name);
        var took = new TaskCompletionSource<long>();
        server.NoticeReceived += _ =>
        {
            var clock = Stopwatch.StartNew();
            server.Dispose(); // Task.WaitAll on the acceptor tasks, one of which is this very handler
            took.TrySetResult(clock.ElapsedMilliseconds);
        };
        Assert.True(server.Start());
        Send(name, ValidMessage(), 8000);
        Assert.True(took.Task.Wait(8000));
        output.WriteLine($"Dispose from inside the handler took {took.Task.Result} ms");
        Assert.True(took.Task.Result < 500, took.Task.Result.ToString());
    }

    [Fact]
    public void Holds_A_Slow_Handler_Loses_Nothing_For_Clients_That_Wait_Their_Turn()
    {
        using var island = new Collector();
        var got = 0;
        island.Server.NoticeReceived += _ =>
        {
            Thread.Sleep(300);
            Interlocked.Increment(ref got);
        };
        var senders = Enumerable.Range(0, 40).Select(_ => Task.Run(() => Send(island.Name, ValidMessage(), 30_000))).ToArray();
        Task.WaitAll(senders, TimeSpan.FromSeconds(60));
        Thread.Sleep(500);
        Assert.Equal(40, got);
    }

    [Fact]
    public void Holds_A_Handler_That_Throws_Is_Swallowed_Every_Time()
    {
        using var island = new Collector();
        island.Server.NoticeReceived += _ => throw new InvalidOperationException("boom");
        for (var i = 0; i < 40; i++) Send(island.Name, ValidMessage());
        AssertStillWorks(island, "x/island");
    }

    // ---- who may connect ------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Security_Is_The_Current_User_Allowed_And_The_Network_Denied_And_Nobody_Else()
    {
        var me = WindowsIdentity.GetCurrent().User!;
        var security = AgentPipeSecurity.ForCurrentUser();
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Assert.Equal(2, rules.Count);
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Deny && r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)));
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(me) && r.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite));
        var sddl = security.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        output.WriteLine(sddl);
        Assert.Matches(@"^D:(P|AI|PAI)?\(D;;0x1f019f;;;NU\)\(A;;0x1f019f;;;S-1-5-[0-9-]+\)$", sddl);
        Assert.StartsWith("D:", sddl);
        // the Deny comes first
        Assert.True(sddl.IndexOf("(D;", StringComparison.Ordinal) < sddl.IndexOf("(A;", StringComparison.Ordinal));
    }

    [Fact]
    public void Holds_The_Running_Pipe_Has_No_Other_Principal_In_Its_Acl()
    {
        using var island = new Collector();
        using var client = Connect(island.Name);
        var acl = client.GetAccessControl();
        var rules = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        var me = WindowsIdentity.GetCurrent().User!;
        output.WriteLine(acl.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.All(rules, r => Assert.True(
            r.IdentityReference.Equals(me) || r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)),
            "unexpected principal " + r.IdentityReference));
    }

    [Theory]
    [InlineData("machine")]
    [InlineData("127.0.0.1")]
    public void Holds_A_Loopback_Connection_Is_Not_A_Network_Logon_So_It_Cannot_Exercise_The_Network_Deny(string host)
    {
        // NOT PROVEN, documented: the network-deny rule can only be exercised by a second machine. Through the machine name or 127.0.0.1 the same
        // user connects with an INTERACTIVE token that holds no NETWORK SID, so the Deny does not apply to it (and the same user is trusted anyway).
        if (host == "machine") host = Environment.MachineName;
        var name = NewName();
        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 0, AgentPipeSecurity.ForCurrentUser());
        var seen = Task.Run(() =>
        {
            server.WaitForConnection();
            var network = true;
            server.RunAsClient(() => network = WindowsIdentity.GetCurrent().Groups!.Contains(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)));
            return network;
        });
        using var client = new NamedPipeClientStream(host, name, PipeDirection.InOut, PipeOptions.None);
        client.Connect(3000);
        Assert.True(seen.Wait(3000));
        output.WriteLine($"via {host}: token has NETWORK SID = {seen.Result}");
        Assert.False(seen.Result);
    }

    [Fact]
    public void Holds_Without_FirstPipeInstance_Rule_A_Same_User_Process_Can_Still_Join_The_Name_As_A_Server_Which_Is_Inside_The_Trust()
    {
        // Documented, not a defect: the allow rule gives the user FullControl, which includes creating more instances. The island needs that for its
        // own acceptors, so a same-user program could also listen and hear some hooks. The order says the same user is trusted.
        using var island = new Collector();
        using var joined = new NamedPipeServerStream(island.Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.NotNull(joined);
    }
}
