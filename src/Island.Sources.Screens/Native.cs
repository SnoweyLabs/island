using System.Runtime.InteropServices;

namespace Island.Sources.Screens;

/// <summary>
/// The Win32 calls that only read the screens and the pointer. Every name, signature and constant was checked on
/// Microsoft Learn on 6 Oct 2026 (GetCursorPos, EnumDisplayMonitors, MONITORENUMPROC, GetMonitorInfoW, MONITORINFO,
/// GetDpiForMonitor, MONITOR_DPI_TYPE). Nothing here moves, shows or changes anything.
/// </summary>
internal static class Native
{
    /// <summary>MONITORINFOF_PRIMARY.</summary>
    public const uint MonitorInfoFPrimary = 1;

    /// <summary>MDT_EFFECTIVE_DPI: the value that includes the scale factor the user set for that display.</summary>
    public const int MdtEffectiveDpi = 0;

    /// <summary>USER_DEFAULT_SCREEN_DPI: 100% scaling.</summary>
    public const double DefaultDpi = 96.0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;
    }

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, nint lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc callback, nint dwData);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}
