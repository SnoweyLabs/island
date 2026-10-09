using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BlurProbe;

/// <summary>Plain Win32 plumbing: the two windows, a message pump, the foreground check and the one permitted capture.</summary>
internal static unsafe partial class Win
{
    // ---- constants (all from winuser.h / wingdi.h; the ones that matter are checked in README.md) ----
    const uint WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, LWA_ALPHA = 2;
    const uint WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    const uint WM_ACTIVATE = 0x6, WM_SETFOCUS = 0x7, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_ERASEBKGND = 0x14,
        WM_MOUSEACTIVATE = 0x21, WM_NCACTIVATE = 0x86, WM_NCHITTEST = 0x84;
    const nint HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
    const int SW_SHOWNOACTIVATE = 4, SW_SHOW = 5;
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    static readonly nint HWND_TOPMOST = -1;
    const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_REMOTESESSION = 0x1000;
    const uint GA_ROOT = 2;
    const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    const uint DESKTOP_SWITCHDESKTOP = 0x100;
    public const int DWMWA_USE_HOSTBACKDROPBRUSH = 17; // value from the DWMWINDOWATTRIBUTE list on Microsoft Learn (counted from DWMWA_NCRENDERING_ENABLED = 1)

    // ---- state read by the window procedures ----
    public static int StripePx = 20;
    public static int ShiftPx;                 // pattern is moved left by this many pixels
    public static bool PatternClosed;
    public static int ProbeActivations;        // WM_ACTIVATE / WM_NCACTIVATE with "active", counted on the probe window
    public static int ProbeFocusMessages;      // WM_SETFOCUS on the probe window

    public static nint Pattern, Probe;
    public static nint Anchor;                 // glass mode: the stand-in for the island's own window

