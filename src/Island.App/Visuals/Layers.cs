using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>
/// WPF's BlurEffect.Radius is three standard deviations (measured by BlurCalibration and
/// recorded in selftest.json). A CSS filter blur(s) is a standard deviation of s; a CSS
/// box-shadow or text-shadow blur radius b is a standard deviation of b / 2.
/// </summary>
internal static class Units
{
    public const double WpfRadiusPerSigma = 3;

    public static double RadiusForFilterBlur(double cssFilterPx) => cssFilterPx * WpfRadiusPerSigma;

    public static double RadiusForBoxShadowBlur(double cssBlurPx) => cssBlurPx / 2 * WpfRadiusPerSigma;

    public static BlurEffect Blur(double radius) => new() { Radius = radius, KernelType = KernelType.Gaussian };
}

/// <summary>One drawn layer of the island, window-sized, redrawn from a <see cref="ShapeFrame"/>.</summary>
internal abstract class Layer : FrameworkElement
{
    protected ShapeFrame F;

    protected Layer()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    /// <summary>The layer's name, for the measuring run (WORK-ORDER-11 section 4).</summary>
    public abstract string LayerName { get; }

    /// <summary>How many times this layer was rendered (WPF calls OnRender when the layer is invalidated), for the measuring run and the check that nothing is drawn again for nothing.</summary>
    public long Renders { get; private set; }

    protected sealed override void OnRender(DrawingContext dc)
    {
        Renders++;
        Draw(dc);
    }

    protected abstract void Draw(DrawingContext dc);

    /// <summary>Draws the layer again from the frame it holds (a layer that was out of sight was not kept up).</summary>
    public void Redraw() => InvalidateVisual();

    /// <summary>True when what this layer draws depends on the page's colour (the glow and the bloom and the rim): a change of colour draws it again.</summary>
    protected virtual bool UsesColour => false;

    /// <summary>True while what this layer draws follows the moving light: it is then drawn again on every frame the light moves. Nothing else about it moves.</summary>
    protected virtual bool FollowsLight => false;

    private bool _drawnOnce;

    /// <summary>
    /// Takes the frame. The layer is drawn again only when what it draws changed: its place or shape, its colour when it draws one, the light's place when it follows
    /// it (WORK-ORDER-11 section 4: every layer used to be drawn again on every frame, most of them blurred over the whole window, for nothing). A change the layer's own
    /// switches make (the glass, the mode's look, the ring) invalidates it by itself.
    /// </summary>
    public void Update(in ShapeFrame frame)
    {
        F = frame;
        if (_drawnOnce && !Changed(_rendered, frame)) return;
        _drawnOnce = true;
        _rendered = frame;
        InvalidateVisual();
    }

    private ShapeFrame _rendered;

    /// <summary>A spring that has come to rest keeps moving by a hair for seconds: a shape that moved by less than this (a hundredth of a device-independent pixel, far below what any pixel shows) is not drawn again.</summary>
    internal const double ShapeHair = 0.01;

    internal static bool ShapeMoved(in ShapeFrame a, in ShapeFrame b) =>
        Math.Abs(a.CentreX - b.CentreX) > ShapeHair || Math.Abs(a.Top - b.Top) > ShapeHair || Math.Abs(a.Width - b.Width) > ShapeHair
        || Math.Abs(a.Height - b.Height) > ShapeHair || Math.Abs(a.Radius - b.Radius) > ShapeHair;

    private bool Changed(in ShapeFrame a, in ShapeFrame b) =>
        ShapeMoved(a, b)
        || UsesColour && a.Category != b.Category
        || FollowsLight && a.ArcHead != b.ArcHead;
}

/// <summary>The drop shadow: black at 32%, 18 down, CSS blur 50. Drawn blurred; the hole under the shape is cut by the window.</summary>
internal sealed class ShadowLayer : Layer
{
    public ShadowLayer() => Effect = Units.Blur(Units.RadiusForBoxShadowBlur(LookConstants.ShadowCssBlur));

    public override string LayerName => "shadow";

    protected override void Draw(DrawingContext dc)
    {
        var r = F.Rect;
        dc.DrawRoundedRectangle(Paint.Brush(Paint.Black, LookConstants.ShadowAlpha), null,
            new Rect(r.X, r.Y + LookConstants.ShadowOffsetY, r.Width, r.Height), F.Radius, F.Radius);
    }
}

/// <summary>The bloom behind the body: a 7 px rim at 45% plus an 11 px arc, plain category colour, blurred, 85%.</summary>
internal sealed class BloomLayer : Layer
{
    public BloomLayer()
    {
        Effect = Units.Blur(Units.RadiusForFilterBlur(LookConstants.BloomBlurCss));
        Opacity = LookConstants.BloomLayerAlpha;
    }

    /// <summary>False while the pill's ring is drawn: nothing moves round the edge then, the glow behind included.</summary>
    public bool ShowArc
    {
        get => _showArc;
        set
        {
            if (_showArc == value) return;
            _showArc = value;
            InvalidateVisual();
        }
    }

    private bool _showArc = true;

