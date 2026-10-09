using Island.Core;

namespace Island.Tests;

internal static class TabFixtures
{
    public static TabObject Tab(int id, string host, bool active = false, int window = 1, string title = "Alpha") =>
        new(id, window, title, host, Audible: false, active, Pinned: false, Incognito: false);

    public static SnapshotMessage Snapshot(params TabObject[] tabs) => new(tabs);

    public static MediaMessage Media(int id, PlaybackState state, string title = "Beta") => new(id, new TabMedia(title, "Gamma", state, 1, 100));

    public static readonly byte[] PngA = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
    public static readonly byte[] PngB = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 2];

    public static TabModel Connected(string connection = "c1", string profile = "p1")
    {
        var model = new TabModel();
        model.Open(connection, profile);
        return model;
    }
}

public class TabModelTests
{
    [Fact]
    public void Nothing_Is_Connected_Before_A_Hello()
    {
        var model = new TabModel();
        Assert.False(model.Connected);
        Assert.False(model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.org"))));
        Assert.Empty(model.Tabs);
    }

    [Fact]
    public void Snapshot_Replaces_One_Connections_Tabs()
    {
        var model = TabFixtures.Connected();
        model.Open("c2", "p2");
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.org"), TabFixtures.Tab(2, "youtube.com")));
        model.Apply("c2", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.net")));
        model.Apply("c1", TabFixtures.Media(2, PlaybackState.Playing));
        model.Apply("c1", new IconMessage(2, TabFixtures.PngA));

        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(2, "youtube.com"), TabFixtures.Tab(3, "twitch.tv")));

        Assert.Equal(["p1:2", "p1:3", "p2:1"], model.Tabs.Select(t => t.Key).Order());
        Assert.Null(model.Tabs.Single(t => t.Key == "p1:2").Media); // a snapshot drops media and icons
        Assert.Null(model.IconPng("p1:2"));
    }

