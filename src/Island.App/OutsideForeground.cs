using Island.Core;

namespace Island.App;

/// <summary>
/// Asking Windows to put a window in front. The only code in the app that does it, and it asks
/// <see cref="OutsideGate"/> first: under the self-test only a window of the self-test's own process is allowed.
/// The foreground is asked for plainly and never forced: no input-queue sharing, no synthetic input, no Alt-key trick.
/// When Windows says no, the answer is simply false.
/// </summary>
internal static class OutsideForeground
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Restores the window if it is minimised, asks for the foreground, and says whether it got it. Call it inside the click or key handler, never after a delay.</summary>
    public static bool BringForward(IntPtr window)
    {
        if (window == IntPtr.Zero || !Native.IsWindow(window)) return false;
        if (!OutsideGate.Current.Allow(OutsideKind.BringForward, (int)Native.ProcessIdOf(window))) return false;

        if (Native.IsIconic(window)) Native.ShowWindow(window, Native.SwRestore);
        SetForegroundWindow(window);
        return Native.GetForegroundWindow() == window;
    }
}
