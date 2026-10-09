using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Island.Bridge;

/// <summary>
/// Whose connection it is (Dan's P26, WORK-ORDER-13). The add-on tries the same five ports on every account of a computer, so on a computer where two people are logged in at once the first island holds
/// the first port and the other person's add-on would be answered by it. A connection from a process of another logged-in session is turned away (the add-on then tries the next port, which is its own
/// island's). Only the session of the process on the other end is looked at (a number, no name): when it cannot be found the connection is let in, as before.
/// </summary>
public static class ConnectionOwner
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool sort, int family, int tableClass, uint reserved);

    [DllImport("kernel32.dll")]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    /// <summary>False only when the other end is known to run in another session than this program; true when it is the same or cannot be told.</summary>
    public static bool IsOurSessionOrUnknown(Socket socket)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || socket.RemoteEndPoint is not IPEndPoint remote || socket.LocalEndPoint is not IPEndPoint local) return true;
            if (!IPAddress.IsLoopback(remote.Address) || remote.AddressFamily != AddressFamily.InterNetwork) return true;
            var pid = OwnerOfClientSide(local, remote);
            if (pid is null || !ProcessIdToSessionId(pid.Value, out var theirs) || !ProcessIdToSessionId((uint)Environment.ProcessId, out var ours)) return true;
            return theirs == ours;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>The process that owns the other end: the row of the table whose local end is the client's port and whose remote end is ours.</summary>
    private static uint? OwnerOfClientSide(IPEndPoint serverSide, IPEndPoint clientSide)
    {
        var size = 0;
        GetExtendedTcpTable(0, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        for (var attempt = 0; attempt < 3 && size > 0; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0) continue; // the table grew meanwhile: asked again with the new size
                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<TcpRow>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<TcpRow>(buffer + 4 + i * rowSize);
                    if (PortOf(row.LocalPort) == clientSide.Port && PortOf(row.RemotePort) == serverSide.Port) return row.OwningPid;
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    /// <summary>The ports of the table are in network order in the low 16 bits.</summary>
    private static int PortOf(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
}
