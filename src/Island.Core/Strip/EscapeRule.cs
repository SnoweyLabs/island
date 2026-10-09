namespace Island.Core;

/// <summary>What one press of Esc does.</summary>
public enum EscapeAction
{
    /// <summary>A lifted tile goes back to its place (WORK-ORDER-5 §6).</summary>
    CancelDrag,

    /// <summary>The second row closes (§5).</summary>
    CloseRow,

    /// <summary>The island is sent away.</summary>
    Dismiss,
}

/// <summary>
/// WORK-ORDER-5 §5: one press of Esc does one thing, the first of these that applies: it cancels a drag, it closes the
/// second row, it sends the island away. (WORK-ORDER-7 puts search in front of the last one.)
/// </summary>
public static class EscapeRule
{
    public static EscapeAction Decide(bool dragging, bool secondRowOpen) =>
        dragging ? EscapeAction.CancelDrag : secondRowOpen ? EscapeAction.CloseRow : EscapeAction.Dismiss;
}
