using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>
/// The whole island drawn onto two canvases: a back one (shadow and bloom; goes in the window
/// that ignores the mouse) and a front one (glass, rim light and contents). The same view is used
/// for the two real windows and for off-screen snapshots.
/// </summary>
internal sealed class IslandView
{
    private readonly Canvas _backRoot;
    private readonly Canvas _backGroup = new();
    private readonly Canvas _frontGroup = new();
    private readonly Canvas _glowHost = new();
    private readonly Canvas _contentsHost = new();
    private readonly ScaleTransform _backScale = new();
    private readonly ScaleTransform _frontScale = new();
    private readonly Layer[] _layers;
    private readonly RimLayer _rim;
    private readonly BloomLayer _bloom;
    private double _glassBase = LookConstants.GlassBaseAlpha;
    private bool _pill;
    private bool _search;
    private bool _notice;
    private readonly BodyFillLayer _fill;
    private readonly double _width;
    private readonly double _height;

    private readonly Canvas _frontRoot;

    /// <summary>What the island shows now, in words, for a screen reader (WORK-ORDER-12 section 4); only the real window's root can say it.</summary>
    public void Describe(string text, bool alert)
    {
        if (_frontRoot is IslandRoot root) root.Describe(text, alert);
    }

    /// <summary>The island went away: the words of the last visit are forgotten, so the next visit is told even when they would be the same.</summary>
    public void ForgetWords()
    {
        if (_frontRoot is IslandRoot root) root.Forget();
    }

    public IslandView(Canvas backRoot, Canvas frontRoot, double windowWidth, double windowHeight, double glassBaseAlpha = LookConstants.GlassBaseAlpha)
    {
        _backRoot = backRoot;
        _width = windowWidth;
        _height = windowHeight;

        var shadow = new ShadowLayer();
        var bloom = new BloomLayer();
        _bloom = bloom;
        var fill = new BodyFillLayer(glassBaseAlpha);
        _fill = fill;
        var bottomGlow = new InsetGlowLayer("bottom-glow", -LookConstants.BottomGlowOffset, LookConstants.BottomGlowCssBlur,
            _ => (Rgb.White, LookConstants.BottomGlowAlpha));
        var categoryGlow = new InsetGlowLayer("category-glow", 0, LookConstants.CategoryGlowCssBlur,
            f => (f.Category, LookConstants.CategoryGlowAlpha), usesColour: true);
        var edge = new BodyEdgeLayer();
        var rim = new RimLayer();
        _rim = rim;
        _layers = [shadow, bloom, fill, bottomGlow, categoryGlow, edge, rim];

        foreach (var layer in _layers)
        {
            layer.Width = windowWidth;
            layer.Height = windowHeight;
        }

        Size(_backGroup, _frontGroup, _glowHost, _contentsHost);
        _backGroup.RenderTransform = _backScale;
        _frontGroup.RenderTransform = _frontScale;

        _backGroup.Children.Add(shadow);
        _backGroup.Children.Add(bloom);
        backRoot.Children.Add(_backGroup);

        _glowHost.Children.Add(bottomGlow);
        _glowHost.Children.Add(categoryGlow);
        _frontGroup.Children.Add(fill);
        _frontGroup.Children.Add(_glowHost);
        _frontGroup.Children.Add(edge);
        _frontGroup.Children.Add(rim);
        _frontRoot = frontRoot;
        frontRoot.Children.Add(_frontGroup);

        _contentsHost.Children.Add(Contents);
        _contentsHost.Children.Add(Pill);
        _contentsHost.Children.Add(Search);
        _contentsHost.Children.Add(Notice);
        frontRoot.Children.Add(_contentsHost);

        // The drag overlay (the lifted tile and the drop zone) covers the window above everything and takes no mouse input.
        Drag = new DragView(windowWidth, windowHeight);
        Contents.PointerReference = Drag;
        frontRoot.Children.Add(Drag);
    }

    public ContentsLayer Contents { get; } = new();

    /// <summary>The notice's contents (WORK-ORDER-7 section 4); shown while the island is the notice.</summary>
    public NoticeView Notice { get; } = new();

    /// <summary>The capsule laid out for search (WORK-ORDER-7 section 3); shown instead of <see cref="Contents"/> while search is open.</summary>
    public SearchView Search { get; } = new();

    /// <summary>The small pill's contents (WORK-ORDER-7 section 2); shown instead of <see cref="Contents"/> while the island is the pill.</summary>
    public PillView Pill { get; } = new();

    /// <summary>
    /// Makes the view the pill or the capsule: the pill's contents or the capsule's, the pill's glass (a darker base), and its ring
    /// (<paramref name="litShare"/>: the part still to play, or null for the approved moving light in its place).
    /// </summary>
    public void SetPill(bool pill, double? litShare, bool notice = false)
    {
        notice &= pill; // the notice is a small shape too
        if (pill != _pill || notice != _notice)
        {
            _pill = pill;
            _notice = notice;
            _fill.BaseAlpha = notice ? NoticeGlassAlpha : pill ? PillLayout.GlassAlpha : _glassBase;
            _fill.InvalidateVisual();
        }

        ApplyLayers();
        var ring = pill && !notice ? litShare : null;
        _rim.SetRing(ring);
        _rim.SetNoticeEdge(notice);
        _bloom.ShowArc = ring is null && !notice;
    }

