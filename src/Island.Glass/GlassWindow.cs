using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Glass;

/// <summary>How the glass window lets the mouse through. Measured in review/glass/glass.json ("click_through").</summary>
internal enum GlassClickThrough
{
    /// <summary>The blur probe's way (WO1 section 7): only WM_NCHITTEST answered with HTTRANSPARENT.</summary>
    HitTestOnly,

    /// <summary>Also WS_EX_TRANSPARENT.</summary>
    Transparent,

    /// <summary>Also WS_EX_TRANSPARENT and WS_EX_LAYERED (made fully opaque with SetLayeredWindowAttributes so it is drawn).</summary>
    LayeredTransparent,
}

/// <summary>The plain Win32 window that holds the composition tree. Values of constants are from winuser.h as listed on Microsoft Learn.</summary>
internal static unsafe class GlassWindow
{
    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
        WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    const uint WM_ACTIVATE = 0x6, WM_SETFOCUS = 0x7, WM_ERASEBKGND = 0x14, WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_NCACTIVATE = 0x86;
    const nint HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
    const int SW_HIDE = 0;
    const uint LWA_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] static extern ushort RegisterClassExW(ref WNDCLASSEXW c);
    [DllImport("user32.dll", SetLastError = true)] static extern nint CreateWindowExW(uint ex, nint cls, nint name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);

    static nint windowClass;

    /// <summary>Activation and focus messages the glass window has ever received; both must stay 0.</summary>
    internal static int Activations, FocusMessages;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint Proc(nint h, uint m, nint w, nint l)
    {
        switch (m)
        {
            case WM_NCHITTEST: return HTTRANSPARENT;
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
            case WM_ERASEBKGND: return 1;
            case WM_ACTIVATE: if ((w & 0xFFFF) != 0) Activations++; break;
            case WM_NCACTIVATE: if (w != 0) Activations++; break;
            case WM_SETFOCUS: FocusMessages++; break;
        }
        return DefWindowProcW(h, m, w, l);
    }

    /// <summary>A hidden, topmost, no-activate tool window with no redirection bitmap. 0 when Windows refuses.</summary>
    public static nint Create(GlassClickThrough mode)
    {
        if (windowClass == 0)
        {
            var name = Marshal.StringToHGlobalUni("IslandGlassLayer");   // lives for the process, as the class does
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&Proc,
                hInstance = GetModuleHandleW(0), lpszClassName = name,
            };
            if (RegisterClassExW(ref wc) == 0) return 0;
            windowClass = name;
        }

        uint ex = WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP;
        if (mode != GlassClickThrough.HitTestOnly) ex |= WS_EX_TRANSPARENT;
        if (mode == GlassClickThrough.LayeredTransparent) ex |= WS_EX_LAYERED;
        var hwnd = CreateWindowExW(ex, windowClass, 0, WS_POPUP, 0, 0, 1, 1, 0, 0, GetModuleHandleW(0), 0);
        if (hwnd != 0 && mode == GlassClickThrough.LayeredTransparent && !SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA))
        {
            DestroyWindow(hwnd);
            return 0;
        }
        return hwnd;
    }

    /// <summary>The anchor window's screen rectangle in physical pixels and its DPI; false when it is gone.</summary>
    public static bool TryAnchor(nint anchor, out RECT r, out uint dpi)
    {
        dpi = 96;
        if (!GetWindowRect(anchor, out r)) return false;
        var d = GetDpiForWindow(anchor);
        if (d != 0) dpi = d;
        return true;
    }

    /// <summary>Covers the anchor's rectangle, directly beneath the anchor in z-order, shown without activation.</summary>
    public static void PlaceBelow(nint glass, nint anchor, RECT r) =>
        SetWindowPos(glass, anchor, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER);

    [DllImport("user32.dll")] static extern nint GetWindow(nint h, uint cmd);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint h, nint region, bool redraw);
    [DllImport("user32.dll")] static extern int GetWindowRgn(nint h, nint region);
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] static extern nint CreatePolygonRgn([In] POINT[] points, int count, int fillMode);
    [DllImport("gdi32.dll")] static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    const int RGN_DIFF = 4;
    const int RGN_ERROR = 0;

    const uint GW_HWNDPREV = 3;
    static readonly nint HwndTopmost = -1;

    /// <summary>Covers the anchor's rectangle, directly above the anchor in z-order (above the window that was above it, which is directly above it), shown without activation.</summary>
    public static void PlaceAbove(nint window, nint anchor, RECT r)
    {
        // The window that lies just above the anchor; the new window goes in front of nothing but the anchor: its own place is "after" that one. At the top, the topmost group's top.
        // When that window is this one, it is already where it should be: asking Windows to put a window after itself is left out (only its place and size are set).
        var above = GetWindow(anchor, GW_HWNDPREV);
        var flags = SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOOWNERZORDER | (above == window ? SWP_NOZORDER : 0);
        SetWindowPos(window, above != 0 ? above : HwndTopmost, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, flags);
    }

    /// <summary>True when the window lies directly above the anchor in z-order.</summary>
    public static bool IsDirectlyAbove(nint window, nint anchor) => GetWindow(anchor, GW_HWNDPREV) == window;

    /// <summary>
    /// Cuts a rounded rectangle out of the window (in the window's own pixels): what is drawn inside it is not shown. The glow of the light is cut out of the island's inside this way, as the old
    /// bloom is (it is drawn only outside the capsule's outline); the glass of the island is above it and would show it through.
    /// </summary>
    public static void CutOutRoundedRectangle(nint window, double x, double y, double width, double height, double radiusX, double radiusY)
    {
        if (!GetWindowRect(window, out var rect)) return;
        var whole = CreateRectRgn(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
        // A polygon whose vertices lie on the true outline (WORK-ORDER-13, P30 and P31): GDI's round-rectangle region draws its corners up to 1.3 px inside the arc, and cannot be elliptical in one corner pair only.
        var polygon = HoleOutline.Polygon(x, y, x + width, y + height, radiusX, radiusY);
        var hole = CreatePolygonRgn(polygon.Select(p => new POINT { X = p.X, Y = p.Y }).ToArray(), polygon.Count, 1 /* ALTERNATE */); // the right and the bottom edge of a region are not part of it
        try
        {
            if (whole == 0 || hole == 0 || CombineRgn(whole, whole, hole, RGN_DIFF) == RGN_ERROR) return;
            if (SetWindowRgn(window, whole, false) != 0) whole = 0; // the system owns the region now
        }
        finally
        {
            if (whole != 0) DeleteObject(whole);
            if (hole != 0) DeleteObject(hole);
        }
    }

    /// <summary>For the self-test: whether the point (in the window's own pixels) is inside the window's region; true when the window has none.</summary>
    public static bool RegionContains(nint window, int x, int y)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            return GetWindowRgn(window, region) == RGN_ERROR || PtInRegion(region, x, y);
        }
        finally
        {
            DeleteObject(region);
        }
    }

    public static void Hide(nint glass) => ShowWindow(glass, SW_HIDE);

    public static void Destroy(nint glass) => DestroyWindow(glass);
}
