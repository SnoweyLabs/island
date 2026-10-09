using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>
/// A row of tiles that slides sideways inside a fixed width (WORK-ORDER-5 §4): the tiles lie in a row that is moved as a whole
/// by a translate; it is cut off at its left and right ends only, never above or below; on a side where more tiles are
/// hidden it fades out over its last 22% and a small arrow (‹ or ›) is drawn over the faded end. The arrow adds no width, and an
/// area of 24 by 40 around it belongs to the arrow and not to the tile under it. Used by the capsule's row of picks and by the
/// second row ("Open now").
/// </summary>
internal sealed class StripView : Canvas
{
    /// <summary>How far past the strip's ends the clip reaches: half the gap between two tiles plus a little, so a selected tile ring and the small + of a tile in the second row are not cut.</summary>
    public const double Slack = 6;

    /// <summary>How far beyond the row's ends the clip lets the selected tile's glow reach (Dan's P20, WORK-ORDER-13): its blur radius is 14, so 20 leaves no straight edge. Where more tiles are hidden the fade mask still cuts the row.</summary>
    public const double GlowReach = 20;

    private readonly Canvas _clipped;
    private readonly Canvas _slide;
    private readonly TranslateTransform _translate = new();
    private readonly Grid _arrowLeft;
    private readonly Grid _arrowRight;
    private readonly double _stripWidth;
    private bool _hiddenLeft;
    private bool _hiddenRight;

    /// <param name="visibleSlots">How many tiles show at once.</param>
    /// <param name="centreY">Vertical centre of the row of tiles, from the top of this view.</param>
    /// <param name="height">Height of the view.</param>
    public StripView(int visibleSlots, double centreY, double height)
    {
        _stripWidth = StripLayout.StripWidth(visibleSlots);
        Width = _stripWidth + 2 * Slack;
        Height = height;

        _clipped = new Canvas { Width = Width, Height = height };
        _clipped.Clip = new RectangleGeometry(new Rect(-GlowReach, -60, Width + 2 * GlowReach, height + 200));
        _slide = new Canvas { RenderTransform = _translate };
        _clipped.Children.Add(_slide);
        SetLeft(_slide, Slack);
        Children.Add(_clipped);

        _arrowLeft = MakeArrow(right: false);
        _arrowRight = MakeArrow(right: true);
        SetLeft(_arrowLeft, Slack);
        SetTop(_arrowLeft, centreY - ChoiceConstants.ArrowAreaHeight / 2);
        SetLeft(_arrowRight, Slack + _stripWidth - ChoiceConstants.ArrowAreaWidth);
        SetTop(_arrowRight, centreY - ChoiceConstants.ArrowAreaHeight / 2);
        Children.Add(_arrowLeft);
        Children.Add(_arrowRight);
        ApplySides();
    }

    /// <summary>Raised when an arrow is clicked (true: the right one).</summary>
    public event Action<bool>? ArrowClicked;

    /// <summary>Puts a tile's host in the row at its slot: <paramref name="index"/> places it at index times the pitch.</summary>
    public void AddTile(FrameworkElement host, int index, double top)
    {
        SetLeft(host, index * LookConstants.WidthItemPitch);
        SetTop(host, top);
        _slide.Children.Add(host);
    }

    /// <summary>Slides the row: how many tiles it has moved from the first at the left, and on which sides more is hidden.</summary>
    public void SetPosition(double position, bool hiddenLeft, bool hiddenRight)
    {
        _translate.X = -position * LookConstants.WidthItemPitch;
        if (hiddenLeft == _hiddenLeft && hiddenRight == _hiddenRight) return;
        _hiddenLeft = hiddenLeft;
        _hiddenRight = hiddenRight;
        ApplySides();
    }

    /// <summary>Whether the left and the right arrow are drawn (for the self-test).</summary>
    public (bool Left, bool Right) ArrowsShown => (_arrowLeft.Visibility == Visibility.Visible, _arrowRight.Visibility == Visibility.Visible);

    /// <summary>The click handler of an arrow, as the mouse reaches it (for the self-test, which presses no mouse button).</summary>
    public void RaiseArrowForSelfTest(bool right) => ArrowClicked?.Invoke(right);

    private void ApplySides()
    {
        _arrowLeft.Visibility = _hiddenLeft ? Visibility.Visible : Visibility.Collapsed;
        _arrowRight.Visibility = _hiddenRight ? Visibility.Visible : Visibility.Collapsed;
        if (!_hiddenLeft && !_hiddenRight)
        {
            _clipped.OpacityMask = null;
            return;
        }

        // Transparent at the end of the row, opaque again over the last 22% of its width, on each side where more is hidden.
        var total = Width;
        var ramp = ChoiceConstants.StripFadeShare * _stripWidth;
        var edge = Slack / total;
        var inner = (Slack + ramp) / total;
        var clear = Color.FromArgb(0, 0, 0, 0);
        // Absolute, not relative to the bounding box: the row inside is much longer than the part that shows.
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(total, 0) };
        if (_hiddenLeft)
        {
            brush.GradientStops.Add(new GradientStop(clear, 0));
            brush.GradientStops.Add(new GradientStop(clear, edge));
            brush.GradientStops.Add(new GradientStop(Colors.Black, inner));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        }

        if (_hiddenRight)
        {
            brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - inner));
            brush.GradientStops.Add(new GradientStop(clear, 1 - edge));
            brush.GradientStops.Add(new GradientStop(clear, 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Colors.Black, 1));
        }

        brush.Freeze();
        _clipped.OpacityMask = brush;
    }

    private Grid MakeArrow(bool right)
    {
        var area = new Grid { Width = ChoiceConstants.ArrowAreaWidth, Height = ChoiceConstants.ArrowAreaHeight, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var glyph = ContentsLayer.Label(ChoiceConstants.ArrowGlyphSize, FontWeights.Normal, Paint.Brush(Rgb.White, ChoiceConstants.ArrowAlpha), ChoiceConstants.ArrowGlyphSize * 1.45);
        glyph.Text = right ? "›" : "‹";
        glyph.TextAlignment = TextAlignment.Center;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.TextTrimming = TextTrimming.None;
        area.Children.Add(glyph);
        area.MouseLeftButtonDown += (_, e) =>
        {
            ArrowClicked?.Invoke(right);
            e.Handled = true;
        };
        return area;
    }
}
