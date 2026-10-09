using Island.Bridge;
using Island.Core;

namespace Island.Attack4.Tests;

/// <summary>
/// WORK-ORDER-9 section 2: the settings row "Browser add-on" reads its number from the bridge, and the bridge raises one event for each thing that happens to
/// the add-on, with nothing but the kind (and, for refusals, a number); the app turns each into a log line. Pretend add-ons on a port of the test's own.
/// </summary>
public class AddonStatusTests
{
    [Fact]
    public async Task Connected_Count_Follows_Hello_And_Close()
    {
        var (bridge, port) = TestBridge.Start();
        using var _ = bridge;
        var changes = 0;
        bridge.Changed += () => Interlocked.Increment(ref changes);
        Assert.Equal(0, bridge.Browsers);
        Assert.Equal("Not connected", AddonText.Status(bridge.Browsers));

        await using var first = new PretendAddon(port);
        Assert.True(await first.AnnounceAsync("one", TimeSpan.FromSeconds(5)));
        Assert.True(await TestBridge.Until(() => bridge.Browsers == 1));
        Assert.Equal("Connected", AddonText.Status(bridge.Browsers));

        await using var second = new PretendAddon(port);
        Assert.True(await second.AnnounceAsync("two", TimeSpan.FromSeconds(5)));
        Assert.True(await TestBridge.Until(() => bridge.Browsers == 2));
        Assert.Equal("Connected (2)", AddonText.Status(bridge.Browsers));
        Assert.True(await TestBridge.Until(() => changes > 0), "the screen is told when the number changes");

        await first.DisposeAsync();
        Assert.True(await TestBridge.Until(() => bridge.Browsers == 1), "a browser that leaves is no longer counted");
        await second.DisposeAsync();
        Assert.True(await TestBridge.Until(() => bridge.Browsers == 0));
        Assert.Equal("Not connected", AddonText.Status(bridge.Browsers));
    }

    [Fact]
    public async Task A_Profile_That_Connects_Again_Is_Counted_Once()
    {
        var (bridge, port) = TestBridge.Start();
        using var _ = bridge;
        await using var old = new PretendAddon(port);
        Assert.True(await old.AnnounceAsync("same", TimeSpan.FromSeconds(5)));
        Assert.True(await TestBridge.Until(() => bridge.Browsers == 1));

        // A restarted service worker dials again before the island notices the old socket died.
        await using var again = new PretendAddon(port);
        Assert.True(await again.AnnounceAsync("same", TimeSpan.FromSeconds(5)));
        await Task.Delay(300);
        Assert.Equal(1, bridge.Browsers);
    }

    [Fact]
    public async Task Log_Lines_Carry_Kinds_And_Counts_Only()
    {
        var events = new List<AddonEvent>();
        var (bridge, port) = StartWith(events);
        using var _ = bridge;
        Assert.Equal([new AddonEvent(AddonEventKind.ListenerOn)], Snapshot(events));

        await using (var addon = new PretendAddon(port))
        {
            Assert.True(await addon.AnnounceAsync("a-profile-name-that-must-not-appear", TimeSpan.FromSeconds(5)));
            Assert.True(await TestBridge.Until(() => Snapshot(events).Contains(new AddonEvent(AddonEventKind.Connected))));
            await addon.SendAsync("""{"type":"snapshot","tabs":[{"id":1,"windowId":1,"title":"A title that must not appear","host":"a-site-that-must-not-appear.example","audible":false,"active":true}]}""");
            await TestBridge.Until(() => bridge.Tabs.Count == 1);
        }

        Assert.True(await TestBridge.Until(() => Snapshot(events).Contains(new AddonEvent(AddonEventKind.Left))));

        // A connection with the wrong Origin is refused; the first refusal is an event with the number 1, the others are only counted.
        for (var i = 0; i < 3; i++)
            await using (var stranger = new PretendAddon(port, "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"))
                Assert.False(await stranger.ConnectAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await TestBridge.Until(() => bridge.Counts.Refused == 3));
        Assert.Single(Snapshot(events), e => e.Kind == AddonEventKind.Refused);
        Assert.Contains(new AddonEvent(AddonEventKind.Refused, 1), Snapshot(events));

        var lines = Snapshot(events).Select(AddonLog.Line).ToList();
        var all = string.Join("\n", lines);
        Assert.DoesNotContain("must-not-appear", all, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaaaaaa", all, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.Matches(@"^add-on (listener (on|off)|connected|left|refused \d+( in total)?)$", line));
    }

    [Fact]
    public async Task The_Total_Of_Refusals_Is_Written_When_The_Listener_Stops()
    {
        var events = new List<AddonEvent>();
        var (bridge, port) = StartWith(events);
        for (var i = 0; i < 5; i++)
            await using (var stranger = new PretendAddon(port, null))
                Assert.False(await stranger.ConnectAsync(TimeSpan.FromSeconds(3)));
        Assert.True(await TestBridge.Until(() => bridge.Counts.Refused == 5));

        bridge.Dispose();
        bridge.Dispose(); // twice does no harm, and writes nothing twice
        var written = Snapshot(events).Where(e => e.Kind is AddonEventKind.Refused or AddonEventKind.Stopped).ToList();
        Assert.Equal([new AddonEvent(AddonEventKind.Refused, 1), new AddonEvent(AddonEventKind.Stopped, 5)], written);
        Assert.Equal("add-on refused 5 in total", AddonLog.Line(written[1]));
    }

    [Fact]
    public void A_Listener_That_Could_Not_Start_Says_So()
    {
        var events = new List<AddonEvent>();
        var taken = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)taken.LocalEndpoint).Port;
            using var bridge = new TabBridge([port]);
            bridge.AddonHappened += events.Add;
            Assert.Null(bridge.Start());
            Assert.Equal([new AddonEvent(AddonEventKind.ListenerOff)], events);
            Assert.Equal("add-on listener off", AddonLog.Line(events[0]));
        }
        finally
        {
            taken.Stop();
        }
    }

    [Fact]
    public async Task A_Subscriber_That_Throws_Hurts_Nothing()
    {
        var (bridge, port) = TestBridge.Start();
        using var _ = bridge;
        bridge.AddonHappened += _ => throw new InvalidOperationException("a log that cannot be written");
        var seen = new List<AddonEvent>();
        bridge.AddonHappened += seen.Add;
        bridge.Changed += () => throw new InvalidOperationException("a screen that is gone");
        var after = 0;
        bridge.Changed += () => Interlocked.Increment(ref after);

        for (var i = 0; i < 10; i++)
        {
            await using var addon = new PretendAddon(port);
            Assert.True(await addon.AnnounceAsync("p" + i, TimeSpan.FromSeconds(5)), "a good add-on still connects after a throwing subscriber, attempt " + i);
        }

        Assert.True(await TestBridge.Until(() => bridge.Browsers == 0 && Volatile.Read(ref after) > 0), "the subscribers after a throwing one are still told");
        Assert.Contains(new AddonEvent(AddonEventKind.Connected), seen);
    }

    private static List<AddonEvent> Snapshot(List<AddonEvent> events)
    {
        lock (events) return [.. events];
    }

    private static (TabBridge Bridge, int Port) StartWith(List<AddonEvent> events)
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var bridge = new TabBridge([port]);
        bridge.AddonHappened += e => { lock (events) events.Add(e); };
        Assert.Equal(port, bridge.Start());
        return (bridge, port);
    }
}
