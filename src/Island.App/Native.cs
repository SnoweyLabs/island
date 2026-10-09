using System.Runtime.InteropServices;

namespace Island.App;

/// <summary>The few Win32 calls the app needs. Constants are from Microsoft Learn, checked 6 Oct 2026.</summary>
internal static class Native
{
    public const int GwlExStyle = -20;
    public const long WsExTopmost = 0x00000008;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExLayered = 0x00080000;
    public const long WsExNoActivate = 0x08000000;

    public const int WmHotKey = 0x0312;
    public const uint ModNoRepeat = 0x4000;
    public static readonly IntPtr HwndMessage = new(-3);

    public const int WmKeyDown = 0x0100;
    public const int WmChar = 0x0102;
    public const int WmSysKeyDown = 0x0104;

    public const int WmMouseActivate = 0x0021;
    public const int MaNoActivate = 3;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    // Messages that say the screens, their scaling or the work area may have changed. WM_DPICHANGED is 0x02E0 (Microsoft Learn,
    // WM_DPICHANGED). The numbers of the other two are not printed on their Learn pages; they come from winuser.h (UNVERIFIED on
    // Learn). A wrong number would only mean one re-read fewer: the reader also listens to SystemEvents.DisplaySettingsChanged.
    public const int WmDisplayChange = 0x007E;
    public const int WmDpiChanged = 0x02E0;
    public const int WmSettingChange = 0x001A;

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;

    public static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    /// <summary>The state of a key as of the input message being handled (high-order bit: down). Read inside the window procedure of a key message; never a hook.</summary>
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    public const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLWin = 0x5B, VkRWin = 0x5C;

    public const int VkLButton = 0x01;
    public const int VkRButton = 0x02;

    /// <summary>True while the left or right mouse button is held down (the state of the buttons only, never a key).</summary>
    public static bool MouseButtonDown() => (GetAsyncKeyState(VkLButton) & 0x8000) != 0 || (GetAsyncKeyState(VkRButton) & 0x8000) != 0;

    public const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returned);

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    /// <summary>
    /// Whether the process runs elevated (as administrator): the pre-check PowerToys makes before it posts a close. Null when it cannot
    /// be told (the process cannot be opened for a limited query: it has more rights than this one, or is protected).
    /// </summary>
    public static bool? IsElevated(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return null;
            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) ? elevated != 0 : null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    /// <summary>The window that currently has the keyboard focus on the foreground thread (zero if none).</summary>
    public static IntPtr FocusWindowOfForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        var tid = GetWindowThreadProcessId(fg, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(tid, ref info) ? info.Focus : IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    public const uint MonitorDefaultToPrimary = 1;

    public static long GetExStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();

    public static void SetExStyle(IntPtr hWnd, long style) =>
        SetWindowLongPtr(hWnd, GwlExStyle, new IntPtr(style));

    public static uint ProcessIdOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hWnd, out var pid);
        return pid;
    }

    /// <summary>Pixel rectangle of the primary monitor.</summary>
    public static Rect PrimaryMonitorBounds()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new Point(0, 0), MonitorDefaultToPrimary);
        GetMonitorInfo(monitor, ref info);
        return info.Monitor;
    }
}
