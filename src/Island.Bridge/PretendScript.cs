using Island.Core;

namespace Island.Bridge;

/// <summary>One step of a scripted run: a fixed name and whether it passed. No names of real things.</summary>
public sealed record ScriptStep(string Name, bool Pass);

/// <summary>
/// The pretend add-on's script for the self-test (WORK-ORDER-4 section 2): a wrong origin, tabs open, a second
/// tab of the same site, playing starts on one then on the other, an icon, a broken frame, tabs close. Each
/// step waits for the bridge's state to show the effect. Every title and host is made up.
/// </summary>
public static class PretendScript
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    /// <summary>A 1x1 PNG, the same as PROTOCOL.md's icon example.</summary>
    private const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public static async Task<IReadOnlyList<ScriptStep>> RunAsync(TabBridge bridge, int port)
    {
        var steps = new List<ScriptStep>();
        var youtube = Pick.ForSite("Pretend video site", "youtube.com", PageIds.Media);
        int Count() => PickStates.For(youtube, new OpenSnapshot([], [], bridge.Tabs, bridge.Connected)).Count;
        int? LastStarted() => bridge.Model.LastStarted(h => h == "youtube.com")?.TabId;
        async Task Step(string name, Func<Task<bool>> act) => steps.Add(new ScriptStep(name, await act()));

        await Step("a wrong origin is refused with 403", async () =>
        {
            await using var wrong = new PretendAddon(port, origin: "null");
            return !await wrong.ConnectAsync(Wait) && wrong.RefusedStatus == 403;
        });
        await Step("no origin is refused with 403", async () =>
        {
            await using var none = new PretendAddon(port, origin: null);
            return !await none.ConnectAsync(Wait) && none.RefusedStatus == 403;
        });

        await using var addon = new PretendAddon(port);
        await Step("hello is answered with welcome", async () => await addon.AnnounceAsync("pretend", Wait) && await Until(() => bridge.Connected));

        await Step("tabs open", async () =>
            await addon.SendAsync("""{"type":"snapshot","tabs":[{"id":11,"windowId":1,"title":"Alpha","host":"youtube.com","audible":false,"active":true},{"id":12,"windowId":1,"title":"Beta","host":"example.org","audible":false,"active":false}]}""")
            && await Until(() => bridge.Tabs.Count == 2 && Count() == 1));

        await Step("a second tab of the same site is counted", async () =>
            await addon.SendAsync("""{"type":"tab","tab":{"id":13,"windowId":1,"title":"Gamma","host":"www.youtube.com","audible":false,"active":true}}""")
            && await Until(() => Count() == 2 && bridge.Tabs[0].Key == "pretend:13"));

        await Step("playing starts on the first tab", async () =>
            await addon.SendAsync("""{"type":"media","id":11,"title":"Alpha track","artist":"Delta","state":"playing","position":1,"length":200}""")
            && await Until(() => LastStarted() == 11));

        await Step("playing starts on the second tab and it takes over", async () =>
            await addon.SendAsync("""{"type":"media","id":13,"title":"Gamma track","artist":null,"state":"playing","position":null,"length":null}""")
            && await Until(() => LastStarted() == 13));

        await Step("an icon arrives for a tab", async () =>
            await addon.SendAsync($$"""{"type":"icon","id":11,"png":"{{TinyPng}}"}""")
            && await Until(() => bridge.IconPng("pretend:11") is { Length: > 8 }));

        await Step("a broken frame is ignored and the connection stays", async () =>
        {
            var before = bridge.Counts.BadFrames;
            return await addon.SendAsync("{\"type\":\"tab\",") && await Until(() => bridge.Counts.BadFrames > before)
                   && await addon.SendAsync("""{"type":"ping"}""")
                   && TabMessages.ParseIsland(await addon.ReceiveAsync(Wait)).Message is PongMessage;
        });

        await Step("tabs close", async () =>
            await addon.SendAsync("""{"type":"tab-removed","id":13}""")
            && await addon.SendAsync("""{"type":"tab-removed","id":11}""")
            && await Until(() => Count() == 0 && bridge.Tabs.Count == 1 && LastStarted() is null));

        await Step("closing the connection drops its tabs", async () =>
        {
            await addon.DisposeAsync();
            return await Until(() => !bridge.Connected && bridge.Tabs.Count == 0);
        });

        return steps;
    }

    /// <summary>Polls until the condition holds or <paramref name="timeout"/> (three seconds by default) passes.</summary>
    public static async Task<bool> Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? Wait);
        while (!condition())
        {
            if (DateTime.UtcNow > until) return false;
            await Task.Delay(10);
        }

        return true;
    }
}
