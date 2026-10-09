using System.Security.Cryptography;
using System.Text;
using Island.Core;

namespace Island.Bridge;

/// <summary>
/// The WebSocket opening handshake of RFC 6455 section 4.2, server side, written by hand so the listener
/// needs nothing but a loopback socket (HttpListener would need a URL reservation). Only an upgrade request
/// for the protocol's path whose Origin is a browser add-on is answered with 101; any other Origin gets 403.
/// </summary>
internal static class Handshake
{
    private const int MaxHeaderBytes = 8 * 1024;

    // RFC 6455 section 1.3: the fixed GUID appended to the client's key.
    private const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>True when the connection was upgraded; false when it was answered with an error and should be closed.</summary>
    public static async Task<bool> Run(Stream stream, BridgeCounts counts, CancellationToken ct, bool acceptPretendOrigin = false)
    {
        var head = await ReadHead(stream, ct);
        if (head is null) return await Answer(stream, "400 Bad Request", ct);

        var lines = head.Split("\r\n");
        var request = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            // A repeated header is suspicious; the first one counts, as the browser sends each once.
            headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }

        if (request is not ["GET", _, "HTTP/1.1"]) return await Answer(stream, "400 Bad Request", ct);

        var origin = headers.GetValueOrDefault("Origin");
        var allowed = origin == TabProtocol.AddonOrigin || (acceptPretendOrigin && origin == PretendAddon.PretendOrigin);
        if (!allowed)
        {
            counts.NoteRefusal();
            return await Answer(stream, "403 Forbidden", ct);
        }

        if (request[1] != TabProtocol.Path) return await Answer(stream, "404 Not Found", ct);

        var key = headers.GetValueOrDefault("Sec-WebSocket-Key");
        var upgrade = headers.GetValueOrDefault("Upgrade");
        var connection = headers.GetValueOrDefault("Connection") ?? "";
        if (!"websocket".Equals(upgrade, StringComparison.OrdinalIgnoreCase)
            || !connection.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
            || headers.GetValueOrDefault("Sec-WebSocket-Version") != "13"
            || string.IsNullOrEmpty(key))
        {
            return await Answer(stream, "400 Bad Request", ct);
        }

        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + AcceptGuid)));
        var reply = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply), ct);
        return true;
    }

    /// <summary>
    /// The request line and headers, up to the blank line. Read one byte at a time so nothing after the
    /// blank line is taken from the WebSocket that follows. Null when it is too long or the peer left.
    /// </summary>
    private static async Task<string?> ReadHead(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(1024);
        var one = new byte[1];
        while (bytes.Count < MaxHeaderBytes)
        {
            if (await stream.ReadAsync(one, ct) == 0) return null;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray(), 0, bytes.Count - 4);
        }

        return null;
    }

    private static async Task<bool> Answer(Stream stream, string status, CancellationToken ct)
    {
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
        }
        catch (IOException)
        {
            // The peer is already gone; there is nobody to tell.
        }

        return false;
    }
}
