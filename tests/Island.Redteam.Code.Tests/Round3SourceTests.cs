using System.Text.RegularExpressions;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 3 (code-3-*): what the repairs of round 2 do in the app's windows, shown by the app's own source (nothing here starts the app, makes a window, a MovingLight or a GlassLayer).
/// Each test states what a repair must contain, in the loosest words that still prove it.
/// </summary>
public sealed class Round3SourceTests
{
    private static string Section(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "not found: " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "end not found: " + end);
        return text[from..to];
    }

    private static string Controller => Repo.Text("src", "Island.App", "IslandController.cs");

    // ---- code-3-1: the next visit is told again (the repair of ease-2-1) ------------------------------------------------------------------------

    [Fact]
    public void Held_The_Words_Key_Holds_The_Items_The_Pill_And_The_Notice_So_A_Change_Inside_A_Visit_Is_Told()
    {
        var key = Section(Controller, "var key = (", "if (key == _describedKey)");
        foreach (var part in new[] { "ContentsPageId", "NoticeText?.Project", "paused", "items[selected]", "rowTile", "SecondRowSelected", "SecondRowOpen" })
            Assert.Contains(part, key);
    }

    [Fact]
    public void Defect_The_Reset_Of_The_Words_Key_Is_In_Draw_Where_A_Hide_Of_The_Island_Never_Passes()
    {
        // code-3-1 (MEDIUM, the code path is proven by the source; whether a screen reader then says nothing needs a person with Narrator). db7c540 put "_describedKey = default" (the comment says: "the
        // next visit is told again") into the branch of Draw that returns when the phase is Hidden. The phase becomes Hidden only inside IslandMachine.Tick (Closing, settled out of sight), and
        // the frame that runs it is Step: "_machine.Tick(now); ... if (_machine.Phase == IslandPhase.Hidden) { Detach(); return false; }" returns BEFORE Draw. Detach (the one place every hide
        // passes through) does not touch the key. So after an ordinary Esc or idle time the key of the last visit stands, and at the next summon Draw finds the same page, selection, count and
        // tile, the same key, and calls nothing: the person who summons the island again to the same page is told nothing. (Draw's own branch runs only when an input or a notice ends the
        // visit between two frames, which is not the ordinary way.) Expected: the key is put back to default where the island leaves (Detach, or Step's Hidden branch).
        var step = Section(Controller, "private bool Step(double now)", "private void UpdateKeyboard(double now)");
        var hiddenInStep = Section(step, "if (_machine.Phase == IslandPhase.Hidden)", "Draw(now);");
        var detach = Section(Controller, "private void Detach()", "private readonly Island.Core.Speed.FrameBudget _budget");
        Assert.True(hiddenInStep.Contains("_describedKey") || detach.Contains("_describedKey"), "an ordinary hide (Step, then Detach) never resets the key of the screen reader's words");
    }

    [Fact]
    public void Defect_The_Island_Root_Ignores_Words_Equal_To_The_Last_Ones_So_A_Reset_Of_The_Key_Alone_Raises_Nothing()
    {
        // code-3-1, second layer. IslandHost builds ONE IslandRoot for the life of the app ("public IslandRoot Root { get; } = new() ..."). IslandRoot.Describe returns at once when the text equals the
        // last text ("if (text == _description) return;"), so even when the controller does call Describe at the next visit with the very words of the last one (the case the repair meant:
        // "told again"), no name change and no live-region event is raised. Expected: the root can be told to forget (a member that sets _description back to the plain name when the island has
        // left, called from the same place as the key's reset), or the equal-text guard is not there.
        var root = Repo.Text("src", "Island.App", "Visuals", "IslandRoot.cs");
        var describe = Section(root, "public void Describe(string text, bool alert)", "protected override AutomationPeer");
        var guard = Regex.IsMatch(describe, @"if\s*\(\s*text\s*==\s*_description\s*\)\s*return\s*;");
        var assignments = Regex.Matches(root, @"_description\s*=[^=]").Count; // the field's start value and Describe's own: two
        Assert.True(!guard || assignments > 2, "IslandRoot.Describe ignores equal text and nothing ever puts the words back to the plain name");
        var host = Repo.Text("src", "Island.App", "IslandHost.cs");
        Assert.Contains("IslandRoot Root { get; } = new()", host); // the premise: one root, kept
    }

    // ---- the repairs of round 2 that touch windows, by reading ----------------------------------------------------------------------------------

    [Fact]
    public void Defect_The_Connectors_Tell_A_Failed_Write_As_A_Settings_File_That_Could_Not_Be_Read()
    {
        // code-3-8b (LOW; a refusal of code-1-17's kind, in a second place). OutsideAgentConnector and OutsideCodexConnector answer a failed Write with AgentRefusals.HooksFileUnreadable
        // ("<helper>'s settings file could not be read, so Island did not connect. ... because writing into a file it cannot read could break <helper>. Open the file, fix it or ask
        // <helper> to"): the file was read; it could not be written (read-only, a folder that refuses, an editor holding it), and the advice is to fix a file that is not broken.
        // Expected: a failed write has a refusal of its own (what happened, why, what to do), or at least not the one that says the file could not be read.
        foreach (var file in new[] { "OutsideClaudeSettings.cs", "OutsideCodexSettings.cs" })
        {
            var writeLines = Repo.Text("src", "Island.App", file).Split('\n').Where(l => l.Contains("!Write(", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(writeLines);
            foreach (var line in writeLines) Assert.DoesNotContain("HooksFileUnreadable", line);
        }
    }

    [Fact]
    public void Defect_The_Connectors_Tell_A_Copy_Of_Island_Notify_That_Failed_As_A_Program_That_Is_Not_There()
    {
        // code-3-8c (LOW). HelperFileEditor.CopyNotify answers false when the program is not beside the island AND when a copy fails because a hook that is running holds the old copy
        // (Held_HelperFileEditor_CopyNotify_With_The_Program_Held_By_A_Running_Hook_Answers_False_And_Leaves_The_Old_Program is the second case): both are told as AgentRefusals.NotifyMissing,
        // "Island.Notify, the small program the hook runs, is not next to Island ... Start Island from its own folder": false for the second, and the advice does nothing. An Update
        // pressed while a helper's hook runs (a hook lives a fraction of a second, but only these two cases exist) is the way to meet it. Expected: the two are told apart (the second is "try again").
        foreach (var file in new[] { "OutsideClaudeSettings.cs", "OutsideCodexSettings.cs" })
        {
            var copyLines = Repo.Text("src", "Island.App", file).Split('\n').Where(l => l.Contains("CopyNotify()", StringComparison.Ordinal) && l.Contains("return", StringComparison.Ordinal)).ToList();
            foreach (var line in copyLines) Assert.DoesNotContain("NotifyMissing", line);
        }

        Assert.Contains("NotifyMissing", Repo.Text("src", "Island.App", "OutsideCodexSettings.cs") + Repo.Text("src", "Island.App", "OutsideClaudeSettings.cs")); // the premise: the refusal is used where the copy is asked for
    }

    [Fact]
    public void Held_The_Glow_Window_Region_Is_Handed_To_The_System_Or_Freed_On_Every_Path()
    {
        // GlassWindow.CutOutRoundedRectangle: the region that SetWindowRgn takes belongs to the system (whole = 0 after a success); on a failure of any step both regions are deleted in finally;
        // GetWindowRgn's copy in RegionContains is deleted in finally. No handle is made outside these three.
        var glass = Repo.Text("src", "Island.Glass", "GlassWindow.cs");
        var cut = Section(glass, "public static void CutOutRoundedRectangle", "/// <summary>For the self-test");
        Assert.Contains("if (SetWindowRgn(window, whole, false) != 0) whole = 0;", cut);
        Assert.Contains("if (whole != 0) DeleteObject(whole);", cut);
        Assert.Contains("if (hole != 0) DeleteObject(hole);", cut);
        var contains = Section(glass, "public static bool RegionContains", "public static void Hide");
        Assert.Contains("DeleteObject(region);", contains);
        Assert.Equal(3, Regex.Matches(glass, @"CreateRectRgn\(|CreatePolygonRgn\(").Count - 2); // the two extern declarations are not calls (the polygon replaced the round-rectangle region in WORK-ORDER-13)
    }

    [Fact]
    public void Held_The_Glow_Hole_Is_Cut_Only_When_The_Shape_Moved_And_In_The_Pixels_Of_The_Shapes_Themselves()
    {
        // PlaceShapes is called from FollowCore only when ShapeMoved (the first Follow after a Show lays out again); offset, size and radius are the same scaled numbers the geometries get.
        var light = Repo.Text("src", "Island.Glass", "MovingLight.cs");
        var place = Section(light, "private void PlaceShapes(RimOutline o)", "private void ApplyStrokes");
        Assert.Contains("var offset = new Vector2((float)o.X * _scale", place);
        Assert.Contains("CutOutRoundedRectangle(_glowWindow, left, top, right - left, bottom - top, radius.X + grow, radius.Y + grow)", place); // repaired in WORK-ORDER-12 (design-3-1): the outer outline, each edge rounded on its own; in WORK-ORDER-13 with the two radii of the stretched ellipse
        var follow = Section(light, "private void FollowCore", "private void KeepOrder");
        Assert.Contains("PlaceShapes(o);", follow);
        Assert.Contains("var moved = !_haveOutline || ShapeMoved(o, _outline);", follow);
    }

    [Fact]
    public void Held_The_Settings_Screen_Escape_Leaves_A_Key_From_A_Popup_To_The_Popup()
    {
        var screen = Repo.Text("src", "Island.App", "SettingsScreen.cs");
        var onKey = Section(screen, "private void OnKey(object sender, KeyEventArgs e)", "private void OnFrame");
        Assert.Contains("PresentationSource.FromDependencyObject(source)", onKey);
        Assert.Contains("!ReferenceEquals(presentation.RootVisual, this)) return;", onKey);
        // a key that is not Esc, and an Esc that comes from the screen itself, are handled as before
        Assert.Contains("if (e.Key != Key.Escape) return;", onKey);
        Assert.Contains("if (!View.HandleEscape()) BeginClose();", onKey);
    }
}
