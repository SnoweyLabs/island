using System.Windows.Controls;
using Island.Redteam.Ease.Tests.Harness;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests;

/// <summary>The settings view can be built and laid out off screen for every section of the settings screen and of the first start: the base every other test here stands on.</summary>
public class ViewBuildTests
{
    [Fact]
    public void Every_Section_Of_Settings_And_Of_The_First_Start_Builds_And_Lays_Out_At_1080p()
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
                    Assert.True(Tree.Of<Button>(view).Any(), $"{section} (setup {setup}) has at least one button");
                }
            }
        });
    }
}
