using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Island.Glass;

/// <summary>
/// The system compositor (Windows.UI.Composition, not the Windows App SDK), one per UI thread. It needs a DispatcherQueue on
/// that thread; any thread that pumps window messages (WPF's does) serves one.
/// </summary>
internal static unsafe class GlassCompositor
{
    [StructLayout(LayoutKind.Sequential)] struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] static extern int CreateDispatcherQueueController(DispatcherQueueOptions o, out IntPtr controller);

    const int DQTYPE_THREAD_CURRENT = 2, DQTAT_COM_NONE = 0;   // values from Microsoft Learn (dispatcherqueue.h enums)
    static readonly Guid IidDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");   // from the Avalonia IDL; not on Learn: UNVERIFIED there, proven by working (WO1 section 7)

    [ThreadStatic] static Compositor? compositor;
    [ThreadStatic] static IntPtr interop;

    /// <summary>The thread's compositor, made on first use. Throws <see cref="COMException"/> when Windows refuses.</summary>
    public static Compositor ForThisThread()
    {
        if (compositor != null) return compositor;
        if (Windows.System.DispatcherQueue.GetForCurrentThread() == null)
        {
            var o = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = DQTYPE_THREAD_CURRENT, ApartmentType = DQTAT_COM_NONE };
            // The controller is never released: the queue must live as long as the thread's compositor, which is the whole run.
            int hr = CreateDispatcherQueueController(o, out _);
            if (hr < 0) throw new COMException("dispatcher queue", hr);
        }
        var c = new Compositor();
        var unk = MarshalInspectable<Compositor>.FromManaged(c);
        int qi = Marshal.QueryInterface(unk, in IidDesktopInterop, out var desktop);
        Marshal.Release(unk);
        if (qi < 0) throw new COMException("desktop interop", qi);
        interop = desktop;
        compositor = c;
        return c;
    }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget (slot 3).</summary>
    public static DesktopWindowTarget CreateTarget(IntPtr hwnd)
    {
        ForThisThread();
        IntPtr result;
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)(*(void***)interop)[3];
        int hr = fn(interop, hwnd, 1, &result);
        if (hr < 0) throw new COMException("desktop window target", hr);
        var target = MarshalInspectable<DesktopWindowTarget>.FromAbi(result);
        Marshal.Release(result);
        return target;
    }

    /// <summary>
    /// "backdrop" blurred with a Gaussian of <paramref name="sigmaPx"/> (hard border mode), then a colour matrix that does
    /// CSS saturate() and brightness() in one pass. Feed it with <c>SetSourceParameter("backdrop", ...)</c>.
    /// </summary>
    public static CompositionEffectBrush CreateGlassBrush(Compositor c, float sigmaPx, float saturation, float brightness, List<object> keepAlive)
    {
        var param = new CompositionEffectSourceParameter("backdrop");
        keepAlive.Add(param);
        var unk = MarshalInspectable<object>.FromManaged(param);
        var iidSource = NativeEffect.IidGraphicsEffectSource;
        int hr = Marshal.QueryInterface(unk, in iidSource, out var source);
        Marshal.Release(unk);
        if (hr < 0) throw new COMException("effect source parameter", hr);

        // D2D1 Gaussian blur properties, in order: standard deviation, optimization (1 = balanced), border mode (1 = hard)
        var blur = NativeEffect.Create(NativeEffect.ClsidGaussianBlur, new object[] { sigmaPx, 1u, 1u }, new[] { source });
        // D2D1 colour matrix properties, in order: the 5x4 matrix, alpha mode (1 = premultiplied), clamp output
        var colour = NativeEffect.Create(NativeEffect.ClsidColorMatrix, new object[] { SaturateBrighten(saturation, brightness), 1u, true },
            new[] { NativeEffect.AsSource(blur) });
        var effect = MarshalInterface<Windows.Graphics.Effects.IGraphicsEffect>.FromAbi(colour);
        keepAlive.Add(effect);
        var factory = c.CreateEffectFactory(effect);
        keepAlive.Add(factory);
        return factory.CreateBrush();
    }

    /// <summary>
    /// A Gaussian blur of whatever is given as the source parameter <paramref name="sourceName"/> (a surface brush of a visual surface, for the glow of the moving light), with the standard
    /// deviation <paramref name="sigmaPx"/> and the same properties and hard border as the glass's blur. Feed it with <c>SetSourceParameter(sourceName, ...)</c>.
    /// </summary>
    public static CompositionEffectBrush CreateBlurBrush(Compositor c, string sourceName, float sigmaPx, List<object> keepAlive)
    {
        var param = new CompositionEffectSourceParameter(sourceName);
        keepAlive.Add(param);
        var unk = MarshalInspectable<object>.FromManaged(param);
        var iidSource = NativeEffect.IidGraphicsEffectSource;
        int hr = Marshal.QueryInterface(unk, in iidSource, out var source);
        Marshal.Release(unk);
        if (hr < 0) throw new COMException("effect source parameter", hr);

        var blur = NativeEffect.Create(NativeEffect.ClsidGaussianBlur, new object[] { sigmaPx, 1u, 1u }, new[] { source });
        var effect = MarshalInterface<Windows.Graphics.Effects.IGraphicsEffect>.FromAbi(blur);
        keepAlive.Add(effect);
        var factory = c.CreateEffectFactory(effect);
        keepAlive.Add(factory);
        return factory.CreateBrush();
    }

    /// <summary>
    /// CSS saturate(s) followed by brightness(b) (Filter Effects Module, the saturate matrix) as a D2D1_MATRIX_5X4_F:
    /// row i is what input channel i (R, G, B, A, then the constant row) adds to each output channel R, G, B, A.
    /// </summary>
    internal static float[] SaturateBrighten(float s, float b) => new[]
    {
        b * (0.213f + 0.787f * s), b * (0.213f - 0.213f * s), b * (0.213f - 0.213f * s), 0,
        b * (0.715f - 0.715f * s), b * (0.715f + 0.285f * s), b * (0.715f - 0.715f * s), 0,
        b * (0.072f - 0.072f * s), b * (0.072f - 0.072f * s), b * (0.072f + 0.928f * s), 0,
        0, 0, 0, 1,
        0, 0, 0, 0,
    };
}
