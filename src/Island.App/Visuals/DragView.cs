using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>
/// WORK-ORDER-5 §6: what is drawn while a pick is dragged off the island — the lifted tile that follows the pointer (tilted 8
/// degrees to the left, with a drop shadow) and the drop zone under the capsule (260 by 40, a dashed line, "Drop here to
/// remove"). It covers the whole window, takes no mouse input of its own, and draws only inside the window: letting go outside
/// the window counts as "not the capsule".
/// </summary>
internal sealed class DragView : Canvas
{
    public const double ZoneWidth = 260;
    public const double ZoneHeight = 40;
    public const double ZoneGap = 8;
    public const double LiftTilt = -8;

    private readonly Border _zone;
    private TileView? _clone;
    private Point _grab;
    private Action? _pending;

    public DragView(double width, double height)
    {
        Width = width;
        Height = height;
        IsHitTestVisible = false;

        var dashed = new Rectangle
        {
            RadiusX = ZoneHeight / 2,
            RadiusY = ZoneHeight / 2,
            Stroke = Paint.Brush(Rgb.White, 0.55),
            StrokeThickness = 1.5,
            StrokeDashArray = [3, 3],
            Fill = Paint.Brush(Rgb.FromHex(LookConstants.GlassBaseColor), 0.35),
            Width = ZoneWidth,
            Height = ZoneHeight,
        };
        var words = ContentsLayer.Label(12, FontWeights.Normal, Brushes.White, 12 * 1.45);
        words.Text = "Drop here to remove";
        words.TextTrimming = TextTrimming.None;
        words.HorizontalAlignment = HorizontalAlignment.Center;
        words.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { Width = ZoneWidth, Height = ZoneHeight };
        grid.Children.Add(dashed);
        grid.Children.Add(words);
        _zone = new Border { Child = grid, Opacity = 0, Visibility = Visibility.Collapsed };
        Children.Add(_zone);
    }

    /// <summary>The drop zone is on screen (for the self-test).</summary>
    public bool ZoneShown => _zone.Visibility == Visibility.Visible && _zone.Opacity > 0.01;

    /// <summary>Where the drop zone is, in window coordinates (empty when it is not shown).</summary>
    public Rect ZoneBounds => ZoneShown ? new Rect(GetLeft(_zone), GetTop(_zone), ZoneWidth, ZoneHeight) : Rect.Empty;

    /// <summary>The lifted tile is on screen (for the self-test).</summary>
    public bool TileLifted => _clone is not null;

    /// <summary>Where the lifted tile's top-left corner is, in window coordinates.</summary>
    public Point? TilePosition => _clone is null ? null : new Point(GetLeft(_clone), GetTop(_clone));

    /// <summary>The drop zone fades in under the capsule, its top <see cref="ZoneGap"/> below the capsule's lower edge, centred on it.</summary>
    public void ShowZone(Rect capsule, bool immediate = false)
    {
        SetLeft(_zone, capsule.Left + capsule.Width / 2 - ZoneWidth / 2);
        SetTop(_zone, capsule.Bottom + ZoneGap);
        _zone.Visibility = Visibility.Visible;
        if (immediate)
        {
            _zone.BeginAnimation(OpacityProperty, null);
            _zone.Opacity = 1;
            return;
        }

        _zone.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(ChoiceConstants.DropZoneFadeInMs)));
    }

    public void HideZone()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ChoiceConstants.DropZoneFadeOutMs));
        fade.Completed += (_, _) =>
        {
            if (_zone.Opacity <= 0.01) _zone.Visibility = Visibility.Collapsed;
        };
        _zone.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Makes the lifted tile out of the pick's own item and puts it where the tile was, held by the point under the pointer.</summary>
    public void Lift(Item item, Rgb colour, Point tileTopLeft, Point pointer)
    {
        Clear();
        _grab = new Point(pointer.X - tileTopLeft.X, pointer.Y - tileTopLeft.Y);
        var clone = new TileView(item, 0) { IsHitTestVisible = false };
        clone.FullStrength = true; // the tile in the hand is solid: the drop zone's dashed line does not show through it (Dan's P22, WORK-ORDER-13)
        clone.SetSelected(false, colour);
        var half = LookConstants.ItemSize / 2;
        clone.RenderTransform = new RotateTransform(LiftTilt, half, half);
        clone.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Opacity = 0.45,
            ShadowDepth = 10,
            Direction = 270,
            BlurRadius = 24 / 2 * Units.WpfRadiusPerSigma,
            RenderingBias = RenderingBias.Quality,
        };
        SetLeft(clone, tileTopLeft.X);
        SetTop(clone, tileTopLeft.Y);
        Children.Add(clone);
        _clone = clone;
    }

    /// <summary>The lifted tile follows the pointer, but only inside the window.</summary>
    public void MoveTo(Point pointer)
    {
        if (_clone is null) return;
        SetLeft(_clone, Math.Clamp(pointer.X - _grab.X, -LookConstants.ItemSize, Math.Max(0, Width)));
        SetTop(_clone, Math.Clamp(pointer.Y - _grab.Y, -LookConstants.ItemSize, Math.Max(0, Height)));
    }

    /// <summary>The tile springs back to its place and is then gone; <paramref name="done"/> runs when it has arrived.</summary>
    public void SpringBack(Point home, Action done)
    {
        if (_clone is not { } clone)
        {
            done();
            return;
        }

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = ChoiceConstants.LiftedReturnAmplitude };
        var span = TimeSpan.FromMilliseconds(ChoiceConstants.LiftedReturnMs);
        var toY = new DoubleAnimation(home.Y, span) { EasingFunction = ease };
        _pending = done;
        toY.Completed += (_, _) =>
        {
            if (ReferenceEquals(_clone, clone)) Clear();
        };
        clone.BeginAnimation(LeftProperty, new DoubleAnimation(home.X, span) { EasingFunction = ease });
        clone.BeginAnimation(TopProperty, toY);
    }

    /// <summary>Takes the lifted tile away at once.</summary>
    public void Clear()
    {
        var pending = _pending;
        _pending = null;
        pending?.Invoke(); // a tile waiting for its spring-back to end is freed now (a new lift, or the island leaving, cut it short)
        if (_clone is null) return;
        _clone.BeginAnimation(LeftProperty, null);
        _clone.BeginAnimation(TopProperty, null);
        Children.Remove(_clone);
        _clone = null;
    }
}
