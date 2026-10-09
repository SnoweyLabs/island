using System.Runtime.InteropServices;
using Island.Core;

namespace BlurProbe;

/// <summary>
/// Bringing the probe's own pattern window to the front. Lives in a file whose name begins "Outside" and asks
/// <see cref="OutsideGate"/> first, like every other call of this kind. The foreground is asked for plainly:
/// never forced (no input-queue sharing, no synthetic input).
/// </summary>
internal static unsafe partial class Win
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(nint h);

    /// <summary>Asks Windows to put the pattern window in front and give it the keyboard focus. True when it did.</summary>
    public static bool EnsurePatternForeground()
    {
        for (int i = 0; i < 6; i++)
        {
            if (PatternIsForeground() && PatternHasFocus()) return true;
            if (!OutsideGate.Current.Allow(OutsideKind.BringForward, Environment.ProcessId)) return false;
            BringWindowToTop(Pattern);
            SetForegroundWindow(Pattern);
            Pump(200);
        }
        return PatternIsForeground() && PatternHasFocus();
    }
}
