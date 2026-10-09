using System.Net;
using System.Net.Sockets;
using Island.Bridge;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack4.Tests;

/// <summary>A test bridge on a free loopback port of its own, never one of the protocol's five, so a real add-on can never reach it.</summary>
internal static class TestBridge
{
    public static (TabBridge Bridge, int Port) Start()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.DoesNotContain(port, TabProtocol.Ports);

        var bridge = new TabBridge([port]);
        Assert.Equal(port, bridge.Start());
        return (bridge, port);
    }

    public static async Task<bool> Until(Func<bool> condition, int ms = 2000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
        return condition();
    }
}

/// <summary>A pretend Windows that grants every key and remembers which ones it holds.</summary>
internal sealed class HoldingRegistrar : IHotkeyRegistrar
{
    public HashSet<HotkeyCombo> Held { get; } = [];

    public bool TryRegister(HotkeyCombo combo, out int error)
    {
        error = 0;
        Held.Add(combo);
        return true;
    }

    public void Release(HotkeyCombo combo) => Held.Remove(combo);
}

/// <summary>A temporary folder that is removed afterwards, read-only files included.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack4-");

    public string File(string name) => Path.Combine(_dir.FullName, name);

    public void Dispose()
    {
        foreach (var f in _dir.GetFiles()) f.Attributes = FileAttributes.Normal;
        _dir.Delete(recursive: true);
    }
}
