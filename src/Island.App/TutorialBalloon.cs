using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core;
using Island.SettingsUi;

namespace Island.App;

/// <summary>
/// The balloon of the tutorial (Dan's, WORK-ORDER-13): under the island at the top of the screen, it says what to press, how many steps there are, and carries "Skip step" and "Skip tutorial". It never takes
/// the keyboard (the island has it, with the keys being practised); only the mouse reaches its two buttons. It exists only while the practice runs.
/// </summary>
internal sealed class TutorialBalloon : Window
{
    public const double BalloonWidth = 360;
    public const string SkipStepText = "Skip step";
    public const string SkipTutorialText = "Skip tutorial";

    private readonly TutorialRun _run;
    private readonly TextBlock _count;
    private readonly TextBlock _text;
    private readonly TextBlock _hint;
    private readonly Button _skipStep;
    private readonly Button _skipAll;

    public TutorialBalloon(TutorialRun run)
    {
        _run = run;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = BalloonWidth;
        Title = "Island practice";

        _count = Label(11.5, FontWeights.SemiBold, 0.62);
        _text = Label(15.5, FontWeights.SemiBold, 1);
        _hint = Label(12.5, FontWeights.Normal, 0.72);
        _skipStep = Link(SkipStepText, () => _run.SkipStep());
        _skipAll = Link(SkipTutorialText, () => _run.SkipAll());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_skipStep);
        _skipAll.Margin = new Thickness(18, 0, 0, 0);
        buttons.Children.Add(_skipAll);
        var column = new StackPanel();
        column.Children.Add(_count);
        column.Children.Add(_text);
        column.Children.Add(_hint);
        column.Children.Add(buttons);
        Content = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x16, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 14, 18, 14),
            Child = column,
        };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_text, System.Windows.Automation.AutomationLiveSetting.Polite);
        _run.Changed += Update;
        Update();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(handle, Native.GetExStyle(handle) | Native.WsExNoActivate | 0x80 /* WS_EX_TOOLWINDOW */); // the mouse reaches the buttons; the keyboard stays with the island
        };
    }

    /// <summary>The words now shown: for the self-test and the tests.</summary>
    public string TextNow => _text.Text;

    public string CountNow => _count.Text;

    public string HintNow => _hint.Text;

    internal Button SkipStepButton => _skipStep;

    internal Button SkipTutorialButton => _skipAll;

    private string? _nudge;

    /// <summary>A word under the step, when the island has been closed by mistake: how to bring it back. Null puts the step's own hint back.</summary>
    public void Nudge(string? words)
    {
        _nudge = words;
        Update();
    }

    private void Update()
    {
        if (_run.Current is not { } step) return;
        _count.Text = $"Step {_run.Index + 1} of {_run.Count}";
        _text.Text = step.Text;
        _hint.Text = _nudge ?? step.Hint;
    }

    /// <summary>Puts the balloon under the island: centred on the screen's work area, <paramref name="topPx"/> down from the top, in real pixels.</summary>
    public void PlaceUnder(PixelRect work, double scale, double topDip)
    {
        Show();
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var width = (int)Math.Round(BalloonWidth * scale);
        var x = (int)(work.Left + (work.Width - width) / 2);
        var y = (int)(work.Top + topDip * scale);
        Native.SetWindowPos(handle, Native.HwndTopmost, x, y, 0, 0, Native.SwpNoSize | Native.SwpNoActivate);
    }

    private static TextBlock Label(double size, FontWeight weight, double alpha) => new()
    {
        FontFamily = new FontFamily($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}"),
        FontSize = TextScale.Of(size),
        FontWeight = weight,
        Foreground = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), 255, 255, 255)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 3),
    };

    private static Button Link(string text, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = TextScale.Of(12.5), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x9B, 0xC4, 0xFF)), TextDecorations = TextDecorations.Underline },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 2, 0, 2),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
            Template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) }, // no chrome of the system's own: just the words
        };
        button.Click += (_, _) => click();
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        return button;
    }
}
