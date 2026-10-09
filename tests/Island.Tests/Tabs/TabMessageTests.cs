using Island.Core;

namespace Island.Tests;

/// <summary>The message of the close button (WORK-ORDER-6 section 5): one example in extension/PROTOCOL.md, read by this side and by the add-on's own tests.</summary>
public class TabMessageTests
{
    private static bool SameJson(string a, string b) =>
        System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(a).RootElement)
        == System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(b).RootElement);

    [Fact]
    public void Close_Tab_Matches_The_Protocol_Example()
    {
        Assert.True(SameJson(ProtocolExamples.Get("close"), TabMessages.CloseTab(11)));

        // The pretend add-on reads it the way the real one does, and the island refuses it from the add-on's side.
        var message = Assert.IsType<CloseTabMessage>(TabMessages.ParseIsland(ProtocolExamples.Get("close")).Message);
        Assert.Equal(11, message.Id);
        Assert.False(TabMessages.ParseAddon(ProtocolExamples.Get("close")).Ok);

        // The add-on's answer to it is read, and an answer for a command that is not in the protocol is not.
        var result = Assert.IsType<ResultMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("result-close")).Message);
        Assert.Equal(("close", 11, true), (result.Command, result.Id, result.Ok));
        Assert.False(TabMessages.ParseAddon("""{"type":"result","cmd":"quit","id":11,"ok":true}""").Ok);
    }

    [Theory]
    [InlineData("""{"type":"close"}""")]
    [InlineData("""{"type":"close","id":-1}""")]
    [InlineData("""{"type":"close","id":"11"}""")]
    [InlineData("""{"type":"close","id":1.5}""")]
    public void A_Close_Without_A_Usable_Tab_Id_Is_Refused(string frame) =>
        Assert.False(TabMessages.ParseIsland(frame).Ok);

    [Fact]
    public void Playing_Message_Carries_Speed_And_Reading_Time()
    {
        // The example both sides read: rate and readAt reach the model, so the position can be worked out between two reports.
        var timing = Assert.IsType<MediaMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("media-timing")).Message);
        Assert.Equal(1.25, timing.Media.Rate);
        Assert.Equal(1790000000000, timing.Media.ReadAtMs);
        Assert.Equal(42.5, timing.Media.PositionSeconds);
        Assert.Equal(3600, timing.Media.LengthSeconds);

        // An older add-on sends neither: nothing is made up.
        var plain = Assert.IsType<MediaMessage>(TabMessages.ParseAddon(ProtocolExamples.Get("media")).Message);
        Assert.Null(plain.Media.Rate);
        Assert.Null(plain.Media.ReadAtMs);

        // A field that is there must be a sensible number; null is "not given".
        Assert.IsType<MediaMessage>(TabMessages.ParseAddon("""{"type":"media","id":1,"title":null,"artist":null,"state":"playing","position":1,"length":9,"rate":null,"readAt":null}""").Message);
        foreach (var bad in new[]
        {
            "\"rate\":\"fast\"",
            "\"rate\":-1",
            "\"rate\":17",
            "\"readAt\":\"now\"",
            "\"readAt\":0",
            "\"readAt\":-5",
            "\"readAt\":1e30",
        })
            Assert.False(TabMessages.ParseAddon("{\"type\":\"media\",\"id\":1,\"title\":null,\"artist\":null,\"state\":\"playing\",\"position\":1,\"length\":9," + bad + "}").Ok, bad);

        // The report the pill works from takes the add-on's reading time, never one from the future.
        var arrived = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
        var report = TabMediaTiming.ToReport(timing.Media.PositionSeconds, timing.Media.LengthSeconds, true, timing.Media.Rate, timing.Media.ReadAtMs, arrived);
        Assert.Equal(1.25, report.Speed);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790000000000), report.ReportedAt); // the add-on's reading time, earlier than the arrival
        var future = TabMediaTiming.ToReport(1, 9, true, 1, 1790000000000, DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), future.ReportedAt); // a reading from the future counts as arrived-now
    }
}
