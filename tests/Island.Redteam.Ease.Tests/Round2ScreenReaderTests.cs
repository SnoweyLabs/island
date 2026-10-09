using Island.Core;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 2: the words the island gives a screen reader (commit 939ef60: <c>IslandRoot</c> and <c>IslandController.DescribeForScreenReader</c>). Island.App is an executable no test project can load,
/// so these read the source (as round 1's source scans do); the one run-time premise (a newer notice replaces the one showing) is run on Island.Core.
/// </summary>
public class Round2ScreenReaderTests
{
    private static string Method()
    {
        var src = Source.Read("src/Island.App/IslandController.cs");
        var start = src.IndexOf("private void DescribeForScreenReader", StringComparison.Ordinal);
        var end = src.IndexOf("private void TurnGraphicsLightOff", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "DescribeForScreenReader was not found where it was");
        return src[start..end];
    }

    private static string KeyLine() => Method().Split('\n').First(l => l.Contains("var key = (", StringComparison.Ordinal));

    // ---- held -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Held: the words are worked out once per change (the key is compared first), not once per frame; the root's own Describe also returns at once for the same text, and asks the automation
    /// peer only when a screen reader has made one (FromElement), so nothing is built per frame for nobody.</summary>
    [Fact]
    public void The_Description_Is_Told_Once_Per_Change_And_Costs_Nothing_Per_Frame_For_Nobody()
    {
        Assert.Contains("if (key == _describedKey) return;", Method());
        var root = Source.Read("src/Island.App/Visuals/IslandRoot.cs");
        Assert.Contains("if (text == _description) return;", root);
        Assert.Contains("UIElementAutomationPeer.FromElement(this) is { } peer", root);
    }

    /// <summary>
    /// Repaired (ease-2-11, WORK-ORDER-12 section 4; this test held "only the notice raises a live-region event" before): every change of the island's words is raised as a live-region event, at once
    /// for a notice and politely for the rest, because the island never holds the keyboard focus. Whether Narrator speaks it is UNVERIFIED (no screen reader here): it is in "De verificat de Dan".
    /// </summary>
    [Fact]
    public void Every_Change_Of_The_Words_Raises_A_Live_Region_Event()
    {
        var root = Source.Read("src/Island.App/Visuals/IslandRoot.cs");
        Assert.Contains("peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);", root);
        Assert.DoesNotContain("if (alert) peer.RaiseAutomationEvent", root);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(root, "RaiseAutomationEvent"));
    }

    /// <summary>Held: the page, the selected tile, its second line and its place are in the words, the hidden island says nothing (Draw returns before it for a hidden island).</summary>
    [Fact]
    public void The_Words_Name_The_Page_The_Tile_Its_Second_Line_And_Its_Place()
    {
        var m = Method();
        Assert.Contains("{page}", m);
        Assert.Contains("items[selected].Title", m);
        Assert.Contains("items[selected].Subtitle", m);
        Assert.Contains("{selected + 1} of {items.Count}", m);
        var controller = Source.Read("src/Island.App/IslandController.cs");
        var draw = controller.IndexOf("private void Draw(double now)", StringComparison.Ordinal);
        Assert.True(controller.IndexOf("if (_machine.Phase == IslandPhase.Hidden) return;", draw, StringComparison.Ordinal) < controller.IndexOf("DescribeForScreenReader(pill, notice, search);", draw, StringComparison.Ordinal));
    }

    /// <summary>The premise of ease-2-2, run: a newer notice of another session replaces the one that is showing and has the same line; only the project differs.</summary>
    [Fact]
    public void A_Newer_Notice_Of_Another_Project_Replaces_The_One_Showing_And_Has_The_Same_Line()
    {
        var queue = new NoticeQueue();
        var t0 = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var first = new AgentNotice(AgentSignal.Finished, "Alpha", "p1", [1]);
        var second = new AgentNotice(AgentSignal.Finished, "Beta", "p2", [2]);
        Assert.True(queue.Post(first, t0));
        Assert.Same(first, queue.Update(t0.AddSeconds(1), capsuleOpen: false, pillUp: false, pointerOver: false).Showing);

        Assert.True(queue.Post(second, t0.AddSeconds(2)));
        var now = queue.Update(t0.AddSeconds(3), capsuleOpen: false, pillUp: false, pointerOver: false).Showing;

        Assert.Same(second, now);
        Assert.Equal(first.Line, second.Line);
        Assert.NotEqual(first.ProjectName, second.ProjectName);
    }

    // ---- ease-2-1 ... 2-4 -------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-2-1 (MEDIUM). The words are made again only when the key tuple changes, and the tuple holds the page, the selected index and the count, not the selected tile's own title and second
    /// line. A tile whose second line changes with the same index and the same count keeps the old words: a pick that was "closed" and is now "open" (a program the person just started with
    /// Enter, when the island comes back), a Terminals tile that was "working" and is now "needs you" (that page exists to say exactly that), the Media tile of the next track. The words
    /// are then false. While the island is hidden Draw returns before the check, so the words of the last visit stand at the next summon when the selection is the same.
    /// Expected: the selected tile's title and second line (items[selected]) are part of the key.
    /// </summary>
    [Fact]
    public void Defect_The_Words_Of_The_Selected_Tile_Go_Stale_When_Its_Second_Line_Changes()
    {
        Assert.Contains("items[selected]", KeyLine());
    }

    /// <summary>
    /// ease-2-2 (MEDIUM). The notice is the one thing told as an alert, and its key holds the notice's line (one of two fixed sentences) but not its project. A second notice from another project
    /// while the first is still on screen (NoticeQueue.Post replaces it; AgentNoticeHost.Show then calls SetNotice(true) again with the new text) has the same line: the key does not change and the
    /// screen reader is not told. Expected: the project is part of the key.
    /// </summary>
    [Fact]
    public void Defect_A_Second_Notice_Of_Another_Project_With_The_Same_Line_Is_Not_Told()
    {
        Assert.Contains("Project", KeyLine());
    }

    /// <summary>
    /// ease-2-3 (LOW). The second row (what is open now, to add a tile) takes the keyboard (Down from the + tile; Left and Right move in it; Enter adds) and draws its selection; the words name the + tile
    /// ("add something") the whole time and never the tile the person is on in the row: SecondRowOpen is in the key but SecondRowSelected is not, and the words do not read the row. A person who
    /// cannot see the row cannot know what Enter will add. (Adding by hand in Settings is the other way and is reachable.) Expected: the words name the second row's selected tile.
    /// </summary>
    [Fact]
    public void Defect_The_Second_Rows_Selected_Tile_Is_Not_Named()
    {
        Assert.Contains("SecondRowSelected", Method());
    }

    /// <summary>
    /// ease-2-4 (LOW). The pill says "Now playing: title" and the key does not hold whether it is paused, though the pill shows a paused state (PillContent has IsPaused): a paused pill is
    /// called "now playing", and a pause or a resume is not told. Expected: the words follow IsPaused ("Paused: title").
    /// </summary>
    [Fact]
    public void Defect_The_Pill_Is_Called_Now_Playing_When_It_Is_Paused()
    {
        Assert.Contains("IsPaused", Method());
    }
}
