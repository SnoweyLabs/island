using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Island.Agents;
using Island.Core;

namespace Island.Attack7B.Tests;

/// <summary>Invented pipe names only, a collector for a server, and Island.Notify run as a bounded child process.</summary>
internal static class Support
{
    public static string NewName() => "island.attack7b." + Guid.NewGuid().ToString("N");

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static string StopJson(string folder, string? extra = null) =>
        "{\"session_id\":\"alpha\",\"cwd\":" + JsonSerializer.Serialize(folder)
        + ",\"hook_event_name\":\"Stop\"" + (extra is null ? "" : "," + extra) + "}";

    public static byte[] ValidMessage(string folder = "x/island", int[]? chain = null) =>
        AgentWire.Encode("Stop", "", folder, chain ?? [1]);

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

    public static void SendRude(string name, byte[] bytes, int ms = 5000)
    {
        using var client = Connect(name, ms);
        client.Write(bytes);
        client.Flush();
    }

    public sealed record NotifyRun(int ExitCode, string Output, string Error, TimeSpan Elapsed, bool TimedOut);

    private static ProcessStartInfo NotifyStart(string[] args)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Island.Notify.exe");
        var dll = Path.Combine(AppContext.BaseDirectory, "Island.Notify.dll");
        var psi = File.Exists(exe) ? new ProcessStartInfo(exe) : new ProcessStartInfo("dotnet") { ArgumentList = { dll } };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.RedirectStandardInput = psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return psi;
    }

    /// <summary>Island.Notify with an invented pipe name as the first argument. Hard timeout, then the whole tree is killed.</summary>
    public static NotifyRun RunNotify(byte[] stdin, string[] args, bool closeStdin = true, int waitMs = 15000)
    {
        if (args.Length == 0) throw new InvalidOperationException("Island.Notify is never run without a pipe-name argument here.");
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(NotifyStart(args))!;
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
            // the program ended before it read everything: allowed
        }

        var exited = process.WaitForExit(waitMs);
        clock.Stop();
        if (!exited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return new NotifyRun(exited ? process.ExitCode : -1, output.Result, error.Result, clock.Elapsed, !exited);
    }

    /// <summary>Writes <paramref name="total"/> bytes as fast as the program takes them, in 64 KB pieces, then closes.</summary>
    public static NotifyRun RunNotifyStreaming(long total, string[] args, int waitMs = 20000)
    {
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(NotifyStart(args))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var chunk = new byte[65536];
        Array.Fill(chunk, (byte)' ');
        try
        {
            for (long sent = 0; sent < total && !process.HasExited; sent += chunk.Length)
                process.StandardInput.BaseStream.Write(chunk);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        var exited = process.WaitForExit(waitMs);
        clock.Stop();
        if (!exited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return new NotifyRun(exited ? process.ExitCode : -1, output.Result, error.Result, clock.Elapsed, !exited);
    }

    public static int LiveNotifyProcesses() => Process.GetProcessesByName("Island.Notify").Length;
}
