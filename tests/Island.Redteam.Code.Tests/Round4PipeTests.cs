using System.Diagnostics;
using System.IO.Pipes;
using Island.Agents;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Redteam.Code.Tests;

/// <summary>ROUND 4: the pipe server's Dispose after bf24c4d and ec974c8 (it waits for the slots; a handler that disposes from inside its own connection does not). A pipe name of the test's own.</summary>
public sealed class Round4PipeTests
{
    private static string NewName() => "island-redteam-round4-" + Guid.NewGuid().ToString("N");

    private static readonly byte[] Wire = SessionWire.Encode("claude", "Stop", "", "s", 1, "Q:\\Invented\\Alpha", [100]);

    [Fact]
    public void Held_A_Server_Disposed_With_Messages_And_Silent_Clients_In_Flight_Frees_The_Name_At_Once_Thirty_Times_Over()
    {
        // The point of the wait in Dispose: when it returns, the name is free, so the next Start (the first instance of the name) succeeds. Each round: some silent clients, some messages
        // written and left, a dispose, an immediate new server on the same name.
        var name = NewName();
        var slowest = 0L;
        for (var round = 0; round < 30; round++)
        {
            var server = new AgentPipeServer(name);
            Assert.True(server.Start(), $"round {round}: the name was not free after the last Dispose");
            var clients = new List<NamedPipeClientStream>();
            for (var i = 0; i < 6; i++)
            {
                var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
                c.Connect(2000);
                if (i % 2 == 0)
                {
                    c.Write(Wire);
                    c.Flush();
                }

                clients.Add(c);
            }

            var watch = Stopwatch.StartNew();
            server.Dispose();
            slowest = Math.Max(slowest, watch.ElapsedMilliseconds);
            foreach (var c in clients) c.Dispose();
        }

        Assert.True(slowest < 2500, $"the slowest Dispose took {slowest} ms");
    }

    [Fact]
    public void Held_A_Handler_That_Disposes_The_Server_From_Inside_Its_Own_Connection_Returns_Quickly_And_The_Name_Is_Free_Afterwards()
    {
        var name = NewName();
        var server = new AgentPipeServer(name);
        var disposed = new ManualResetEventSlim();
        var took = new long[1];
        server.MessageReceived += _ =>
        {
            var watch = Stopwatch.StartNew();
            server.Dispose();
            took[0] = watch.ElapsedMilliseconds;
            disposed.Set();
        };
        Assert.True(server.Start());
        using (var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None))
        {
            c.Connect(2000);
            c.Write(Wire);
            c.Flush();
            Assert.True(disposed.Wait(8000), "the handler never ran");
        }

        Assert.True(took[0] < 1500, $"a handler waited {took[0]} ms for its own slot");
        Thread.Sleep(300);
        using var next = new AgentPipeServer(name);
        Assert.True(next.Start(), "the name is still held after the handler's Dispose");
    }

    [Fact]
    public void Held_A_Dispose_From_Outside_While_A_Handler_Is_Blocked_Waits_No_Longer_Than_The_Limit_And_Does_Not_Hang()
    {
        // The bounded cost of the wait: a handler that does not return (it would be a bug in the app's handler) holds its slot; the UI thread's Dispose waits at most the 2 s the code names
        // (plus the 2 s of the acceptors' own wait before it), then goes on. The test states the ceiling: four seconds and a half.
        var name = NewName();
        var server = new AgentPipeServer(name);
        var release = new ManualResetEventSlim();
        var inHandler = new ManualResetEventSlim();
        server.MessageReceived += _ =>
        {
            inHandler.Set();
            release.Wait(15000);
        };
        Assert.True(server.Start());
        using var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
        c.Connect(2000);
        c.Write(Wire);
        c.Flush();
        Assert.True(inHandler.Wait(8000));
        var watch = Stopwatch.StartNew();
        server.Dispose();
        var took = watch.ElapsedMilliseconds;
        release.Set();
        Assert.True(took < 4500, $"Dispose took {took} ms with a handler blocked");
    }

    [Fact]
    public void Held_The_Thread_Flag_Is_Cleared_After_The_Handler_So_A_Later_Dispose_On_The_Same_Pool_Thread_Waits()
    {
        // _inHandler is [ThreadStatic] and set round the two handlers: after both return, the same thread (a pool thread is reused) must not go on skipping the wait. By the source: the flag is
        // reset in a finally that covers the second handler, and the first handler's own exceptions are caught above it.
        var source = Repo.Text("src", "Island.Agents", "AgentPipeServer.cs");
        var set = source.IndexOf("_inHandler = true;", StringComparison.Ordinal);
        var reset = source.IndexOf("_inHandler = false;", StringComparison.Ordinal);
        Assert.True(set > 0 && reset > set);
        Assert.Contains("finally", source[set..reset], StringComparison.Ordinal);
    }
}
