using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Island.Core;

namespace Island.Bridge;

/// <summary>
/// A pretend browser add-on for the self-test and the smoke console. It only ever dials this computer
/// (ws://127.0.0.1, the guard OutsideGuardTests.App_Has_No_Internet_Client checks the address) and speaks
/// extension/PROTOCOL.md with made-up tabs. It is the one file allowed to hold a client socket.
/// </summary>
public sealed class PretendAddon : IAsyncDisposable
{
    /// <summary>What a browser would send as the Origin of an add-on (the id is made up).</summary>
    public const string PretendOrigin = TabProtocol.OriginPrefix + "pretendaddonpretendaddonpretendad";

    private readonly ClientWebSocket _ws = new();
    private readonly int _port;
    private readonly Channel<string> _received = Channel.CreateUnbounded<string>();

    /// <param name="origin">The Origin header to send; null sends none.</param>
    public PretendAddon(int port, string? origin = PretendOrigin)
    {
        _port = port;
        _ws.Options.CollectHttpResponseDetails = true;
        _ws.Options.KeepAliveInterval = TimeSpan.Zero;
        if (origin is not null) _ws.Options.SetRequestHeader("Origin", origin);
    }

    public bool IsOpen => _ws.State == WebSocketState.Open && !Closed;

    /// <summary>True once the island has closed the connection or it broke.</summary>
    public bool Closed { get; private set; }

    /// <summary>The HTTP status of a refused upgrade (403 for a wrong Origin), 0 when there was none.</summary>
    public int RefusedStatus => (int)_ws.HttpStatusCode is var s && s != 101 ? s : 0;

    public async Task<bool> ConnectAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}{TabProtocol.Path}"), cts.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        {
            Closed = true;
            return false;
        }

        _ = Task.Run(Pump);
        return true;
    }

    /// <summary>Connects and says hello; true when the island answered welcome.</summary>
    public async Task<bool> AnnounceAsync(string profile, TimeSpan timeout)
    {
        if (!await ConnectAsync(timeout)) return false;
        await SendAsync($$"""{"type":"hello","v":1,"client":"{{TabProtocol.ClientName}}","browser":"chrome","profile":"{{profile}}","version":"0.0.0-pretend"}""");
        return TabMessages.ParseIsland(await ReceiveAsync(timeout)).Message is WelcomeMessage;
    }

    public async Task<bool> SendAsync(string frame)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _ws.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, cts.Token);
            return true;
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The next frame from the island, or null when none came in time or the connection closed.</summary>
    public async Task<string?> ReceiveAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await _received.Reader.ReadAsync(cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Waits until the island closes the connection.</summary>
    public async Task<bool> WaitClosedAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!Closed && DateTime.UtcNow < until) await Task.Delay(20);
        return Closed;
    }

    /// <summary>
    /// Opens a plain socket to the listener and sends nothing, like a stuck or hostile local program.
    /// The caller disposes it.
    /// </summary>
    public static async Task<IDisposable?> OpenSilentSocketAsync(int port)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port);
            return client;
        }
        catch (SocketException)
        {
            client.Dispose();
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
            {
                // Closing a connection that is already broken: nothing left to do.
            }
        }

        _ws.Dispose();
    }

    private async Task Pump()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                using var whole = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    whole.Write(buffer, 0, r.Count);
                }
                while (!r.EndOfMessage);

                _received.Writer.TryWrite(Encoding.UTF8.GetString(whole.GetBuffer(), 0, (int)whole.Length));
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The island closed or cut the connection.
        }
        finally
        {
            Closed = true;
            _received.Writer.TryComplete();
        }
    }
}
