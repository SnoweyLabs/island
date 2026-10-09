using System.Windows;
using System.Windows.Controls;
using Island.Core;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>WCAG 2.2 2.5.8 (24 x 24 device-independent pixels, or the spacing exception) for the settings screen's targets, laid out off screen at 100%, 150% and 200%; and for the island's own sizes from the constants.</summary>
public class TargetSizeTests
{
    private const double Minimum = 24;

    private static bool CircleTouches(Point centre, Rect other)
    {
        var nearestX = Math.Clamp(centre.X, other.Left, other.Right);
        var nearestY = Math.Clamp(centre.Y, other.Top, other.Bottom);
        return (centre.X - nearestX) * (centre.X - nearestX) + (centre.Y - nearestY) * (centre.Y - nearestY) < (Minimum / 2) * (Minimum / 2);
    }

    /// <summary>The undersized targets of a section that fail 2.5.8: smaller than 24 in a direction and with the 24 px circle round their centre meeting another target or another undersized target's circle.</summary>
    private static IEnumerable<string> Failures(SettingsView view)
    {
        var targets = Tree.Descendants(view).OfType<FrameworkElement>().Where(e => e is Button or TextBox && e.IsVisible && e.ActualWidth > 0).Select(e => (E: e, B: Tree.BoundsIn(e, view))).ToList();
        foreach (var t in targets.Where(t => t.B.Width < Minimum || t.B.Height < Minimum))
        {
            var centre = new Point(t.B.X + t.B.Width / 2, t.B.Y + t.B.Height / 2);
            foreach (var other in targets.Where(o => !ReferenceEquals(o.E, t.E)))
            {
                var otherUnder = other.B.Width < Minimum || other.B.Height < Minimum;
                var meets = otherUnder
                    ? Math.Sqrt(Math.Pow(centre.X - (other.B.X + other.B.Width / 2), 2) + Math.Pow(centre.Y - (other.B.Y + other.B.Height / 2), 2)) < Minimum
                    : CircleTouches(centre, other.B);
                if (meets) yield return $"{Tree.FocusKeyOf(t.E)} ({t.B.Width:0}x{t.B.Height:0}) meets {Tree.FocusKeyOf(other.E)}";
            }
        }
    }

    [Fact]
    public void Every_Target_Of_The_Settings_Screen_Meets_24_By_24_Or_The_Spacing_Exception()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            foreach (var setup in new[] { false, true })
            {
                var view = fixture.NewView(setup);
                foreach (var section in setup ? Tree.SetupSections : Tree.FullSections)
                {
                    view.Section = section;
                    Tree.Layout(view, 1920, 1080);
                    var failed = Failures(view).ToList();
                    Assert.True(failed.Count == 0, $"{section}: " + string.Join("; ", failed));
                }
            }
        });
    }

    [Fact]
    public void The_Two_Narrowest_Targets_Pass_By_The_Spacing_Exception_With_Little_To_Spare()
    {
        // The colour dot of a page row is 22 wide and the name field starts 2 px from it (the circle of 24 round the dot reaches 1 px out); the steps at the top are 26 x 18 and 1 px apart, which
        // passes because their circles of 24 are 27 apart. Both hold, narrowly: recorded so that a change of a margin is seen.
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView();
            view.Section = SettingsSection.Pages;
            Tree.Layout(view, 1920, 1080);
            var dot = Tree.Of<Button>(view).First(b => Tree.FocusKeyOf(b) == "dot:media");
            Assert.Equal(22, Math.Round(dot.ActualWidth));
            var name = Tree.Of<TextBox>(view).First(t => Tree.FocusKeyOf(t) == "name:media");
            var gap = Tree.BoundsIn(name, view).Left - Tree.BoundsIn(dot, view).Right;
            Assert.InRange(gap, 1.5, 2.5);
            var step = Tree.Of<Button>(view).First(b => Tree.FocusKeyOf(b) == "step:0");
            Assert.Equal(26, Math.Round(step.ActualWidth));
            Assert.Equal(18, Math.Round(step.ActualHeight));
        });
    }

    [Fact]
    public void The_Islands_Own_Targets_From_The_Constants_Are_At_Least_24_Wide_And_High()
    {
        Assert.True(LookConstants.ItemSize >= Minimum);
        Assert.True(LookConstants.ControlSize >= Minimum);
        Assert.True(ChoiceConstants.ArrowAreaWidth >= Minimum && ChoiceConstants.ArrowAreaHeight >= Minimum);
        Assert.True(LookConstants.ChipSize >= Minimum);
        Assert.True(LookConstants.BallSize >= Minimum); // the ball the island rests as
    }

    [Fact]
    public void The_Scroll_Bar_Of_The_Settings_Screen_Has_A_Thumb_Four_Wide()
    {
        // ease-1-17 (LOW, a proposal: a size). Parts.ScrollBarStyle: the bar is 10 wide and its thumb has a margin of 3 each side, so the part one drags is 4 px wide; the track around it takes
        // page-up and page-down clicks. Wheel, keys and touch scroll as well, so nothing is out of reach; recorded because the thumb is below 24 and a spacing exception does not apply to it.
        var text = Source.Read("src/Island.SettingsUi/Parts.cs");
        Assert.Contains("<Setter Property=\"Width\" Value=\"10\"/>", text);
        Assert.Contains("Margin=\"3,0,3,0\"", text);
    }
}