    // ---- P/Invoke ----
    [StructLayout(LayoutKind.Sequential)] struct WNDCLASSEXW
    {
        public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct PAINTSTRUCT
    {
        public nint hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern ushort RegisterClassExW(ref WNDCLASSEXW c);
    [DllImport("user32.dll")] static extern nint CreateWindowExW(uint ex, nint cls, nint name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG m, nint h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern nint DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint GetFocus();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] static extern bool InvalidateRect(nint h, nint rect, bool erase);
    [DllImport("user32.dll")] static extern bool UpdateWindow(nint h);
    [DllImport("user32.dll")] static extern nint BeginPaint(nint h, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(nint h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern int FillRect(nint dc, ref RECT r, nint brush);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(nint h, ref POINT p);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] static extern bool AdjustWindowRectExForDpi(ref RECT r, uint style, bool menu, uint ex, uint dpi);
    [DllImport("user32.dll")] static extern nint GetDC(nint h);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint h, nint dc);
    [DllImport("user32.dll")] static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] static extern bool CloseDesktop(nint d);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint GetStockObject(int i);
    [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    // ---- window procedures ----
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint PatternProc(nint h, uint m, nint w, nint l)
    {
        switch (m)
        {
            case WM_ERASEBKGND: return 1;
            case WM_PAINT: PaintPattern(h); return 0;
            case WM_CLOSE: PatternClosed = true; break;
        }
        return DefWindowProcW(h, m, w, l);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint ProbeProc(nint h, uint m, nint w, nint l)
    {
        switch (m)
        {
            case WM_NCHITTEST: return HTTRANSPARENT;       // clicks fall through to what is below
            case WM_MOUSEACTIVATE: return MA_NOACTIVATE;
            case WM_ERASEBKGND: return 1;
            case WM_ACTIVATE: if ((w & 0xFFFF) != 0) ProbeActivations++; break;
            case WM_NCACTIVATE: if (w != 0) ProbeActivations++; break;
            case WM_SETFOCUS: ProbeFocusMessages++; break;
        }
        return DefWindowProcW(h, m, w, l);
    }

    static void PaintPattern(nint h)
    {
        var dc = BeginPaint(h, out var ps);
        GetClientRect(h, out var c);
        var black = GetStockObject(4);   // BLACK_BRUSH
        var white = GetStockObject(0);   // WHITE_BRUSH
        var all = c; FillRect(dc, ref all, black);
        int s = StripePx, period = 2 * s;
        for (int left = -ShiftPx - period; left < c.Right; left += period)
        {
            var r = new RECT { Left = Math.Max(left, 0), Top = 0, Right = Math.Min(left + s, c.Right), Bottom = c.Bottom };
            if (r.Right > r.Left) FillRect(dc, ref r, white);
        }
        EndPaint(h, ref ps);
    }

    static nint Register(string name, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> proc)
    {
        var namePtr = Marshal.StringToHGlobalUni(name);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = (nint)proc,
            hInstance = GetModuleHandleW(0), lpszClassName = namePtr
        };
        if (RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("RegisterClassExW failed, error " + Marshal.GetLastWin32Error());
        return namePtr;
    }

    // ---- the two windows ----
    public static (int clientX, int clientY, int clientW, int clientH) CreatePattern(int wantW, int wantH)
    {
        var cls = Register("BlurProbePattern", &PatternProc);
        int sw = GetSystemMetrics(SM_CXSCREEN), sh = GetSystemMetrics(SM_CYSCREEN);
        int cw = Math.Min(wantW, sw - 160), ch = Math.Min(wantH, sh - 200);
        uint style = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        uint dpi = GetDpiForSystem();
        var r = new RECT { Left = 0, Top = 0, Right = cw, Bottom = ch };
        AdjustWindowRectExForDpi(ref r, style, false, 0, dpi);
        int x = (sw - (r.Right - r.Left)) / 2, y = (sh - (r.Bottom - r.Top)) / 2;
        Pattern = CreateWindowExW(0, cls, Marshal.StringToHGlobalUni("BlurProbe pattern"), style, x, y, r.Right - r.Left, r.Bottom - r.Top, 0, 0, GetModuleHandleW(0), 0);
        if (Pattern == 0) throw new InvalidOperationException("CreateWindowExW (pattern) failed");
        ShowWindow(Pattern, SW_SHOW);
        UpdateWindow(Pattern);
        var (px, py, pw, ph) = PatternClient();
        return (px, py, pw, ph);
    }

    public static (int x, int y, int w, int h) PatternClient()
    {
        GetClientRect(Pattern, out var c);
        var p = new POINT();
        ClientToScreen(Pattern, ref p);
        return (p.X, p.Y, c.Right, c.Bottom);
    }

    public static nint CreateProbe(int screenX, int screenY, int w, int h)
    {
        if (probeClass == 0) probeClass = Register("BlurProbeGlass", &ProbeProc);
        Probe = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP, probeClass, 0, WS_POPUP,
            screenX, screenY, w, h, 0, 0, GetModuleHandleW(0), 0);
        if (Probe == 0) throw new InvalidOperationException("CreateWindowExW (probe) failed, error " + Marshal.GetLastWin32Error());
        return Probe;
    }
    static nint probeClass;

    /// <summary>
    /// Glass mode: the stand-in for the island's window, which the glass layer sits beneath. Invisible and click-through
    /// (layered, alpha 0, transparent to the mouse), so that it never hides what the glass layer itself does.
    /// </summary>
    public static nint CreateAnchor(int screenX, int screenY, int w, int h)
    {
        if (probeClass == 0) probeClass = Register("BlurProbeGlass", &ProbeProc);
        Anchor = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT, probeClass, 0, WS_POPUP,
            screenX, screenY, w, h, 0, 0, GetModuleHandleW(0), 0);
        if (Anchor == 0) throw new InvalidOperationException("CreateWindowExW (anchor) failed, error " + Marshal.GetLastWin32Error());
        SetLayeredWindowAttributes(Anchor, 0, 0, LWA_ALPHA);
        ShowWindow(Anchor, SW_SHOWNOACTIVATE);
        SetWindowPos(Anchor, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        return Anchor;
    }

    public static void DestroyAnchor()
    {
        if (Anchor != 0) { DestroyWindow(Anchor); Anchor = 0; }
    }

    public static void ShowProbe()
    {
        ShowWindow(Probe, SW_SHOWNOACTIVATE);
        SetWindowPos(Probe, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public static void DestroyProbe()
    {
        if (Probe != 0) { DestroyWindow(Probe); Probe = 0; }
        Pump(50);
    }

    public static void DestroyAll()
    {
        DestroyProbe();
        DestroyAnchor();
        if (Pattern != 0) { DestroyWindow(Pattern); Pattern = 0; }
    }

    public static void SetShift(int px)
    {
        ShiftPx = px;
        InvalidateRect(Pattern, 0, false);
        UpdateWindow(Pattern);
    }

    // ---- pump and waits ----
    public static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        do
        {
            while (PeekMessageW(out var m, 0, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessageW(ref m); }
            Thread.Sleep(4);
        } while (Environment.TickCount64 < end);
    }

    public static bool WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            Pump(15);
            if (cond()) return true;
            if (Environment.TickCount64 > end) return false;
        }
    }

    // ---- focus ----
    public static bool PatternIsForeground() => GetForegroundWindow() == Pattern && Pattern != 0;
    public static bool PatternHasFocus() => GetFocus() == Pattern;

    // ---- facts about the machine ----
    public static uint SystemDpi => GetDpiForSystem();
    public static (int w, int h) ScreenPx => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    public static bool RemoteSession => GetSystemMetrics(SM_REMOTESESSION) != 0;

    /// <summary>False when the input desktop cannot be opened: the session is locked or a secure desktop is showing.</summary>
    public static bool InputDesktopAvailable()
    {
        var d = OpenInputDesktop(0, false, DESKTOP_SWITCHDESKTOP);
        if (d == 0) return false;
        CloseDesktop(d);
        return true;
    }

    // ---- the one permitted capture ----

    /// <summary>True when, at a grid of points in the pattern window's client rectangle, the window found there belongs to the pattern or probe window.</summary>
    public static bool OnlyOurWindowsInPatternRect()
    {
        var (x, y, w, h) = PatternClient();
        for (int gy = 0; gy < 7; gy++)
            for (int gx = 0; gx < 15; gx++)
            {
                int i = CaptureInsetPx;
                var p = new POINT { X = x + i + (w - 2 * i - 1) * gx / 14, Y = y + i + (h - 2 * i - 1) * gy / 6 };
                var top = WindowFromPoint(p);
                var root = top == 0 ? 0 : GetAncestor(top, GA_ROOT);
                if (root != Pattern && root != Probe && root != Anchor) return false;
            }
        return true;
    }

    public sealed record CaptureResult(bool Taken, string Reason, Pixels? Image);

    /// <summary>
    /// Copies the screen rectangle of the pattern window's client area, and nothing else. Refuses unless the pattern window is the
    /// foreground window with focus, is visible and not minimised, and only our own two windows are found in its rectangle.
    /// </summary>
    public static CaptureResult CapturePatternRect()
    {
        if (Pattern == 0) return new(false, "pattern window does not exist", null);
        if (!InputDesktopAvailable()) return new(false, "input desktop not available (screen locked or secure desktop)", null);
        if (IsIconic(Pattern) || !IsWindowVisible(Pattern)) return new(false, "pattern window is minimised or hidden", null);
        if (!PatternIsForeground()) return new(false, "pattern window is not the foreground window", null);
        if (!PatternHasFocus()) return new(false, "pattern window does not have focus", null);
        if (!OnlyOurWindowsInPatternRect()) return new(false, "another window overlaps the pattern rectangle", null);
        var (x, y, w, h) = PatternClient();
        // Windows 11 rounds the window's bottom corners, so the outermost pixels of the client rectangle can show whatever lies
        // behind the window. Those pixels are never read: only the rectangle inset by CaptureInsetPx is copied, and the frame
        // around it in the returned picture is a flat grey (128).
        int i = CaptureInsetPx, iw = w - 2 * i, ih = h - 2 * i;
        var screen = GetDC(0);
        var mem = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = iw, biHeight = -ih, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(screen, ref bmi, 0, out var bits, 0, 0);
        var old = SelectObject(mem, bmp);
        bool ok = BitBlt(mem, 0, 0, iw, ih, screen, x + i, y + i, SRCCOPY | CAPTUREBLT);
        var inner = new byte[iw * ih * 4];
        if (ok) Marshal.Copy(bits, inner, 0, inner.Length);
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(0, screen);
        if (!ok) return new(false, "BitBlt failed", null);
        var data = new byte[w * h * 4];
        Array.Fill(data, (byte)128);
        for (int row = 0; row < ih; row++) Buffer.BlockCopy(inner, row * iw * 4, data, ((row + i) * w + i) * 4, iw * 4);
        // everything must still be true after the copy, or the picture is thrown away
        if (!PatternIsForeground() || !OnlyOurWindowsInPatternRect()) return new(false, "foreground or overlap changed during the capture", null);
        return new(true, "", new Pixels(w, h, data));
    }

    /// <summary>Width of the frame around the photographed area, in pixels: 12 at 96 dpi, scaled with the system dpi.</summary>
    public static int CaptureInsetPx => (int)Math.Ceiling(12 * GetDpiForSystem() / 96.0);
}
