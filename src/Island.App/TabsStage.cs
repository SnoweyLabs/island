using System.Net;
using System.Net.Sockets;
using Island.Bridge;
using Island.Core;

namespace Island.App;

/// <summary>
/// WORK-ORDER-4 section 2 checks, without a browser: a bridge on a free loopback port of its own (never the real
/// ports, so Dan's add-on can never connect to a test), a pretend add-on that plays the script of PROTOCOL.md, and
/// the island's page state read at each step: site picks become open or closed with a count and the tab's number,
/// "Now playing" follows a tab, clicks go through the tab door (refused and counted under the self-test, nothing is
/// sent), and when the add-on goes away the site picks fall back to "website". Only made-up names and counts.
/// </summary>
internal sealed class TabsStage(SelfTestReport report, TimeSpan hangLimit)
{
    public async Task RunAsync()
    {
        var port = FreePort();
        using var bridge = new TabBridge([port]);
        report.Check("the bridge for the add-on listens on this computer only", bridge.Start() == port && Equals(bridge.Endpoint?.Address, IPAddress.Loopback), "127.0.0.1, a port of its own");

        // The scripted add-on of the bridge itself (tabs open, a second tab of one site, playing on one then the other, tabs close).
        var steps = await PretendScript.RunAsync(bridge, port);
        foreach (var step in steps) report.Check("pretend add-on: " + step.Name, step.Pass, "scripted step");
        report.Info["pretendAddonSteps"] = new { total = steps.Count, passed = steps.Count(s => s.Pass) };
        report.Info["bridgeCounts"] = new { bridge.Counts.Announced, bridge.Counts.Refused, bridge.Counts.BadFrames, bridge.Counts.ClosedForBadFrames };

        await PageStateAsync(bridge, port);
    }

