using System.Runtime.InteropServices;
using System.Text;

namespace Island.Sources.Programs;

/// <summary>
/// The Win32 calls that only read the machine. Everything that acts on it (bringing a window forward,
/// starting something) is declared in OutsideActions.cs and nowhere else.
/// </summary>
internal static class Native
{
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    public delegate void WinEventProc(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint timeMs);

    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectDestroy = 0x8001;
    public const uint EventObjectShow = 0x8002;
    public const uint EventObjectHide = 0x8003;
    public const uint EventObjectNameChange = 0x800C;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint WmTimer = 0x0113;
    public const uint WmQuit = 0x0012;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int GwOwner = 4;
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x80;
    public const long WsExAppWindow = 0x40000;
    public const int DwmaCloaked = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PtX, PtY;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(nint parent, EnumWindowsProc proc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hwnd, int cmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetPropW(nint hwnd, string name);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc proc, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    public static extern int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern nint SetTimer(nint hwnd, nint id, uint elapseMs, nint proc);

    [DllImport("user32.dll")]
    public static extern bool KillTimer(nint hwnd, nint id);

    [DllImport("user32.dll")]
    public static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    public static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetPackageFamilyName(nint process, ref uint length, StringBuilder name);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    public static string ClassNameOf(nint hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    public static string TitleOf(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        return GetWindowTextW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>The file name (never the path) of the program a process runs; null when it cannot be read (system or elevated processes).</summary>
    public static string? ExeNameOf(uint processId) => WithProcess(processId, process =>
    {
        var sb = new StringBuilder(1024);
        var size = sb.Capacity;
        return QueryFullProcessImageNameW(process, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : null;
    });

    /// <summary>The package family name of a packaged process; null for an ordinary desktop program.</summary>
    public static string? PackageFamilyOf(uint processId) => WithProcess(processId, process =>
    {
        uint length = 0;
        GetPackageFamilyName(process, ref length, null!); // 122 = buffer too small: the program is packaged
        if (length == 0) return null;
        var sb = new StringBuilder((int)length);
        return GetPackageFamilyName(process, ref length, sb) == 0 ? sb.ToString() : null;
    });

    private static T? WithProcess<T>(uint processId, Func<nint, T?> read)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return default;
        try
        {
            return read(process);
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