    [Fact]
    public void Snapshot_Keeps_Known_Tabs_Order_And_Puts_The_Active_Tab_Newest()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "a.org", active: true), TabFixtures.Tab(2, "b.org")));
        Assert.Equal("p1:1", model.Tabs[0].Key);
        var orderOfTwo = model.Tabs.Single(t => t.TabId == 2).LastActiveOrder;

        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "a.org", active: true), TabFixtures.Tab(2, "b.org")));

        Assert.Equal(orderOfTwo, model.Tabs.Single(t => t.TabId == 2).LastActiveOrder);
    }

    [Fact]
    public void Closing_A_Connection_Drops_Its_Tabs()
    {
        var model = TabFixtures.Connected();
        model.Open("c2", "p2");
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.org")));
        model.Apply("c2", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.org")));

        Assert.True(model.Close("c1"));

        Assert.Equal(["p2:1"], model.Tabs.Select(t => t.Key));
        Assert.True(model.Connected);
        Assert.True(model.Close("c2"));
        Assert.False(model.Connected);
        Assert.Empty(model.Tabs);
        Assert.False(model.Close("c2"));
    }

    [Fact]
    public void Second_Tab_Of_The_Same_Site_Is_Counted()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "youtube.com")));
        model.Apply("c1", new TabMessage(TabFixtures.Tab(2, "youtube.com")));

        var youtube = Pick.ForSite("YouTube", "youtube.com", PageIds.Media);
        var state = PickStates.For(youtube, new OpenSnapshot([], [], model.Tabs, model.Connected));

        Assert.True(state.IsOpen);
        Assert.Equal(2, state.Count);
    }

    [Fact]
    public void Newest_Active_Tab_Has_The_Highest_Order()
    {
        var model = TabFixtures.Connected();
        model.Open("c2", "p2");
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "a.org"), TabFixtures.Tab(2, "b.org")));
        model.Apply("c2", TabFixtures.Snapshot(TabFixtures.Tab(1, "c.org")));

        model.Apply("c1", new TabActivatedMessage(1, 1));

        Assert.Equal("p1:1", model.Tabs[0].Key);
        Assert.True(model.Tabs[0].Active);
        var orders = model.Tabs.Select(t => t.LastActiveOrder).ToList();
        Assert.Equal(orders.Count, orders.Distinct().Count()); // unique across connections
        Assert.Equal(orders.OrderDescending(), orders);         // listed newest first

        model.Apply("c2", new TabMessage(TabFixtures.Tab(9, "d.org")));
        Assert.Equal("p2:9", model.Tabs[0].Key); // a new tab is newest
    }

    [Fact]
    public void Activating_A_Tab_Deactivates_The_Others_Of_Its_Window_Only()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(
            TabFixtures.Tab(1, "a.org", active: true, window: 1),
            TabFixtures.Tab(2, "b.org", window: 1),
            TabFixtures.Tab(3, "c.org", active: true, window: 2)));

        model.Apply("c1", new TabActivatedMessage(2, 1));

        Assert.Equal([2, 3], model.Tabs.Where(t => t.Active).Select(t => t.TabId).Order());
    }

    [Fact]
    public void Two_Profiles_With_The_Same_Tab_Id_Do_Not_Clash()
    {
        var model = TabFixtures.Connected();
        model.Open("c2", "p2");
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(7, "a.org")));
        model.Apply("c2", TabFixtures.Snapshot(TabFixtures.Tab(7, "b.org")));

        model.Apply("c2", new TabRemovedMessage(7));

        Assert.Equal("a.org", Assert.Single(model.Tabs).Host);
        Assert.Equal(new TabAddress("c1", 7, 1), model.Locate("p1:7"));
        Assert.Null(model.Locate("p2:7"));
    }

    [Fact]
    public void A_Second_Connection_Of_The_Same_Profile_Replaces_The_First()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "a.org")));

        var replaced = model.Open("c9", "p1");

        Assert.Equal("c1", replaced);
        Assert.Empty(model.Tabs);
        Assert.False(model.Apply("c1", new TabMessage(TabFixtures.Tab(2, "a.org")))); // the old one is no longer heard
        Assert.True(model.Apply("c9", new TabMessage(TabFixtures.Tab(2, "a.org"))));
    }

    [Fact]
    public void Unknown_Ids_Are_Ignored_And_A_Tab_For_An_Unknown_Id_Is_An_Upsert()
    {
        var model = TabFixtures.Connected();
        Assert.False(model.Apply("c1", new TabRemovedMessage(5)));
        Assert.False(model.Apply("c1", new TabActivatedMessage(5, 1)));
        Assert.False(model.Apply("c1", new IconMessage(5, TabFixtures.PngA)));
        Assert.False(model.Apply("c1", TabFixtures.Media(5, PlaybackState.Playing)));
        Assert.True(model.Apply("c1", new TabMessage(TabFixtures.Tab(5, "a.org"))));
        Assert.False(model.Apply("c1", new TabMessage(TabFixtures.Tab(5, "a.org")))); // the same again changes nothing
        Assert.Single(model.Tabs);
    }

    [Fact]
    public void Media_Is_Kept_Only_For_The_Five_Media_Sites()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "example.org"), TabFixtures.Tab(2, "music.youtube.com")));

        Assert.False(model.Apply("c1", TabFixtures.Media(1, PlaybackState.Playing)));
        Assert.True(model.Apply("c1", TabFixtures.Media(2, PlaybackState.Playing)));

        Assert.Null(model.Tabs.Single(t => t.TabId == 1).Media);
        Assert.Equal(PlaybackState.Playing, model.Tabs.Single(t => t.TabId == 2).Media!.State);
    }

    [Fact]
    public void The_Tab_That_Started_Playing_Last_Wins()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "youtube.com"), TabFixtures.Tab(2, "youtube.com"), TabFixtures.Tab(3, "twitch.tv")));
        bool Picked(string host) => host == "youtube.com";

        Assert.Null(model.LastStarted(Picked));
        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Playing));
        model.Apply("c1", TabFixtures.Media(2, PlaybackState.Playing));
        Assert.Equal(2, model.LastStarted(Picked)!.TabId);

        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Paused));
        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Playing));   // back to the first
        Assert.Equal(1, model.LastStarted(Picked)!.TabId);

        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Playing, title: "Delta")); // still playing: not a new start
        model.Apply("c1", TabFixtures.Media(3, PlaybackState.Playing));   // an unpicked site does not take over (N7)
        Assert.Equal(1, model.LastStarted(Picked)!.TabId);

        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Paused));    // paused keeps the last one (N3)
        Assert.Equal(PlaybackState.Paused, model.LastStarted(Picked)!.Media!.State);
    }

    [Fact]
    public void Too_Many_Tabs_On_One_Connection_Are_Not_Kept()
    {
        var model = TabFixtures.Connected();
        for (var i = 0; i < TabProtocol.MaxTabsPerConnection + 50; i++) model.Apply("c1", new TabMessage(TabFixtures.Tab(i, "a.org")));
        Assert.Equal(TabProtocol.MaxTabsPerConnection, model.Tabs.Count);
    }

    [Fact]
    public void Many_Threads_At_Once_Leave_A_Consistent_Model()
    {
        var model = new TabModel();
        Parallel.For(0, 8, c =>
        {
            model.Open("c" + c, "p" + c);
            for (var i = 0; i < 300; i++)
            {
                model.Apply("c" + c, new TabMessage(TabFixtures.Tab(i, "youtube.com")));
                model.Apply("c" + c, new TabActivatedMessage(i / 2, 1));
                _ = model.Tabs.Count;
                if (i % 3 == 0) model.Apply("c" + c, new TabRemovedMessage(i));
            }
        });

        Assert.Equal(8 * 200, model.Tabs.Count);
        Assert.Equal(model.Tabs.Count, model.Tabs.Select(t => t.LastActiveOrder).Distinct().Count());
    }

    [Fact]
    public void Tab_Logic_Has_No_File_Api()
    {
        // EVALS I8: titles, hosts, tracks and icons of tabs live in memory only.
        var source = string.Concat(Directory.EnumerateFiles(RepoPaths.File("src", "Island.Core", "Tabs"), "*.cs").Select(File.ReadAllText));
        foreach (var word in new[] { "System.IO", "File.", "Directory.", "FileStream", "StreamWriter", "Path.Combine", "Save(" })
            Assert.DoesNotContain(word, source);
        Assert.DoesNotContain(typeof(TabModel).GetMethods(), m => m.GetParameters().Any(p => p.ParameterType == typeof(Stream) || p.Name is "path"));
    }
}

