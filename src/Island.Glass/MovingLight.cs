using System.Numerics;
using System.Runtime.InteropServices;
using Island.Core;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Island.Glass;

/// <summary>
/// The moving light of the island's edge, drawn and moved by the system's compositor (Windows.UI.Composition, the one <see cref="GlassLayer"/> already uses), so that the app
/// does no work per frame for it (WORK-ORDER-12 section 2). Two windows of their own, each made like the glass window (no redirection bitmap, topmost, tool window, no
/// activation, every click let through): the glow directly above the island's shadow window (so beneath the glass and the capsule, where the bloom is drawn today) and the lit rim
/// directly above the island's window. The rim is a base line and two arcs; the arcs are stroked shapes on a rounded-rectangle geometry whose trim is moved by a keyframe animation
/// that runs in the compositor, started where <see cref="ArcClock"/> says; the glow is the same ring and arc, drawn into a visual surface and blurred by the compositor.
/// The numbers are <see cref="LightSpec"/>'s, which are the old drawing's. Facts it rests on: the shapes, the geometry, the trim properties and the animation are on Microsoft Learn
/// (UniversalApiContract 6.0, Windows 10 1803; the visual surface 8.0, 1903); what is not: where the compositor's rounded rectangle path starts and which way it runs, and that a trim
/// offset wraps (<see cref="CompositorPath"/>, UNVERIFIED), and, as for the glass, the interop interface that makes a window target (proven by working).
///
/// Threading: create it, and call every member, on the one UI thread that owns the anchor window. Creating it never throws: when it cannot be made, <see cref="IsAvailable"/>
/// is false and <see cref="UnavailableReason"/> holds a short code, and every member is a no-op.
/// </summary>
public sealed class MovingLight : IMovingLight, IDisposable
{
    /// <summary>A shape that moved by less than this many pixels is not followed again (a spring at rest keeps moving by a hair).</summary>
    private const float Hair = 0.01f;

    private const int BreathKeyFrames = 44;

    private readonly nint _anchor;
    private readonly nint _shadow;
    private readonly List<object> _keepAlive = [];
    private nint _rimWindow, _glowWindow;
    private DesktopWindowTarget? _rimTarget, _glowTarget;
    private Compositor? _c;
    private string? _failure;

    // the lit rim
    private ShapeVisual? _rim;
    private CompositionVisualSurface? _rimSurface;
    private SpriteVisual? _rimSprite;
    private float _rimBlurSigmaPx = -1;
    private CompositionRoundedRectangleGeometry? _baseGeo, _arc1Geo, _arc2Geo;
    private CompositionSpriteShape? _baseShape, _arc1Shape, _arc2Shape;
    private CompositionColorBrush? _baseBrush, _arc1Brush, _arc2Brush;

    // the glow
    private ShapeVisual? _glowShapes;
    private SpriteVisual? _glowSprite;
    private CompositionVisualSurface? _glowSurface;
    private CompositionRoundedRectangleGeometry? _ringGeo, _glowArcGeo;
    private CompositionSpriteShape? _ringShape, _glowArcShape;
    private CompositionColorBrush? _ringBrush, _glowArcBrush;

    private bool _shown;
    private float _scale = 1;
    private Vector2 _windowPx;
    private RimOutline _outline;
    private bool _haveOutline;
    private bool _animating;
    private bool _breathing;
    private float _blurSigmaPx = -1;
    private LightSpec? _spec;
    private LightSpec? _appliedSpec;
    private double _offsetsSetAtMs = double.NaN;
    private float _firstArcOffset = float.NaN;
    private bool? _appliedBreathing;

    /// <param name="anchorWindow">The island's own window (the capsule's).</param>
    /// <param name="shadowWindow">The island's shadow window, which lies directly beneath the capsule's: the glow goes directly above it.</param>
    public MovingLight(nint anchorWindow, nint shadowWindow)
    {
        _anchor = anchorWindow;
        _shadow = shadowWindow;
        try
        {
            Build();
        }
        catch (Exception e)
        {
            _failure ??= "TREE_FAILED";
            UnavailableHResult = e.HResult;
            TearDown();
        }
    }

