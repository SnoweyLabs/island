namespace Island.Core;

/// <summary>
/// Win32 virtual-key codes the island's keyboard rules name, as plain ints (Island.Core cannot see WPF). Every value is from Microsoft Learn,
/// "Virtual-Key Codes" (Winuser.h), read 2026-10-07.
/// </summary>
public static class KeyCodes
{
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Delete = 0x2E;

    /// <summary>The digit row: 0x30 is "0", 0x31 to 0x39 are "1" to "9".</summary>
    public const int Digit1 = 0x31;

    public const int Digit9 = 0x39;

    /// <summary>The number pad: 0x60 is "0", 0x61 to 0x69 are "1" to "9".</summary>
    public const int Pad1 = 0x61;

    public const int Pad9 = 0x69;
}

/// <summary>
/// One key-down event of the island's own window, as plain values. <paramref name="IsRepeat"/> is true for the second and later key-down
/// messages of a key that is held down (WPF: <c>KeyEventArgs.IsRepeat</c>; the raw message: bit 30 of lParam).
/// </summary>
public readonly record struct KeyInput(int VirtualKey, bool Shift = false, bool Ctrl = false, bool Alt = false, bool Win = false, bool IsRepeat = false);

/// <summary>What the selected tile of the active row is. In the second row the context's flags say what is there; this is about the first row.</summary>
public enum SelectedTile
{
    None,
    Pick,
    Plus,
}

/// <summary>
/// Exactly what the keyboard rules of WORK-ORDER-10 section 2 need to know about the island. The machine and the app fill it; the rules keep no
/// state of their own. <see cref="AtFirst"/> and <see cref="AtLast"/> are about the row the selection is in (the first row, or the second when
/// <see cref="SelectionInSecondRow"/>): true when there is no tile before (after) the selected one, and true for both when the row is empty.
/// </summary>
public sealed record KeyContext
{
    /// <summary>The island was called with the main key and still has the keyboard.</summary>
    public bool HasKeyboard { get; init; }

    public bool SearchOpen { get; init; }

    /// <summary>The settings screen is showing.</summary>
    public bool SettingsShowing { get; init; }

    /// <summary>A tile is lifted (a drag is under way). The app may fold "a tile is pressed" into this: every key then only counts as use.</summary>
    public bool TileLifted { get; init; }

    /// <summary>The island is hidden or on its way out.</summary>
    public bool IslandLeaving { get; init; }

    /// <summary>The row that is laid out is the current page's row (the machine's contents page is its page).</summary>
    public bool RowIsCurrentPage { get; init; }

    /// <summary>A page change is under way (the contents are going out or coming in).</summary>
    public bool PageChangeUnderWay { get; init; }

    public bool SecondRowOpen { get; init; }

    /// <summary>The selection is in the second row (only meaningful while <see cref="SecondRowOpen"/>).</summary>
    public bool SelectionInSecondRow { get; init; }

    /// <summary>The second row has at least one tile (the app counts what would be shown).</summary>
    public bool SecondRowHasItems { get; init; }

    /// <summary>What the selected tile of the first row is.</summary>
    public SelectedTile Selected { get; init; }

    public bool AtFirst { get; init; }

    public bool AtLast { get; init; }

    /// <summary>How many pages there are (Tab goes round them).</summary>
    public int PageCount { get; init; }

    /// <summary>A click on the + tile would do something now (it would open or close the second row). Where it would not, Down only counts as use.</summary>
    public bool PlusClickActs { get; init; }

    /// <summary>The current page is Media and something plays: the middle button has something to play or pause.</summary>
    public bool MediaCanPlayPause { get; init; }

    /// <summary>A first Delete asked "Delete again to remove" and nothing has cancelled it since.</summary>
    public bool DeletePending { get; init; }

    /// <summary>The selected tile is the very pick the pending Delete asked about (the pick, not its place in the row).</summary>
    public bool SelectedIsPendingPick { get; init; }
}

public enum KeyAction
{
    /// <summary>Nothing for these rules. With <see cref="KeyDecision.Handled"/> false the caller runs its existing path (search, Esc, digits, the idle clock).</summary>
    Nothing,

    /// <summary>The key was taken and does nothing but reset the idle clock (the machine's OtherKey).</summary>
    CountsAsUse,

    /// <summary>The selection moves one tile to the left (in the row it is in); never returned at the first tile.</summary>
    MoveLeft,

    /// <summary>The selection moves one tile to the right; never returned at the last tile.</summary>
    MoveRight,

    /// <summary>Enter on the first row: exactly what a click on the selected tile does, through the one path a click takes.</summary>
    Activate,

    /// <summary>Down: open the second row if it is closed, and move the selection onto its first tile.</summary>
    OpenSecondRowAndEnter,

    /// <summary>Down with nothing in the second row: open it and stay where the selection is.</summary>
    OpenSecondRow,

    /// <summary>Up in the second row: the selection goes back to the first row and the second closes.</summary>
    LeaveSecondRow,

    /// <summary>Enter in the second row: jump to that thing once, add nothing.</summary>
    SecondRowJump,

    /// <summary>Shift+Enter in the second row: add that thing to the page, as a click on its small + does.</summary>
    SecondRowAdd,

    /// <summary>Tab: the next page (the app wraps with <see cref="IslandKeys.PageAfterTab"/>).</summary>
    NextPage,

    /// <summary>Shift+Tab: the page before.</summary>
    PreviousPage,

    /// <summary>Space on the Media page: the same as the middle button.</summary>
    PlayPause,

    /// <summary>First Delete on a pick: the text block asks "Delete again to remove"; the app remembers which pick.</summary>
    AskRemove,

    /// <summary>A second Delete, pressed anew, on the same pick: remove it; nothing is closed.</summary>
    ConfirmRemove,

    /// <summary>Esc with a Delete pending: only that Delete is cancelled (Esc's first duty after a drag); the island stays.</summary>
    CancelPendingDelete,
}

/// <summary>
/// What a key does. <paramref name="Handled"/> true: the decision accounts for the key, and the caller must not also run its existing path
/// (search, Esc, digits, OtherKey). False: the key is left to the existing code, unchanged. <paramref name="CancelsPendingDelete"/> true: a
/// pending Delete must be forgotten before the action runs: it holds for every key while one is pending, except a held repeat of the Delete
/// and the Delete that removes (which uses it up).
/// </summary>
public readonly record struct KeyDecision(KeyAction Action, bool Handled, bool CancelsPendingDelete);
