using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Island.Agents;
using Island.Core.Agents.Sessions;

namespace Island.Attack11.Tests;

/// <summary>A server on a new invented name that keeps every message of version 1 or 2 that arrives. Never the real pipe's name.</summary>
internal sealed class PipeCollector : IDisposable
{
    private readonly BlockingCollection<SessionMessage> _seen = [];

    public PipeCollector()
    {
        Name = "island-attack11-" + Guid.NewGuid().ToString("N");
        Server = new AgentPipeServer(Name);
        Server.MessageReceived += _seen.Add;
        Assert.True(Server.Start());
    }

    public string Name { get; }

    public AgentPipeServer Server { get; }

    public SessionMessage? Next(int ms) => _seen.TryTake(out var m, ms) ? m : null;

    public int Count => _seen.Count;

    public void Dispose() => Server.Dispose();
}

internal static class Pipe
{
    /// <summary>A client as Island.Notify is: writes, then waits (bounded) for the island to close its end. An oversized message may be cut off by the island: allowed.</summary>
    public static void Send(string name, byte[] bytes)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
        client.Connect(5000);
        try
        {
            client.Write(bytes);
            client.Flush();
            client.ReadAsync(new byte[1]).AsTask().Wait(5000);
        }
        catch (IOException)
        {
            // the island closes its end of an oversized message before it has been written to the end
        }
    }
}

internal sealed record NotifyRun(int ExitCode, string Output, string Error, TimeSpan Elapsed, bool TimedOut);

internal static class NotifyExe
{
    /// <summary>
    /// Runs the Island.Notify built beside the tests as a child process: a hidden process with all three streams redirected (as the existing NotifyTests of
    /// Island.Agents.Tests do), on an invented pipe name. It is a small console program that reads the process list and writes to a pipe; it opens no window.
    /// </summary>
    public static NotifyRun Run(byte[] stdin, string[] args, bool closeStdin = true, int waitMs = 15000)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Island.Notify.exe");
        var dll = Path.Combine(AppContext.BaseDirectory, "Island.Notify.dll");
        var psi = File.Exists(exe) ? new ProcessStartInfo(exe) : new ProcessStartInfo("dotnet") { ArgumentList = { dll } };
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
            // the program ended before it read everything
        }

        var exited = process.WaitForExit(waitMs);
        clock.Stop();
        if (!exited) process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return new NotifyRun(exited ? process.ExitCode : -1, output.Result, error.Result, clock.Elapsed, !exited);
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static string Hook(string ev, string? kind = null, string session = "s1", string cwd = @"Q:\Invented\island", string? tool = null)
    {
        var parts = new List<string> { $"\"session_id\":\"{session}\"", $"\"cwd\":\"{cwd.Replace("\\", "\\\\")}\"", $"\"hook_event_name\":\"{ev}\"" };
        if (kind is not null) parts.Add($"\"notification_type\":\"{kind}\"");
        if (tool is not null) parts.Add($"\"tool_name\":\"{tool}\"");
        return "{" + string.Join(",", parts) + "}";
    }
}