    public bool IsAvailable => _rim != null && _rimSprite != null && _glowSprite != null;

    public string? UnavailableReason => _failure;

    /// <summary>The HRESULT behind a failure, when there was one; 0 otherwise. A number, never message text.</summary>
    public int UnavailableHResult { get; private set; }

    public bool IsShown => _shown;

    public nint RimWindowHandle => _rimWindow;

    public nint GlowWindowHandle => _glowWindow;

    /// <summary>For the self-test: whether a point of the glow window (its own pixels) is where the glow is shown, i.e. outside the island's outline.</summary>
    public bool GlowShownAt(int x, int y) => GlassWindow.RegionContains(_glowWindow, x, y);

    /// <summary>What the compositor's objects were last given, for the self-test (the numbers must equal the old light's).</summary>
    public MovingLightApplied? Applied { get; private set; }

    public void Show(double nowMs)
    {
        try
        {
            ShowCore(nowMs);
        }
        catch (COMException e)
        {
            Lose(e);
        }
    }

    /// <summary>The compositor failed after the light was made (the graphics device was lost): the light is given up, and the island draws it the old way from the next frame (<see cref="IsAvailable"/> is false).</summary>
    private void Lose(COMException e)
    {
        _failure ??= "DEVICE_LOST";
        UnavailableHResult = e.HResult;
        _shown = false;
        GlassWindow.Hide(_glowWindow);
        GlassWindow.Hide(_rimWindow);
        TearDown();
    }

    private void ShowCore(double nowMs)
    {
        if (!IsAvailable || !GlassWindow.TryAnchor(_anchor, out var r, out var dpi)) return;
        _scale = dpi / 96f;
        _windowPx = new Vector2(r.Right - r.Left, r.Bottom - r.Top);
        _rim!.Size = _windowPx;
        _rimSurface!.SourceSize = _windowPx;
        _rimSprite!.Size = _windowPx;
        _glowShapes!.Size = _windowPx;
        _glowSurface!.SourceSize = _windowPx;
        _glowSprite!.Size = _windowPx;
        SetBlur((float)(LookConstants.BloomBlurCss * _scale));
        SetRimBlur((float)(LookConstants.FrontRimBlurCss * _scale)); // = LightSpec.RimBlurSigma: the spec carries the number, the compositor is given it here
        _shown = true;
        _appliedSpec = null; // the strokes are given again (the scale may have changed)
        _haveOutline = false; // the first Follow after a Show lays everything out again and starts the light where the clock says
        _animating = false;
        if (_shadow != 0 && GlassWindow.TryAnchor(_shadow, out var sr, out _)) GlassWindow.PlaceAbove(_glowWindow, _shadow, sr);
        else GlassWindow.PlaceBelow(_glowWindow, _anchor, r);
        GlassWindow.PlaceAbove(_rimWindow, _anchor, r);
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        try
        {
            if (_animating) StopRamps();
            if (_breathing) _glowSprite!.StopAnimation("Opacity");
        }
        catch (COMException e)
        {
            Lose(e); // the compositor failed: the light is given up (the windows are hidden by Lose)
            return;
        }
        finally
        {
            _breathing = false;
        }

        GlassWindow.Hide(_glowWindow);
        GlassWindow.Hide(_rimWindow);
    }

    public void Follow(in LightFrame frame)
    {
        try
        {
            FollowCore(frame);
        }
        catch (COMException e)
        {
            Lose(e);
        }
    }

