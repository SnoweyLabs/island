using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>What the pill shows: the source's tile (its icon, or its two letters), the title, and whether the buttons work and which glyph the middle one has.</summary>
internal sealed record PillContent(string Title, string Mark, IconImage? Icon, bool IsPaused, bool CanControl);

/// <summary>
/// The small pill's contents (WORK-ORDER-7 section 2, picture class <c>pill</c>): the source's tile at 30, the title (12.5 semi-bold, at most 150
/// wide, cut with an ellipsis) and four buttons of 26: previous, play or pause, next, search. Laid out centred on the window, at the pill's
/// width for this title; the glass and the ring are the island view's own layers.
/// </summary>
internal sealed class PillView : Canvas
{
    private const double Centre = PillLayout.Height / 2;
    private readonly Canvas _host = new();
    private readonly TranslateTransform _top = new();
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _rise = new();
    private readonly List<Grid> _buttons = [];
    private TextBlock? _title;
    private Grid? _middle;
    private PillContent? _shown;

    /// <summary>The Media colour's hue in degrees (#FF4055), for the tile of letters.</summary>
    private const double MediaHue = 353;

    public PillView()
    {
        IsHitTestVisible = true;
        _host.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_rise);
        _host.RenderTransform = group;
        RenderTransform = _top;
        Children.Add(_host);
        Visibility = Visibility.Collapsed;
    }

    public event Action? PreviousClicked;

    public event Action? PlayPauseClicked;

    public event Action? NextClicked;

    public event Action? SearchClicked;

    public event Action? TitleClicked;

    /// <summary>How wide the title is drawn (24 to 150), which decides the pill's width.</summary>
    public double TitleWidth { get; private set; } = PillLayout.TitleMaxWidth;

    /// <summary>The title as drawn (for the self-test).</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>The glyph of the middle button: "pause" while playing, "play" while paused (for the self-test).</summary>
    public string MiddleGlyph => _shown is null ? string.Empty : _shown.IsPaused ? "play" : "pause";

    /// <summary>The number of buttons (for the self-test): four.</summary>
    public int ButtonCount => _buttons.Count;

    /// <summary>The two letters or the picture of the source tile, as it was given (for the self-test).</summary>
    public PillContent? Shown => _shown;

    public void SetTop(double top) => _top.Y = top;

    /// <summary>Lays the pill out for this content, centred on <paramref name="centreX"/>. Nothing happens when it is what is shown already.</summary>
    public void Show(PillContent content, double centreX)
    {
        if (content == _shown && _host.Children.Count > 0) return;
        _shown = content;
        _host.Children.Clear();
        _buttons.Clear();

        _title = ContentsLayer.Label(12.5, FontWeights.SemiBold, Brushes.White, 16);
        _title.Text = content.Title;
        _title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TitleWidth = Math.Clamp(_title.DesiredSize.Width, PillLayout.TitleMinWidth, PillLayout.TitleMaxWidth);
        _title.Width = TitleWidth;
        _title.MaxWidth = PillLayout.TitleMaxWidth;

        var width = PillLayout.Width(TitleWidth);
        var x = centreX - width / 2 + PillLayout.PadLeft;

        // The source's tile: the program's or the site's icon filling a circle, or its two letters, at 30 (the round tile of the island, smaller).
        var tile = new TileView(new Item(content.Title, string.Empty, content.Mark, MediaHue, Icon: content.Icon), 0)
        {
            IsHitTestVisible = false,
        };
        tile.SetColour(Rgb.FromHex(LookConstants.MediaColor));
        tile.SetSelected(false, Rgb.FromHex(LookConstants.MediaColor));
        var viewbox = new Viewbox { Width = PillLayout.Tile, Height = PillLayout.Tile, Child = tile, IsHitTestVisible = false };
        Place(viewbox, x, Centre - PillLayout.Tile / 2);
        x += PillLayout.Tile + PillLayout.Gap;

        // The title is the click that brings the player or the tab forward.
        var hit = new Border { Width = TitleWidth, Height = PillLayout.Height - 8, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        hit.MouseLeftButtonDown += (_, e) =>
        {
            TitleClicked?.Invoke();
            e.Handled = true;
        };
        Place(hit, x, 4);
        Place(_title, x, Centre - 8);
        x += TitleWidth + PillLayout.Gap;

        var glyphs = new[] { "prev", content.IsPaused ? "play" : "pause", "next", "search" };
        Action?[] actions = [() => PreviousClicked?.Invoke(), () => PlayPauseClicked?.Invoke(), () => NextClicked?.Invoke(), () => SearchClicked?.Invoke()];
        for (var i = 0; i < glyphs.Length; i++)
        {
            var action = actions[i];
            var button = new Grid { Width = PillLayout.Button, Height = PillLayout.Button, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            button.Children.Add(Icons.Create(glyphs[i], LookConstants.ControlGlyphSize, Brushes.White));
            button.MouseLeftButtonDown += (_, e) =>
            {
                action?.Invoke();
                e.Handled = true;
            };
            // The media buttons do nothing while the source is gone (between tracks); search does not need it.
            if (!content.CanControl && i < 3) button.Opacity = LookConstants.DimmedControlOpacity;
            Place(button, x, Centre - PillLayout.Button / 2);
            _buttons.Add(button);
            if (i == 1) _middle = button;
            x += PillLayout.Button + PillLayout.Gap;
        }
    }

    /// <summary>The entrance and exit of the contents, as the capsule's contents have it: opacity, a rise, a scale and a blur.</summary>
    public void SetPose(Pose pose)
    {
        _host.Opacity = Math.Clamp(pose.Opacity, 0, 1);
        _scale.ScaleX = _scale.ScaleY = pose.Scale;
        _rise.Y = pose.Rise;
        _host.Effect = pose.BlurRadius > 0.05 ? Units.Blur(pose.BlurRadius) : null;
    }

    /// <summary>The click handlers of the buttons, as the mouse reaches them (for the self-test, which presses no mouse button).</summary>
    internal void RaiseForSelfTest(int button)
    {
        switch (button)
        {
            case 0: PreviousClicked?.Invoke(); break;
            case 1: PlayPauseClicked?.Invoke(); break;
            case 2: NextClicked?.Invoke(); break;
            case 3: SearchClicked?.Invoke(); break;
            default: TitleClicked?.Invoke(); break;
        }
    }

    private void Place(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        _host.Children.Add(element);
    }
}
