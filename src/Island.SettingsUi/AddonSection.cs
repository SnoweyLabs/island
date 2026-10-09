using System.Windows;
using System.Windows.Controls;

namespace Island.SettingsUi;

/// <summary>
/// The step "Chrome" of the first start (Dan, 2026-10-09, version 1.0.1): the optional add-on, loaded by hand in three steps, with the same card as in General
/// ("Open the add-on folder" and whether a browser is connected). Nothing is done unless the person presses the button; Continue skips the step.
/// </summary>
internal static class AddonSection
{
    public static readonly string[] HowToLoad =
    [
        "Press \u201cOpen the add-on folder\u201d below.",
        "In Chrome, go to chrome://extensions and switch on Developer mode (top right).",
        "Click \u201cLoad unpacked\u201d and choose the folder that opened.",
    ];

    public const string AfterLoading = "When the add-on talks to Island, the card says Connected. You can do this later from General.";

    public static FrameworkElement Build(ISectionHost host)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        for (var i = 0; i < HowToLoad.Length; i++)
        {
            var row = Look.Label($"{i + 1}. {HowToLoad[i]}", Look.BodySize, brush: Look.Sub);
            row.TextWrapping = TextWrapping.Wrap;
            row.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(row);
        }

        var card = GeneralSection.AddonCard(host, host.Session);
        card.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(card);

        var after = Look.Label(AfterLoading, Look.HintSize, brush: Look.Sub);
        after.TextWrapping = TextWrapping.Wrap;
        after.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(after);
        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }
}
