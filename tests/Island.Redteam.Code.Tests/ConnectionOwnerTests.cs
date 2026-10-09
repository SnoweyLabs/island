using System.Net;
using System.Net.Sockets;
using Island.Bridge;

namespace Island.Redteam.Code.Tests;

/// <summary>WORK-ORDER-13, Dan's P26: a connection of the same logged-in session is let in; what cannot be told is let in too (as before).</summary>
public class ConnectionOwnerTests
{
    [Fact]
    public async Task A_Connection_From_A_Process_Of_This_Session_Is_Let_In()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var client = new TcpClient();
            var connecting = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            using var accepted = await listener.AcceptSocketAsync();
            await connecting;
            Assert.True(ConnectionOwner.IsOurSessionOrUnknown(accepted));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void A_Socket_That_Is_Not_Connected_Cannot_Be_Told_And_Is_Let_In()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.True(ConnectionOwner.IsOurSessionOrUnknown(socket));
    }

    [Fact]
    public void The_Bridge_Asks_Before_It_Serves()
    {
        var text = File.ReadAllText(Path.Combine(Repo.Root, "src", "Island.Bridge", "TabBridge.cs"));
        Assert.True(text.IndexOf("ConnectionOwner.IsOurSessionOrUnknown(socket)", StringComparison.Ordinal) is var asked and > 0 && asked < text.IndexOf("Task.Run(() => Serve(socket))", StringComparison.Ordinal));
    }
}
