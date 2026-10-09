using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Island.Agents;
using Island.Core;

namespace Island.Agents.Tests;

/// <summary>An invented pipe name, a server that collects what arrives, and Island.Notify run as a child process.</summary>
internal static class PipeTestSupport
{
    public static string NewName() => "island-test-" + Guid.NewGuid().ToString("N");

    /// <summary>The shape of the hooks page's Stop example, with a folder that ends in "island" and is made up.</summary>
    public static string StopJson(string folder) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["session_id"] = "abc123",
        ["prompt_id"] = "550e8400-e29b-41d4-a716-446655440000",
        ["cwd"] = folder,
        ["permission_mode"] = "default",
        ["hook_event_name"] = "Stop",
        ["last_assistant_message"] = "Done.",
        ["tool_use_count"] = 5,
        ["turn_number"] = 3,
    });

    public static string NotificationJson(string folder, string kind) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["session_id"] = "abc123",
        ["cwd"] = folder,
        ["hook_event_name"] = "Notification",
        ["notification_type"] = kind,
        ["message"] = "m",
    });

    public static string IslandFolder() => Path.Combine(Path.GetTempPath(), "island");

    public static byte[] ValidMessage(string folder = "x/island", int[]? chain = null) =>
        AgentWire.Encode("Stop", "", folder, chain ?? [1]);

    /// <summary>Starts a server on a new invented name; the notices that arrive are collected.</summary>
    public sealed class Collector : IDisposable
    {
        private readonly BlockingCollection<AgentNotice> _seen = [];

        public Collector()
        {
            Name = NewName();
            Server = new AgentPipeServer(Name);
            Server.NoticeReceived += _seen.Add;
            Assert.True(Server.Start());
        }

        public string Name { get; }

        public AgentPipeServer Server { get; }

        public int Count => _seen.Count;

        public AgentNotice? Next(int ms) => _seen.TryTake(out var n, ms) ? n : null;

        public List<AgentNotice> TakeAll(int expected, int ms)
        {
            var list = new List<AgentNotice>();
            var end = Environment.TickCount64 + ms;
            while (list.Count < expected && Environment.TickCount64 < end)
                if (_seen.TryTake(out var n, 100)) list.Add(n);
            return list;
        }

        public void Dispose() => Server.Dispose();
    }

    public static NamedPipeClientStream Connect(string name, int ms = 3000)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
        client.Connect(ms);
        return client;
    }

    /// <summary>A client as Island.Notify is: writes, then waits (bounded) for the island to close its end.</summary>
    public static void Send(string name, byte[] bytes, int ms = 5000)
    {
        using var client = Connect(name, ms);
        client.Write(bytes);
        client.Flush();
        client.ReadAsync(new byte[1]).AsTask().Wait(ms);
    }

    /// <summary>A rude client: writes and closes at once.</summary>
    public static void SendRude(string name, byte[] bytes, int ms = 5000)
    {
        using var client = Connect(name, ms);
        client.Write(bytes);
        client.Flush();
        // A client that closes the very moment it has written can beat the server's acceptor on a loaded machine, and then nothing can be read (the
        // server says so in its own comment); this one closes a moment later, which is what "taken when the client closes" is about.
        Thread.Sleep(150);
    }

    public sealed record NotifyRun(int ExitCode, string Output, string Error, TimeSpan Elapsed, bool TimedOut);

    /// <summary>
    /// Runs the Island.Notify built beside the tests as a child process: a hidden process with all three streams
    /// redirected. <paramref name="stdin"/> is written and, when <paramref name="closeStdin"/>, closed.
    /// </summary>
    public static NotifyRun RunNotify(byte[] stdin, string[] args, bool closeStdin = true, int waitMs = 15000)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Island.Notify.exe");
        var dll = Path.Combine(AppContext.BaseDirectory, "Island.Notify.dll");
        var psi = File.Exists(exe)
            ? new ProcessStartInfo(exe)
            : new ProcessStartInfo("dotnet") { ArgumentList = { dll } };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        var clock = Stopwatch.StartNew();
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.BaseStream.Write(stdin);
            process.StandardInput.BaseStream.Flush();
            if (closeStdin) process.StandardInput.Close();
        }
        catch (IOException)
        {
            // the program ended before it read everything (an oversized input): that is allowed
        }

        var exited = process.WaitForExit(waitMs);
        clock.Stop();
        if (!exited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return new NotifyRun(exited ? process.ExitCode : -1, output.Result, error.Result, clock.Elapsed, !exited);
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A server on a new invented name that keeps every message of version 1 or 2 that arrives (what the sessions are made from), notice or not.</summary>
    public sealed class MessageCollector : IDisposable
    {
        private readonly BlockingCollection<Island.Core.Agents.Sessions.SessionMessage> _seen = [];

        public MessageCollector()
        {
            Name = NewName();
            Server = new AgentPipeServer(Name);
            Server.MessageReceived += _seen.Add;
            Assert.True(Server.Start());
        }

        public string Name { get; }

        public AgentPipeServer Server { get; }

        public Island.Core.Agents.Sessions.SessionMessage? Next(int ms) => _seen.TryTake(out var m, ms) ? m : null;

        public void Dispose() => Server.Dispose();
    }
}
