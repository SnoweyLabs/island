namespace Island.Core;

public enum SearchKeyKind
{
    /// <summary>Typed text (one character, or a surrogate pair, or an IME's string): the island's own window's text input.</summary>
    Text,
    Left,
    Right,
    Enter,
    Backspace,
    Escape,
}

/// <summary>One key event the island's window received. Plain values; no hook is involved.</summary>
public readonly record struct SearchKey(SearchKeyKind Kind, string Text = "")
{
    public static SearchKey Typed(string text) => new(SearchKeyKind.Text, text);

    public static readonly SearchKey Left = new(SearchKeyKind.Left);
    public static readonly SearchKey Right = new(SearchKeyKind.Right);
    public static readonly SearchKey Enter = new(SearchKeyKind.Enter);
    public static readonly SearchKey Backspace = new(SearchKeyKind.Backspace);
    public static readonly SearchKey Escape = new(SearchKeyKind.Escape);
}

/// <param name="IsOpen">Search is showing.</param>
/// <param name="Text">The field's text.</param>
/// <param name="Selected">Index of the selected tile.</param>
/// <param name="TileCount">How many tiles there are now: the matches plus the service tile, if any (the caller counts).</param>
public sealed record SearchState(bool IsOpen, string Text, int Selected, int TileCount)
{
    /// <summary>The island has the keyboard and search is not open.</summary>
    public static SearchState Closed { get; } = new(false, "", 0, 0);
}

public enum SearchKeyAction
{
    /// <summary>The key means nothing to search; the caller treats it as it would without search.</summary>
    None,
    /// <summary>A digit with the field empty (or search closed): switch to that page. <see cref="SearchKeyResult.Page"/> holds it.</summary>
    SwitchPage,
    /// <summary>Search just opened, with the typed character in it.</summary>
    Opened,
    /// <summary>The text changed: the caller works out the new matches (the selection is back on the first).</summary>
    TextChanged,
    /// <summary>Only the selection moved.</summary>
    SelectionChanged,
    /// <summary>Enter: do what a click on the selected tile does.</summary>
    Activate,
    /// <summary>Esc with nothing typed: search closes and the page shows again.</summary>
    Leave,
}

/// <param name="Action">What happened.</param>
/// <param name="State">The state after the key.</param>
/// <param name="Page">The digit for <see cref="SearchKeyAction.SwitchPage"/>, otherwise -1.</param>
public sealed record SearchKeyResult(SearchKeyAction Action, SearchState State, int Page = -1);

/// <summary>
/// What each key does in search (EVALS F3, WORK-ORDER-7.md section 3). A pure function of the state and the key: it never
/// touches a screen, a key hook or a clock, and it keeps no copy of the text.
/// </summary>
public static class SearchKeys
{
    /// <summary>Longest text the field takes; more is ignored. No search needs more, and the address is cut at 200 anyway.</summary>
    public const int MaxFieldChars = 256;

    public static SearchKeyResult Apply(SearchState state, SearchKey key)
    {
        var selected = Clamp(state.Selected, state.TileCount);
        state = state with { Selected = selected };
        return key.Kind switch
        {
            SearchKeyKind.Text => Typed(state, key.Text),
            SearchKeyKind.Backspace => state.IsOpen && state.Text.Length > 0 ? Changed(state, DeleteLast(state.Text)) : None(state),
            SearchKeyKind.Left => state.IsOpen ? Moved(state, selected - 1) : None(state),
            SearchKeyKind.Right => state.IsOpen ? Moved(state, selected + 1) : None(state),
            SearchKeyKind.Enter => state.IsOpen && state.TileCount > 0 ? new(SearchKeyAction.Activate, state) : None(state),
            SearchKeyKind.Escape => Escape(state),
            _ => None(state),
        };
    }

    private static SearchKeyResult Typed(SearchState state, string? input)
    {
        var text = StripControls(input);
        if (text.Length == 0) return None(state);

        if (text.Length == 1 && text[0] is >= '0' and <= '9' && (!state.IsOpen || state.Text.Length == 0))
            return new(SearchKeyAction.SwitchPage, state, text[0] - '0'); // only ASCII digits switch pages

        if (!state.IsOpen)
        {
            // A space, a control character or anything that draws as nothing does not open search: it would open an empty-looking field by accident.
            if (!BlankText.HasVisible(text[..(char.IsHighSurrogate(text[0]) && text.Length > 1 ? 2 : 1)])) return None(state);
            return Append(state, text) is { } opened
                ? new(SearchKeyAction.Opened, opened.State with { IsOpen = true })
                : None(state);
        }

        // Open with an empty field, a space is not a first character: words do not start with one.
        if (state.Text.Length == 0 && !BlankText.HasVisible(text)) return None(state);
        return Append(state, text) ?? None(state);
    }

    private static SearchKeyResult? Append(SearchState state, string text)
    {
        if (state.Text.Length + text.Length > MaxFieldChars) return null;
        return Changed(state, state.Text + text);
    }

    private static SearchKeyResult Escape(SearchState state)
    {
        if (!state.IsOpen) return None(state);
        if (state.Text.Length > 0) return Changed(state, "");
        return new(SearchKeyAction.Leave, SearchState.Closed);
    }

    private static SearchKeyResult Changed(SearchState state, string text) =>
        new(SearchKeyAction.TextChanged, state with { Text = text, Selected = 0, TileCount = 0 });

    private static SearchKeyResult Moved(SearchState state, int to)
    {
        var selected = Clamp(to, state.TileCount);
        return selected == state.Selected
            ? None(state)
            : new(SearchKeyAction.SelectionChanged, state with { Selected = selected });
    }

    private static SearchKeyResult None(SearchState state) => new(SearchKeyAction.None, state);

    private static int Clamp(int index, int count) => count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);

    private static string StripControls(string? text) =>
        string.IsNullOrEmpty(text) ? "" : string.Concat(text.Where(c => !char.IsControl(c)));

    /// <summary>Removes the last character, or the last surrogate pair whole.</summary>
    private static string DeleteLast(string text) =>
        text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]) ? text[..^2] : text[..^1];
}