    private void FollowCore(in LightFrame frame)
    {
        if (!_shown || !IsAvailable) return;
        KeepOrder();
        var spec = LightSpec.For(frame.Colour, frame.Look);
        if (spec is null) return;
        _spec = spec;
        var o = RimOutline.Of(frame.Left, frame.Top, frame.Width, frame.Height, frame.Radius, frame.RadiusX, frame.RadiusY);
        var moved = !_haveOutline || ShapeMoved(o, _outline);
        if (moved)
        {
            _outline = o;
            _haveOutline = true;
            PlaceShapes(o);
        }

        // What the compositor was given is remembered here: a call that changes nothing (the mouse moves over the island) asks the compositor nothing.
        if (!spec.Equals(_appliedSpec))
        {
            ApplyStrokes(spec);
            _appliedSpec = spec;
            _appliedBreathing = null;
        }

        if (_appliedBreathing != frame.Breathing)
        {
            ApplyGlow(spec, frame);
            _appliedBreathing = frame.Breathing;
        }

        if (moved)
        {
            if (_animating) StopRamps();
            SetOffsets(frame.NowMs); // the shape moves: the light's place is set on every frame, as the old drawing does
        }
        else if (!_animating)
        {
            StartRamps(frame.NowMs, spec); // at rest: the compositor takes the light from here
        }

        Applied = new MovingLightApplied(spec, o, _animating, _breathing, _scale, _offsetsSetAtMs, _firstArcOffset);
    }

    /// <summary>
    /// The windows lie directly above the island's windows only while nothing raised the island's window (a click that gave it the keyboard puts it on top of its band). Looked at on every beat
    /// (one question to Windows); put back when it is not so.
    /// </summary>
    private void KeepOrder()
    {
        if (!GlassWindow.IsDirectlyAbove(_rimWindow, _anchor) && GlassWindow.TryAnchor(_anchor, out var r, out _)) GlassWindow.PlaceAbove(_rimWindow, _anchor, r);
        if (_shadow != 0 && !GlassWindow.IsDirectlyAbove(_glowWindow, _shadow) && GlassWindow.TryAnchor(_shadow, out var sr, out _)) GlassWindow.PlaceAbove(_glowWindow, _shadow, sr);
    }

    public void Dispose()
    {
        _shown = false;
        try
        {
            TearDown();
        }
        catch (COMException)
        {
            // the compositor is gone already; the windows below are destroyed by TearDown's own order where it got to them
        }
    }

    // ---- laying out ---------------------------------------------------------------------------------------------

    private static bool ShapeMoved(RimOutline a, RimOutline b) =>
        Math.Abs(a.X - b.X) > Hair || Math.Abs(a.Y - b.Y) > Hair || Math.Abs(a.Width - b.Width) > Hair || Math.Abs(a.Height - b.Height) > Hair || Math.Abs(a.Radius - b.Radius) > Hair || Math.Abs(a.RadiusX - b.RadiusX) > Hair || Math.Abs(a.RadiusY - b.RadiusY) > Hair;

    private void PlaceShapes(RimOutline o)
    {
        var offset = new Vector2((float)o.X * _scale, (float)o.Y * _scale);
        var size = new Vector2((float)o.Width * _scale, (float)o.Height * _scale);
        var radius = new Vector2((float)o.RadiusX * _scale, (float)o.RadiusY * _scale);
        foreach (var g in new[] { _baseGeo!, _arc1Geo!, _arc2Geo!, _ringGeo!, _glowArcGeo! })
        {
            g.Offset = offset;
            g.Size = size;
            g.CornerRadius = radius;
        }

        // The glow is cut out of the island's inside, as the old bloom is (it is drawn only outside the capsule's outline): the window the glow is drawn in has a hole in the shape of the outline.
        // The hole is the capsule's outer outline: the rim's outline grown by the inset the rim keeps inside it.
        var grow = (float)(LookConstants.RimInset * _scale);
        // The edges are not rounded (WORK-ORDER-13): the hole holds the pixels whose centre is inside the outline, wherever the edges fall.
        double left = offset.X - grow, top = offset.Y - grow, right = offset.X + size.X + grow, bottom = offset.Y + size.Y + grow;
        GlassWindow.CutOutRoundedRectangle(_glowWindow, left, top, right - left, bottom - top, radius.X + grow, radius.Y + grow);
    }

