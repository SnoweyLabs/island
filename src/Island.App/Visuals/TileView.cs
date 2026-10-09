using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Island.Core;
using Island.Core.Terminals;

namespace Island.App.Visuals;

/// <summary>
/// One round item: a gradient circle with a two-letter label, or the program's own icon filling the circle (choice 1B,
/// WORK-ORDER-5 §1). Unselected it sits at 74%; selected it is fully opaque with a 2 px white ring and a glow in the
/// category colour. A closed pick is the same tile without colour, a little darker, at 85%, with no glow (choice 2B, §2),
/// and a pick with several windows has one small white dot per window under it (choice 3B, §3).
/// </summary>
internal sealed class TileView : Canvas
{
    public static readonly DependencyProperty ClosedProgressProperty = DependencyProperty.Register(
        nameof(ClosedProgress), typeof(double), typeof(TileView), new PropertyMetadata(0.0, (d, _) => ((TileView)d).ApplyLook()));

    private readonly Grid _inner;
    private readonly Grid _openFace;
    private readonly Grid? _closedFace;
    private readonly Ellipse _glow;
    private readonly Ellipse _ring;
    private readonly Path _highlight;
    private readonly Item _item;

    /// <summary>The thing this tile shows (for the words a screen reader is told).</summary>
    internal Item Item => _item;
    private readonly int _index;
    private readonly List<Ellipse> _dotGlows = [];
    private bool _selected;

