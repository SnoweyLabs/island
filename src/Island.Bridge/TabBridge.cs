using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Island.Core;

namespace Island.Bridge;

/// <summary>
/// The island's side of extension/PROTOCOL.md: a WebSocket listener on the loopback address only. Every
/// connection is read on its own task, with its own deadlines, so a slow or hostile one never holds up the
/// others or the app. <see cref="Changed"/> is raised on a pool thread, at most once per 50 ms burst.
/// Nothing that arrives is written anywhere: it lives in <see cref="Model"/>, in memory.
/// </summary>
public sealed class TabBridge : ITabSource, IAddonStatus, IDisposable
{
    /// <summary>Chosen by the B4 agent: a few browsers and profiles at once, not an unbounded crowd.</summary>
    private const int MaxConnections = 8;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(50);

    private readonly IReadOnlyList<int> _ports;
    private readonly TimeSpan _silenceLimit;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, BridgeLink> _links = new();
    private readonly Timer _debounce;
    private TcpListener? _listener;
    private int _pendingChange;
    private int _disposed;
    private int _open;
    private long _nextId;
    private readonly bool _acceptPretendOrigin;

    /// <param name="ports">The ports to try in turn; the protocol's five when null.</param>
    /// <param name="silenceLimit">How long a connection may stay silent; the protocol's 60 s when null (the smoke console shortens it).</param>
    public TabBridge(IReadOnlyList<int>? ports = null, TimeSpan? silenceLimit = null)
    {
        _ports = ports ?? TabProtocol.Ports;
        _acceptPretendOrigin = ports is not null; // a bridge on ports of its own is a test bridge; the real one accepts only the add-on
        _silenceLimit = silenceLimit ?? TabProtocol.SilenceLimit;
        _debounce = new Timer(_ => RaiseChanged());
        Counts.OnRefused = count =>
        {
            if (count == 1) Raise(AddonEventKind.Refused, 1); // only the first: any web page can knock as often as it likes
        };
    }

    /// <summary>
    /// What happened to the listener (WORK-ORDER-9 section 2): a kind and, for refusals, a number. The bridge writes nothing itself; the app turns each into a
    /// log line. Raised on whatever thread it happened on; a subscriber that throws is passed over and never hurts the bridge. Subscribe before <see cref="Start"/>.
    /// </summary>
    public event Action<AddonEvent>? AddonHappened;

