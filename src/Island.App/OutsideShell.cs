using System.Diagnostics;
using Island.Core;

namespace Island.App;

/// <summary>
/// The app's other doors to the shell: opening one of its own files (the settings file) and starting a second
/// copy of itself (the self-test's one-copy check). Both ask <see cref="OutsideGate"/> first.
/// </summary>
internal static class OutsideShell
{
    /// <summary>Opens a file with whatever program Windows has for it. False when the gate refused it.</summary>
    public static bool OpenFile(string path)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenFile)) return false;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    /// <summary>Starts a second copy of this very program. Null when the gate refused it or it could not be started.</summary>
    public static Process? StartOwnCopy(ProcessStartInfo info) =>
        OutsideGate.Current.Allow(OutsideKind.StartOwnCopy) ? Process.Start(info) : null;
}
