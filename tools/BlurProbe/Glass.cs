using System.Numerics;
using System.Runtime.InteropServices;
using Island.Glass;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace BlurProbe;

/// <summary>The system compositor (Windows.UI.Composition), created once on the UI thread.</summary>
internal static unsafe class GlassHost
{
    [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions o, out IntPtr controller);

    const int DQTYPE_THREAD_CURRENT = 2, DQTAT_COM_NONE = 0;   // values from Microsoft Learn (dispatcherqueue.h enums)
    static readonly Guid IidDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");   // from the Avalonia IDL; not found on Learn: UNVERIFIED there, proven by working

    public static Compositor Compositor = null!;
    static IntPtr interop;
    static IntPtr dispatcherQueueController;
    public static readonly List<object> KeepAlive = new();   // projected objects whose native twins are referenced from raw memory

    public static void Init()
    {
        var o = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = DQTYPE_THREAD_CURRENT, ApartmentType = DQTAT_COM_NONE };
        int hr = CreateDispatcherQueueController(o, out dispatcherQueueController);
        if (hr < 0) throw new COMException("CreateDispatcherQueueController failed", hr);
        Compositor = new Compositor();
        var unk = MarshalInspectable<Compositor>.FromManaged(Compositor);
        hr = Marshal.QueryInterface(unk, in IidDesktopInterop, out interop);
        Marshal.Release(unk);
        if (hr < 0) throw new COMException("compositor has no ICompositorDesktopInterop", hr);
    }

    public static DesktopWindowTarget CreateTarget(IntPtr hwnd, bool topmost)
    {
        IntPtr result;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)(*(void***)interop)[3];
        int hr = fn(interop, hwnd, topmost ? 1 : 0, &result);
        if (hr < 0) throw new COMException("CreateDesktopWindowTarget failed", hr);
        var target = MarshalInspectable<DesktopWindowTarget>.FromAbi(result);
        Marshal.Release(result);
        return target;
    }

    /// <summary>Blur of "backdrop" with a Gaussian of the given standard deviation (device pixels), hard border mode.</summary>
    public static CompositionEffectBrush CreateBlurBrush(float sigma)
    {
        var param = new CompositionEffectSourceParameter("backdrop");
        KeepAlive.Add(param);
        var unk = MarshalInspectable<object>.FromManaged(param);
        var iidSource = NativeEffect.IidGraphicsEffectSource;
        int hr = Marshal.QueryInterface(unk, in iidSource, out var source);
        Marshal.Release(unk);
        if (hr < 0) throw new COMException("source parameter is not an IGraphicsEffectSource", hr);

        // D2D1 Gaussian blur properties, in order: standard deviation, optimization (1 = balanced), border mode (1 = hard)
        var native = NativeEffect.Create(NativeEffect.ClsidGaussianBlur, new object[] { sigma, 1u, 1u }, new[] { source });
        var effect = MarshalInterface<IGraphicsEffect>.FromAbi(native);
        KeepAlive.Add(effect);
        var factory = Compositor.CreateEffectFactory(effect);
        KeepAlive.Add(factory);
        return factory.CreateBrush();
    }
}

/// <summary>One probe window's composition tree: a sprite filled with the blurred backdrop, clipped to a rounded rectangle.</summary>
internal sealed class Glass : IDisposable
{
    readonly DesktopWindowTarget target;
    readonly SpriteVisual sprite;
    readonly CompositionRoundedRectangleGeometry geometry;

    readonly IntPtr hwnd;

    /// <summary>Sets the window attribute that lets a desktop window use host backdrop brushes (Windows 11 build 22000 and later).</summary>
    public void ApplyHostAttribute()
    {
        int on = 1;
        int hr = Win.DwmSetWindowAttribute(hwnd, Win.DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));
        if (hr < 0) throw new COMException("DwmSetWindowAttribute(DWMWA_USE_HOSTBACKDROPBRUSH) failed", hr);
    }

    /// <param name="attributeFirst">host brush only: true sets the attribute before the tree is built, false leaves it to the caller (ApplyHostAttribute).</param>
    public Glass(IntPtr hwnd, int width, int height, bool hostBackdrop, float sigma, bool attributeFirst = true)
    {
        this.hwnd = hwnd;
        var c = GlassHost.Compositor;
        if (hostBackdrop && attributeFirst) ApplyHostAttribute();
        target = GlassHost.CreateTarget(hwnd, topmost: true);
        CompositionBackdropBrush backdrop = hostBackdrop ? c.CreateHostBackdropBrush() : c.CreateBackdropBrush();
        var blur = GlassHost.CreateBlurBrush(sigma);
        blur.SetSourceParameter("backdrop", backdrop);

        sprite = c.CreateSpriteVisual();
        sprite.Size = new Vector2(width, height);
        sprite.Brush = blur;
        geometry = c.CreateRoundedRectangleGeometry();
        sprite.Clip = c.CreateGeometricClip(geometry);
        target.Root = sprite;
    }

    /// <summary>Moves and resizes the clip, in the probe window's own pixels. The window itself never changes.</summary>
    public void SetShape(Shape s)
    {
        geometry.Offset = new Vector2((float)s.X, (float)s.Y);
        geometry.Size = new Vector2((float)s.W, (float)s.H);
        geometry.CornerRadius = new Vector2((float)s.R, (float)s.R);
    }

    public void Dispose()
    {
        target.Root = null;
        target.Dispose();
    }
}
