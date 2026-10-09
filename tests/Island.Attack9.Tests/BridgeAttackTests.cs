using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using Island.Bridge;
using Island.Core;

namespace Island.Attack9.Tests;

/// <summary>
/// ATTACK9 on WORK-ORDER-9 section 2 (the add-on status row and the events of TabBridge, from which the app writes the log lines). Pretend add-ons on a free
/// port of the test's own, never one of the protocol's five. Defect_ tests failed against the first build because of the defect they name and pass since it
/// was repaired (the repair is named in review/attack-wo9.md); Holds_ tests passed from the start. The tests were first written against a log hook of the
/// bridge; version 2 of the work order has the bridge raise events instead, and they were moved to that.
/// </summary>
public class BridgeAttackTests
{
    private static (TabBridge Bridge, int Port, List<AddonEvent> Lines) Start(Func<AddonEvent, bool>? throwOn = null)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.DoesNotContain(port, TabProtocol.Ports);

        var lines = new List<AddonEvent>();
        var bridge = new TabBridge([port]);
        bridge.AddonHappened += e =>
        {
            lock (lines) lines.Add(e);
            if (throwOn?.Invoke(e) == true) throw new InvalidOperationException("a subscriber that throws");
        };
        Assert.Equal(port, bridge.Start());
        return (bridge, port, lines);
    }

    private static AddonEvent[] Snapshot(List<AddonEvent> lines)
    {
        lock (lines) return [.. lines];
    }

    private static async Task<bool> Until(Func<bool> condition, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
        return condition();
    }

    // ---------------------------------------------------------------------------------------------------------------- defects

    [Fact]
    public async Task Defect_A_Log_Sink_That_Throws_Leaks_A_Connection_Slot_Until_No_Add_On_Can_Connect()
    {
        // TabBridge.Serve calls Log inside its try (Connected) and inside its finally (Left). A sink that throws on either (the island's own sink guards IOException
        // and UnauthorizedAccessException only) ends Serve before `Interlocked.Decrement(ref _open)`: every connection then costs one of the eight slots for good.
        var (bridge, port, _) = Start(e => e.Kind is AddonEventKind.Connected or AddonEventKind.Left);
        using var _ = bridge;
        for (var i = 0; i < 10; i++)
        {
            await using var attempt = new PretendAddon(port);
            await attempt.AnnounceAsync("p" + i, TimeSpan.FromSeconds(2));
        }

        await using var good = new PretendAddon(port);
        Assert.True(await good.AnnounceAsync("a-good-one", TimeSpan.FromSeconds(3)), $"turned away: {bridge.Counts.TurnedAway}");
        Assert.True(await Until(() => bridge.Browsers == 1));
    }

    [Fact]
    public void Defect_A_Throwing_Changed_Subscriber_Escapes_The_Timer_And_Blocks_The_Subscribers_After_It()
    {
        // RaiseChanged runs on a Timer: an exception there is unhandled on a pool thread and ends the process (AppWorld.Raise guards the app's own listener;
        // the bridge itself does not). It is called here the way the timer calls it, so the test cannot take the test host down. The settings row's handler,
        // subscribed after a throwing one, is never told.
        using var bridge = new TabBridge([1]);
        var told = 0;
        bridge.Changed += () => throw new InvalidOperationException("a subscriber that throws");
        bridge.Changed += () => told++;

        var raise = typeof(TabBridge).GetMethod("RaiseChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var error = Record.Exception(() => raise.Invoke(bridge, null));

        Assert.Null(error);
        Assert.Equal(1, told);
    }

    [Fact]
    public async Task Defect_A_Refusal_Flood_Is_Written_At_One_Line_In_A_Hundred_Without_End()
    {
        // WORK-ORDER-9 section 2: "one line for the first refusal since the listener started, and the total when the listener stops - never a line per
        // refusal, because any web page can knock on that port as often as it likes." The first build wrote the first ten and then every hundredth, for ever.
        var (bridge, port, lines) = Start();
        using var _ = bridge;
        for (var i = 0; i < 40; i++)
        {
            await using var stranger = new PretendAddon(port, null);
            Assert.False(await stranger.ConnectAsync(TimeSpan.FromSeconds(3)));
        }

        Assert.True(await Until(() => bridge.Counts.Refused == 40));
        await Task.Delay(100);
        Assert.Single(Snapshot(lines), e => e.Kind == AddonEventKind.Refused);
    }

    [Fact]
    public async Task Defect_The_Total_Of_Refusals_Is_Not_Written_When_The_Listener_Stops()
    {
        var (bridge, port, lines) = Start();
        for (var i = 0; i < 15; i++)
        {
            await using var stranger = new PretendAddon(port, "https://example.org");
            Assert.False(await stranger.ConnectAsync(TimeSpan.FromSeconds(3)));
        }

        Assert.True(await Until(() => bridge.Counts.Refused == 15));
        bridge.Dispose();
        await Task.Delay(300);

        Assert.Contains(new AddonEvent(AddonEventKind.Stopped, 15), Snapshot(lines)); // refusals 2 to 15 were not written one by one: the total at the end is the only place they appear
    }

    // ---------------------------------------------------------------------------------------------------------------- what held

    [Fact]
    public async Task Holds_Refusals_Over_Real_Sockets_Are_Counted_And_Only_The_First_Is_An_Event()
    {
        var (bridge, port, lines) = Start();
        using var _ = bridge;
        for (var i = 0; i < 25; i++)
        {
            await using var stranger = new PretendAddon(port, i % 2 == 0 ? "https://example.org" : null);
            Assert.False(await stranger.ConnectAsync(TimeSpan.FromSeconds(3)));
        }

        Assert.True(await Until(() => bridge.Counts.Refused == 25));
        await Task.Delay(100);
        Assert.Equal([new AddonEvent(AddonEventKind.ListenerOn), new AddonEvent(AddonEventKind.Refused, 1)], Snapshot(lines));
        Assert.Equal(0, bridge.Browsers);
    }

    [Fact]
    public async Task Holds_A_Profile_That_Connects_Again_And_Again_Is_One_Browser_And_The_Slots_Come_Back()
    {
        var (bridge, port, lines) = Start();
        using var _ = bridge;
        var kept = new List<PretendAddon>();
        for (var i = 0; i < 20; i++)
        {
            var addon = new PretendAddon(port);
            kept.Add(addon);
            Assert.True(await addon.AnnounceAsync("same-profile", TimeSpan.FromSeconds(3)));
        }

        await Task.Delay(300);
        Assert.Equal(1, bridge.Browsers);
        foreach (var addon in kept) await addon.DisposeAsync();
        Assert.True(await Until(() => bridge.Browsers == 0));

        // Twenty connections replaced each other; the eight slots are all free again.
        var fresh = new List<PretendAddon>();
        for (var i = 0; i < 8; i++)
        {
            var addon = new PretendAddon(port);
            fresh.Add(addon);
            Assert.True(await addon.AnnounceAsync("profile-" + i, TimeSpan.FromSeconds(3)), $"connection {i} turned away ({bridge.Counts.TurnedAway})");
        }

        Assert.True(await Until(() => bridge.Browsers == 8));
        foreach (var addon in fresh) await addon.DisposeAsync();
        Assert.True(await Until(() => bridge.Browsers == 0));
        AssertKindsOnly(Snapshot(lines));
    }

    [Fact]
    public async Task Holds_A_Storm_Of_Twelve_Browsers_At_Once_Respects_The_Cap_And_Recovers()
    {
        var (bridge, port, lines) = Start();
        using var _ = bridge;
        var addons = Enumerable.Range(0, 12).Select(_ => new PretendAddon(port)).ToList();
        var outcomes = await Task.WhenAll(addons.Select((a, i) => a.AnnounceAsync("storm-" + i, TimeSpan.FromSeconds(4))));

        Assert.True(await Until(() => bridge.Browsers == outcomes.Count(o => o)));
        Assert.InRange(bridge.Browsers, 1, 8);

        foreach (var a in addons) await a.DisposeAsync();
        Assert.True(await Until(() => bridge.Browsers == 0));

        await using var after = new PretendAddon(port);
        Assert.True(await after.AnnounceAsync("after-the-storm", TimeSpan.FromSeconds(3)));
        AssertKindsOnly(Snapshot(lines));
    }

    [Fact]
    public async Task Holds_A_Second_Hello_On_One_Connection_Is_Not_A_Second_Browser()
    {
        var (bridge, port, _) = Start();
        using var _ = bridge;
        await using var addon = new PretendAddon(port);
        Assert.True(await addon.AnnounceAsync("first", TimeSpan.FromSeconds(3)));
        await addon.SendAsync("""{"type":"hello","v":1,"client":"island-addon","browser":"chrome","profile":"second","version":"1"}""");
        await Task.Delay(200);
        Assert.Equal(1, bridge.Browsers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("quote\\\"quote")]
    [InlineData("slash/slash")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x")]
    [InlineData("éé")]
    public async Task Holds_A_Hello_With_A_Bad_Profile_Is_Not_Counted(string profile)
    {
        var (bridge, port, lines) = Start();
        using var _ = bridge;
        await using var addon = new PretendAddon(port);
        Assert.True(await addon.ConnectAsync(TimeSpan.FromSeconds(3)));
        await addon.SendAsync($$"""{"type":"hello","v":1,"client":"island-addon","browser":"chrome","profile":"{{profile}}","version":"1"}""");
        await Task.Delay(300);
        Assert.Equal(0, bridge.Browsers);
        Assert.DoesNotContain(new AddonEvent(AddonEventKind.Connected), Snapshot(lines));
    }

    [Fact]
    public async Task Holds_Changed_Is_Never_Raised_After_Dispose_Returned()
    {
        for (var round = 0; round < 10; round++)
        {
            var (bridge, port, _) = Start();
            var changes = 0;
            bridge.Changed += () => Interlocked.Increment(ref changes);
            var addon = new PretendAddon(port);
            Assert.True(await addon.AnnounceAsync("p" + round, TimeSpan.FromSeconds(3)));
            await addon.SendAsync("""{"type":"snapshot","tabs":[{"id":1,"windowId":1,"title":"A","host":"example.org","audible":false,"active":true}]}""");
            if (round % 2 == 0) await Until(() => changes > 0);

            bridge.Dispose();
            var atDispose = Volatile.Read(ref changes);
            await Task.Delay(250);
            Assert.Equal(atDispose, Volatile.Read(ref changes));
            await addon.DisposeAsync();
        }
    }

    [Fact]
    public async Task Holds_Dispose_Twice_And_Dispose_During_A_Connection_Do_Not_Throw()
    {
        var (bridge, port, _) = Start();
        await using var addon = new PretendAddon(port);
        Assert.True(await addon.AnnounceAsync("p", TimeSpan.FromSeconds(3)));
        bridge.Dispose();
        bridge.Dispose();
        Assert.True(await addon.WaitClosedAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Holds_No_Event_Of_The_Bridge_Outside_TabBridge_Exists()
    {
        // The guard GuardTests.Addon_Log_Never_Names_A_Browser_Or_A_Tab reads the bridge's files; this is the plain check that no other file raises or hands on an event.
        var root = FindRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Island.Bridge"), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "TabBridge.cs") continue;
            var code = Regex.Replace(File.ReadAllText(file), @"//.*$", "", RegexOptions.Multiline);
            Assert.DoesNotMatch(@"\bAddonHappened\b|\bAddonEvent\b|\bRaise\s*\(", code);
        }
    }

    private static void AssertKindsOnly(AddonEvent[] events) =>
        Assert.All(events.Select(AddonLog.Line), line => Assert.Matches(@"^add-on (listener (on|off)|connected|left|refused \d+( in total)?)$", line));

    internal static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Island.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Island.sln not found above the test output folder.");
    }
}
