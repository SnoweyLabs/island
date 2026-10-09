using Island.Core;
using Island.SettingsUi;
using Island.Tests.Ui.Harness;

namespace Island.Tests.Ui;

/// <summary>WORK-ORDER-13, Dan's P2: the settings screen follows Windows' text size.</summary>
public class WindowsSettingsTests
{
    [Fact]
    public void The_Type_Of_The_Settings_Screen_Grows_With_Windows_Text_Size_And_Is_Back_At_100_Percent()
    {
        Sta.Run(() =>
        {
            try
            {
                using var fixture = Fixture.Make();
                var view = fixture.NewView();
                view.Section = SettingsSection.General;
                Tree.Layout(view, 1920, 1080);
                var normal = Tree.Of<System.Windows.Controls.TextBlock>(view).Select(t => t.FontSize).Max();

                TextScale.Factor = 1.5;
                var big = fixture.NewView();
                big.Section = SettingsSection.General;
                Tree.Layout(big, 1920, 1080);
                var bigger = Tree.Of<System.Windows.Controls.TextBlock>(big).Select(t => t.FontSize).Max();
                Assert.Equal(normal * 1.5, bigger, 1);
            }
            finally
            {
                TextScale.Factor = 1;
            }
        });
    }

    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(2.25, 2.25)]
    [InlineData(9.0, 2.25)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void The_Text_Size_Is_Held_Between_100_And_225_Percent(double given, double expected) => Assert.Equal(expected, TextScale.Clamp(given));
}

/// <summary>Dan's tutorial (WORK-ORDER-13): the step Try it of the first start and its button.</summary>
public class PracticeStepTests
{
    [Fact]
    public void The_Step_Try_It_Is_In_The_First_Start_And_Not_In_The_Settings_And_Its_Button_Asks_For_The_Practice()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView(setup: true);
            view.Section = SettingsSection.Practice;
            Tree.Layout(view, 1920, 1080);
            Assert.True(view.CountTextContaining("Try it out") > 0);
            Assert.True(view.CountTextContaining("Nothing is opened, added or removed") > 0);
            var asked = 0;
            view.PracticeRequested += (_, _) => asked++;
            Assert.True(view.PressButton("practice:start"));
            Assert.Equal(1, asked);
            Assert.False(view.PressButton("practice:nonexistent"));
        });
    }

    [Fact]
    public void The_Settings_Screen_Has_No_Practice_Step()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = fixture.NewView(setup: false);
            foreach (var section in Enum.GetValues<SettingsSection>().Where(s => s is not (SettingsSection.Welcome or SettingsSection.Practice)))
            {
                view.Section = section;
                Tree.Layout(view, 1920, 1080);
                Assert.False(view.PressButton("practice:start"), section.ToString());
            }
        });
    }
}
