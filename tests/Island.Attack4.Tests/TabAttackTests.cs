using System.Text;
using Island.Bridge;
using Island.Core;

namespace Island.Attack4.Tests;

/// <summary>Defects in the island's side of extension/PROTOCOL.md: TabMessages, TabModel and TabBridge.</summary>
public class TabAttackTests
{
    // The add-on cuts a title with title.slice(0, 200) (extension/lib/protocol.js tabObject): a 200th UTF-16 unit
    // that is the first half of an emoji leaves a lone surrogate, which JSON.stringify writes as "\ud83d".
    private static readonly string TitleCutInsideAnEmoji = new string('a', 199) + "\\ud83d";

    private static string TabFrame(string titleJson) =>
        "{\"type\":\"tab\",\"tab\":{\"id\":11,\"windowId\":1,\"title\":\"" + titleJson + "\",\"host\":\"youtube.com\",\"audible\":false,\"active\":true}}";

    [Fact]
    public void ParseAddon_Throws_On_A_Title_Cut_Inside_An_Emoji()
    {
        // Defect 1. ParseAddon promises "a bad frame gives a reason and never throws".
        foreach (var frame in new[] { TabFrame(TitleCutInsideAnEmoji), "{\"type\":\"\\ud800\"}" })
        {
            Parsed<AddonMessage> parsed = default;
            var thrown = Record.Exception(() => parsed = TabMessages.ParseAddon(frame));
            Assert.Null(thrown);
            Assert.True(parsed.Ok || parsed.Reject is not null);
        }

        // The tab itself is a real tab: it should arrive, its broken last character replaced.
        Assert.True(TabMessages.ParseAddon(TabFrame(TitleCutInsideAnEmoji)).Message is TabMessage);
    }

    [Fact]
    public async Task Bridge_Drops_The_Connection_On_A_Title_Cut_Inside_An_Emoji()
    {
        // Defect 1, end to end: the exception escapes the read loop, the connection ends uncounted, all tabs vanish,
        // and the add-on's next snapshot (same title) ends it again: a reconnect loop with no tabs on the island.
        var (bridge, port) = TestBridge.Start();
        using var _ = bridge;
        await using var addon = new PretendAddon(port);
        Assert.True(await addon.AnnounceAsync("p1", TimeSpan.FromSeconds(3)));

        await addon.SendAsync(TabFrame(TitleCutInsideAnEmoji));
        await addon.SendAsync("{\"type\":\"ping\"}");

        Assert.Equal("{\"type\":\"pong\"}", await addon.ReceiveAsync(TimeSpan.FromSeconds(2)));
        Assert.True(bridge.Connected);
        Assert.Single(bridge.Tabs);
    }

    [Fact]
    public void Hello_With_A_Newline_After_The_Profile_Is_Accepted()
    {
        // Defect 2. PROTOCOL.md: profile is 1-64 chars of [A-Za-z0-9_-]. The rule's "$" also matches before a final
        // "\n", so "p1\n" passes and becomes a second profile beside "p1" (and tab keys "p1\n:11").
        var frame = "{\"type\":\"hello\",\"v\":1,\"client\":\"island-addon\",\"browser\":\"chrome\",\"profile\":\"p1\\n\",\"version\":\"0.1.0\"}";

        var parsed = TabMessages.ParseAddon(frame);

        Assert.False(parsed.Ok, "a profile with a newline must be refused");
    }

    [Fact]
    public void Snapshot_That_Shows_A_Known_Tab_Newly_Active_Does_Not_Make_It_Newest()
    {
        // Defect 8. PROTOCOL.md: a tab gets the next LastActiveOrder "when it is created or becomes active".
        var model = new TabModel();
        model.Open("c1", "p1");
        static TabObject Tab(int id, bool active) => new(id, 1, "Alpha", "example.org", false, active, false, false);
        model.Apply("c1", new SnapshotMessage([Tab(1, true), Tab(2, false)]));
        Assert.Equal(1, model.Tabs[0].TabId);

        model.Apply("c1", new SnapshotMessage([Tab(1, false), Tab(2, true)]));

        Assert.Equal(2, model.Tabs[0].TabId); // a click on the site must go to the tab that is active now
    }

    [Fact]
    public async Task Snapshot_Of_1000_Tabs_With_Full_Titles_Is_Over_The_Frame_Limit_And_The_Island_Shows_No_Tabs()
    {
        // Defect 9. The protocol allows 2000 tabs per connection and titles of 200 characters, but a snapshot is one
        // frame and a frame over 256 KB closes the connection. 1000 such tabs are about 285 KB: the island drops the
        // connection, shows no tabs, and the add-on redials into the same snapshot every 5 s.
        var (bridge, port) = TestBridge.Start();
        using var _ = bridge;
        await using var addon = new PretendAddon(port);
        Assert.True(await addon.AnnounceAsync("p1", TimeSpan.FromSeconds(3)));

        var title = new string('a', TabProtocol.MaxTitleChars);
        var frame = new StringBuilder("{\"type\":\"snapshot\",\"tabs\":[");
        for (var i = 1; i <= 1000; i++)
            frame.Append(i > 1 ? "," : "").Append($"{{\"id\":{i},\"windowId\":1,\"title\":\"{title}\",\"host\":\"example.org\",\"audible\":false,\"active\":false}}");
        frame.Append("]}");
        // Fixed: the frame limit is now 1 MB, and a snapshot of every tab the island accepts (2000) with full titles fits it.
        Assert.True(Encoding.UTF8.GetByteCount(frame.ToString()) <= TabProtocol.MaxFrameBytes);
        Assert.True(2 * Encoding.UTF8.GetByteCount(frame.ToString()) <= TabProtocol.MaxFrameBytes);

        await addon.SendAsync(frame.ToString());

        Assert.True(await TestBridge.Until(() => bridge.Tabs.Count == 1000), $"tabs: {bridge.Tabs.Count}, oversize closes: {bridge.Counts.Oversize}");
    }
}