    private void Raise(AddonEventKind kind, int count = 0)
    {
        foreach (var handler in AddonHappened?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<AddonEvent>)handler)(new AddonEvent(kind, count));
            }
            catch (Exception)
            {
                // A log that cannot be written is no reason to lose a connection or a slot.
            }
        }
    }

    /// <summary>How many browsers are connected now.</summary>
    public int Browsers => Model.ConnectionCount;

    public TabModel Model { get; } = new();

    /// <summary>The address actually listened on, once <see cref="Start"/> succeeded.</summary>
    public IPEndPoint? Endpoint { get; private set; }

    public BridgeCounts Counts { get; } = new();

    public bool Connected => Model.Connected;

    public IReadOnlyList<TabInfo> Tabs => Model.Tabs;

    public event Action? Changed;

    public byte[]? IconPng(string tabKey) => Model.IconPng(tabKey);

    /// <summary>Starts listening on the first free port. Returns the port, or null when none could be had (fail soft: the island simply has no tabs).</summary>
    public int? Start()
    {
        foreach (var port in _ports)
        {
            var listener = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                continue;
            }

            _listener = listener;
            Endpoint = (IPEndPoint)listener.LocalEndpoint;
            _ = Task.Run(() => AcceptLoop(listener));
            Raise(AddonEventKind.ListenerOn);
            return port;
        }

        Raise(AddonEventKind.ListenerOff);
        return null;
    }

    /// <summary>Queues a frame to one connection without waiting. False when that connection is gone.</summary>
    public bool TrySend(string connectionId, string frame)
    {
        if (!_links.TryGetValue(connectionId, out var link)) return false;
        _ = link.Send(frame);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        var refused = Volatile.Read(ref Counts.Refused);
        if (refused > 1) Raise(AddonEventKind.Stopped, refused); // the first was written when it came; the total when the listener stops
        _stop.Cancel();
        _listener?.Stop();
        foreach (var link in _links.Values) link.Abort();
        _debounce.Dispose();
    }

    private async Task AcceptLoop(TcpListener listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await listener.AcceptSocketAsync(_stop.Token);
            }
            catch (SocketException)
            {
                // A connection that went before it was taken, or a listener that keeps failing: a short pause, so that a failing listener cannot spin a core.
                try
                {
                    await Task.Delay(100, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            if (!ConnectionOwner.IsOurSessionOrUnknown(socket))
            {
                // another person's add-on (another logged-in session of this computer): turned away, and it tries the next port, where its own island listens (Dan's P26, WORK-ORDER-13)
                Counts.Add(ref Counts.TurnedAway);
                socket.Dispose();
                continue;
            }

            if (Interlocked.Increment(ref _open) > MaxConnections)
            {
                Interlocked.Decrement(ref _open);
                Counts.Add(ref Counts.TurnedAway);
                socket.Dispose();
                continue;
            }

            _ = Task.Run(() => Serve(socket));
        }
    }

    private async Task Serve(Socket socket)
    {
        var id = "c" + Interlocked.Increment(ref _nextId);
        WebSocket? ws = null;
        try
        {
            using var stream = new NetworkStream(socket, ownsSocket: true);
            using var hello = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            hello.CancelAfter(TabProtocol.HelloDeadline);

            if (await Handshake.Run(stream, Counts, hello.Token, _acceptPretendOrigin) is not true) return;
            ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            var link = new BridgeLink(id, ws);

            var first = await link.Receive(hello.Token);
            if (first.Kind != FrameKind.Text || TabMessages.ParseAddon(first.Text).Message is not HelloMessage h)
            {
                Counts.Add(ref Counts.NoHello);
                await link.Close(WebSocketCloseStatus.PolicyViolation);
                return;
            }

            _links[id] = link;
            if (Model.Open(id, h.Profile) is { } replaced && _links.TryRemove(replaced, out var old)) old.Abort();
            Counts.Add(ref Counts.Announced);
            Raise(AddonEventKind.Connected);
            await link.Send(TabMessages.Welcome());
            MarkChanged();
            await ReadLoop(link);
        }
        catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
        {
            Counts.Add(ref Counts.TimedOut);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or WebSocketException or SocketException or ObjectDisposedException)
        {
            // The peer went away or broke the protocol at the socket level: the connection simply ends.
        }
        finally
        {
            _links.TryRemove(id, out _);
            if (Model.Close(id))
            {
                MarkChanged();
                Raise(AddonEventKind.Left);
            }
            ws?.Dispose();
            Interlocked.Decrement(ref _open);
        }
    }

    private async Task ReadLoop(BridgeLink link)
    {
        var badInARow = 0;
        while (true)
        {
            using var silence = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            silence.CancelAfter(_silenceLimit);
            var frame = await link.Receive(silence.Token);

            if (frame.Kind == FrameKind.Close)
            {
                await link.Close(WebSocketCloseStatus.NormalClosure);
                return;
            }

            if (frame.Kind == FrameKind.TooBig)
            {
                Counts.Add(ref Counts.Oversize);
                await link.Close(WebSocketCloseStatus.MessageTooBig);
                return;
            }

            var parsed = frame.Kind == FrameKind.Text ? TabMessages.ParseAddon(frame.Text) : Parsed<AddonMessage>.Bad("binary");
            if (parsed.Message is null or HelloMessage)
            {
                Counts.Add(ref Counts.BadFrames);
                if (++badInARow < TabProtocol.MaxBadFramesInARow) continue;
                Counts.Add(ref Counts.ClosedForBadFrames);
                await link.Close(WebSocketCloseStatus.PolicyViolation);
                return;
            }

            badInARow = 0;
            switch (parsed.Message)
            {
                case PingMessage:
                    await link.Send(TabMessages.Pong());
                    break;
                case ResultMessage:
                    Counts.Add(ref Counts.Results);
                    break;
                default:
                    if (Model.Apply(link.Id, parsed.Message)) MarkChanged();
                    break;
            }
        }
    }

    private void MarkChanged()
    {
        if (Interlocked.Exchange(ref _pendingChange, 1) == 0)
        {
            try
            {
                _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Stopping: nobody is listening any more.
            }
        }
    }

    private void RaiseChanged()
    {
        Interlocked.Exchange(ref _pendingChange, 0);
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception)
            {
                // One subscriber that throws must not stop the ones after it, nor take the timer's thread down.
            }
        }
    }
}

/// <summary>Numbers only, for the smoke console and selftest.json: nothing about what was sent.</summary>
public sealed class BridgeCounts
{
    /// <summary>Called with the running count each time a connection is refused (wrong Origin).</summary>
    internal Action<int>? OnRefused { get; set; }

    public int Refused;
    public int Announced;
    public int NoHello;
    public int TimedOut;
    public int BadFrames;
    public int ClosedForBadFrames;
    public int Oversize;
    public int TurnedAway;
    public int Results;

    public void Add(ref int counter) => Interlocked.Increment(ref counter);

    /// <summary>One more refusal; the new total is handed to <see cref="OnRefused"/> from the same atomic step, so two refusals never read the same number.</summary>
    public void NoteRefusal() => OnRefused?.Invoke(Interlocked.Increment(ref Refused));
}
