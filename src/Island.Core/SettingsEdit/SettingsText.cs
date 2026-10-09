namespace Island.Core.SettingsEdit;

/// <summary>The sentences of the settings screen that carry a rule, kept as data so a test can pin them. English only; plain words.</summary>
public static class SettingsText
{
    /// <summary>EVALS K9: what Windows cannot tell us, said once, near the key list.</summary>
    public const string PrivateShortcutWarning =
        "Windows does not tell an app when another program uses the same keys privately as its own shortcut, "
        + "so a key can look free here and still clash with that program.";

    public const string MainActionName = "Show or hide the island";

    /// <summary>WORK-ORDER-10 §2: the keys of the island once it was called with the main key, one line each (the settings screen's "Key" section lists them).</summary>
    public static IReadOnlyList<string> IslandKeys { get; } =
    [
        "[Left] [Right] move along the row · [Enter] does what a click does",
        "[Down] opens the second row (what is open now) and goes into it · [Up] goes back out and closes it",
        "[Shift+Enter] in the second row adds the thing to the page",
        "[Tab] the next page · [Shift+Tab] the page before",
        "[Space] plays or pauses, on the Media page",
        "[Delete], then [Delete] again, removes the selected pick (nothing that is open is closed)",
        "[Esc] closes the second row first, then the island",
    ];

    /// <summary>"Add…" (WORK-ORDER-10 §3): Windows' own window for choosing could not be opened here.</summary>
    public const string NoChoosingWindow = "Windows' window for choosing could not be opened here, so nothing was added.";

    public const string NothingAdded = "Nothing was added.";

    public static string AlreadyOnIsland(string name, string pageName) => $"{name} is already on the island, on the page {pageName}.";

    public const string WhileOpenHint = "While the island is open, the number keys change page and Esc closes it.";

    public const string PressYourKeys = "Press your keys";

    public const string PressYourKeysHelp = "Hold Ctrl, Alt or Shift and press one key. Esc cancels.";

    public static string DigitHint(int pageCount) =>
        pageCount switch
        {
            <= 1 => "Key 1 changes page while the island is open.",
            _ => $"Keys 1 to {Math.Min(9, pageCount)} change page while the island is open.",
        };

    // ---- Refusals (each says what happened, why, and what to do) ------------

    public static Refusal KeyHasWindowsKey { get; } = new(
        "KEY_WINDOWS",
        "That combination uses the Windows key.",
        "Windows keeps it for itself.",
        "Try Ctrl, Alt or Shift with another key.");

    public static Refusal KeyUnknown { get; } = new(
        "KEY_UNSUPPORTED",
        "Island cannot keep that key in its settings.",
        "It only knows letters, digits, F1 to F24, Space, Tab, Enter, Esc, Home, End, Insert, Delete, Page Up, Page Down and the arrows.",
        "Press one of those, with Ctrl, Alt or Shift.");

    public static Refusal KeyUnsafe(string reason) => new(
        "KEY_UNSAFE",
        "That combination cannot be used.",
        reason,
        "Press a different combination.");

    /// <summary>WORK-ORDER-6 register: no two things in the app hold one key (the main key, a page, a pick).</summary>
    public static Refusal KeyTakenInside(HotkeyCombo combo, string ownerName, string newName) => new(
        "KEY_ALREADY_USED_HERE",
        $"{combo} already belongs to \"{ownerName}\" in Island, so \"{newName}\" was not changed.",
        "Two things on one key would make the key do something different each time.",
        "Clear it there first, or pick another combination.");

    /// <summary>EVALS K6: HOTKEY_TAKEN as the editor says it: the old key is still in force.</summary>
    public static Refusal KeyTakenByAnotherProgram(HotkeyCombo combo, bool hadKey = true) => new(
        Refusals.HotkeyTaken.Code,
        $"{combo} is already used by another program.",
        "Island left it alone, because taking it would break that program." + (hadKey ? " Your old key still works." : " Nothing was changed."),
        "Press a different combination.");

    public static Refusal KeyRefusedByWindows(HotkeyCombo combo, int error, bool hadKey = true) => new(
        "KEY_REFUSED",
        $"Windows would not hand {combo} to Island (error {error}).",
        hadKey ? "Your old key still works." : "Nothing was changed.",
        "Press a different combination.");

    public static Refusal MainKeyCannotBeEmpty { get; } = new(
        "KEY_REQUIRED",
        "The main key cannot be removed.",
        "It is the only way to bring the island back.",
        "Press another combination instead, or restore the default.");

    public static Refusal KeyNotSaved { get; } = new(
        "KEY_NOT_SAVED",
        "The new key could not be saved.",
        "Island does not change a key it cannot remember.",
        "Your old key still works. Try again.");

