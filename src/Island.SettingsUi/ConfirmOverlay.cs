using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Island.SettingsUi;

/// <summary>
/// The yes/no question (page removal, restoring a starter list). A dim layer over the whole screen and a small
/// glass card. The keyboard starts on "No", so Enter on its own never throws anything away; Esc is "No".
/// </summary>
internal sealed class ConfirmOverlay : Grid
{
    private readonly StackPanel _card = new() { MaxWidth = 480, Margin = new Thickness(28) };
    private Action? _onYes;
    private Button? _no;

    public ConfirmOverlay()
    {
        Visibility = Visibility.Collapsed;
        Background = Look.Scrim;
        Focusable = false;
        var frame = new Border
        {
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Background = Look.DialogFill,
            BorderBrush = Look.GhostLine,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _card,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.4, ShadowDepth = 12, Direction = 270, BlurRadius = 36 },
        };
        Children.Add(frame);
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Raised when the question is closed either way, so the screen can enable itself again.</summary>
    public event Action? Closed;

    public void Ask(string text, string yesText, Action onYes, string noText = "No, keep it")
    {
        _onYes = onYes;
        _card.Children.Clear();
        var question = Look.Label(text, Look.SubSize);
        // A long question (the lines a connection would add) scrolls: every line is there, none is cut (WORK-ORDER-11 section 3).
        var scroll = new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Content = question, Margin = new Thickness(0, 0, 0, 20) };
        _card.Children.Add(scroll);

        var yes = Parts.Pill(yesText, true, () => Close(confirmed: true), "dialog:yes");
        _no = Parts.Pill(noText, false, () => Close(confirmed: false), "dialog:no");
        yes.Margin = new Thickness(8, 0, 0, 0);
        // The keyboard goes to "No": a screen reader says that button, and with it the question it answers.
        System.Windows.Automation.AutomationProperties.SetHelpText(_no, text);
        System.Windows.Automation.AutomationProperties.SetHelpText(yes, text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_no);
        buttons.Children.Add(yes);
        _card.Children.Add(buttons);

        Visibility = Visibility.Visible;
        _no.Loaded += (_, _) => _no.Focus();
        _no.Focus();
    }

    /// <summary>Closes the question as "No". True when it was open.</summary>
    public bool Cancel()
    {
        if (!IsOpen) return false;
        Close(confirmed: false);
        return true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsOpen) return;
        if (e.Key == Key.Escape)
        {
            Close(confirmed: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            // Keep the keyboard on the two buttons while the question is open.
            var forward = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
            MoveFocus(new TraversalRequest(forward ? FocusNavigationDirection.Next : FocusNavigationDirection.Previous));
            e.Handled = true;
        }
    }

    private void Close(bool confirmed)
    {
        var action = confirmed ? _onYes : null;
        Visibility = Visibility.Collapsed;
        _onYes = null;
        _card.Children.Clear();
        Closed?.Invoke();
        action?.Invoke();
    }
}
