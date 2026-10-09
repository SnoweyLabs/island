using System.Net;
using System.Net.Sockets;
using Island.Bridge;
using Island.Core;

// The bridge, end to end, on this computer only: a listener on a free loopback port and pretend add-ons that
// play scripts against it. Prints a fixed step name with PASS or FAIL, and counts. Shows no window and touches
// no browser. Exit code 0 when every step passed.

var results = new List<ScriptStep>();
var wait = TimeSpan.FromSeconds(3);
void Record(string name, bool pass) => results.Add(new ScriptStep(name, pass));

var port = FreePort();
using (var bridge = new TabBridge([port]))
{
    var changedEvents = 0;
    bridge.Changed += () => Interlocked.Increment(ref changedEvents);
    Record("the listener starts on the given port", bridge.Start() == port);
    Record("the listener is bound to the loopback address", bridge.Endpoint is { } ep && IPAddress.IsLoopback(ep.Address) && ep.Address.Equals(IPAddress.Loopback));

    using (var second = new TabBridge([port, FreePort()]))
        Record("a taken port falls back to the next one", second.Start() is { } p && p != port);

    results.AddRange(await PretendScript.RunAsync(bridge, port));

    // A stalled local program holds a socket open and says nothing; an add-on must still get in at once.
    using (var stalled = await PretendAddon.OpenSilentSocketAsync(port))
    {
        await using var addon = new PretendAddon(port);
        var started = DateTime.UtcNow;
        Record("a stalled socket does not block another add-on", stalled is not null && await addon.AnnounceAsync("smoke-a", wait) && DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
        var timedOut = bridge.Counts.TimedOut;
        // The drop is due 3 s after the socket was accepted; waiting exactly 3 s from here raced it. Allow 2 s past it.
        Record("the stalled socket is dropped after the hello deadline",
            await PretendScript.Until(() => bridge.Counts.TimedOut > timedOut, TabProtocol.HelloDeadline + TimeSpan.FromSeconds(2)));
    }

    await using (var mute = new PretendAddon(port))
    {
        Record("an upgraded connection that never says hello is closed", await mute.ConnectAsync(wait) && await mute.WaitClosedAsync(TimeSpan.FromSeconds(4)));
    }

    await using (var rude = new PretendAddon(port))
    {
        var before = bridge.Counts.NoHello;
        Record("a first frame that is not hello closes the connection",
            await rude.ConnectAsync(wait) && await rude.SendAsync("""{"type":"ping"}""") && await rude.WaitClosedAsync(wait) && bridge.Counts.NoHello == before + 1);
    }

    await using (var noisy = new PretendAddon(port))
    {
        await noisy.AnnounceAsync("smoke-bad", wait);
        for (var i = 0; i < 9; i++) await noisy.SendAsync("""{"type":"launch-missiles"}""");
        await noisy.SendAsync("""{"type":"ping"}""");
        var pong = await noisy.ReceiveAsync(wait);
        for (var i = 0; i < 9; i++) await noisy.SendAsync("[1,2,3]");
        await Task.Delay(200);
        Record("nine bad frames, a good one, nine bad: still open", pong is not null && noisy.IsOpen);
        var closedBefore = bridge.Counts.ClosedForBadFrames;
        await noisy.SendAsync("{");
        Record("the tenth bad frame in a row closes the connection", await noisy.WaitClosedAsync(wait) && bridge.Counts.ClosedForBadFrames == closedBefore + 1);
    }

    await using (var big = new PretendAddon(port))
    {
        await big.AnnounceAsync("smoke-big", wait);
        await big.SendAsync("{\"type\":\"ping\",\"pad\":\"" + new string('x', 300 * 1024) + "\"}");
        Record("a frame over 256 KB closes the connection", await big.WaitClosedAsync(wait) && bridge.Counts.Oversize >= 1);
    }

    await using (var busy = new PretendAddon(port))
    {
        await busy.AnnounceAsync("smoke-busy", wait);
        var eventsBefore = Volatile.Read(ref changedEvents);
        for (var i = 1; i <= 500; i++)
            await busy.SendAsync($$$"""{"type":"tab","tab":{"id":{{{i}}},"windowId":1,"title":"Tab","host":"example.org","audible":false,"active":false}}""");
        Record("five hundred frames in a row are all applied", await PretendScript.Until(() => bridge.Tabs.Count(t => t.Key.StartsWith("smoke-busy:", StringComparison.Ordinal)) == 500));
        await Task.Delay(200);
        var events = Volatile.Read(ref changedEvents) - eventsBefore;
        Record("change events are coalesced", events >= 1 && events < 500);
        Console.WriteLine($"  (change events for 500 frames: {events})");
    }

    await using (var a = new PretendAddon(port))
    await using (var b = new PretendAddon(port))
    {
        await a.AnnounceAsync("smoke-p1", wait);
        await b.AnnounceAsync("smoke-p2", wait);
        await a.SendAsync("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"A","host":"example.org","audible":false,"active":true}}""");
        await b.SendAsync("""{"type":"tab","tab":{"id":1,"windowId":5,"title":"B","host":"example.net","audible":false,"active":true}}""");
        Record("two profiles with the same tab id do not clash", await PretendScript.Until(() => bridge.Tabs.Any(t => t.Key == "smoke-p1:1") && bridge.Tabs.Any(t => t.Key == "smoke-p2:1")));

        var commands = new OutsideTabs(bridge);
        Record("activate reaches the right add-on with its window",
            commands.Activate("smoke-p2:1") && TabMessages.ParseIsland(await b.ReceiveAsync(wait)).Message is ActivateMessage { Id: 1, WindowId: 5 });
        Record("a media command reaches the add-on",
            commands.Media("smoke-p1:1", MediaCommand.Next) && TabMessages.ParseIsland(await a.ReceiveAsync(wait)).Message is MediaCommandMessage { Id: 1, Command: MediaCommand.Next });
        Record("a command for an unknown tab is not sent", !commands.Activate("smoke-p1:99") && !commands.Activate("nonsense"));

        var normal = OutsideGate.Current;
        OutsideGate.Current = new OutsideGate(selfTest: true);
        var refused = !commands.Activate("smoke-p1:1") && !commands.Media("smoke-p1:1", MediaCommand.PlayPause);
        Record("under the self-test gate no command is sent", refused && await a.ReceiveAsync(TimeSpan.FromMilliseconds(300)) is null && OutsideGate.Current.Refused(OutsideKind.TabCommand) == 2);
        OutsideGate.Current = normal;

        await using var again = new PretendAddon(port);
        Record("the same profile dialling again replaces the old connection",
            await again.AnnounceAsync("smoke-p1", wait) && await a.WaitClosedAsync(wait) && await PretendScript.Until(() => !bridge.Tabs.Any(t => t.Key == "smoke-p1:1")));
    }
}

// Silence: a short limit, to see the rule work without waiting the protocol's 60 seconds.
var quietPort = FreePort();
using (var quiet = new TabBridge([quietPort], silenceLimit: TimeSpan.FromSeconds(2)))
{
    quiet.Start();
    await using var talker = new PretendAddon(quietPort);
    await using var silent = new PretendAddon(quietPort);
    await talker.AnnounceAsync("smoke-talker", wait);
    await silent.AnnounceAsync("smoke-silent", wait);
    for (var i = 0; i < 6; i++)
    {
        await talker.SendAsync("""{"type":"ping"}""");
        await Task.Delay(500);
    }

    Record("a silent connection is closed after the silence limit", silent.Closed);
    Record("a connection that pings stays open", talker.IsOpen);
}

// More connections than allowed: the ninth is turned away and the eight stay.
var crowdPort = FreePort();
using (var crowded = new TabBridge([crowdPort]))
{
    crowded.Start();
    var crowd = new List<PretendAddon>();
    var welcomed = 0;
    for (var i = 0; i < 8; i++)
    {
        var addon = new PretendAddon(crowdPort);
        crowd.Add(addon);
        if (await addon.AnnounceAsync("smoke-crowd" + i, wait)) welcomed++;
    }

    Record("eight add-ons are welcomed", welcomed == 8);
    await using var extra = new PretendAddon(crowdPort);
    var extraIn = await extra.AnnounceAsync("smoke-extra", wait);
    // ">= 1", not "== 1": the .NET client may dial again once when a connection is closed before any answer,
    // and the bridge rightly turns that socket away too (seen as 2 in 2 of 15 runs).
    Record("a ninth connection is turned away", !extraIn && crowded.Counts.TurnedAway >= 1);
    Record("the eight stay open", crowd.All(c => c.IsOpen));
    if (welcomed != 8 || extraIn || crowded.Counts.TurnedAway < 1)
        Console.WriteLine($"  (crowd: welcomed {welcomed}, extra in {extraIn}, turned away {crowded.Counts.TurnedAway}, open {crowd.Count(c => c.IsOpen)})");
    foreach (var c in crowd) await c.DisposeAsync();
}

foreach (var r in results) Console.WriteLine($"{(r.Pass ? "PASS" : "FAIL")}  {r.Name}");
var passed = results.Count(r => r.Pass);
Console.WriteLine($"steps: {results.Count}, passed: {passed}, failed: {results.Count - passed}");
return passed == results.Count ? 0 : 1;

static int FreePort()
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}
