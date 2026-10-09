using System.Diagnostics;
using System.IO.Pipes;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Notify;

/// <summary>
/// Run by a helper's hook (Claude Code, Codex). Reads the hook's JSON from standard input (at most 64 KB), tells the running island in one attempt through a named
/// pipe, and ends. It ends with code 0 on every path, prints nothing, and opens no file that its input names. Arguments (WORK-ORDER-11 section 3): <c>--agent &lt;name&gt;</c>
/// (Claude Code when absent, so entries written before today keep working), <c>--event &lt;name&gt;</c> (for a helper whose input does not name its event) and at most one
/// other argument, the name of another pipe (what every test does); a pipe name never begins with a hyphen. It says something only for an event the signal table knows,
/// as a message of version 2 with the session's id and the time at which this very process was created.
/// </summary>
internal static class Program
{
    /// <summary>A hook whose input never ends must not leave this program hanging.</summary>
    private const int StdinWaitMs = 2000;

    private static int Main(string[] args)
    {
        try
        {
            // A name that was given but is not plain, an option that is not known, a second pipe name: nothing is sent, and the real pipe is never used instead.
            if (NotifyArguments.Parse(args) is not { } options) return 0;
            var pipeName = options.PipeName ?? AgentPipe.ForThisUser();

            var read = Task.Run(() => ReadLimited(Console.OpenStandardInput(), AgentPipe.StdinLimitBytes));
            if (!read.Wait(StdinWaitMs)) return 0;

            var input = HookInputReader.Read(read.Result, options.Event);
            if (input is null) return 0;
            var eventName = options.Event ?? input.Event;
            var kind = AgentSignalTables.KindOf(input);
            if (AgentSignalTables.All.Match(options.Agent, eventName, kind) is null) return 0; // an event the island does not know: silent, as ever

            var chain = ProcessChain.Build(Environment.ProcessId, ProcessSnapshot.ParentMap());
            // The moment this process was created, on the system's steady counter (milliseconds since Windows started, the counter the island reads when a message arrives):
            // the counter's reading now minus how long ago Windows says this process was created. The program's start-up takes longer than the gap between two hooks, so the
            // moment of sending would put two messages in the wrong order; the moment of creation does not.
            var age = DateTime.Now - Process.GetCurrentProcess().StartTime;
            var startedAt = Math.Max(0, Environment.TickCount64 - (long)Math.Max(0, age.TotalMilliseconds));
            var message = SessionWire.Encode(options.Agent, eventName, kind, input.SessionId, startedAt, input.Folder, chain);

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(AgentPipe.ConnectTimeoutMs); // one attempt, as the order says: the island makes a free instance for every client, so none is left waiting
            pipe.Write(message);
            pipe.Flush();
            // The island closes its end once it has the message; closing first could lose it when the island is busy.
            // Bounded, so a stuck island never holds this program.
            pipe.ReadAsync(new byte[1]).AsTask().Wait(AgentPipe.ConnectTimeoutMs);
            return 0;
        }
        catch
        {
            return 0; // island not running, bad input, anything: the helper is never held up and never sees an error
        }
    }

    private static byte[] ReadLimited(Stream input, int limit)
    {
        var buffer = new byte[limit];
        var total = 0;
        while (total < limit)
        {
            var n = input.Read(buffer, total, limit - total);
            if (n <= 0) break;
            total += n;
        }

        return buffer.AsSpan(0, total).ToArray();
    }
}
