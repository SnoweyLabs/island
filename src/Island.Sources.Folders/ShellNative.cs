using System.Runtime.InteropServices;
using System.Text;

namespace Island.Sources.Folders;

/// <summary>Windows calls used to READ File Explorer. Nothing here changes a window.</summary>
internal static class ShellNative
{
    public const string FrameClass = "CabinetWClass";
    public const string TabClass = "ShellTabWindowClass";

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hwnd, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, nint token, out nint path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint ptr);

    public static string ClassOf(nint hwnd)
    {
        var text = new StringBuilder(256);
        return GetClassNameW(hwnd, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    /// <summary>The real path of a known folder, or null when Windows has none for this user.</summary>
    public static string? KnownFolderPath(Guid folderId)
    {
        if (SHGetKnownFolderPath(ref folderId, 0, 0, out var ptr) != 0) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { CoTaskMemFree(ptr); }
    }

    // SID_STopLevelBrowser and IID_IShellBrowser: ask an Explorer tab's object for its browser, whose
    // window is that tab's own ShellTabWindowClass window (Research/windows-apis.md section 5).
    public static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    public static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, out nint ppv);
    }

    // Only the first method of IOleWindow, which IShellBrowser starts with, is ever called.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellBrowser
    {
        [PreserveSig]
        int GetWindow(out nint hwnd);
    }
}
