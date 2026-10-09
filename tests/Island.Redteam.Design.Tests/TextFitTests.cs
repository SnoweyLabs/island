using System.Windows;
using Island.Core;
using Island.Core.Terminals;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// Does every text the island can put in a fixed slot fit it? The capsule's text block is 130 wide (<see cref="LookConstants.WidthTextBlock"/>): the title at 13.5 semi-bold, the line under it at 11.5;
/// both are cut with an ellipsis at the END, so a state word at the end of a line is the first thing lost. The notice's text is 90 to 230 wide (11 for its second line).
/// Measured with a TextBlock set up as <c>ContentsLayer.Label</c> sets its own, in the island's font list and again in plain Segoe UI (what a computer without Segoe UI Variable draws).
/// </summary>
public class TextFitTests(ITestOutputHelper output)
{
    private const double Slot = LookConstants.WidthTextBlock;

    private static double Subtitle(string s, string family = TextWidth.Primary) => Sta.Run(() => TextWidth.Of(s, LookConstants.SubtitleFontSize, FontWeights.Normal, family));

    private static double Title(string s, string family = TextWidth.Primary) => Sta.Run(() => TextWidth.Of(s, LookConstants.TitleFontSize, FontWeights.SemiBold, family));

    private static readonly string[] FixedSecondLines =
    [
        "open", "closed", "not found", "2 windows", "12 windows", "5 tabs", "terminal",
        PlusRow.HoverSubtitle, // "open · not on the island"
        "let go to remove", "Delete again to remove", CloseButton.AdminLine,
        "press + to add something", "Enter to open", "3 opened", PlusRow.AddSubtitle(40),
    ];

    [Fact]
    public void Fonts_Are_Recorded()
    {
        output.WriteLine("Segoe UI Variable Text installed: " + Sta.Run(TextWidth.HasVariable));
    }

    [Fact]
    public void Every_Fixed_Second_Line_Of_The_Island_Fits_Its_Slot_Except_The_Ones_Listed()
    {
        var over = new List<string>();
        foreach (var l in FixedSecondLines)
        {
            var w = Subtitle(l);
            var wFallback = Subtitle(l, TextWidth.FallbackOnly);
            output.WriteLine($"{w,6:0.0} / plain Segoe UI {wFallback,6:0.0}  of {Slot}  \"{l}\"");
            if (w > Slot) over.Add(l);
        }

        // Known when written: only the empty-page hint, by about one pixel (finding DESIGN-1-05). A second line that grows turns this red.
        Assert.True(over.Count <= 1, "second lines wider than the slot: " + string.Join("; ", over));
    }

    /// <summary>
    /// The hint of a page with nothing on it ("press + to add something", <c>ContentsLayer.ApplySelection</c>) is drawn in the 130-wide slot at 11.5 and is wider by about a pixel: its last letter is replaced by an ellipsis.
    /// The slot is the approved width (<c>LookConstants.WidthTextBlock</c>); the smallest repair is a shorter words or a slot 4 wider, either of which is a proposal.
    /// </summary>
    [Fact]
    public void The_Empty_Page_Hint_Is_About_One_Pixel_Wider_Than_Its_Slot()
    {
        var w = Subtitle("press + to add something");
        output.WriteLine($"{w:0.00} of {Slot}");
        Assert.InRange(w, Slot - 3, Slot + 6); // records "about one pixel over"; moves out of the range when the text or the slot is changed
    }

    [Fact]
    public void Every_Fixed_Title_Of_The_Island_Fits_Its_Slot()
    {
        string[] titles = [PickItems.NothingHere, PickItems.NoTerminal, "Click here to type", "Search Tunes", PlusRow.Label, PlusRow.AddTitle];
        foreach (var t in titles) output.WriteLine($"{Title(t),6:0.0} / plain Segoe UI {Title(t, TextWidth.FallbackOnly),6:0.0}  of {Slot}  \"{t}\"");
        Assert.All(titles, t => Assert.True(Title(t) <= Slot, $"\"{t}\" is {Title(t):0.0} wide in a slot of {Slot}"));
    }

    /// <summary>
    /// The second line of a helper's tile on the Terminals page is the helper's name plus " · working", " · needs you" or " · finished" (WORK-ORDER-11 section 2: four helpers). Cut at the end, what is
    /// lost is the state word. All twelve fit in the font list the island asks for; the margin of the longest is recorded.
    /// </summary>
    [Fact]
    public void The_Twelve_Helper_Lines_Fit_The_Slot_And_The_Tightest_Margin_Is_Recorded()
    {
        var names = new[] { TerminalConstants.ClaudeCode, TerminalConstants.Codex, TerminalConstants.Antigravity, TerminalConstants.Gemini };
        var states = new[] { HelperState.Working, HelperState.NeedsYou, HelperState.Finished };
        double tightest = double.MaxValue;
        string tightestLine = "";
        foreach (var name in names)
            foreach (var state in states)
            {
                var line = name + TerminalConstants.WordsOf(state);
                var w = Subtitle(line);
                var wFallback = Subtitle(line, TextWidth.FallbackOnly);
                output.WriteLine($"{w,6:0.0} / plain Segoe UI {wFallback,6:0.0}  of {Slot}  \"{line}\"");
                Assert.True(w <= Slot, $"\"{line}\" is {w:0.0} wide in a slot of {Slot}");
                if (Slot - w < tightest) { tightest = Slot - w; tightestLine = line; }
            }

        output.WriteLine($"tightest margin {tightest:0.0} on \"{tightestLine}\"");
    }

    /// <summary>
    /// With no project name known a helper tile's second line is the window's title (or the program's name) plus the state words. An ordinary title does not leave room: "Windows PowerShell · needs you" is
    /// about 166 wide in the 130 slot, so the line reads "Windows PowerShell · nee…" and the state is the part cut. (When a project name is known the line is the helper's name and fits, above.)
    /// </summary>
    [Fact]
    public void A_Window_Title_Of_The_Usual_Length_With_A_State_Word_Is_Cut_At_The_State_Word()
    {
        foreach (var title in new[] { "Windows PowerShell", "Command Prompt", "WSL" })
        {
            var line = title + TerminalConstants.NeedsYouWords;
            var w = Subtitle(line);
            output.WriteLine($"{w:0.0} of {Slot}  \"{line}\"");
        }

        Assert.True(Subtitle("Windows PowerShell" + TerminalConstants.NeedsYouWords) > Slot);
    }

    [Fact]
    public void The_Notice_Lines_Fit_The_Notice_Text_Width()
    {
        var lines = new[] { "Agent finished — waiting for you", "Agent needs your answer" };
        foreach (var l in lines)
        {
            var w = Sta.Run(() => TextWidth.Of(l, 11, FontWeights.Normal));
            output.WriteLine($"{w:0.0} of {NoticeLayout.TextMaxWidth}  \"{l}\"");
            Assert.True(w <= NoticeLayout.TextMaxWidth, $"\"{l}\" is {w:0.0} wide in a notice text of at most {NoticeLayout.TextMaxWidth}");
        }
    }
}
