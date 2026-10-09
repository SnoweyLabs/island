using System.IO.Pipes;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Agents;

/// <summary>
/// The island's side of the pipe. Listens on the name it is given, for the current Windows user only. Each connection
/// gets one message of bounded size under a time limit; anything that is not exactly the expected shape is ignored. A
/// notice is raised on a thread-pool thread: the app moves it to its own thread. It survives bad clients, many clients
/// and a client that never writes: a fixed number of acceptors each take one connection at a time and are free again
/// within the read limit, so later clients wait their turn (they have their own connect limit) and nothing grows.
/// The server never writes: it closes its end after reading, which is the client's sign that the message was taken (the
/// pipe is two-way only for that). No window, no outside action, nothing written anywhere.
/// </summary>
public sealed class AgentPipeServer : IDisposable
{
    /// <summary>Connections that are listened for or read at once.</summary>
    private const int Acceptors = 64;

    /// <summary>One slot for each pipe instance that is listening or being read: a client that connects and stays silent holds one for the read limit, and no more than <see cref="Acceptors"/> are held at once.</summary>
    private readonly SemaphoreSlim _slots = new(Acceptors, Acceptors);

    [ThreadStatic]
    private static bool _inHandler;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private Task[] _running = [];
    private bool _started;
    private int _disposed;

    public AgentPipeServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        _pipeName = pipeName;
    }

    /// <summary>Raised on a thread-pool thread for every notice that arrives. A handler that throws is ignored.</summary>
    public event Action<AgentNotice>? NoticeReceived;

    /// <summary>
    /// Raised on a thread-pool thread for every message that is exactly the shape of version 1 or version 2 (WORK-ORDER-11 section 3), whether or not it raises a notice:
    /// the app keeps the sessions from them. A handler that throws is ignored.
    /// </summary>
    public event Action<SessionMessage>? MessageReceived;

    /// <summary>The table that says which events raise the notice: every connected helper's rows.</summary>
    public HelperSignalTable Table { get; set; } = AgentSignalTables.All;

    public bool IsListening { get; private set; }

    /// <summary>
    /// Begins listening. False when the pipe cannot be made (the name is already taken, or access is refused): the
    /// island then runs without notices. Calling it again changes nothing.
    /// </summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (_started || Volatile.Read(ref _disposed) != 0) return IsListening;
            _started = true;
            var first = TryCreate(first: true); // the first instance proves the name is ours
            if (first is null) return false;
            IsListening = true;
            _running = Enumerable.Range(0, Acceptors)
                .Select(i => Task.Run(() => Accept(i == 0 ? first : null)))
                .ToArray();
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try
        {
            Task.WaitAll(_running, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // the acceptors end by cancellation; nothing to report
        }

        // The connections that were being read let go of their pipe instances as the stop reaches them: the name is free when every slot is back.
        // (Not when a handler of an event disposes the server from inside its own connection: that connection's slot is its own and is given back only after the handler returns.)
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!_inHandler && _slots.CurrentCount < Acceptors && wait.ElapsedMilliseconds < 2000) Thread.Sleep(5);

        IsListening = false;
        _stop.Dispose();
    }

    private NamedPipeServerStream? TryCreate(bool first)
    {
        try
        {
            var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
            return NamedPipeServerStreamAcl.Create(
                _pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, options, AgentPipe.MaxMessageBytes, 0, AgentPipeSecurity.ForCurrentUser());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task Accept(NamedPipeServerStream? initial)
    {
        var token = _stop.Token;
        var server = initial;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _slots.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            server ??= TryCreate(first: false);
            if (server is null)
            {
                _slots.Release();
                await Pause(token);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                _slots.Release();
                break;
            }
            catch (IOException)
            {
                // The client wrote and closed before this acceptor got to it (Island.Notify waits for our close so it
                // does not; a rude client may). Nothing can be read from that instance any more.
                server.Dispose();
                server = null;
                _slots.Release();
                continue;
            }

            // The connection is read on a task of its own and gives its slot back when it is done: a client that connects and stays silent holds one slot for the time limit,
            // and no more than Acceptors of them are held at once; the rest wait their turn (they have their own connect limit).
            var connected = server;
            server = null;
            _ = Task.Run(() => Receive(connected, token), CancellationToken.None);
        }

        server?.Dispose();
    }

    private static async Task Pause(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    private async Task Receive(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            await ReceiveOne(pipe, token);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task ReceiveOne(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            using (pipe)
            {
                var message = await ReadOne(pipe, token);
                if (message is null || token.IsCancellationRequested) return;
                _inHandler = true;
                try
                {
                    MessageReceived?.Invoke(message);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // the sessions' handler threw: the notice below is still raised
                }

                try
                {
                    if (AgentNotice.From(message, Table) is { } notice) NoticeReceived?.Invoke(notice);
                }
                finally
                {
                    _inHandler = false;
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // a client that broke, or the island stopping: nothing to do
        }
        catch (Exception)
        {
            // a handler of the event threw: the server goes on
        }
    }

    /// <summary>One message: up to the first newline or the end of the client's writing, within the size and time limits.</summary>
    private static async Task<SessionMessage?> ReadOne(Stream pipe, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(AgentPipe.ReadTimeoutMs);
        var buffer = new byte[AgentPipe.MaxMessageBytes + 1];
        var total = 0;
        var end = -1;
        while (total < buffer.Length && end < 0)
        {
            int n;
            if (total == 0)
            {
                using var firstByte = CancellationTokenSource.CreateLinkedTokenSource(limit.Token);
                firstByte.CancelAfter(AgentPipe.FirstByteTimeoutMs);
                n = await pipe.ReadAsync(buffer.AsMemory(total), firstByte.Token);
            }
            else n = await pipe.ReadAsync(buffer.AsMemory(total), limit.Token);
            if (n == 0) break;
            total += n;
            end = buffer.AsSpan(0, total).IndexOf((byte)'\n');
        }

        var length = end >= 0 ? end : total;
        return length is 0 or > AgentPipe.MaxMessageBytes - 1 || (end < 0 && total == buffer.Length)
            ? null
            : SessionWire.TryParseAny(buffer.AsSpan(0, length));
    }
}
