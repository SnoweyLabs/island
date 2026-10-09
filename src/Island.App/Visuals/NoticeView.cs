using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>What the notice says: the project's name (cleaned and cut by Island.Core) and the line under it.</summary>
internal sealed record NoticeContent(string Project, string Line);

/// <summary>
/// The notice "Your agent is done" (WORK-ORDER-7 section 4, picture class <c>note big</c>): a disc of 36 in the Vibe-coding colour with a dark check mark,
/// then two lines: the project's name (12.5 semi-bold) and under it, 11 at 80%, "Agent finished — waiting for you" or "Agent needs your answer". A click on it
/// brings the terminal forward. It takes no keyboard and no focus.
/// </summary>
internal sealed class NoticeView : Canvas
{
    private const double Centre = NoticeLayout.Height / 2;
    private readonly Canvas _host = new();
    private readonly TranslateTransform _top = new();
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _rise = new();
    private NoticeContent? _shown;

    public NoticeView()
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

    /// <summary>The notice was clicked: the terminal comes forward.</summary>
    public event Action? Clicked;

    /// <summary>How wide the text is drawn (90 to 230), which decides the notice's width.</summary>
    public double TextWidth { get; private set; } = NoticeLayout.TextMaxWidth;

    /// <summary>The two lines as drawn (for the self-test; the project there is "island").</summary>
    public (string Project, string Line) Lines => (_shown?.Project ?? string.Empty, _shown?.Line ?? string.Empty);

    public void SetTop(double top) => _top.Y = top;

    public void Show(NoticeContent content, double centreX)
    {
        if (content == _shown && _host.Children.Count > 0) return;
        _shown = content;
        _host.Children.Clear();

        var project = ContentsLayer.Label(12.5, FontWeights.SemiBold, Brushes.White, 17);
        project.Text = content.Project;
        project.TextTrimming = TextTrimming.CharacterEllipsis;
        var line = ContentsLayer.Label(11, FontWeights.Normal, Paint.Brush(Rgb.White, 0.8), 15);
        line.Text = content.Line;
        project.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TextWidth = Math.Clamp(Math.Max(project.DesiredSize.Width, line.DesiredSize.Width), NoticeLayout.TextMinWidth, NoticeLayout.TextMaxWidth);
        project.Width = TextWidth;
        line.Width = TextWidth;

        var width = NoticeLayout.Width(TextWidth);
        var x = centreX - width / 2 + NoticeLayout.PadLeft;

        // A click anywhere on the notice.
        var hit = new Border { Width = width, Height = NoticeLayout.Height, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
        hit.MouseLeftButtonDown += (_, e) =>
        {
            Clicked?.Invoke();
            e.Handled = true;
        };
        SetLeft(hit, centreX - width / 2);
        SetTop(hit, 0);
        _host.Children.Add(hit);

        // The disc in the Vibe-coding colour with a dark check mark.
        var disc = new Grid { Width = NoticeLayout.Disc, Height = NoticeLayout.Disc, IsHitTestVisible = false };
        disc.Children.Add(new Ellipse { Fill = Paint.Brush(Rgb.FromHex(LookConstants.VibeColor)) });
        disc.Children.Add(new Path
        {
            Data = Geometry.Parse("M10.5 18.5 L16 24 L26 12.5"),
            Stroke = Paint.Brush(Rgb.FromHex(NoticeLayout.CheckColour)),
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = NoticeLayout.Disc,
            Height = NoticeLayout.Disc,
        });
        Place(disc, x, Centre - NoticeLayout.Disc / 2);
        x += NoticeLayout.Disc + NoticeLayout.Gap;

        Place(project, x, Centre - 16);
        Place(line, x, Centre + 1);
    }

    public void SetPose(Pose pose)
    {
        _host.Opacity = Math.Clamp(pose.Opacity, 0, 1);
        _scale.ScaleX = _scale.ScaleY = pose.Scale;
        _rise.Y = pose.Rise;
        _host.Effect = pose.BlurRadius > 0.05 ? Units.Blur(pose.BlurRadius) : null;
    }

    /// <summary>The click handler, as the mouse reaches it (for the self-test, which presses no mouse button).</summary>
    internal void RaiseClickForSelfTest() => Clicked?.Invoke();

    private void Place(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        _host.Children.Add(element);
    }
}
