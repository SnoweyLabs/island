using System.Text.Json;
using Island.Core;

namespace Island.Tests;

public class TabMessagesTests
{
    private static IEnumerable<KeyValuePair<string, string>> Good =>
        ProtocolExamples.All.Where(p => !p.Key.StartsWith("bad-", StringComparison.Ordinal));

    [Fact]
    public void The_Protocol_File_Has_Its_Examples()
    {
        Assert.True(ProtocolExamples.All.Count >= 18);
        Assert.Contains("snapshot", ProtocolExamples.All.Keys);
        Assert.Equal(4, ProtocolExamples.All.Keys.Count(k => k.StartsWith("bad-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_Good_Example_Parses()
    {
        foreach (var (label, json) in Good)
        {
            var ok = ProtocolExamples.FromIsland.Contains(label)
                ? TabMessages.ParseIsland(json).Ok
                : TabMessages.ParseAddon(json).Ok;
            Assert.True(ok, label);
        }
    }

    [Fact]
    public void Every_Bad_Example_Is_Rejected()
    {
        foreach (var (label, json) in ProtocolExamples.All.Where(p => p.Key.StartsWith("bad-", StringComparison.Ordinal)))
        {
            var parsed = TabMessages.ParseAddon(json);
            Assert.False(parsed.Ok, label);
            Assert.False(string.IsNullOrEmpty(parsed.Reject), label);
        }
    }

    [Fact]
    public void Examples_Parse_To_What_They_Say()
    {
        var snap = Assert.IsType<SnapshotMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("snapshot")).Message);
        Assert.Equal(2, snap.Tabs.Count);
        Assert.Equal(new TabObject(11, 1, "Lo-fi beats", "youtube.com", true, true, false, false), snap.Tabs[0]);
        Assert.True(snap.Tabs[1].Pinned);

        var hello = Assert.IsType<HelloMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("hello")).Message);
        Assert.Equal(("chrome", "p1"), (hello.Browser, hello.Profile));

        var icon = Assert.IsType<IconMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("icon")).Message);
        Assert.Equal(0x89, icon.Png[0]);

        var media = Assert.IsType<MediaMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("media")).Message);
        Assert.Equal(new TabMedia("Lo-fi beats to study to", "Some Channel", PlaybackState.Playing, 42.5, 3600), media.Media);

        var live = Assert.IsType<MediaMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("media-no-length")).Message);
        Assert.Null(live.Media.Artist);
        Assert.Null(live.Media.LengthSeconds);

        Assert.IsType<TabActivatedMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("tab-activated")).Message);
        Assert.IsType<ResultMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("result")).Message);
        Assert.IsType<PingMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("ping")).Message);
    }

    [Fact]
    public void Island_Frames_Are_Built_Exactly_As_The_Examples()
    {
        Assert.True(SameJson(ProtocolExamples.Get("welcome"), TabMessages.Welcome()));
        Assert.True(SameJson(ProtocolExamples.Get("activate"), TabMessages.Activate(11, 1)));
        Assert.True(SameJson(ProtocolExamples.Get("media-command"), TabMessages.MediaCommandFrame(11, MediaCommand.Next)));
        Assert.True(SameJson(ProtocolExamples.Get("resync"), TabMessages.Resync()));
        Assert.True(SameJson(ProtocolExamples.Get("pong"), TabMessages.Pong()));
    }

    [Fact]
    public void Island_Frames_Are_Not_Accepted_From_The_Addon()
    {
        foreach (var label in ProtocolExamples.FromIsland)
            Assert.False(TabMessages.ParseAddon(ProtocolExamples.Get(label)).Ok, label);
    }

    [Theory]
    [InlineData("""{"type":"tab-removed","id":-1}""")]                      // out of range
    [InlineData("""{"type":"tab-removed","id":1.5}""")]                     // not a whole number
    [InlineData("""{"type":"tab-removed","id":99999999999}""")]             // too big
    [InlineData("""{"type":"tab-removed","id":null}""")]
    [InlineData("""{"type":"hello","v":2,"client":"island-addon","browser":"chrome","profile":"p1","version":"0.1.0"}""")]
    [InlineData("""{"type":"hello","v":1,"client":"other","browser":"chrome","profile":"p1","version":"0.1.0"}""")]
    [InlineData("""{"type":"hello","v":1,"client":"island-addon","browser":"chrome","profile":"p 1","version":"0.1.0"}""")]
    [InlineData("""{"type":"hello","v":1,"client":"island-addon","browser":"","profile":"p1","version":"0.1.0"}""")]
    [InlineData("""{"type":"media","id":1,"title":null,"artist":null,"state":"rewinding","position":null,"length":null}""")]
    [InlineData("""{"type":"media","id":1,"title":null,"artist":null,"state":"playing","position":-3,"length":null}""")]
    [InlineData("""{"type":"media","id":1,"title":5,"artist":null,"state":"playing","position":null,"length":null}""")]
    [InlineData("""{"type":"icon","id":1,"png":"not base64!"}""")]
    [InlineData("""{"type":"icon","id":1,"png":"aGVsbG8gd29ybGQ="}""")]                 // base64, but not a PNG
    [InlineData("""{"type":"result","cmd":"launch","id":1,"ok":true}""")]
    [InlineData("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"x","host":"a.b","audible":"yes","active":false}}""")]
    [InlineData("""{"type":"tab","tab":{"id":1,"windowId":1,"title":"x","host":"a.b","audible":false,"active":false,"pinned":1}}""")]
    [InlineData("""{"type":"snapshot","tabs":[{"id":1,"windowId":1,"title":"x","host":"a.b","audible":false,"active":false},7]}""")]
    [InlineData("""{"type":"snapshot","tabs":{}}""")]
    [InlineData("""{"type":7}""")]
    [InlineData("""{"kind":"ping"}""")]
    [InlineData("""not json""")]
    [InlineData("{\"type\":\"ping\"")]
    [InlineData("""null""")]
    [InlineData("\"ping\"")]
    [InlineData("")]
    public void Bad_Frames_Are_Rejected_With_A_Reason(string frame)
    {
        var parsed = TabMessages.ParseAddon(frame);
        Assert.False(parsed.Ok);
        Assert.False(string.IsNullOrEmpty(parsed.Reject));
    }

    [Fact]
    public void A_Null_Frame_Is_Rejected()
    {
        Assert.False(TabMessages.ParseAddon(null).Ok);
        Assert.False(TabMessages.ParseIsland(null).Ok);
    }

    [Fact]
    public void Host_Is_Normalized_And_Unknown_Fields_Are_Ignored()
    {
        var parsed = TabMessages.ParseAddon("""{"type":"tab","extra":[1],"tab":{"id":3,"windowId":2,"title":"x","host":"WWW.Example.org","audible":false,"active":true,"new":true}}""");
        Assert.Equal("example.org", Assert.IsType<TabMessage>(parsed.Message).Tab.Host);
    }

    [Fact]
    public void A_Title_Over_200_Characters_Is_Rejected()
    {
        var at = Frame(new string('a', 200));
        var over = Frame(new string('a', 201));
        Assert.True(TabMessages.ParseAddon(at).Ok);
        Assert.False(TabMessages.ParseAddon(over).Ok);

        static string Frame(string title) =>
            $$$"""{"type":"tab","tab":{"id":1,"windowId":1,"title":"{{{title}}}","host":"a.b","audible":false,"active":false}}""";
    }

    [Fact]
    public void An_Icon_Over_64_KB_Is_Rejected()
    {
        var png = new byte[TabProtocol.MaxIconBytes + 1];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        Assert.True(TabMessages.ParseAddon(IconFrame(png[..TabProtocol.MaxIconBytes])).Ok);
        Assert.False(TabMessages.ParseAddon(IconFrame(png)).Ok);

        static string IconFrame(byte[] b) => $$"""{"type":"icon","id":1,"png":"{{Convert.ToBase64String(b)}}"}""";
    }

    [Fact]
    public void A_Huge_Frame_Is_Rejected_As_Oversize()
    {
        var huge = $$$"""{"type":"tab","tab":{"id":1,"windowId":1,"title":"{{{new string('x', TabProtocol.MaxFrameBytes)}}}","host":"a.b","audible":false,"active":false}}""";
        Assert.Equal("oversize", TabMessages.ParseAddon(huge).Reject);

        // Under the limit in characters, over it in UTF-8 bytes.
        var wide = $$"""{"type":"ping","x":"{{new string('é', TabProtocol.MaxFrameBytes / 2 + 10)}}"}""";
        Assert.Equal("oversize", TabMessages.ParseAddon(wide).Reject);
    }

    [Fact]
    public void Deep_Nesting_Is_Rejected_Without_Throwing()
    {
        var deep = """{"type":"ping","x":""" + new string('[', 5000) + new string(']', 5000) + "}";
        Assert.False(TabMessages.ParseAddon(deep).Ok);
        var deepIsland = """{"type":"pong","x":""" + new string('{', 3000);
        Assert.False(TabMessages.ParseIsland(deepIsland).Ok);
    }

    [Fact]
    public void Hundreds_Of_Random_Frames_Never_Throw()
    {
        var random = new Random(4);
        var seeds = ProtocolExamples.All.Values.ToArray();
        for (var i = 0; i < 2000; i++)
        {
            var chars = seeds[random.Next(seeds.Length)].ToCharArray();
            var cuts = random.Next(1, 6);
            for (var c = 0; c < cuts; c++) chars[random.Next(chars.Length)] = (char)random.Next(32, 127);
            var frame = new string(chars, 0, random.Next(chars.Length + 1));
            var a = TabMessages.ParseAddon(frame);
            var b = TabMessages.ParseIsland(frame);
            Assert.True(a.Ok ^ (a.Reject is not null));
            Assert.True(b.Ok ^ (b.Reject is not null));
        }
    }

    private static bool SameJson(string a, string b) =>
        JsonSerializer.Serialize(JsonDocument.Parse(a).RootElement) == JsonSerializer.Serialize(JsonDocument.Parse(b).RootElement);
}