    private void ApplyStrokes(LightSpec spec)
    {
        Style(_baseShape!, _baseBrush!, spec.BaseRim ?? new StrokeSpec(default, 0, 0, false));
        Style(_arc1Shape!, _arc1Brush!, spec.FirstArc!.Value.Stroke);
        Style(_arc2Shape!, _arc2Brush!, spec.SecondArc!.Value.Stroke);
        Style(_ringShape!, _ringBrush!, spec.BloomRing);
        Style(_glowArcShape!, _glowArcBrush!, spec.BloomArc!.Value.Stroke);
        Trim(_arc1Geo!, spec.FirstArc!.Value.Fraction);
        Trim(_arc2Geo!, spec.SecondArc!.Value.Fraction);
        Trim(_glowArcGeo!, spec.BloomArc!.Value.Fraction);
    }

    private void Style(CompositionSpriteShape shape, CompositionColorBrush brush, StrokeSpec stroke)
    {
        var colour = Color.FromArgb((byte)Math.Round(Math.Clamp(stroke.Alpha, 0, 1) * 255), (byte)Math.Round(Math.Clamp(stroke.Colour.R, 0, 255)),
            (byte)Math.Round(Math.Clamp(stroke.Colour.G, 0, 255)), (byte)Math.Round(Math.Clamp(stroke.Colour.B, 0, 255)));
        if (brush.Color != colour) brush.Color = colour;
        var thickness = (float)(stroke.Width * _scale);
        if (shape.StrokeThickness != thickness) shape.StrokeThickness = thickness;
    }

    private static void Trim(CompositionGeometry g, double fraction)
    {
        var end = (float)Math.Clamp(fraction, 0, 1);
        if (g.TrimStart != 0) g.TrimStart = 0;
        if (g.TrimEnd != end) g.TrimEnd = end;
    }

    /// <summary>
    /// The old rim has a blur of its own, 0.6 px (<see cref="LookConstants.FrontRimBlurCss"/>); Dan said yes to carrying it (P23): the rim's shapes are drawn into a visual surface that is not in any window
    /// and shown through a blur, as the glow is. UNVERIFIED by eye: no picture of the compositor's light can be taken.
    /// </summary>
    private void SetRimBlur(float sigmaPx)
    {
        if (Math.Abs(sigmaPx - _rimBlurSigmaPx) < 1e-3f) return;
        _rimBlurSigmaPx = sigmaPx;
        var brush = GlassCompositor.CreateBlurBrush(_c!, "source", sigmaPx, _keepAlive);
        brush.SetSourceParameter("source", _c!.CreateSurfaceBrush(_rimSurface));
        _rimSprite!.Brush = brush;
    }

    private void SetBlur(float sigmaPx)
    {
        if (Math.Abs(sigmaPx - _blurSigmaPx) < 1e-3f) return;
        _blurSigmaPx = sigmaPx;
        var brush = GlassCompositor.CreateBlurBrush(_c!, "source", sigmaPx, _keepAlive);
        brush.SetSourceParameter("source", _c!.CreateSurfaceBrush(_glowSurface));
        _glowSprite!.Brush = brush; // a blur's strength is fixed when its brush is made: made again only when the screen's scaling changes
    }

    // ---- the glow's opacity: still, or breathing --------------------------------------------------------------------

    private void ApplyGlow(LightSpec spec, in LightFrame frame)
    {
        var sprite = _glowSprite!;
        if (frame.Breathing)
        {
            if (_breathing) return;
            _breathing = true;
            sprite.StartAnimation("Opacity", BreathAnimation(frame.NowMs));
            return;
        }

        if (_breathing)
        {
            _breathing = false;
            sprite.StopAnimation("Opacity");
        }

        var opacity = (float)spec.GlowOpacity;
        if (sprite.Opacity != opacity) sprite.Opacity = opacity;
    }