    /// <summary>The glow behind the edge as a share of the approved one (the mode's mark: Vibe breathes, DND has none). 1 is the approved look exactly.</summary>
    public void SetStrength(double share)
    {
        _share = share;
        var opacity = LookConstants.BloomLayerAlpha * Math.Clamp(share, 0, 1);
        if (Opacity != opacity) Opacity = opacity;
        var visibility = opacity <= 0 || _hidden ? Visibility.Collapsed : Visibility.Visible;
        if (Visibility == visibility) return;
        Visibility = visibility;
        InvalidateVisual(); // it was not kept up while it was out of sight: the light is where it is now
    }

    private double _share = 1;
    private bool _hidden;

    /// <summary>True while the graphics-card light draws the glow instead (WORK-ORDER-12 section 2): this layer is then not drawn at all.</summary>
    public bool Hidden
    {
        get => _hidden;
        set
        {
            if (_hidden == value) return;
            _hidden = value;
            SetStrength(_share);
        }
    }

    public override string LayerName => "bloom";

    protected override bool UsesColour => true;

    protected override bool FollowsLight => _showArc && Visibility == Visibility.Visible && Opacity > 0;

    protected override void Draw(DrawingContext dc)
    {
        var line = Geo.Inset(F.Rect, LookConstants.RimInset);
        var radius = F.Radius - LookConstants.RimInset;
        dc.DrawRoundedRectangle(null, Paint.Pen(F.Category, LookConstants.BloomBaseAlpha, LookConstants.BloomBaseWidth), line, radius, radius);

        if (!_showArc) return;
        var perimeter = new RoundedPerimeter(line.X, line.Y, line.Width, line.Height, radius);
        var arc = Geo.Arc(perimeter, F.ArcHead - LookConstants.ArcFraction, LookConstants.ArcFraction);
        dc.DrawGeometry(null, Paint.Pen(F.Category, 1, LookConstants.BloomArcWidth, roundCaps: true), arc);
    }
}

/// <summary>The glass fill: dark base at 60% under a white gradient from 24% (top) to 5% (bottom).</summary>
internal sealed class BodyFillLayer(double baseAlpha) : Layer
{
    private static readonly Rgb Base = Rgb.FromHex(LookConstants.GlassBaseColor);

    /// <summary>The opacity of the dark base; changed when the glass is switched.</summary>
    public double BaseAlpha { get; set; } = baseAlpha;

    public override string LayerName => "fill";

    protected override void Draw(DrawingContext dc)
    {
        dc.DrawGeometry(Paint.Brush(Base, BaseAlpha), null, F.Outline());

        var gradient = new LinearGradientBrush(
            Paint.Of(Rgb.White, LookConstants.GlassGradientTopAlpha),
            Paint.Of(Rgb.White, LookConstants.GlassGradientBottomAlpha),
            90);
        gradient.Freeze();
        dc.DrawGeometry(gradient, null, F.PaddingBox());
    }
}

/// <summary>
/// A soft inner glow, as a CSS inset box-shadow: the area outside the (shifted) padding box,
/// blurred, then cut to the padding box by its parent.
/// </summary>
internal sealed class InsetGlowLayer : Layer
{
    private readonly double _offsetY;
    private readonly Func<ShapeFrame, (Rgb Colour, double Alpha)> _paint;
    private readonly string _name;

    private readonly bool _usesColour;

    protected override bool UsesColour => _usesColour;

    public InsetGlowLayer(string name, double offsetY, double cssBlur, Func<ShapeFrame, (Rgb Colour, double Alpha)> paint, bool usesColour = false)
    {
        _name = name;
        _usesColour = usesColour;
        _offsetY = offsetY;
        _paint = paint;
        Effect = Units.Blur(Units.RadiusForBoxShadowBlur(cssBlur));
    }

    public override string LayerName => _name;

    protected override void Draw(DrawingContext dc)
    {
        var padding = Geo.Inset(F.Rect, LookConstants.BorderWidth);
        var shifted = Geo.RoundedRect(new Rect(padding.X, padding.Y + _offsetY, padding.Width, padding.Height),
            F.Radius - LookConstants.BorderWidth);

        // Big enough that the blurred frame is solid right up to the padding box.
        var reach = WindowMetrics.ShadowReach + Math.Abs(_offsetY);
        var outer = new Rect(padding.X - reach, padding.Y - reach, padding.Width + 2 * reach, padding.Height + 2 * reach);

        var (colour, alpha) = _paint(F);
        dc.DrawGeometry(Paint.Brush(colour, alpha), null, Geo.Frame(outer, shifted));
    }
}

/// <summary>The 1 px white highlight along the top edge and the 1 px border, on top of the glows.</summary>
internal sealed class BodyEdgeLayer : Layer
{
    public override string LayerName => "edge";

