using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>Round 4: the island's words after the repairs of round 3 (3e7ec3d), by reading (Island.App cannot be loaded by a test).</summary>
public class Round4ScreenReaderTests
{
    private static string Controller() => Source.Read("src/Island.App/IslandController.cs");

    private static string Words()
    {
        var text = Controller();
        var start = text.IndexOf("private void DescribeForScreenReader", StringComparison.Ordinal);
        var end = text.IndexOf("private void TurnGraphicsLightOff", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return text[start..end];
    }

    /// <summary>Held: the pending Delete is in the key and in the words of the pick it was asked about (and of no other), and the words of the next visit start from the plain name (Detach forgets them).</summary>
    [Fact]
    public void The_Pending_Delete_Is_Told_Only_For_The_Pick_It_Was_Asked_About_And_The_Visit_Is_Forgotten_At_A_Hide()
    {
        var words = Words();
        Assert.Contains("_machine.PendingDeleteId is not null && items[selected].PickId == _machine.PendingDeleteId", words);
        var detach = Controller()[Controller().IndexOf("private void Detach()", StringComparison.Ordinal)..];
        Assert.Contains("_view.ForgetWords();", detach[..detach.IndexOf("Left?.Invoke();", StringComparison.Ordinal)]);
    }

    /// <summary>
    /// ease-4-1 (LOW). The repair of ease-3-5 (3e7ec3d) tells the pending Delete and nothing else of the three notes the text block can carry in place of a tile's second line. After a scene's key the island comes
    /// in and draws the scene's name and "N opened" (<c>IslandRuntime.RunScene</c> calls <c>ShowSceneNote</c>); that note is held in <c>ContentsLayer</c> only, and the words (and their key) never read it. The person
    /// who cannot see the island presses a scene's key and is told the page and the selected tile, not that the scene ran or how many things it opened (a scene that opened fewer than it holds is not told as such to
    /// anyone). The note of a close that is refused is mouse-only (the X has no key: WORK-ORDER-10), so only the scene note is reachable by a keyboard. Expected: the controller keeps the scene's name and count and the
    /// words (and their key) carry them until the selection moves.
    /// </summary>
    [Fact]
    public void Defect_The_Scene_That_Just_Ran_Is_Drawn_As_A_Note_And_Not_Told()
    {
        var words = Words();
        Assert.Contains("cene", words);
    }

    /// <summary>Held: the way a scene is run by key reaches the island through <c>ShowSceneNote</c> only (so the one place that would carry the words is IslandController.ShowSceneNote).</summary>
    [Fact]
    public void The_Scene_Note_Reaches_The_Island_Through_One_Method()
    {
        Assert.Contains("public void ShowSceneNote(string name, string opened)", Controller()); // repaired (ease-4-1): the method also keeps the note for the words
    }
}