    /// <summary>
    /// The glow's opacity for one breath of Vibe, from <see cref="ModeMark.Breath"/> itself at the moments of one breath starting where the clock is now: keyframes at equal steps, linear
    /// between them, looping for ever, so the breath goes on without the app. The last keyframe is the first again (a breath is periodic).
    /// </summary>
    private ScalarKeyFrameAnimation BreathAnimation(double nowMs)
    {
        var c = _c!;
        var a = c.CreateScalarKeyFrameAnimation();
        var linear = c.CreateLinearEasingFunction();
        for (var i = 0; i <= BreathKeyFrames; i++)
        {
            var t = nowMs / 1000.0 + ModeMark.BreathSeconds * i / BreathKeyFrames;
            a.InsertKeyFrame((float)i / BreathKeyFrames, (float)(LookConstants.BloomLayerAlpha * ModeMark.Breath(t)), linear);
        }

        a.Duration = TimeSpan.FromSeconds(ModeMark.BreathSeconds);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        return a;
    }

    // ---- the light going round ---------------------------------------------------------------------------------------

    private IEnumerable<(CompositionGeometry Geometry, ArcSpec Arc)> Arcs(LightSpec spec) =>
    [
        (_arc1Geo!, spec.FirstArc!.Value), (_arc2Geo!, spec.SecondArc!.Value), (_glowArcGeo!, spec.BloomArc!.Value),
    ];

    /// <summary>The trim offsets for this instant, set once and never moving: while the shape changes they are set again on every frame.</summary>
    private void SetOffsets(double nowMs)
    {
        if (_spec is null) return;
        var head = ArcClock.Head(nowMs / 1000.0);
        var first = true;
        foreach (var (geometry, arc) in Arcs(_spec))
        {
            var offset = (float)CompositorPath.TrimOffset(head, arc, _outline);
            geometry.TrimOffset = offset;
            if (first) _firstArcOffset = offset;
            first = false;
        }

        _offsetsSetAtMs = nowMs;
    }

    /// <summary>
    /// Starts the light going round in the compositor, from where the clock puts it now: for each arc a linear keyframe animation of the trim offset by one whole lap, over one lap's time,
    /// for ever (an offset past 1 wraps, UNVERIFIED on Learn).
    /// </summary>
    private void StartRamps(double nowMs, LightSpec spec)
    {
        var head = ArcClock.Head(nowMs / 1000.0);
        var c = _c!;
        var linear = c.CreateLinearEasingFunction();
        foreach (var (geometry, arc) in Arcs(spec))
        {
            var from = (float)CompositorPath.TrimOffset(head, arc, _outline);
            if (spec.FirstArc is { } firstArc && arc == firstArc) _firstArcOffset = from;
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0f, from, linear);
            a.InsertKeyFrame(1f, from + 1f, linear);
            a.Duration = TimeSpan.FromSeconds(spec.LapSeconds);
            a.IterationBehavior = AnimationIterationBehavior.Forever;
            geometry.StartAnimation("TrimOffset", a);
        }

