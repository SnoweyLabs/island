using Island.Core;

namespace Island.App;

/// <summary>
/// Asking another program's window to close (WORK-ORDER-6 section 5): the only code in the app that does it, and it asks
/// <see cref="OutsideGate"/> first. Under the self-test only a window of the self-test's own process may be closed. The request is the
/// one a click on the window's X produces (WM_SYSCOMMAND with SC_CLOSE), POSTED and never waited for, so the island does not freeze
/// while the program shows its own "save?" question; it never ends a process. Windows drops a request aimed at a program that runs
/// with more rights, so <see cref="CloseButton"/> checks that beforehand and does not ask.
/// </summary>
internal static class OutsideClose
{
    private const uint WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Posts the request. False when the window is gone, the gate refused it, or Windows did not take the post.</summary>
    public static bool Request(IntPtr window)
    {
        if (window == IntPtr.Zero || !Native.IsWindow(window)) return false;
        if (!OutsideGate.Current.Allow(OutsideKind.CloseWindow, (int)Native.ProcessIdOf(window))) return false;
        return PostMessage(window, WmSysCommand, new IntPtr(ScClose), IntPtr.Zero);
    }
}