public class IconCacheTests
{
    [Fact]
    public void Changed_Address_Drops_The_Old_Icon()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "youtube.com")));
        model.Apply("c1", new IconMessage(1, TabFixtures.PngA));
        model.Apply("c1", TabFixtures.Media(1, PlaybackState.Playing));
        Assert.Equal(TabFixtures.PngA, model.IconPng("p1:1"));

        model.Apply("c1", new TabMessage(TabFixtures.Tab(1, "example.org", title: "Other")));

        Assert.Null(model.IconPng("p1:1"));
        Assert.Null(model.Tabs.Single().Media);
    }

    [Fact]
    public void Same_Address_Keeps_The_Icon_And_A_New_One_Replaces_It()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "youtube.com")));
        model.Apply("c1", new IconMessage(1, TabFixtures.PngA));

        model.Apply("c1", new TabMessage(TabFixtures.Tab(1, "youtube.com", title: "Next video")));
        Assert.Equal(TabFixtures.PngA, model.IconPng("p1:1"));

        Assert.False(model.Apply("c1", new IconMessage(1, TabFixtures.PngA)));
        Assert.True(model.Apply("c1", new IconMessage(1, TabFixtures.PngB)));
        Assert.Equal(TabFixtures.PngB, model.IconPng("p1:1"));
    }

    [Fact]
    public void A_Closed_Tab_Has_No_Icon()
    {
        var model = TabFixtures.Connected();
        model.Apply("c1", TabFixtures.Snapshot(TabFixtures.Tab(1, "youtube.com")));
        model.Apply("c1", new IconMessage(1, TabFixtures.PngA));

        model.Apply("c1", new TabRemovedMessage(1));

        Assert.Null(model.IconPng("p1:1"));
        Assert.Null(model.IconPng("nonsense"));
        Assert.Null(model.IconPng("p1:-1"));
    }
}