        _animating = true;
        _offsetsSetAtMs = nowMs;
    }

    private void StopRamps()
    {
        foreach (var g in new CompositionGeometry[] { _arc1Geo!, _arc2Geo!, _glowArcGeo! }) g.StopAnimation("TrimOffset");
        _animating = false;
    }

    // ---- building and tearing down ---------------------------------------------------------------------------------------

    private void Build()
    {
        if (_anchor == 0 || !GlassWindow.IsWindow(_anchor) || !GlassWindow.TryAnchor(_anchor, out _, out _))
        {
            _failure = "ANCHOR_INVALID";
            return;
        }

        _failure = "NO_COMPOSITOR";
        _c = GlassCompositor.ForThisThread();
        var c = _c;

        _failure = "WINDOW_FAILED";
        _rimWindow = GlassWindow.Create(GlassClickThrough.LayeredTransparent);
        _glowWindow = GlassWindow.Create(GlassClickThrough.LayeredTransparent);
        if (_rimWindow == 0 || _glowWindow == 0)
        {
            UnavailableHResult = Marshal.GetHRForLastWin32Error();
            return;
        }

        _failure = "TREE_FAILED";
        _rimTarget = GlassCompositor.CreateTarget(_rimWindow);
        _glowTarget = GlassCompositor.CreateTarget(_glowWindow);

        // the lit rim: a base line and two arcs, in a shape visual that is the window's root
        _rim = c.CreateShapeVisual();
        (_baseGeo, _baseBrush, _baseShape) = Stroked(c, round: false);
        (_arc1Geo, _arc1Brush, _arc1Shape) = Stroked(c, round: true);
        (_arc2Geo, _arc2Brush, _arc2Shape) = Stroked(c, round: true);
        _rim.Shapes.Add(_baseShape);
        _rim.Shapes.Add(_arc1Shape);
        _rim.Shapes.Add(_arc2Shape);
        _rimSurface = c.CreateVisualSurface();
        _rimSurface.SourceVisual = _rim;
        _rimSprite = c.CreateSpriteVisual();
        _rimTarget.Root = _rimSprite;

        // the glow: a ring and an arc in a shape visual that is not in any window, drawn into a visual surface and blurred
        _glowShapes = c.CreateShapeVisual();
        (_ringGeo, _ringBrush, _ringShape) = Stroked(c, round: false);
        (_glowArcGeo, _glowArcBrush, _glowArcShape) = Stroked(c, round: true);
        _glowShapes.Shapes.Add(_ringShape);
        _glowShapes.Shapes.Add(_glowArcShape);
        _glowSurface = c.CreateVisualSurface();
        _glowSurface.SourceVisual = _glowShapes;
        _glowSprite = c.CreateSpriteVisual();
        _glowSprite.Opacity = (float)LookConstants.BloomLayerAlpha;
        _glowTarget.Root = _glowSprite;
        _failure = null;
    }

    private static (CompositionRoundedRectangleGeometry, CompositionColorBrush, CompositionSpriteShape) Stroked(Compositor c, bool round)
    {
        var geometry = c.CreateRoundedRectangleGeometry();
        var brush = c.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        var shape = c.CreateSpriteShape(geometry);
        shape.StrokeBrush = brush;
        shape.FillBrush = null;
        if (round)
        {
            shape.StrokeStartCap = CompositionStrokeCap.Round;
            shape.StrokeEndCap = CompositionStrokeCap.Round;
        }

        return (geometry, brush, shape);
    }

    private void TearDown()
    {
        _rim = null;
        _rimSprite = null;
        _glowSprite = null;
        foreach (var target in new[] { _rimTarget, _glowTarget })
        {
            if (target == null) continue;
            try
            {
                target.Root = null;
                target.Dispose();
            }
            catch (COMException)
            {
                // the window is going anyway; nothing to recover
            }
        }

        _rimTarget = _glowTarget = null;
        if (_rimWindow != 0) GlassWindow.Destroy(_rimWindow);
        if (_glowWindow != 0) GlassWindow.Destroy(_glowWindow);
        _rimWindow = _glowWindow = 0;
    }
}

/// <summary>What the light's compositor objects were last given (for the self-test): the numbers, the outline they sit on, whether the compositor is moving the light and the glow's breath, and the scale.</summary>
/// <param name="OffsetsSetAtMs">The moment of the clock the compositor's animation was last started or its offsets last set from (NaN before any).</param>
/// <param name="FirstArcOffset">The trim offset the first arc was given at that moment.</param>
public sealed record MovingLightApplied(LightSpec Spec, RimOutline Outline, bool Animating, bool Breathing, float Scale, double OffsetsSetAtMs = double.NaN, float FirstArcOffset = float.NaN);
