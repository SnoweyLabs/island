namespace Island.Core;

/// <summary>
/// WORK-ORDER-6 §1 (W2): the island changes screen only while nothing of it is visible. This keeps the last placement while the
/// island is visible and chooses again only when it is not (at the moment of a summon, before the ball starts), so a pointer
/// that moves to another screen while the island is open never drags it along. Pure: no Windows call, no clock.
/// </summary>
public sealed class ScreenPicker
{
    /// <summary>The placement in force; null until the first choice.</summary>
    public ScreenPlacement? Current { get; private set; }

    /// <summary>How many times a choice was made (a visible island makes none).</summary>
    public int Choices { get; private set; }

    /// <summary>
    /// The placement to use now. While <paramref name="visible"/> is true the placement in force is returned unchanged (when there
    /// is none yet, one is chosen: something has to be shown somewhere); otherwise the screen under the pointer is chosen again.
    /// </summary>
    public ScreenPlacement Update(ScreenPoint? pointer, IReadOnlyList<ScreenInfo>? screens, bool visible, double windowWidthDip, double windowHeightDip)
    {
        if (visible && Current is { } kept) return kept;
        Current = ScreenChooser.Choose(pointer, screens, windowWidthDip, windowHeightDip);
        Choices++;
        return Current.Value;
    }
}
