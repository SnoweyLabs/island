using System.Windows.Controls;
using Island.SettingsUi;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 3, finding design-3-3: the notice line is the last child of a section; nothing brings it into view.</summary>
public class Round3NoticeDefectTests
{
    /// <summary>
    /// FINDING design-3-3 (LOW). Expected: when a refusal or a warning is put in the notice line, the line is in sight (the section scrolls to it, or the line is where the pressed control is). Today nothing in Island.SettingsUi
    /// scrolls: on "On the island" the line starts at 1019 in a 1076 high section, where the room shows 934 (1920 by 1080), 718 (125%) or 574 (150%); on General at 150% it starts at 886 under 574.
    /// </summary>
    [Fact]
    public void Defect_A_Refusal_Put_In_The_Notice_Line_Is_Brought_Into_Sight()
    {
        var scrolls = Src.Visual().Where(f => f.Name.StartsWith("Island.SettingsUi/", StringComparison.Ordinal)).Any(f => f.Text.Contains("BringIntoView") || f.Text.Contains("ScrollToVerticalOffset"));
        var (noticeTop, viewport) = Round2World.WithView(false, (view, stage, session) =>
        {
            view.Section = SettingsSection.OnTheIsland;
            SettingsWorld.Layout(stage);
            var scroll = SettingsWorld.Descendants(view).OfType<ScrollViewer>().First();
            var notice = SettingsWorld.Descendants(view).OfType<TextBlock>().First(t => SettingsWorld.KeyOf(t) == "notice");
            var top = notice.TransformToAncestor(scroll.Content as System.Windows.Media.Visual ?? scroll).TransformBounds(new System.Windows.Rect(0, 0, notice.ActualWidth, notice.ActualHeight)).Top;
            return (top, scroll.ViewportHeight);
        }, 1536, 864);
        Assert.True(scrolls || noticeTop < viewport, $"the notice line starts at {noticeTop:0} in a section that shows {viewport:0} and nothing scrolls it into view");
    }
}
