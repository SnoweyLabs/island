using System.Runtime.InteropServices;
using System.Text;

namespace Island.Sources.Front;

/// <summary>Windows calls used to READ what is in front. Nothing here changes a window or reaches the outside world.</summary>
internal static class FrontNative
{
    // Window styles (Microsoft Learn, "Window Styles" and "Extended Window Styles").
    public const long WsDlgFrame = 0x00400000;
    public const long WsThickFrame = 0x00040000;
    public const long WsExToolWindow = 0x00000080;

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint MonitorDefaultToNearest = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder exeName, ref uint size);

    public static bool TryWindowRect(nint window, out Rect rect)
    {
        rect = default;
        return window != 0 && GetWindowRect(window, out rect);
    }

    public static long Style(nint window) => GetWindowLongPtr(window, GwlStyle);

    public static long ExStyle(nint window) => GetWindowLongPtr(window, GwlExStyle);

    public static string ClassOf(nint window)
    {
        var text = new StringBuilder(256);
        return GetClassNameW(window, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    public static bool IsDesktopOrShell(nint window) =>
        window == GetDesktopWindow() || (GetShellWindow() is var shell && shell != 0 && window == shell);

    /// <summary>The full rectangle of the monitor that holds most of the rectangle, or null when Windows gives none.</summary>
    public static Rect? MonitorOf(Rect rect)
    {
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (monitor == 0) return null;
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) ? info.Monitor : null;
    }

    /// <summary>Windows' own answer to "may I notify now" (QUERY_USER_NOTIFICATION_STATE), or null when the call fails.</summary>
    public static int? NotificationState() => SHQueryUserNotificationState(out var state) == 0 ? state : null;

    /// <summary>The executable's file name (never its path) of the process that owns the window, or null when it cannot be read.</summary>
    public static string? ExeFileNameOf(nint window)
    {
        if (GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0) return null;
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return null;
        try
        {
            var text = new StringBuilder(1024);
            var size = (uint)text.Capacity;
            return QueryFullProcessImageNameW(process, 0, text, ref size) ? Path.GetFileName(text.ToString()) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
