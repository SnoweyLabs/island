namespace Island.App;

/// <summary>
/// Borrows the keyboard for the island and gives it back. Taking is done inside the keybind's own
/// message handler, because that key press is what lets Windows bring the island to the front.
/// No synthetic input, no keyboard hook, and the foreground is never forced: the island asks for it
/// plainly through <see cref="OutsideForeground"/> and, if Windows says no, simply has no keyboard.
/// </summary>
internal sealed class KeyboardFocus(OverlayWindow island)
{
    private IntPtr _previous;

    /// <summary>
    /// For the self-test only: pretends Windows granted the keyboard, so that the handlers a key reaches can be tried where the real grant depends on
    /// who the person has been clicking in (the real grant is a check for a person, as it always was). Never set outside a stage.
    /// </summary>
    internal bool PretendGranted { get; set; }

    /// <summary>True while the island's window is the foreground window.</summary>
    public bool IslandIsForeground => PretendGranted || Native.GetForegroundWindow() == island.Handle;

    /// <summary>Remembers who had the keyboard and asks for it. False when Windows did not allow it.</summary>
    public bool Take()
    {
        if (PretendGranted) return true;
        var before = Native.GetForegroundWindow();
        _previous = before == island.Handle ? IntPtr.Zero : before;
        if (!OutsideForeground.BringForward(island.Handle)) return false;
        Native.SetFocus(island.Handle);
        return IslandIsForeground;
    }

    /// <summary>
    /// Hands the keyboard back to the window that had it, but only while the island still has it: if
    /// the person clicked into another window, that window already has the keyboard and is left alone.
    /// </summary>
    public void Give()
    {
        if (PretendGranted) return;
        var back = _previous;
        _previous = IntPtr.Zero;
        if (back == IntPtr.Zero || !IslandIsForeground || Native.IsIconic(back)) return;
        OutsideForeground.BringForward(back);
    }
}
