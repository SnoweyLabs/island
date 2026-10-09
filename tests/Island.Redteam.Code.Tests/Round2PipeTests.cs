using System.IO.Pipes;
using System.Text;
using Island.Agents;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 2 (code-2-*): the slots of AgentPipeServer (2c48c4e). A pipe name of the test's own; every client is the test's.</summary>
public sealed class Round2PipeTests
{
    private static string NewName() => "island-redteam-round2-" + Guid.NewGuid().ToString("N");

    private static NamedPipeClientStream Client(string name) => new(".", name, PipeDirection.InOut, PipeOptions.None);

    private static int ConnectSilent(string name, int count, int connectMs, List<NamedPipeClientStream> keep)
    {
        var ok = 0;
        var tasks = Enumerable.Range(0, count).Select(_ => Task.Factory.StartNew(() =>
        {
            var c = Client(name);
            try
            {
                c.Connect(connectMs);
                lock (keep) keep.Add(c);
                Interlocked.Increment(ref ok);
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                c.Dispose();
            }
        }, TaskCreationOptions.LongRunning)).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(30)), "clients did not finish");
        return ok;
    }

    [Fact]
    public async Task Every_Slot_Comes_Back_After_Clients_That_Leave_Rudely_At_Every_Step()
    {
        // Slot leaks on any exit path: a client that connects and goes at once, one that writes half a message and goes, one that writes a whole message and goes without waiting for
        // the island's close, one that writes garbage. After 800 of them (13 times the 64 slots) and the read limits' time, 64 silent clients can all connect again at once.
        var name = NewName();
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var server = new AgentPipeServer(name);
        server.MessageReceived += m => seen.Add(m.SessionId);
        Assert.True(server.Start());
        var wire = SessionWire.Encode("claude", "Stop", "", "s", 1, "Q:\\Invented\\Alpha", [100]);
        var tasks = Enumerable.Range(0, 800).Select(i => Task.Factory.StartNew(() =>
        {
            try
            {
                using var c = Client(name);
                c.Connect(8000);
                switch (i % 4)
                {
                    case 0:
                        break;
                    case 1:
                        c.Write("{\"v\":2,"u8);
                        c.Flush();
                        break;
                    case 2:
                        c.Write(wire);
                        c.Flush();
                        break;
                    default:
                        c.Write(new byte[] { 0xFF, 0xFE, 0x0A });
                        c.Flush();
                        break;
                }
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                // a client that the test itself lost: no matter
            }
        }, TaskCreationOptions.LongRunning)).ToArray();
        Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(120)));
        await Task.Delay(AgentPipe.ReadTimeoutMs + 1500);

        var keep = new List<NamedPipeClientStream>();
        try
        {
            var connected = ConnectSilent(name, 64, 3000, keep);
            Assert.Equal(64, connected);
        }
        finally
        {
            lock (keep) foreach (var c in keep) c.Dispose();
        }
    }

    [Fact]
    public async Task A_Good_Notice_Waits_Behind_A_Full_House_Of_Silent_Clients_For_Their_First_Byte_Limit_And_Is_Then_Taken()
    {
        // The limit stated: 64 silent clients of the same Windows user hold every slot for the first-byte limit (1 s); Island.Notify's own connect limit is 300 ms, so a notice that
        // arrives in that second is dropped (Notify says nothing and exits). A class text says "later clients wait their turn (they have their own connect limit)": by design.
        var name = NewName();
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var server = new AgentPipeServer(name);
        server.MessageReceived += m => seen.Add(m.SessionId);
        Assert.True(server.Start());
        var keep = new List<NamedPipeClientStream>();
        try
        {
            Assert.Equal(64, ConnectSilent(name, 64, 3000, keep));
            using var early = Client(name);
            Assert.Throws<TimeoutException>(() => early.Connect(AgentPipe.ConnectTimeoutMs));

            await Task.Delay(AgentPipe.FirstByteTimeoutMs + 600);
            using var late = Client(name);
            late.Connect(2000);
            late.Write(SessionWire.Encode("claude", "Stop", "", "late", 1, "Q:\\Invented\\Alpha", [100]));
            late.Flush();
            await late.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(200);
            Assert.Contains("late", seen);
        }
        finally
        {
            lock (keep) foreach (var c in keep) c.Dispose();
        }
    }

    [Fact]
    public async Task Dispose_With_Every_Slot_Held_Returns_At_Once_Frees_The_Name_And_Leaves_No_Unobserved_Exception()
    {
        var unobserved = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        void OnUnobserved(object? s, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            for (var round = 0; round < 6; round++)
            {
                var name = NewName();
                var server = new AgentPipeServer(name);
                Assert.True(server.Start());
                var keep = new List<NamedPipeClientStream>();
                try
                {
                    Assert.Equal(64, ConnectSilent(name, 64, 3000, keep));
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    server.Dispose();
                    Assert.True(clock.ElapsedMilliseconds < 3500, $"Dispose took {clock.ElapsedMilliseconds} ms");
                    server.Dispose(); // twice
                    using var again = new AgentPipeServer(name);
                    Assert.True(again.Start(), "the name was not freed by Dispose");
                }
                finally
                {
                    lock (keep) foreach (var c in keep) c.Dispose();
                }
            }

            await Task.Delay(300);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(200);
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [Fact]
    public void A_Server_Disposed_Before_It_Was_Started_And_One_Started_Twice_Are_Plain()
    {
        var name = NewName();
        var never = new AgentPipeServer(name);
        never.Dispose();
        Assert.False(never.Start());
        using var twice = new AgentPipeServer(NewName());
        Assert.True(twice.Start());
        Assert.True(twice.Start());
        using var clash = new AgentPipeServer(name + "x");
        Assert.True(clash.Start());
        Assert.Throws<ArgumentException>(() => new AgentPipeServer(""));
    }

    [Fact]
    public async Task A_Handler_That_Blocks_Holds_Its_Slot_And_Nothing_Else_And_One_That_Throws_Frees_It()
    {
        var name = NewName();
        var gate = new ManualResetEventSlim(false);
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var server = new AgentPipeServer(name);
        server.MessageReceived += m =>
        {
            if (m.SessionId.StartsWith("block", StringComparison.Ordinal)) gate.Wait(TimeSpan.FromSeconds(20));
            if (m.SessionId.StartsWith("throw", StringComparison.Ordinal)) throw new InvalidOperationException("a handler that throws");
            seen.Add(m.SessionId);
        };
        Assert.True(server.Start());
        async Task Send(string id)
        {
            using var c = Client(name);
            c.Connect(3000);
            c.Write(SessionWire.Encode("claude", "Stop", "", id, 1, "Q:\\Invented\\Alpha", [100]));
            c.Flush();
            await c.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }

        try
        {
            for (var i = 0; i < 70; i++) await Send("throw" + i); // more than the 64 slots: every thrown handler gave its slot back
            for (var i = 0; i < 3; i++) { var id = "block" + i; _ = Task.Factory.StartNew(() => Send(id), TaskCreationOptions.LongRunning); }
            await Task.Delay(500);
            await Send("after");
            await Task.Delay(300);
            Assert.Contains("after", seen);
        }
        finally
        {
            gate.Set();
        }
    }
}
