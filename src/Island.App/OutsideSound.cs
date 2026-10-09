using Island.Core;

namespace Island.App;

/// <summary>
/// Windows' own notification sound (WORK-ORDER-7 section 1): the one place the app makes a sound. It asks <see cref="OutsideGate"/> first,
/// which refuses it unless the app was started for real: not a test, not the self-test, not a helper. <c>MessageBeep</c> plays the sound the
/// person set in the Sound control panel for "Asterisk" (so mute and volume apply), queues it and returns at once (Microsoft Learn,
/// MessageBeep: MB_ICONINFORMATION 0x40, asynchronous). Nothing is shipped as an audio file.
/// </summary>
internal static class OutsideSound
{
    private const uint IconInformation = 0x00000040;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);

    /// <summary>Plays the one short sound. False when the gate refused it or Windows did not queue it.</summary>
    public static bool PlayNotification()
    {
        if (!OutsideGate.Current.Allow(OutsideKind.PlaySound)) return false;
        return MessageBeep(IconInformation);
    }
}
