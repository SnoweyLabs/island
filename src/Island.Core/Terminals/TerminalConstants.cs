namespace Island.Core.Terminals;

/// <summary>The numbers and words of the Terminals page that Claude chose (WORK-ORDER-11). Each is pinned by a test.</summary>
public static class TerminalConstants
{
    /// <summary>A tile's title is cut to this many characters (Claude).</summary>
    public const int MaxTitleChars = 40;

    /// <summary>
    /// Only this many characters of a window's title are ever looked at (Claude): a title can be a megabyte long, and the
    /// first characters are all a tile or a project-name match needs.
    /// </summary>
    public const int TitleReadLimit = 512;

    /// <summary>The second line of a plain terminal's tile.</summary>
    public const string TerminalWord = "terminal";

    /// <summary>The words added to the second line of a helper's tile (Claude): " · working", " · needs you", " · finished".</summary>
    public const string WorkingWords = " · working";
    public const string NeedsYouWords = " · needs you";
    public const string FinishedWords = " · finished";

    /// <summary>The name of a terminal program that is not in the table and gave no usable file name (Claude).</summary>
    public const string UnknownTerminalName = "Terminal";

    public const string ClaudeCode = "Claude Code";
    public const string Codex = "Codex";
    public const string Antigravity = "Antigravity";
    public const string Gemini = "Gemini";

    public const string PseudoConsoleClass = "PseudoConsoleWindow";
    public const string ClassicConsoleClass = "ConsoleWindowClass";
    public const string WindowsTerminalClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    // Helper colours (WORK-ORDER-11 section 2).
    public static readonly HelperColor ClaudeCodeColor = new(217, 119, 60);
    public static readonly HelperColor CodexColor = new(16, 163, 127);
    public static readonly HelperColor AntigravityColor = new(76, 141, 255);
    public static readonly HelperColor GeminiColor = new(142, 117, 255);

    /// <summary>The disc of a helper the table has no colour for (Claude): a neutral grey-blue.</summary>
    public static readonly HelperColor UnknownHelperColor = new(128, 128, 140);

    /// <summary>The most process ids of a chain that are looked at (Claude). A chain from outside may be longer; the nearest ones decide.</summary>
    public const int MaxChainUsed = 32;

    /// <summary>The words for a state on the second line; empty for idle.</summary>
    public static string WordsOf(HelperState state) => state switch
    {
        HelperState.Working => WorkingWords,
        HelperState.NeedsYou => NeedsYouWords,
        HelperState.Finished => FinishedWords,
        _ => "",
    };

    /// <summary>How urgent a state is: needs you, then working, then finished, then idle (WORK-ORDER-11 section 3).</summary>
    public static int UrgencyOf(HelperState state) => state switch
    {
        HelperState.NeedsYou => 3,
        HelperState.Working => 2,
        HelperState.Finished => 1,
        _ => 0,
    };
}