    /// <summary>The same words for an action that had no key: there is no old key to say still works.</summary>
    public static Refusal KeyNotSavedFor(bool hadKey) => hadKey
        ? KeyNotSaved
        : new("KEY_NOT_SAVED", "The new key could not be saved.", "Island does not change a key it cannot remember.", "Nothing was changed. Try again.");

    // ---- Pages ---------------------------------------------------------------

    public const string PageNameEmpty = "A page needs a name.";

    public const string PageNameBad = "A page name can hold letters, digits and ordinary punctuation, but no hidden or control characters.";

    public const string PageLimit = "The island has room for nine pages, one for each number key 1 to 9.";

    public const string PageColourBad = "A colour is six digits after a #, for example #19E6B3.";

    public const int MaxPageNameLength = 24;

    public static string PageNameTooLong => $"A page name can have at most {MaxPageNameLength} characters.";

    public static string PageNameTaken(string name) => $"There is already a page called \"{name}\".";

    public static string PageColourClose(string otherName) => $"This colour is very close to {otherName}. It is allowed, but the two will be hard to tell apart.";

    /// <summary>The one sentence "What goes on the island" shows for the Terminals page (WORK-ORDER-11 section 1).</summary>
    public const string PageFillsItself = "This page fills itself with the terminals and AI programs that are open.";

    public const string BuiltInPageCannotBeDeleted = "The six pages that come with the island cannot be removed. You can rename or recolour them.";

    public const string NoSuchPage = "That page no longer exists.";

    public const string StartupNotAvailable = "Starting with Windows cannot be changed here.";

    public const string StartupNotTaken = "Windows did not take the change, so nothing was changed. Try again, or leave it as it is.";

    public static string KeyNotGivenBack(string combo) => $"{combo}, the key you had set for this, is used by something else now, so it was not given back. Press a key for it again.";

    public static string KeyNotGivenBackNotSaved(string combo) => $"{combo}, the key you had set for this, was not given back, because the settings file could not be saved. Press a key for it again.";

    public static string KeyNotGivenBackBecause(string combo, string reason) => $"{combo}, the key you had set for this, was not given back. {reason}";

    public static string AlreadyOnTheIsland(string name, string? page = null, string? otherName = null) =>
        $"{name} is already on the island{(page is null ? string.Empty : $", on the page {page}")}{(otherName is null || otherName == name ? string.Empty : $" as {otherName}")}, so this one cannot be switched on again. Its key, if it had one, is gone with it.";

    public const string KeyStillHeld = "A key that belonged to something that is gone could not be given back to Windows (the settings file could not be saved), so it is still held. It is tried again at the next change.";

    public const string ModeNextActionName = "Next mode";

    public const string ModeUnknown = "That is not one of the three modes.";

    public const string NeverOverRefused = "That program cannot be put on the list: it needs a name and an executable file name that ends in .exe, with no folder in it.";

    public const string NeverOverFull = "The list is full. Remove one first.";

    public const string IdleNotUsable = "The idle time must be a number of seconds.";

    public const string NoSuchPick = "That pick is no longer on the island.";

    public const string PickCannotMove = "That pick cannot be moved to that page, so it stays where it is.";

    public static string DeletePageQuestion(string pageName, int pickCount) =>
        pickCount switch
        {
            0 => $"Remove the page \"{pageName}\"? Nothing is on it.",
            1 => $"Remove the page \"{pageName}\"? The 1 pick on it goes with it. Nothing that is open is closed.",
            _ => $"Remove the page \"{pageName}\"? The {pickCount} picks on it go with it. Nothing that is open is closed.",
        };

    public static string RestoreStarterQuestion(string pageName, int yourPicks, int starterPicks) =>
        $"Bring back the starter list for {pageName}? Your {yourPicks} {(yourPicks == 1 ? "pick" : "picks")} on this page "
        + $"will be replaced by the {starterPicks} {(starterPicks == 1 ? "pick" : "picks")} that come with Island. Nothing that is open is closed.";

    public static string RestoreAllKeysQuestion(int keys) =>
        $"Go back to the original keys? The {(keys == 1 ? "1 key" : $"{keys} keys")} you set will be changed back: the main key to the one it had, the page keys, pick keys and the mode key taken away. Idle time and glass stay as they are.";

    // ---- Glass ---------------------------------------------------------------

    /// <summary>The register's own three parts (what, why, what to do), so the screen and the balloon say the same.</summary>
    public static string BlurUnavailable => Refusals.BlurUnavailable.Message;

    /// <summary>The same words for the glass in use: Island uses the Approved glass instead of Blur (when Blur was the choice or the choice is Approved); with the Darker glass chosen the sentence that says so would be false and is left out.</summary>
    public static string BlurUnavailableFor(GlassKind glassInUse) =>
        glassInUse != GlassKind.Darker ? BlurUnavailable : $"{Refusals.BlurUnavailable.WhatHappened} Island keeps the Darker glass you chose. {Refusals.BlurUnavailable.NextAction}";

}
