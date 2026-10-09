using Island.Core;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 3: the words the island gives a screen reader after the repairs of round 2 (db7c540: <c>IslandController.DescribeForScreenReader</c>, <c>IslandRoot.Describe</c>, the reset of the key at a hide).
/// Island.App is an executable no test project can load, so these read the source (as rounds 1 and 2 do); the keyboard rules they must agree with are run on Island.Core.
/// </summary>
public class Round3ScreenReaderTests
{
    private static string Controller() => Source.Read("src/Island.App/IslandController.cs");

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{from} was not found where it was");
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{to} was not found after {from}");
        return text[start..end];
    }

    private static string Method() => Between(Controller(), "private void DescribeForScreenReader", "private void TurnGraphicsLightOff");

    private static string KeyLine() => Method().Split('\n').First(l => l.Contains("var key = (", StringComparison.Ordinal));

    private static KeyContext InSecondRow() => new()
    {
        HasKeyboard = true,
        RowIsCurrentPage = true,
        SecondRowOpen = true,
        SelectionInSecondRow = true,
        SecondRowHasItems = true,
        Selected = SelectedTile.Plus,
        PageCount = 3,
        PlusClickActs = true,
    };

    // ---- ease-3-1 --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The premise of ease-3-1, run on the keyboard rules: in the second row Enter jumps to the thing (adds nothing) and Shift+Enter adds it. The settings screen says the same ("[Shift+Enter] in the second row adds the thing to the page").</summary>
    [Fact]
    public void In_The_Second_Row_Enter_Jumps_And_Only_Shift_Enter_Adds()
    {
        Assert.Equal(KeyAction.SecondRowJump, IslandKeys.Decide(new KeyInput(0x0D), InSecondRow()).Action);
        Assert.Equal(KeyAction.SecondRowAdd, IslandKeys.Decide(new KeyInput(0x0D, Shift: true), InSecondRow()).Action);
        Assert.Contains(Island.Core.SettingsEdit.SettingsText.IslandKeys, l => l.Contains("[Shift+Enter] in the second row adds"));
    }

    /// <summary>
    /// ease-3-1 (MEDIUM). The repair of ease-2-3 (db7c540) names the second row's tile with the words "press Enter to add it". Enter in the second row jumps to that thing and adds nothing (the
    /// island's own key help, the self-test and IslandKeys all say so); the key that adds is Shift+Enter. A person who cannot see the row follows the words, presses Enter, and is taken to a window
    /// instead (the row's whole purpose, adding, is never reachable by the words). Expected: the words name the key that adds ("Shift+Enter") and the one that jumps ("Enter").
    /// </summary>
    [Fact]
    public void Defect_The_Second_Rows_Words_Say_Enter_Adds_The_Thing_But_Enter_Jumps_To_It()
    {
        var words = Method();
        Assert.DoesNotContain("press Enter to add it", words);
        Assert.Contains("Shift+Enter", words);
    }

    // ---- ease-3-2 --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The premise of ease-3-2: while the keyboard is in the second row the selected tile of the first row stays on the + tile (the machine moves only SecondRowSelected), so the key's tile slot holds the + tile.</summary>
    [Fact]
    public void While_The_Keyboard_Is_In_The_Second_Row_The_Selected_Tile_Of_The_First_Row_Stays_On_The_Plus_Tile()
    {
        var machine = Source.Read("src/Island.Core/IslandMachine.cs");
        var enter = Between(machine, "public bool EnterSecondRow", "public void LeaveSecondRow");
        Assert.Contains("SecondRowSelected = 0;", enter);
        Assert.DoesNotContain("SelectedItem =", enter);
        var move = Between(machine, "public void MoveSecondRow", "public bool AskRemove");
        Assert.DoesNotContain("SelectedItem =", move);
        // and the row is drawn again, under the same index, when something opens or closes (RefreshIfOpen): the tile at an index changes without the key changing
        Assert.Contains("RefreshIfOpen", Source.Read("src/Island.App/SecondRow.cs"));
    }

    /// <summary>
    /// ease-3-2 (MEDIUM, a narrow trigger). The key that decides whether the words are made again holds the selected tile of the first row, or the row's tile only when the first row has no valid
    /// selection: <c>selected &gt;= 0 &amp;&amp; selected &lt; items.Count ? items[selected] : rowTile</c>. In the second row the first row's selection is still valid (the + tile), so the row's own tile is not in
    /// the key; only its index is. The second row is drawn again while it is open when something opens or closes (<c>SecondRow.RefreshIfOpen</c>): the tile under the same index is another thing, the key is
    /// the same, and the words go on saying the old name ("open now: Alpha, press ... 1 of 3") while the key that acts goes to another one. The count "of N" is not in the key either. Expected: the
    /// row's tile and the row's count are in the key on their own.
    /// </summary>
    [Fact]
    public void Defect_The_Words_Of_The_Second_Rows_Tile_Are_Not_Made_Again_When_The_Row_Is_Drawn_Again_Under_The_Same_Index()
    {
        var line = KeyLine().Replace("items[selected] : rowTile", string.Empty, StringComparison.Ordinal);
        Assert.Contains("rowTile", line);
        Assert.Contains("SecondRowCount", line);
    }

    // ---- ease-3-3 --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-3-3 (MEDIUM). While search is open the words are the constant "Island, search": the typed text, how many things were found, the selected one and "Nothing found" / "Type to search"
    /// (all in <c>SearchViewData</c> and drawn in the text block) are never told, and the key holds only the flag <c>search</c>, so nothing is told again when the text or the selection changes.
    /// Search is opened by typing any letter, so a person with a screen reader who types one is in a mode where the arrows choose among things he is never told, and Enter starts the chosen one.
    /// Expected: the words name the selected match (or "Nothing found") and its place, and the key holds what they say.
    /// </summary>
    [Fact]
    public void Defect_The_Words_Of_Search_Are_The_Same_Whatever_Is_Typed_And_Found()
    {
        var branch = Between(Method(), "if (search)", "var page = ");
        Assert.DoesNotContain("Describe(\"Island, search\"", branch);
        Assert.Contains("_searchData", KeyLine());
    }

    // ---- ease-3-4 --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The premise of ease-3-4: the frame step that sees the machine hidden detaches and returns BEFORE it calls Draw, so Draw's own "hidden: forget the described key" line is not reached on the ordinary way out.</summary>
    [Fact]
    public void The_Frame_Step_Detaches_And_Returns_Before_Draw_When_The_Island_Is_Hidden()
    {
        var step = Between(Controller(), "private bool Step(double now)", "private void Draw(double now)");
        var hidden = step.IndexOf("IslandPhase.Hidden", StringComparison.Ordinal);
        var detach = step.IndexOf("Detach();", hidden, StringComparison.Ordinal);
        var ret = step.IndexOf("return false;", detach, StringComparison.Ordinal);
        var draw = step.IndexOf("Draw(now);", StringComparison.Ordinal);
        Assert.True(hidden >= 0 && detach > hidden && ret > detach && draw > ret, "Step no longer returns before Draw for a hidden island");
        var detachBody = Between(Controller(), "private void Detach()", "private void Draw(double now)");
        Assert.Contains("_describedKey = default", detachBody); // repaired (ease-3-4, WORK-ORDER-12 section 4): this test held "not" before; Detach now forgets what was told, so the next visit is told again
    }

    /// <summary>
    /// ease-3-4 (LOW). The repair of ease-2-1 says "the next visit is told again" and resets the key inside Draw's hidden branch; on the ordinary way out (Step sees the phase hidden, calls Detach and returns)
    /// that line is not run, and Detach does not reset the key. What the key does not hold then stands at the next summon: the page's NAME (the key holds the page id), so a page renamed in Settings
    /// is still told by its old name when the next visit is on the same page, tile and count. (A closed, open or windows-count change of the tile IS in the key through the Item: held.) Also, the
    /// root's own Describe returns at once for the same text, so even the reset could not make the same words be told twice: the comment's promise is not what the code does. Expected: the page's name
    /// is in the key, or Detach resets the key.
    /// </summary>
    [Fact]
    public void Defect_A_Page_Renamed_While_The_Island_Was_Hidden_Is_Told_By_Its_Old_Name_At_The_Next_Summon()
    {
        var detachBody = Between(Controller(), "private void Detach()", "private void Draw(double now)");
        Assert.True(KeyLine().Contains("ContentsPage.Name", StringComparison.Ordinal) || detachBody.Contains("_describedKey", StringComparison.Ordinal),
            "neither the key holds the page's name nor the way out resets the key");
    }

    /// <summary>
    /// ease-3-4 (second half, MEDIUM, UNVERIFIED without a screen reader). Even where the key is reset (Draw's hidden branch), the words are told again only if they differ: <c>IslandRoot.Describe</c> returns at
    /// once for the same text, and nothing else ever forgets the last words. The most ordinary summon is the same page, the same first tile and the same count as the last visit: the words are the same,
    /// no property change and no live-region event is raised, and the person who cannot see the island is told only the window's own name ("Island") when it comes. The comment of the repair of ease-2-1
    /// says "the next visit is told again"; the code cannot do it. Expected: the root forgets its words when the island hides (so the next summon raises the event again), or Describe has a way to say the
    /// same words again.
    /// </summary>
    [Fact]
    public void Defect_The_Same_Words_At_The_Next_Summon_Are_Never_Raised_Again_Because_The_Root_Forgets_Nothing()
    {
        var root = Source.Read("src/Island.App/Visuals/IslandRoot.cs");
        var assignments = System.Text.RegularExpressions.Regex.Matches(root, @"_description\s*=[^=]").Count; // the field, and the change in Describe: two
        Assert.True(assignments >= 3 || root.Contains("public void Forget", StringComparison.Ordinal) || root.Contains("bool again", StringComparison.Ordinal), "nothing makes the root say the same words again");
    }

    // ---- ease-3-5 --------------------------------------------------------------------------------------------------------------------------

    /// <summary>The premise of ease-3-5: after a first Delete the island draws "Delete again to remove" in its text block; after a scene it draws the scene's name and "N opened"; a close that is refused draws a note
    /// ("runs as administrator"). All three are drawn by the text block (ContentsLayer), none by the words.</summary>
    [Fact]
    public void The_Text_Block_Draws_The_Pending_Delete_The_Scene_Note_And_The_Close_Note()
    {
        var layer = Source.Read("src/Island.App/Visuals/ContentsLayer.cs");
        Assert.Contains("_subtitle.Text = _removeNote;", layer);
        Assert.Contains("_title.Text = _sceneTitle;", layer);
        Assert.Contains("_closeNote ?? item?.Subtitle", layer);
        Assert.Contains("Delete again to remove", Controller());
    }

    /// <summary>
    /// ease-3-5 (LOW). The words are made from the tiles only. What the text block says after a first Delete ("Delete again to remove"), after a scene ran ("Mix, 3 opened") and when a close is refused
    /// ("runs as administrator") is on the screen and never in the words: a person with a screen reader presses Delete once and hears nothing, so he does not know that a second press removes (a
    /// held Delete is held, not repeated, so nothing is lost by accident), and he is told nothing after a scene or a refused close. Expected: the words (and their key) hold the pending delete, the scene
    /// note and the close note.
    /// </summary>
    [Fact]
    public void Defect_A_First_Delete_The_Scene_Note_And_The_Close_Note_Are_Drawn_And_Not_Told()
    {
        var all = Method();
        Assert.Contains("PendingDeleteId", all);
    }

    // ---- held -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Held (ease-2-11): the live-region event is raised only through a peer that a client has already made (FromElement), only when the text changed (the early return), and never for empty text (the empty text is the name).</summary>
    [Fact]
    public void The_Live_Region_Event_Is_Never_Raised_Before_The_Peer_Exists_Or_For_The_Same_Words()
    {
        var root = Source.Read("src/Island.App/Visuals/IslandRoot.cs");
        var describe = Between(root, "public void Describe", "protected override AutomationPeer OnCreateAutomationPeer");
        Assert.True(describe.IndexOf("if (text == _description) return;", StringComparison.Ordinal) < describe.IndexOf("RaiseAutomationEvent", StringComparison.Ordinal));
        Assert.True(describe.IndexOf("UIElementAutomationPeer.FromElement(this) is { } peer", StringComparison.Ordinal) < describe.IndexOf("RaiseAutomationEvent", StringComparison.Ordinal));
        Assert.Contains("string.IsNullOrWhiteSpace(text) ? Name : text", describe);
    }

    /// <summary>Held: the held key repeats of Left and Right are the only repeats that act, so the words are made at most once for each repeat; nothing in the words is on a timer. How a screen reader treats thirty polite changes a second is UNVERIFIED (no screen reader here).</summary>
    [Fact]
    public void A_Held_Arrow_Makes_One_Change_Of_The_Words_For_Each_Repeat_And_Nothing_Coalesces_Them()
    {
        var repeat = IslandKeys.Decide(new KeyInput(0x27, IsRepeat: true), new KeyContext { HasKeyboard = true, RowIsCurrentPage = true, Selected = SelectedTile.Pick, PageCount = 3 });
        Assert.Equal(KeyAction.MoveRight, repeat.Action);
        Assert.DoesNotContain("Timer", Method());
        Assert.DoesNotContain("Delay", Method());
    }

    /// <summary>Held (ease-2-1): the selected tile's whole record is in the key, so a closed, open or windows-count change of the same tile is told again, and a tile's icon arriving changes only the record, not the words (the root returns for the same text).</summary>
    [Fact]
    public void The_Whole_Selected_Tile_Is_In_The_Key_So_A_Change_Of_Its_Second_Line_Is_Told()
    {
        Assert.Contains("items[selected]", KeyLine());
        Assert.Contains("public sealed record Item(", Source.Read("src/Island.Core/Page.cs"));
    }
}