    /// <param name="fadeFromClosed">How this pick looked before, when the row is drawn again after it opened or closed: the tile then cross-fades (WORK-ORDER-5 §2). Null: no fade.</param>
    public TileView(Item item, int index, bool? fadeFromClosed = null, bool addBadge = false)
    {
        _item = item;
        _index = index;
        Width = LookConstants.ItemSize;
        Height = LookConstants.ItemSize;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;

        var d = LookConstants.ItemSize;
        _glow = new Ellipse
        {
            Width = d,
            Height = d,
            Effect = Units.Blur(Units.RadiusForBoxShadowBlur(LookConstants.SelectedGlowRadius)),
            IsHitTestVisible = false,
        };
        _ring = new Ellipse
        {
            Width = d + 2 * LookConstants.SelectedRingWidth,
            Height = d + 2 * LookConstants.SelectedRingWidth,
            Stroke = Paint.Brush(Rgb.White, LookConstants.SelectedRingAlpha),
            StrokeThickness = LookConstants.SelectedRingWidth,
            IsHitTestVisible = false,
        };

        _inner = new Grid { Width = d, Height = d };
        _openFace = new Grid { Width = d, Height = d };
        var fades = fadeFromClosed is { } was && was != item.IsClosed && !item.IsPlus;
        // A closed pick has only its grey face and an open one only its own, unless it is cross-fading from one to the other.
        var hasOpen = !item.IsClosed || fades;
        if (item.IsClosed || fades) _closedFace = new Grid { Width = d, Height = d };
        if (hasOpen) _inner.Children.Add(_openFace);
        if (_closedFace is not null) _inner.Children.Add(_closedFace);
        if (hasOpen) BuildFace(_openFace, item, d, grey: false);
        if (_closedFace is not null) BuildFace(_closedFace, item, d, grey: true);
        _highlight = new Path { Data = ContentsLayer.TopCrescent(d), IsHitTestVisible = false };
        _inner.Children.Add(_highlight);

        var target = item.IsClosed ? 1.0 : 0.0;
        ClosedProgress = target;
        if (fades)
        {
            // Starts from how it looked before and moves to how it looks now (200 ms); the end value is then held by the property itself.
            var animation = new DoubleAnimation(fadeFromClosed!.Value ? 1.0 : 0.0, target, TimeSpan.FromMilliseconds(ChoiceConstants.ClosedFadeMs)) { FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => ClosedProgress = target;
            BeginAnimation(ClosedProgressProperty, animation);
        }

        // A canvas does not clip children that overhang its edge, which the ring and glow do.
        SetLeft(_ring, -LookConstants.SelectedRingWidth);
        SetTop(_ring, -LookConstants.SelectedRingWidth);
        Children.Add(_glow);
        Children.Add(_ring);
        Children.Add(_inner);
        AddDots(item);
        AddStateRing(item);
        if (addBadge) AddPlusBadge(index);
    }

    private bool _fullStrength;

    /// <summary>True for the clone a drag holds: drawn at its full strength whether or not it was selected, without the white ring.</summary>
    public bool FullStrength
    {
        get => _fullStrength;
        set
        {
            _fullStrength = value;
            ApplyLook();
        }
    }

    public event Action<int>? Clicked;

    /// <summary>Raised when the small + at the tile's upper right is clicked (the second row's "add").</summary>
    public event Action<int>? AddClicked;

    /// <summary>0 while the tile looks open, 1 when it looks closed; in between during the 200 ms cross-fade.</summary>
    public double ClosedProgress
    {
        get => (double)GetValue(ClosedProgressProperty);
        private set => SetValue(ClosedProgressProperty, value);
    }

    /// <summary>The icon picture the tile draws as it looks now (grey when closed), before it is laid on the glass; null for a tile of letters (for the self-test).</summary>
    public BitmapSource? IconPicture => (_item.Icon is null || _item.IsPlus) ? null : ToBitmap(RoundIcons.Of(_item.Icon).Drawn, _item.IsClosed);

    /// <summary>
    /// Where pointer positions are measured from, so that a drag can be followed outside the capsule: the island's window-sized
    /// overlay. Set by the view that owns the tiles.
    /// </summary>
    public UIElement? PointerReference { get; set; }

    /// <summary>
    /// When true the tile reports the raw pointer (press, move, release, loss of the mouse) to whoever decides what it means: a click
    /// acting on the release, or a drag (WORK-ORDER-5 §6). When false the tile only reports <see cref="Clicked"/>, on the release.
    /// </summary>
    public bool RawPointer { get; set; }

    /// <summary>The button went down on the tile, with the pointer's place in the window.</summary>
    public event Action<int, Point>? Pressed;

    /// <summary>The pointer moved with the button down (also outside the window: the tile holds the mouse capture).</summary>
    public event Action<int, Point>? Moved;

    /// <summary>The button went up, with the pointer's place in the window.</summary>
    public event Action<int, Point>? Released;

    /// <summary>The window lost the mouse without the button coming up.</summary>
    public event Action<int>? MouseLost;

    private bool _pressed;

    private Point Position(MouseEventArgs e) => e.GetPosition(PointerReference ?? this);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = true;
        CaptureMouse();
        if (RawPointer) Pressed?.Invoke(_index, Position(e));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressed && RawPointer) Moved?.Invoke(_index, Position(e));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_pressed) return;
        _pressed = false;
        var at = e.GetPosition(this);
        var inside = at.X >= 0 && at.Y >= 0 && at.X <= ActualWidth && at.Y <= ActualHeight; // the mouse is captured, so IsMouseOver would always say yes
        ReleaseMouseCapture();
        if (RawPointer) Released?.Invoke(_index, Position(e));
        else if (inside) Clicked?.Invoke(_index); // acts on the release, and only if it is still over the tile
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_pressed) return;
        _pressed = false;
        if (RawPointer) MouseLost?.Invoke(_index);
    }

    /// <summary>A tile that is lifted leaves its place empty (it is drawn again where the pointer is).</summary>
    public void SetLifted(bool lifted) => Opacity = lifted ? 0 : 1;

    public void SetSelected(bool selected, Rgb colour)
    {
        _selected = selected;
        // A tile with a state ring has no room for the white ring of the selected tile between two neighbours: its glow and its full brightness say it is selected.
        _ring.Visibility = TerminalRing.WhiteRingShown(_item.Ring, selected) ? Visibility.Visible : Visibility.Collapsed;
        _highlight.Fill = Paint.Brush(Rgb.White, selected ? 0.6 : 0.55);
        SetColour(colour);
        ApplyLook();
    }

    private Rgb? _colour;

    public void SetColour(Rgb colour)
    {
        if (_colour == colour) return;
        _colour = colour;
        _glow.Fill = Paint.Brush(colour, 1);
        foreach (var glow in _dotGlows) glow.Fill = Paint.Brush(colour, 1);
    }

    /// <summary>
    /// Opacity of the whole tile, the glow and the two faces from the selection and the open/closed progress: an open
    /// tile is at 74%, or 100% and glowing when selected; a closed one is at 85% whether selected or not, with no glow.
    /// </summary>
    private void ApplyLook()
    {
        if (_inner is null || _openFace is null) return; // the property is set once while the tile is being built
        var p = Math.Clamp(ClosedProgress, 0, 1);
        var open = _selected || _fullStrength ? 1 : LookConstants.ItemUnselectedOpacity;
        _inner.Opacity = open + (ChoiceConstants.ClosedOpacity - open) * p;
        _openFace.Opacity = 1 - p;
        if (_closedFace is not null) _closedFace.Opacity = p;
        _glow.Opacity = 1 - p;
        _glow.Visibility = _selected && p < 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- The face of the tile ----------------------------------------------

    private static void BuildFace(Grid face, Item item, double d, bool grey)
    {
        if (item.IsPlus)
        {
            // The + tile: an empty circle with a dashed edge, so it never looks like a pick.
            face.Children.Add(new Ellipse
            {
                Fill = Paint.Brush(Rgb.White, 0.08),
                Stroke = Paint.Brush(Rgb.White, 0.6),
                StrokeThickness = 1.4,
                StrokeDashArray = [2.2, 2.2],
                Margin = new Thickness(0.7),
                IsHitTestVisible = false,
            });
            // Drawn as a path, centred on the circle (Dan's P19, WORK-ORDER-13): a bold "+" of text sat 3.5 dp below the middle, where the font's baseline puts it.
            face.Children.Add(new Path
            {
                Data = Geometry.Parse("M7 0V14M0 7H14"),
                Stroke = Brushes.White,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            });
            return;
        }

        if (item.Disc is { } helper)
        {
            // A helper's tile (WORK-ORDER-11 section 2): its own colour, two letters, no logo of anyone.
            face.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(helper.R, helper.G, helper.B)), IsHitTestVisible = false });
            AddLetters(face, item.Mark, TileLabel.For(new Rgb(helper.R, helper.G, helper.B), 1), d);
            return;
        }

        if (item.Icon is null)
        {
            face.Children.Add(new Ellipse { Fill = Gradient(item.Hue, grey), IsHitTestVisible = false });
            AddLetters(face, item.Mark, grey ? ClosedLetters : TileLabel.ForHue(item.Hue), d);
            return;
        }

        // WORK-ORDER-10 §1: a full disc in the icon's own main colour, the icon smaller on it (a flat shape turned white). Nothing of the glass shows through.
        var look = RoundIcons.Of(item.Icon);
        if (look.Plan.Kind == RoundIconKind.Letters)
        {
            // No opaque pixel to take a colour from: the two-letter tile, which is round already.
            face.Children.Add(new Ellipse { Fill = Gradient(item.Hue, grey), IsHitTestVisible = false });
            AddLetters(face, item.Mark, grey ? ClosedLetters : TileLabel.ForHue(item.Hue), d);
            return;
        }

        var disc = grey ? look.ClosedDisc : look.Plan.Disc;
        face.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(disc.R, disc.G, disc.B)), IsHitTestVisible = false });
        var box = RoundIconLayout.PlaceOnPixels(look.Drawn.Width, look.Drawn.Height, d, PixelsPerDip);
        var image = new Image
        {
            Source = ToBitmap(look.Drawn, grey),
            Width = box.Width,
            Height = box.Height,
            Margin = new Thickness(box.X, box.Y, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        face.Children.Add(image);
    }

    /// <summary>The face of a tile alone, with none of the layers laid over it on the island (opacity, crescent, ring, glow, dots, bars): what the self-test reads.</summary>
    internal static Grid FaceOf(Item item, bool grey)
    {
        var d = LookConstants.ItemSize;
        var face = new Grid { Width = d, Height = d };
        BuildFace(face, item, d, grey);
        return face;
    }

    /// <summary>Device pixels per device-independent pixel on the screen the island is on; decides whether an icon is drawn at its own size or fitted into its box on the disc.</summary>
    public static double PixelsPerDip { get; set; } = 1;

    /// <summary>A closed tile's letters are white on its grey, as chosen (choice 2B): they are not made brighter.</summary>
    private static readonly LabelPlan ClosedLetters = new(Rgb.White, new Rgb(0, 0, 0), 0);

    /// <summary>
    /// The two letters (or the + of a new tile), in the colour that reads on the face (Dan's P4, WORK-ORDER-13), over a soft disc of the opposite colour where the face alone is not enough. The disc is part of
    /// the face, so it dims with it.
    /// </summary>
    private static void AddLetters(Grid face, string text, LabelPlan plan, double d)
    {
        if (plan.ScrimAlpha > 0)
        {
            var size = d * 0.78;
            face.Children.Add(new Ellipse
            {
                Width = size,
                Height = size,
                Fill = SoftDisc(plan),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            });
        }

        var label = Letters(text, LookConstants.ItemLabelFontSize);
        label.Foreground = Paint.Brush(plan.Text, 1);
        face.Children.Add(label);
    }

    /// <summary>The disc is the same strength as far as the letters reach (two thirds of its radius) and fades out to its edge.</summary>
    private static RadialGradientBrush SoftDisc(LabelPlan plan)
    {
        var brush = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5, GradientOrigin = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Paint.Of(plan.Scrim, plan.ScrimAlpha), 0));
        brush.GradientStops.Add(new GradientStop(Paint.Of(plan.Scrim, plan.ScrimAlpha), 0.7));
        brush.GradientStops.Add(new GradientStop(Paint.Of(plan.Scrim, 0), 1));
        brush.Freeze();
        return brush;
    }

    private static TextBlock Letters(string text, double size)
    {
        var label = ContentsLayer.Label(size, FontWeights.Bold, Brushes.White, size * 1.45);
        label.Text = text;
        label.TextAlignment = TextAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        return label;
    }

    // ---- The small + of a tile in the second row -------------------------------------------

    /// <summary>A white disc 16 across with a dark + in it, reaching 5 outside the tile at its upper right (picture: class badge-plus).</summary>
    private void AddPlusBadge(int index)
    {
        var size = ChoiceConstants.BadgeSize;
        var disc = new Grid { Width = size, Height = size, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        disc.Children.Add(new Ellipse { Fill = Brushes.White, IsHitTestVisible = false });
        var plus = new Path
        {
            Data = Geometry.Parse("M3.2 0V6.4M0 3.2H6.4"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 6.4,
            Height = 6.4,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        disc.Children.Add(plus);
        disc.MouseLeftButtonDown += (_, e) =>
        {
            AddClicked?.Invoke(index);
            e.Handled = true;
        };
        SetLeft(disc, LookConstants.ItemSize - size + ChoiceConstants.BadgeReach);
        SetTop(disc, -ChoiceConstants.BadgeReach);
        Children.Add(disc);
    }

    // ---- The bars of the tile that is playing (WORK-ORDER-5 §7) ---------------------------------

    private Grid? _equalizer;
    private Rectangle[]? _bars;

    /// <summary>
    /// Covers the tile with a disc of black at 45% and three upright white bars of the given heights (3 wide, round ends, 2.5 apart,
    /// centred); null takes it away. The heights depend only on the island's clock and never on the sound.
    /// </summary>
    public void SetEqualizer(IReadOnlyList<double>? heights)
    {
        if (heights is null)
        {
            if (_equalizer is not null) _equalizer.Visibility = Visibility.Collapsed;
            return;
        }

        if (_equalizer is null)
        {
            var d = LookConstants.ItemSize;
            _equalizer = new Grid { Width = d, Height = d, IsHitTestVisible = false };
            _equalizer.Children.Add(new Ellipse { Fill = Paint.Brush(Rgb.FromHex("#000000"), Equalizer.DiscAlpha) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _bars = new Rectangle[Equalizer.Delays.Count];
            for (var i = 0; i < _bars.Length; i++)
            {
                _bars[i] = new Rectangle { Width = Equalizer.BarWidth, RadiusX = Equalizer.BarWidth / 2, RadiusY = Equalizer.BarWidth / 2, Fill = Brushes.White, Margin = new Thickness(i == 0 ? 0 : Equalizer.BarGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(_bars[i]);
            }

            _equalizer.Children.Add(row);
            Children.Add(_equalizer); // above the tile and not inside it: the disc and the bars keep their own strength
        }

        _equalizer.Visibility = Visibility.Visible;
        for (var i = 0; i < _bars!.Length && i < heights.Count; i++) _bars[i].Height = heights[i];
    }

    /// <summary>The heights of the three bars as drawn now; null when the tile is not playing (for the self-test).</summary>
    public IReadOnlyList<double>? EqualizerHeights => _equalizer is { Visibility: Visibility.Visible } && _bars is not null ? [.. _bars.Select(b => b.Height)] : null;

    // ---- The windows of a pick: one dot each --------------------------------

    /// <summary>
    /// Under the tile, centred: one small white dot per window (two to five of them), each 4 across and 3 apart with its centre
    /// 7 below the tile's lower edge, and a soft glow of 4 in the page colour. One window or none: no dots.
    /// </summary>
    private void AddDots(Item item)
    {
        var count = WindowDots.For(item.IsClosed || item.IsPlus ? 0 : item.Count);
        if (count == 0) return;

        var size = ChoiceConstants.DotSize;
        var width = count * size + (count - 1) * ChoiceConstants.DotGap;
        var left = (LookConstants.ItemSize - width) / 2;
        var top = LookConstants.ItemSize + ChoiceConstants.DotCentreBelowTile - size / 2;
        for (var i = 0; i < count; i++)
        {
            var x = left + i * (size + ChoiceConstants.DotGap);
            var glow = new Ellipse { Width = size, Height = size, Effect = Units.Blur(Units.RadiusForBoxShadowBlur(ChoiceConstants.DotGlow)), IsHitTestVisible = false };
            _dotGlows.Add(glow);
            SetLeft(glow, x);
            SetTop(glow, top);
            Children.Add(glow);
            var dot = new Ellipse { Width = size, Height = size, Fill = Brushes.White, IsHitTestVisible = false };
            SetLeft(dot, x);
            SetTop(dot, top);
            Children.Add(dot);
        }
    }

    // ---- The ring of a helper's state (WORK-ORDER-11 section 3) ----------------------------

    private RotateTransform? _arcTurn;
    private Path? _arc;

    /// <summary>The colour of the state ring as drawn (the arc of "working", the full ring of the others); null for none (for the self-test).</summary>
    public Color? StateRingColour { get; private set; }

    /// <summary>Whether a turning arc is on this tile.</summary>
    public bool HasTurningArc => _arc is not null;

    /// <summary>
    /// A ring just outside the disc: its inner edge 0.75 beyond it, 2.5 thick, at full strength whether the tile is selected or not (it lies outside the tile's own opacity).
    /// Working: a quarter of the circle on a faint full track; needs you and finished: a full ring, still.
    /// </summary>
    private void AddStateRing(Item item)
    {
        if (TerminalRing.ColourOf(item.Ring) is not { } colour) return;
        var d = LookConstants.ItemSize;
        var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();
        StateRingColour = brush.Color;
        var centreRadius = d / 2 + TerminalRing.InnerGap + TerminalRing.Thickness / 2;
        var size = 2 * centreRadius + TerminalRing.Thickness; // an ellipse's stroke lies inside its box
        var track = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = item.Ring == HelperState.Working ? Paint.Brush(Rgb.White, TerminalRing.TrackAlpha) : brush,
            StrokeThickness = TerminalRing.Thickness,
            IsHitTestVisible = false,
        };
        SetLeft(track, d / 2 - size / 2);
        SetTop(track, d / 2 - size / 2);
        Children.Add(track);
        if (item.Ring != HelperState.Working) return;

        var sweep = 2 * Math.PI * TerminalRing.ArcShare;
        var start = new Point(d / 2, d / 2 - centreRadius); // the top of the circle
        var end = new Point(d / 2 + centreRadius * Math.Sin(sweep), d / 2 - centreRadius * Math.Cos(sweep));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(centreRadius, centreRadius), 0, sweep > Math.PI, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        _arcTurn = new RotateTransform(0, d / 2, d / 2);
        _arc = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = TerminalRing.Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = _arcTurn,
            IsHitTestVisible = false,
        };
        Children.Add(_arc);
    }

    /// <summary>Turns the working arc to where the island's clock puts it (a function of time alone). Does nothing for a tile without one.</summary>
    public void SetRingClock(double seconds)
    {
        if (_arcTurn is null) return;
        _arcTurn.Angle = TerminalRing.ArcAngle(seconds, Animations);
    }

    /// <summary>False where Windows is set to show no animations: the arc then stands still.</summary>
    private static bool Animations => SystemParameters.ClientAreaAnimation;

    /// <summary>How many dots this tile draws (for the self-test).</summary>
    public int DotCount => _dotGlows.Count;

    // ---- Brushes and bitmaps -----------------------------------------------

    /// <summary>linear-gradient(160deg, hsl(h 72% 62%), hsl(h 70% 40%)), both at 92%. Grey (a closed pick): the same without colour and a little darker.</summary>
    private static LinearGradientBrush Gradient(double hue, bool grey)
    {
        var angle = LookConstants.ItemGradientAngle * Math.PI / 180;
        var (dx, dy) = (Math.Sin(angle), -Math.Cos(angle));
        var half = (Math.Abs(dx) + Math.Abs(dy)) / 2;
        var topSaturation = grey ? 0 : LookConstants.ItemTopSaturation;
        var bottomSaturation = grey ? 0 : LookConstants.ItemBottomSaturation;
        var dim = grey ? ChoiceConstants.ClosedBrightness : 1;
        var brush = new LinearGradientBrush(
            Paint.Of(ColorMath.FromHsl(hue, topSaturation, LookConstants.ItemTopLightness * dim), LookConstants.ItemFillAlpha),
            Paint.Of(ColorMath.FromHsl(hue, bottomSaturation, LookConstants.ItemBottomLightness * dim), LookConstants.ItemFillAlpha),
            new Point(0.5 - dx * half, 0.5 - dy * half),
            new Point(0.5 + dx * half, 0.5 + dy * half));
        brush.Freeze();
        return brush;
    }

    /// <summary>A frozen bitmap from the icon's BGRA pixels (the pixels are straight, not premultiplied, as Windows gives them). Grey is the icon's grey copy (<see cref="GreyIcons"/>), made once.</summary>
    internal static BitmapSource ToBitmap(IconImage icon, bool grey)
    {
        var source = grey ? GreyIcons.Of(icon) : icon;
        var bitmap = BitmapSource.Create(source.Width, source.Height, 96, 96, PixelFormats.Bgra32, null, source.Bgra, source.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
