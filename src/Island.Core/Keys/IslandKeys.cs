namespace Island.Core;

/// <summary>
/// The island's keyboard rules (WORK-ORDER-10 section 2) as one pure function: a key, the modifiers, whether it is a repeat, and a description
/// of the island, to one named action. It keeps no selection and no state; it never touches a screen, a clock or a key hook.
/// </summary>
public static class IslandKeys
{
    public static KeyDecision Decide(KeyInput key, KeyContext context)
    {
        var pending = context.DeletePending;

        // No keyboard, or the island is going: the machine itself ignores keys then, and nothing may act.
        if (!context.HasKeyboard || context.IslandLeaving) return Pass(pending);

        // Search has its own keys (SearchKeys), tried first as before, repeats included (a held Backspace must go on deleting).
        if (context.SearchOpen) return Pass(pending);

        if (key.VirtualKey == KeyCodes.Escape) return Escape(key, context);

        // While a tile is lifted only Esc acts; every other key, a repeat too, only counts as use.
        if (context.TileLifted) return Use(pending);

        if (!IsOurs(key.VirtualKey))
            return key.IsRepeat ? Use(pending) : Pass(pending); // digits, Backspace, modifiers, letters: the existing path; a held one only counts as use

        // A held Delete: the ask stays; a long press neither removes nor cancels.
        if (key.IsRepeat && key.VirtualKey == KeyCodes.Delete) return Use(false);

        if (key.Ctrl || key.Alt || key.Win || context.SettingsShowing) return Use(pending);

        if (key.IsRepeat && key.VirtualKey is not (KeyCodes.Left or KeyCodes.Right)) return Use(pending);

        // From here the key is one of ours, plain or with Shift only, and a repeat only for Left and Right.
        var inSecondRow = context.SecondRowOpen && context.SelectionInSecondRow;
        var rowReady = context.RowIsCurrentPage && !context.PageChangeUnderWay;

        return key.VirtualKey switch
        {
            KeyCodes.Left => key.Shift || context.AtFirst ? Use(pending) : Act(KeyAction.MoveLeft, pending),
            KeyCodes.Right => key.Shift || context.AtLast ? Use(pending) : Act(KeyAction.MoveRight, pending),
            KeyCodes.Down => key.Shift || !rowReady ? Use(pending) : Down(context, inSecondRow),
            KeyCodes.Up => key.Shift || !rowReady || !inSecondRow ? Use(pending) : Act(KeyAction.LeaveSecondRow, pending),
            KeyCodes.Tab => context.PageCount < 2 ? Use(pending) : Act(key.Shift ? KeyAction.PreviousPage : KeyAction.NextPage, pending),
            KeyCodes.Enter => rowReady ? Enter(key, context, inSecondRow) : Use(pending),
            KeyCodes.Space => key.Shift || !rowReady || !context.MediaCanPlayPause ? Use(pending) : Act(KeyAction.PlayPause, pending),
            KeyCodes.Delete => key.Shift || !rowReady ? Use(pending) : Delete(context, inSecondRow),
            _ => Pass(pending),
        };
    }

    private static KeyDecision Pass(bool pending) => new(KeyAction.Nothing, false, pending);

    private static KeyDecision Use(bool pending) => new(KeyAction.CountsAsUse, true, pending);

    private static KeyDecision Act(KeyAction action, bool pending) => new(action, true, pending);

    /// <summary>
    /// Esc keeps its order (WORK-ORDER-5 section 5): a drag first, then a pending Delete, then the second row, then the island. Only the pending
    /// Delete is ours; the rest stays with the existing code. A held Esc only counts as use, so one long press never closes the row and then the island.
    /// </summary>
    private static KeyDecision Escape(KeyInput key, KeyContext context)
    {
        var pending = context.DeletePending;
        if (key.IsRepeat) return Use(pending);
        if (context.TileLifted || !pending) return Pass(pending);
        return Act(KeyAction.CancelPendingDelete, true);
    }

    private static KeyDecision Down(KeyContext context, bool inSecondRow)
    {
        var pending = context.DeletePending;
        if (inSecondRow) return Use(pending); // there is no third row
        if (context.SecondRowOpen) return context.SecondRowHasItems ? Act(KeyAction.OpenSecondRowAndEnter, pending) : Use(pending);
        if (!context.PlusClickActs) return Use(pending); // where a click on the + tile would do nothing
        return Act(context.SecondRowHasItems ? KeyAction.OpenSecondRowAndEnter : KeyAction.OpenSecondRow, pending);
    }

    private static KeyDecision Enter(KeyInput key, KeyContext context, bool inSecondRow)
    {
        var pending = context.DeletePending;
        if (inSecondRow)
        {
            if (!context.SecondRowHasItems) return Use(pending);
            return Act(key.Shift ? KeyAction.SecondRowAdd : KeyAction.SecondRowJump, pending);
        }

        if (key.Shift) return Use(pending); // Shift+Enter is for the second row only
        return context.Selected == SelectedTile.None ? Use(pending) : Act(KeyAction.Activate, pending);
    }

    private static KeyDecision Delete(KeyContext context, bool inSecondRow)
    {
        var pending = context.DeletePending;
        // Delete does nothing on the + tile, on nothing, and in the second row; it still cancels a pending one.
        if (inSecondRow || context.Selected != SelectedTile.Pick) return Use(pending);
        if (!pending) return Act(KeyAction.AskRemove, false);
        // A second Delete removes only when the selected tile is still that very pick; otherwise it only cancels (it does not ask again).
        return context.SelectedIsPendingPick ? Act(KeyAction.ConfirmRemove, false) : Use(true);
    }

    /// <summary>The keys these rules act on. Everything else (digits, Esc, Backspace, letters, modifiers) is left to the existing code.</summary>
    private static bool IsOurs(int virtualKey) =>
        virtualKey is KeyCodes.Left or KeyCodes.Right or KeyCodes.Up or KeyCodes.Down
            or KeyCodes.Tab or KeyCodes.Enter or KeyCodes.Space or KeyCodes.Delete;

    /// <summary>
    /// The page index Tab goes to from <paramref name="index"/> among <paramref name="count"/> pages: the next one, the one before when
    /// <paramref name="backwards"/>, wrapping at the ends. With fewer than two pages, or an index outside them, the index is returned unchanged
    /// (a number the machine can clamp).
    /// </summary>
    public static int PageAfterTab(int index, int count, bool backwards)
    {
        if (count < 2 || index < 0 || index >= count) return index;
        return backwards ? (index - 1 + count) % count : (index + 1) % count;
    }

    /// <summary>
    /// Whether typed text may open search (or go to it): false for null, empty, and text made only of spaces, control characters or other
    /// characters that draw as nothing. Space, Enter, Tab and Esc can arrive as typed characters too and must never open search. Digits are
    /// true here: search turns a lone digit into a page switch by its own rules.
    /// </summary>
    public static bool IsOpeningText(string? text) => BlankText.HasVisible(text);
}