    protected override void Draw(DrawingContext dc)
    {
        var padding = Geo.Inset(F.Rect, LookConstants.BorderWidth);
        var radius = F.Radius - LookConstants.BorderWidth;
        var shifted = Geo.RoundedRect(new Rect(padding.X, padding.Y + LookConstants.TopHighlightWidth, padding.Width, padding.Height), radius);
        var highlight = new CombinedGeometry(GeometryCombineMode.Exclude, Geo.RoundedRect(padding, radius), shifted);
        dc.DrawGeometry(Paint.Brush(Rgb.White, LookConstants.TopHighlightAlpha), null, highlight);

        var line = Geo.Inset(F.Rect, LookConstants.BorderWidth / 2);
        var lineRadius = F.Radius - LookConstants.BorderWidth / 2;
        dc.DrawRoundedRectangle(null, Paint.Pen(Rgb.White, LookConstants.BorderAlpha, LookConstants.BorderWidth), line, lineRadius, lineRadius);
    }
}

/// <summary>
/// The rim light on the front: a base line and two moving arcs. Every line is centred half a
/// pixel inside the outer edge. A very slight blur softens the whole layer.
/// </summary>
internal sealed class RimLayer : Layer
{
    private ModeMark.Look _look = ModeMark.Look.Approved;
    private double? _ring;
    private bool _noticeEdge;

    /// <summary>The notice's edge (WORK-ORDER-7 section 4): a steady line of 1.5 in the page colour (the Vibe-coding colour), nothing moving round it.</summary>
    public void SetNoticeEdge(bool on)
    {
        if (on == _noticeEdge) return;
        _noticeEdge = on;
        InvalidateVisual();
    }

    /// <summary>
    /// The small pill's edge is its progress (WORK-ORDER-7 section 2): a ring 2.4 thick, the part still to play lit in the page colour from the top
    /// centre clockwise, the part already played white at 16%. Null draws the approved edge (the pill then shows the moving light).
    /// </summary>
    public void SetRing(double? litShare)
    {
        if (litShare == _ring) return;
        _ring = litShare;
        InvalidateVisual();
    }

    public RimLayer() => Effect = Units.Blur(Units.RadiusForFilterBlur(LookConstants.FrontRimBlurCss));

    /// <summary>The mode's mark on the edge. The approved look draws exactly what it always drew.</summary>
    public void SetLook(ModeMark.Look look)
    {
        if (look == _look) return;
        _look = look;
        InvalidateVisual();
    }

    public override string LayerName => "rim";

    protected override bool UsesColour => true;

    // The two moving arcs are drawn only with a moving light, and not while the notice's edge, the pill's ring or the dashed rim stand in its place.
    protected override bool FollowsLight => _look.MovingLight > 0 && !_noticeEdge && _ring is null;

    protected override void Draw(DrawingContext dc)
    {
        var line = Geo.Inset(F.Rect, LookConstants.RimInset);
        var radius = F.Radius - LookConstants.RimInset;
        if (_noticeEdge && _look.DashedRim <= 0)
        {
            dc.DrawRoundedRectangle(null, Paint.Pen(F.Category, _look.PageRim, NoticeLayout.EdgeWidth), line, radius, radius);
            return;
        }

        if (_ring is { } share && _look.DashedRim <= 0)
        {
            dc.DrawRoundedRectangle(null, Paint.Pen(Rgb.White, PillLayout.PlayedAlpha, PillLayout.RingThickness), line, radius, radius);
            if (share > 0)
            {
                var ring = new RoundedPerimeter(line.X, line.Y, line.Width, line.Height, radius);
                dc.DrawGeometry(null, Paint.Pen(F.Category, 1, PillLayout.RingThickness), Geo.Arc(ring, 0, Math.Clamp(share, 0, 1)));
            }

            return;
        }

        if (_look.PageRim > 0)
            dc.DrawRoundedRectangle(null, Paint.Pen(F.Category, LookConstants.BaseRimAlpha * _look.PageRim, LookConstants.BaseRimWidth), line, radius, radius);

        if (_look.DashedRim > 0)
        {
            // DND: a dashed white line instead of the page-coloured one.
            var dashed = new Pen(Paint.Brush(Rgb.White, ModeMark.DashedRimAlpha * _look.DashedRim), ModeMark.DashedRimWidth)
            {
                DashStyle = new DashStyle([ModeMark.DashLength, ModeMark.DashGap], 0),
            };
            dashed.Freeze();
            dc.DrawRoundedRectangle(null, dashed, line, radius, radius);
        }

        if (_look.MovingLight <= 0 || _noticeEdge) return;
        var perimeter = new RoundedPerimeter(line.X, line.Y, line.Width, line.Height, radius);
        var hot = ColorMath.ArcColor(F.Category);

        var first = Geo.Arc(perimeter, F.ArcHead - LookConstants.ArcFraction, LookConstants.ArcFraction);
        dc.DrawGeometry(null, Paint.Pen(hot, _look.MovingLight, LookConstants.ArcWidth, roundCaps: true), first);

        var second = Geo.Arc(perimeter, F.ArcHead + LookConstants.SecondArcPhase - LookConstants.SecondArcFraction, LookConstants.SecondArcFraction);
        dc.DrawGeometry(null, Paint.Pen(hot, LookConstants.SecondArcAlpha * _look.MovingLight, LookConstants.SecondArcWidth, roundCaps: true), second);
    }
}