    /// <summary>The notice's glass is the small pill's, at 72%.</summary>
    private const double NoticeGlassAlpha = 0.72;

    /// <summary>How many times each layer of the capsule was rendered, in drawing order (for the measuring run and the self-test's check that nothing is drawn again for nothing).</summary>
    public IReadOnlyList<(string Name, long Renders)> LayerRenders() => [.. _layers.Select(l => (l.LayerName, l.Renders))];

    /// <summary>The lifted tile and the drop zone of a drag (WORK-ORDER-5 §6).</summary>
    public DragView Drag { get; }

    /// <summary>Switches the glass: the opacity of the dark base (the approved 0.60 or the darker 0.70). Takes effect on the next frame.</summary>
    public void SetGlassAlpha(double alpha)
    {
        _glassBase = alpha;
        _fill.BaseAlpha = _notice ? NoticeGlassAlpha : _pill ? PillLayout.GlassAlpha : alpha;
        _fill.InvalidateVisual();
    }

    /// <summary>Search open or not: the capsule's contents give way to the search view and come back.</summary>
    public void SetSearch(bool search)
    {
        _search = search;
        ApplyLayers();
    }

    private void ApplyLayers()
    {
        Contents.Visibility = _pill || _search ? Visibility.Collapsed : Visibility.Visible;
        Pill.Visibility = _pill && !_notice ? Visibility.Visible : Visibility.Collapsed;
        Notice.Visibility = _notice ? Visibility.Visible : Visibility.Collapsed;
        Search.Visibility = _search && !_pill ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The mode's mark on the edge (WORK-ORDER-7 section 1): the glow behind it, the moving light, the page-coloured rim and the dashed one.
    /// <see cref="ModeMark.Look.Approved"/> is the approved look, drawn exactly as before. Takes effect on the next frame.
    /// </summary>
    private bool _gpuLight;

    /// <summary>
    /// True while the graphics-card light draws the whole of the moving light, the glow and the lit rim (WORK-ORDER-12 section 2): the rim and the bloom layers are then not drawn at all
    /// (collapsed, so they cost nothing), and drawn again from the frame they hold when it is switched off.
    /// </summary>
    public void SetGpuLight(bool on)
    {
        if (_gpuLight == on) return;
        _gpuLight = on;
        _bloom.Hidden = on;
        _rim.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (!on)
        {
            _rim.Redraw();
            _bloom.Redraw();
        }
    }

    public bool GpuLightOn => _gpuLight;

    public void SetModeLook(ModeMark.Look look)
    {
        _bloom.SetStrength(look.Glow);
        _rim.SetLook(look);
    }

    /// <summary>Turns the rim light off, so the glass body alone can be measured (the rim reaches 0.3 beyond the outer edge by design).</summary>
    public bool RimVisible
    {
        get => _rim.Visibility == Visibility.Visible;
        set => _rim.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Shows or empties the view. An empty view leaves every pixel of both windows at alpha 0, so
    /// nothing is drawn and nothing catches the mouse while the island is hidden.
    /// </summary>
    public void SetShown(bool shown)
    {
        var v = shown ? Visibility.Visible : Visibility.Collapsed;
        _backGroup.Visibility = _frontGroup.Visibility = _contentsHost.Visibility = v;
        if (!shown)
        {
            SetPill(false, null, false);
            SetSearch(false);
        }
        if (!shown) Drag.Clear();
        if (!shown) _backRoot.Clip = null;
        _framed = false; // the next frame puts the clips again
    }

    /// <summary>Draws one frame. <paramref name="stretch"/> is the travel stretch of the whole shape.</summary>
    private ShapeFrame _lastFrame;
    private (double X, double Y) _lastStretch;
    private bool _framed;

    public void Apply(in ShapeFrame frame, double stretchX = 1, double stretchY = 1)
    {
        foreach (var layer in _layers) layer.Update(frame);

        // The clips, the tops of the contents and the stretch are put again only when the shape or its stretch moved: a new clip makes the windows draw everything under it again
        // (WORK-ORDER-11 section 4). The moving light and the colour change none of them.
        if (_framed && _lastStretch == (stretchX, stretchY) && !Layer.ShapeMoved(_lastFrame, frame)) return;
        _lastFrame = frame;
        _lastStretch = (stretchX, stretchY);
        _framed = true;

        var padding = frame.PaddingBox();
        _glowHost.Clip = padding;
        _contentsHost.Clip = padding.CloneCurrentValue();
        Contents.SetTop(frame.Top);
        Pill.SetTop(frame.Top);
        Search.SetTop(frame.Top);
        Notice.SetTop(frame.Top);

        var centre = frame.Centre;
        foreach (var scale in new[] { _backScale, _frontScale })
        {
            scale.CenterX = centre.X;
            scale.CenterY = centre.Y;
            scale.ScaleX = stretchX;
            scale.ScaleY = stretchY;
        }

        // The shadow lies only outside the outline, and follows the same stretch.
        var hole = frame.Outline();
        hole.Transform = new ScaleTransform(stretchX, stretchY, centre.X, centre.Y);
        _backRoot.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, _width, _height)), hole);
    }

    private void Size(params Canvas[] canvases)
    {
        foreach (var c in canvases)
        {
            c.Width = _width;
            c.Height = _height;
        }
    }
}
