using System.Numerics;
using System.Runtime.InteropServices;
using Island.Core;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.UI.ViewManagement;

namespace Island.Glass;

/// <summary>
/// The blurred glass under the island (WORK-ORDER-3 section 8): a window of its own, directly beneath the island's window in
/// z-order and covering the same rectangle, that shows what is behind it blurred, only inside the capsule's rounded shape.
/// Uses the plain backdrop brush, the method review/blur/blur.json records as YES on this laptop (the host backdrop brush gave
/// black). Never takes focus, never catches the mouse.
///
/// Threading: create it, and call every member, on the one UI thread that owns the anchor window (WPF's dispatcher thread).
/// Creating it never throws: when blur cannot be had, <see cref="IsAvailable"/> is false and <see cref="UnavailableReason"/>
/// holds a short code. Every member is then a no-op.
/// </summary>
public sealed class GlassLayer : IGlassLayer, IDisposable
{
    /// <summary>The reference's blurred glass (island-previews.html, ".isl-b .body,.isl-d .body"): blur(18px) saturate(190%) brightness(1.08).</summary>
    public const double BlurDip = 18;
    public const float Saturation = 1.9f;
    public const float Brightness = 1.08f;

    private readonly nint _anchor;
    private readonly List<object> _keepAlive = [];
    private UISettings? _settings;
    private nint _hwnd;
    private DesktopWindowTarget? _target;
    private SpriteVisual? _sprite;
    private CompositionRoundedRectangleGeometry? _geometry;
    private string? _failure;

    private bool _shown;
    private float _scale = 1;
    private double _left, _top, _width, _height, _radius;
    private Vector2 _offsetPx, _sizePx, _radiusPx;

    /// <param name="anchorWindow">The island's own window (in WPF: <c>new WindowInteropHelper(window).Handle</c>, after SourceInitialized).</param>
    public GlassLayer(nint anchorWindow)
        : this(anchorWindow, GlassClickThrough.LayeredTransparent)
    {
    }

    internal GlassLayer(nint anchorWindow, GlassClickThrough clickThrough)
    {
        _anchor = anchorWindow;
        try
        {
            Build(clickThrough);
        }
        catch (Exception e)
        {
            _failure ??= "TREE_FAILED";
            UnavailableHResult = e.HResult;
            TearDown();
        }
    }

    /// <summary>False when transparency effects are off in Windows (read, never changed) or the layer could not be made.</summary>
    public bool IsAvailable => _sprite != null && TransparencyState() == null;

    /// <summary>
    /// Null when available. Otherwise one of: ANCHOR_INVALID, SETTINGS_UNREADABLE, NO_COMPOSITOR, WINDOW_FAILED, TREE_FAILED
    /// (fixed for the layer's life), or TRANSPARENCY_OFF (follows the Windows setting live).
    /// </summary>
    public string? UnavailableReason => _failure ?? TransparencyState();

    /// <summary>The HRESULT behind a failure, when there was one; 0 otherwise. A number, never message text.</summary>
    public int UnavailableHResult { get; private set; }

    /// <summary>True between <see cref="Show"/> and <see cref="Hide"/>.</summary>
    public bool IsShown => _shown;

    internal nint WindowHandle => _hwnd;

    /// <summary>
    /// Reads the anchor's rectangle and DPI, applies the last shape given to <see cref="Follow"/> and shows the layer beneath the
    /// anchor. Call it when the island appears (and again if the island's window moved or changed monitor).
    /// </summary>
    public void Show()
    {
        if (!IsAvailable || !GlassWindow.TryAnchor(_anchor, out var r, out var dpi)) return;
        _scale = dpi / 96f;
        _sprite!.Size = new Vector2(r.Right - r.Left, r.Bottom - r.Top);
        _shown = true;
        Apply();
        GlassWindow.PlaceBelow(_hwnd, _anchor, r);
    }

    /// <summary>
    /// The capsule's shape this frame, in device-independent pixels from the anchor window's top-left. Allocation-free; while the
    /// layer is hidden it only remembers the values.
    /// </summary>
    public void Follow(double left, double top, double width, double height, double cornerRadius)
    {
        _left = left;
        _top = top;
        _width = width;
        _height = height;
        _radius = cornerRadius;
        if (_shown) Apply();
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        GlassWindow.Hide(_hwnd);
    }

    public void Dispose()
    {
        _shown = false;
        TearDown();
    }

    private void Apply()
    {
        var w = (float)Math.Max(0, _width) * _scale;
        var h = (float)Math.Max(0, _height) * _scale;
        var rad = (float)Math.Clamp(_radius, 0, Math.Min(Math.Max(0, _width), Math.Max(0, _height)) / 2) * _scale;
        var offset = new Vector2((float)_left * _scale, (float)_top * _scale);
        var size = new Vector2(w, h);
        var radius = new Vector2(rad, rad);
        var g = _geometry!;
        if (offset != _offsetPx) g.Offset = _offsetPx = offset;
        if (size != _sizePx) g.Size = _sizePx = size;
        if (radius != _radiusPx) g.CornerRadius = _radiusPx = radius;
    }

    private string? TransparencyState()
    {
        if (_settings == null) return "SETTINGS_UNREADABLE";
        try
        {
            return _settings.AdvancedEffectsEnabled ? null : "TRANSPARENCY_OFF";
        }
        catch (COMException)
        {
            return "SETTINGS_UNREADABLE";
        }
    }

    private void Build(GlassClickThrough clickThrough)
    {
        if (_anchor == 0 || !GlassWindow.IsWindow(_anchor) || !GlassWindow.TryAnchor(_anchor, out _, out var dpi))
        {
            _failure = "ANCHOR_INVALID";
            return;
        }

        _failure = "SETTINGS_UNREADABLE";
        _settings = new UISettings();

        _failure = "NO_COMPOSITOR";
        var c = GlassCompositor.ForThisThread();

        _failure = "WINDOW_FAILED";
        _hwnd = GlassWindow.Create(clickThrough);
        if (_hwnd == 0)
        {
            UnavailableHResult = Marshal.GetHRForLastWin32Error();
            return;
        }

        _failure = "TREE_FAILED";
        _target = GlassCompositor.CreateTarget(_hwnd);
        var brush = GlassCompositor.CreateGlassBrush(c, (float)(BlurDip * dpi / 96.0), Saturation, Brightness, _keepAlive);
        brush.SetSourceParameter("backdrop", c.CreateBackdropBrush());
        _geometry = c.CreateRoundedRectangleGeometry();   // size 0: nothing is visible until the first Follow
        var sprite = c.CreateSpriteVisual();
        sprite.Brush = brush;
        sprite.Clip = c.CreateGeometricClip(_geometry);
        _target.Root = sprite;
        _sprite = sprite;
        _failure = null;
    }

    private void TearDown()
    {
        _sprite = null;
        if (_target != null)
        {
            try
            {
                _target.Root = null;
                _target.Dispose();
            }
            catch (COMException)
            {
                // the window is going anyway; nothing to recover
            }
            _target = null;
        }
        if (_hwnd != 0) GlassWindow.Destroy(_hwnd);
        _hwnd = 0;
    }
}
