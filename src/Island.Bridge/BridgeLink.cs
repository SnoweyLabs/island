using System.Net.WebSockets;
using System.Text;
using Island.Core;

namespace Island.Bridge;

internal enum FrameKind
{
    Text,
    Binary,
    Close,
    TooBig,
}

internal readonly record struct Frame(FrameKind Kind, string? Text = null);

/// <summary>One announced add-on connection: reads whole frames within the size limit, and sends one frame at a time.</summary>
internal sealed class BridgeLink(string id, WebSocket socket)
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _sending = new(1, 1);

    public string Id { get; } = id;

    public async Task<Frame> Receive(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var whole = new MemoryStream();
        while (true)
        {
            var r = await socket.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return new Frame(FrameKind.Close);
            if (whole.Length + r.Count > TabProtocol.MaxFrameBytes) return new Frame(FrameKind.TooBig);
            whole.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;
            return r.MessageType == WebSocketMessageType.Text
                ? new Frame(FrameKind.Text, Encoding.UTF8.GetString(whole.GetBuffer(), 0, (int)whole.Length))
                : new Frame(FrameKind.Binary);
        }
    }

    /// <summary>
    /// Sends one frame; a peer that does not take it within 5 s is cut off (the socket is aborted), so a
    /// stuck browser never holds a sender. False when it could not be sent.
    /// </summary>
    public async Task<bool> Send(string frame)
    {
        using var cts = new CancellationTokenSource(SendTimeout);
        try
        {
            await _sending.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            socket.Abort();
            return false;
        }

        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, cts.Token);
            return true;
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
            socket.Abort();
            return false;
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task Close(WebSocketCloseStatus status)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await socket.CloseOutputAsync(status, null, cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
        {
            socket.Abort();
        }
    }

    public void Abort() => socket.Abort();
}
