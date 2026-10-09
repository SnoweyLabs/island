using System.Windows;
using System.Windows.Controls;

namespace Island.SettingsUi;

/// <summary>
/// The step "Try it" of the first start (Dan's tutorial, WORK-ORDER-13): what will be tried, a button that starts the practice on the real island, and the way past it. The screen steps back while the
/// person practises and returns on the next step. Nothing is opened, added or removed during the practice.
/// </summary>
internal static class PracticeSection
{
    public const string StartKey = "practice:start";

    public static readonly string[] WhatYouWillTry =
    [
        "bring the island with your key",
        "move between the tiles and choose one",
        "change page",
        "look at what is open now, add it and take a tile off",
        "close the island",
    ];

    public static FrameworkElement Build(ISectionHost host)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        var intro = Look.Label("You will:", Look.BodySize, FontWeights.SemiBold);
        intro.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(intro);
        foreach (var line in WhatYouWillTry)
        {
            var row = Look.Label("• " + line, Look.BodySize, brush: Look.Sub);
            row.Margin = new Thickness(8, 0, 0, 4);
            stack.Children.Add(row);
        }

        var start = Parts.Pill("Start the practice", true, host.RequestPractice, StartKey);
        start.HorizontalAlignment = HorizontalAlignment.Left;
        start.Margin = new Thickness(0, 18, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(start, "Start the practice: this screen steps back and the island comes when you press your key");
        stack.Children.Add(start);

        var skip = Look.Label("Or press Continue to skip it. You can run the setup again from General whenever you like.", Look.HintSize, brush: Look.Sub);
        skip.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(skip);
        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }
}
