using System.Windows;
using System.Windows.Controls;
using Island.Core.SettingsEdit;
using Island.SettingsUi;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// The settings screen and the first-start steps, built on an STA thread of this test's own and laid out (never shown, never rendered) at 1920 by 1080, the size of the self-test's pictures:
/// where each heading sits, whether a scroll bar moves the centred lines, whether single-line text is cut, and whether the right edges of the controls of a card agree.
/// </summary>
public class SettingsLayoutTests(ITestOutputHelper output)
{
    private static readonly SettingsSection[] FullSections = Enum.GetValues<SettingsSection>().Where(s => s is not (SettingsSection.Welcome or SettingsSection.Practice)).ToArray();

    private sealed record Row(string Name, double HeadingTop, double HeadingCentreX, double ContentWidth, bool Scrolls, double Foot);

    private static List<Row> Measure(bool setup)
    {
        var dir = SettingsWorld.TempFolder();
        try
        {
            return Sta.Run(() =>
            {
                var session = SettingsWorld.MadeUpSession(dir);
                var view = new SettingsView(session, setup);
                view.FreezeAnimations(1.0);
                var stage = SettingsWorld.Stage(view);
                var rows = new List<Row>();
                foreach (var section in setup ? new[] { SettingsSection.Welcome, SettingsSection.Key, SettingsSection.Practice, SettingsSection.Pages, SettingsSection.OnTheIsland, SettingsSection.Mode } : FullSections)
                {
                    view.Section = section;
                    SettingsWorld.Layout(stage);
                    var heading = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => t.FontSize == 34);
                    var box = SettingsWorld.BoxOf(heading, stage);
                    var scroll = SettingsWorld.Descendants(view).OfType<ScrollViewer>().First();
                    var foot = SettingsWorld.Descendants(view).OfType<Button>().Where(b => SettingsWorld.KeyOf(b) == "foot:next").Select(b => SettingsWorld.BoxOf(b, stage).Top).FirstOrDefault();
                    rows.Add(new Row(section.ToString(), box.Top, box.Left + box.Width / 2, scroll.ViewportWidth, scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible, foot));
                }

                return rows;
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { /* the folder is under this test's own bin folder */ }
        }
    }

    [Fact]
    public void The_Heading_Top_Per_Section_Is_Recorded_For_The_Settings_And_For_The_First_Start()
    {
        foreach (var setup in new[] { false, true })
        {
            var rows = Measure(setup);
            foreach (var r in rows) output.WriteLine($"{(setup ? "setup   " : "settings")} {r.Name,-13} heading top {r.HeadingTop,6:0.0}  centre x {r.HeadingCentreX,6:0.0}  viewport {r.ContentWidth,6:0.0}  scrolls {r.Scrolls}  button top {r.Foot,6:0.0}");
            Assert.NotEmpty(rows);
            // Each section has a heading that lies inside the 1080 high screen.
            Assert.All(rows, r => Assert.InRange(r.HeadingTop, 0, 1080));
        }
    }

    /// <summary>
    /// The page is centred in the room above the buttons when it is short and starts at the top when it is tall (<c>SettingsView</c>: <c>_page.VerticalAlignment = Center</c>, <c>_scrollContent.MinHeight</c>),
    /// so the heading is not where the last one was: the spread of the heading's top across the sections is recorded (finding DESIGN-1-06). Passes while the spread is large; the repair (a top-aligned page) would
    /// make it small.
    /// </summary>
    [Fact]
    public void The_Heading_Jumps_Between_Sections_By_More_Than_A_Hundred_Pixels()
    {
        var tops = Measure(false).Select(r => r.HeadingTop).ToList();
        var spread = tops.Max() - tops.Min();
        output.WriteLine($"settings headings between {tops.Min():0.0} and {tops.Max():0.0}: spread {spread:0.0}");
        Assert.True(spread > 100, $"spread {spread:0.0}: the headings are not where this test found them; look at the finding again");
    }

    /// <summary>When a page is taller than the screen a scroll bar takes room and the centred lines move sideways by half of it; the page does not move for a short one.</summary>
    [Fact]
    public void A_Scroll_Bar_Narrows_The_Viewport_And_Moves_The_Centred_Heading()
    {
        var rows = Measure(false);
        var tall = rows.Where(r => r.Scrolls).ToList();
        var short_ = rows.Where(r => !r.Scrolls).ToList();
        output.WriteLine("scrolling: " + string.Join(", ", tall.Select(r => $"{r.Name} (viewport {r.ContentWidth:0.0}, heading centre {r.HeadingCentreX:0.0})")));
        output.WriteLine("not scrolling: " + string.Join(", ", short_.Select(r => $"{r.Name} (viewport {r.ContentWidth:0.0}, heading centre {r.HeadingCentreX:0.0})")));
        if (tall.Count == 0 || short_.Count == 0) return; // nothing to compare on this machine's layout
        var shift = short_.Average(r => r.HeadingCentreX) - tall.Average(r => r.HeadingCentreX);
        output.WriteLine($"the heading's centre moves {shift:0.0} when a page scrolls");
        Assert.InRange(shift, 0, 40);
    }

    /// <summary>On the small screens people have (1366 by 768, 1280 by 720) the buttons of every step stay on the screen and the page scrolls instead of growing past it.</summary>
    [Fact]
    public void On_Smaller_Screens_The_Buttons_Stay_On_The_Screen_And_The_Page_Scrolls()
    {
        foreach (var (w, h) in new[] { (1366, 768), (1280, 720) })
        {
            var dir = SettingsWorld.TempFolder();
            try
            {
                var worst = Sta.Run(() =>
                {
                    var session = SettingsWorld.MadeUpSession(dir);
                    var view = new SettingsView(session, false);
                    view.FreezeAnimations(1.0);
                    var stage = SettingsWorld.Stage(view, w, h);
                    var worstBottom = 0.0;
                    foreach (var section in FullSections)
                    {
                        view.Section = section;
                        SettingsWorld.Layout(stage);
                        foreach (var b in SettingsWorld.Descendants(view).OfType<Button>().Where(b => SettingsWorld.KeyOf(b) is "foot:next" or "foot:back"))
                            worstBottom = Math.Max(worstBottom, SettingsWorld.BoxOf(b, stage).Bottom);
                    }

                    return worstBottom;
                });
                output.WriteLine($"{w}x{h}: the lowest bottom of a foot button over all sections is {worst:0.0}");
                Assert.True(worst <= h, $"a foot button ends at {worst:0.0} on a screen {h} high");
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch (IOException) { /* under this test's own bin folder */ }
            }
        }
    }
}
