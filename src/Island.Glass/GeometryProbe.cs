using System.Runtime.InteropServices;

namespace Island.Glass;

/// <summary>
/// Asks Direct2D itself where its rounded rectangle geometry starts and which way it runs (WORK-ORDER-12 section 2). Microsoft Learn does not say where the system compositor's rounded
/// rectangle path starts or which way it runs, and the graphics-card light needs both. The compositor draws its shapes with Direct2D geometry, so what Direct2D's own rounded rectangle does
/// is the best evidence there is without a picture of the screen (which may not be taken): this is that evidence, a measurement of a different API, not a proof about the compositor's.
/// Direct2D's factory, <c>ID2D1Factory::CreateRoundedRectangleGeometry</c> and <c>ID2D1Geometry::ComputeLength</c> / <c>ComputePointAtLength</c> are on Microsoft Learn; the order of their slots in the
/// tables is from d2d1.h as remembered and is proven here by the length that comes back being the rounded rectangle's own perimeter. Used only by the self-test.
/// </summary>
public static unsafe class GeometryProbe
{
    [DllImport("d2d1.dll")]
    private static extern int D2D1CreateFactory(int factoryType, in Guid riid, nint options, out nint factory);

    private static readonly Guid IidFactory = new("06152247-6F50-465A-9245-118BFD3B6007");

    /// <summary>What Direct2D says about a rounded rectangle of this size and corner radius, in the rectangle's own units with its top left at (0, 0).</summary>
    public sealed record Result(bool Worked, double Length, double ExpectedLength, double StartX, double StartY, double NextX, double NextY, double TangentX, double TangentY)
    {
        /// <summary>The probe's own slot order is right: the length that came back is the perimeter of the rounded rectangle.</summary>
        public bool SlotsProven => Worked && Math.Abs(Length - ExpectedLength) < 0.05;
    }

    public static Result Probe(float width, float height, float radius)
    {
        var expected = 2 * (width - 2 * radius) + 2 * (height - 2 * radius) + 2 * Math.PI * radius;
        var none = new Result(false, 0, expected, 0, 0, 0, 0, 0, 0);
        nint factory = 0, geometry = 0;
        try
        {
            if (D2D1CreateFactory(0, in IidFactory, 0, out factory) < 0 || factory == 0) return none; // D2D1_FACTORY_TYPE_SINGLE_THREADED
            var rect = stackalloc float[6] { 0, 0, width, height, radius, radius }; // D2D1_ROUNDED_RECT: left, top, right, bottom, radiusX, radiusY
            var createRounded = (delegate* unmanaged[Stdcall]<nint, float*, nint*, int>)(*(void***)factory)[6];
            if (createRounded(factory, rect, &geometry) < 0 || geometry == 0) return none;

            var table = *(void***)geometry;
            var computeLength = (delegate* unmanaged[Stdcall]<nint, float*, float, float*, int>)table[14];
            var computePoint = (delegate* unmanaged[Stdcall]<nint, float, float*, float, float*, float*, int>)table[15];
            float length;
            if (computeLength(geometry, null, 0.01f, &length) < 0) return none;
            var p0 = stackalloc float[2];
            var t0 = stackalloc float[2];
            var p1 = stackalloc float[2];
            var t1 = stackalloc float[2];
            if (computePoint(geometry, 0, null, 0.01f, p0, t0) < 0) return none;
            if (computePoint(geometry, Math.Min(length / 2, Math.Max(1f, width / 8)), null, 0.01f, p1, t1) < 0) return none;
            return new Result(true, length, expected, p0[0], p0[1], p1[0], p1[1], t0[0], t0[1]);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or AccessViolationException)
        {
            return none;
        }
        finally
        {
            if (geometry != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)geometry)[2])(geometry);
            if (factory != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)factory)[2])(factory);
        }
    }
}