    private async Task PageStateAsync(TabBridge bridge, int port)
    {
        var store = new PickStore(
        [
            Pick.ForSite("Alpha Video", "youtube.com", PageIds.Media),
            Pick.ForSite("Beta Music", "music.youtube.com", PageIds.Media),
            Pick.ForSite("Gamma Site", "example.org", PageIds.Media),
        ]);
        var pretend = new PretendWorld();
        var recording = new RecordingOutside();
        var world = new AppWorld
        {
            Windows = pretend,
            Catalog = pretend,
            Icons = pretend,
            Folders = pretend,
            Media = pretend,
            MediaControl = pretend,
            Tabs = bridge,
            TabControl = new OutsideTabs(bridge),
            Outside = recording,
        };
        world.Listen();
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        var media = Pages.Get(PageIds.Media);
        var page = new MediaPage(world, () => store);

        var before = pages.ItemsOf(media);
        report.Check("without the add-on the site picks read \"website\" and are not marked closed", before.All(i => i is { Subtitle: "website", IsClosed: false }), "whether a site is open cannot be known");

        await using var addon = new PretendAddon(port);
        var announced = await addon.AnnounceAsync("self-test", TimeSpan.FromSeconds(5));
        report.Check("the pretend add-on connects with the add-on's own announcement", announced, "hello, welcome");

        await addon.SendAsync("""{"type":"snapshot","tabs":[{"id":1,"windowId":1,"title":"Alpha clip","host":"youtube.com","audible":false,"active":true},{"id":2,"windowId":1,"title":"Other clip","host":"youtube.com","audible":false,"active":false},{"id":3,"windowId":1,"title":"Beta mix","host":"music.youtube.com","audible":false,"active":false}]}""");
        var counted = await Until(() => bridge.Tabs.Count == 3);
        pages.Invalidate();
        var open = pages.ItemsOf(media);
        report.Check("with the add-on connected a site pick is open with the number of its tabs, and a second site is its own pick",
            counted && open[0] is { IsClosed: false, Count: 2 } && open[0].Subtitle == "2 tabs" && open[1] is { Count: 1 } && open[1].Subtitle == "open", $"{open[0].Subtitle} / {open[1].Subtitle}");
        report.Check("a site that has no tab reads closed", open[2].IsClosed && open[2].Subtitle == "closed", open[2].Subtitle);

        // EVALS I2 and I9: a site tile shows the icon of its tab and nothing borrowed: letters before one arrives, the tab's own picture after, letters again when the tab's address changes.
        report.Check("a site pick whose tab has sent no icon shows its letters, never a borrowed logo", open[0].Icon is null && open[1].Icon is null, "no picture before the add-on sends one");
        await addon.SendAsync("{\"type\":\"icon\",\"id\":1,\"png\":\"" + InventedIconPng() + "\"}");
        var iconShown = await Waiter.UntilAsync(() => { pages.Invalidate(); return pages.ItemsOf(media)[0].Icon is { Width: > 0 }; }, "the site tile showing the tab's icon", hangLimit, report);
        report.Check("after the add-on sends the tab's icon the site's tile shows it", iconShown, "the picture of the tab, made into the tile's disc");
        await addon.SendAsync("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"Alpha clip","host":"example.org","audible":false,"active":true}}""");
        var iconDropped = await Waiter.UntilAsync(() => { pages.Invalidate(); var items = pages.ItemsOf(media); return items[0].Icon is null && items[2].Icon is null; }, "the icon dropped when the tab's address changed", hangLimit, report);
        report.Check("when the tab's address changes its icon is dropped and the tiles read letters again", iconDropped, "an icon belongs to an address");
        await addon.SendAsync("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"Alpha clip","host":"youtube.com","audible":false,"active":true}}""");
        await Until(() => bridge.Tabs.Any(t => t.TabId == 1 && t.Host == "youtube.com"));
        pages.Invalidate();

        // A click goes to the newest tab through the tab door: under the self-test the gate refuses it and counts it.
        var refusedBefore = OutsideGate.Current.Refused(OutsideKind.TabCommand);
        var plan = pages.Click(media, 0);
        report.Check("a click on an open site goes to its newest tab through the tab door, which the self-test gate refuses and counts",
            plan is { Kind: ClickKind.BringForward } && OutsideGate.Current.Refused(OutsideKind.TabCommand) == refusedBefore + 1, $"plan {plan?.Kind}");

        // WORK-ORDER-9 section 3. A tab on a picked site makes sound and says nothing about what it plays; Windows says the browser is playing: its words fill the line, the
        // pick's tile dances, a click on the line goes to the tab, the buttons go to Windows' session. Invented names and an invented session only.
        await addon.SendAsync("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"Alpha clip","host":"youtube.com","audible":true,"active":true}}""");
        await Until(() => bridge.Tabs.Any(t => t.TabId == 1 && t.Audible));
        pretend.Sessions = [new MediaSessionInfo("self-test-browser", "browser.exe", true, "Windows title", "Windows artist", PlaybackState.Playing, 20, 100)];
        page.Refresh(DateTimeOffset.UtcNow);
        var fallbackView = page.View;
        var mediaPicks = store.ForPage(PageIds.Media);
        report.Check("a tab that makes sound and says nothing: the line shows Windows' title for the browser, as a tab of that site",
            fallbackView is { Title: "Windows title", Where: "YouTube", FallbackTabKey: not null } && PlayingTile.SecondLine(fallbackView) == "YouTube \u00b7 tab", fallbackView?.Where ?? "nothing");
        report.Check("and that site's tile dances", PlayingTile.PickIdFor(fallbackView, mediaPicks) == mediaPicks[0].Id, "the pick of the site");
        var sessionSent = OutsideGate.Current.Refused(OutsideKind.TabCommand);
        page.Send(MediaCommand.Next);
        report.Check("the buttons go to Windows' session for the browser, not to a tab", fallbackView?.Target.Kind == MediaTargetKind.Session && OutsideGate.Current.Refused(OutsideKind.TabCommand) == sessionSent, "no tab command was made");
        var clickBefore = OutsideGate.Current.Refused(OutsideKind.TabCommand);
        page.BringLineForward();
        report.Check("a click on the line goes to the tab, through the tab door (refused and counted under the self-test)", OutsideGate.Current.Refused(OutsideKind.TabCommand) == clickBefore + 1, "nothing was sent to a browser");

        // Now playing follows the tab that started playing.
        await addon.SendAsync("""{"type":"media","id":1,"title":"Alpha clip","artist":"Beta channel","state":"playing","position":10,"length":100}""");
        await Until(() => bridge.Tabs.Any(t => t.Media is { State: PlaybackState.Playing }));
        page.Refresh(DateTimeOffset.UtcNow);
        report.Check("the Now-playing line follows the playing tab", page.View is { } v && v.Title == "Alpha clip" && v.Target.Kind == MediaTargetKind.Tab, page.View?.Where ?? "nothing");
        report.Check("the tab's own report replaced Windows' words and the item stayed the same one: the line is still there and can be controlled", page.View is { CanControl: true, FallbackTabKey: null } && page.View.Target.Id == fallbackView?.FallbackTabKey, "the same tab");
        pretend.Sessions = [];
        var sentBefore = OutsideGate.Current.Refused(OutsideKind.TabCommand);
        page.Send(MediaCommand.PlayPause);
        report.Check("the buttons go to that tab through the tab door (refused and counted under the self-test)", OutsideGate.Current.Refused(OutsideKind.TabCommand) == sentBefore + 1, "nothing was sent to a browser");

        await addon.DisposeAsync();
        var gone = await Until(() => !bridge.Connected);
        pages.Invalidate();
        report.Check("when the add-on goes away the site picks read \"website\" again", gone && pages.ItemsOf(media).All(i => i.Subtitle == "website"), "tabs dropped");
    }

    /// <summary>A small PNG of an invented picture, as the add-on sends one (base64): a plain square of colour, made in memory.</summary>
    private static string InventedIconPng()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200; pixels[i + 1] = 90; pixels[i + 2] = 30; pixels[i + 3] = 255;
        }

        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(16, 16, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16 * 4);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    private async Task<bool> Until(Func<bool> condition) => await Waiter.UntilAsync(condition, "the bridge showing the add-on's report", hangLimit, report);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
