namespace Island.Core.Terminals;

/// <summary>A terminal program by file name. <paramref name="Unconfirmed"/>: the author could not confirm the name (Claude).</summary>
public sealed record TerminalProgramRow(string FileName, string Name, bool Unconfirmed = false);

/// <summary>An AI program with its own window: its name, the file names of its windows' programs, and the beginnings of its package names.</summary>
public sealed record AiProgramRow(string Name, IReadOnlyList<string> ExeNames, IReadOnlyList<string> PackagePrefixes, bool Unconfirmed = false);

/// <summary>A terminal helper by the file name of its process.</summary>
public sealed record HelperProgramRow(string FileName, string HelperName, bool Unconfirmed = false);

/// <summary>
/// The tables of the Terminals page, handed in as data so that tests and the self-test can hand in invented ones
/// (WORK-ORDER-11 sections 1 and 2). <see cref="Default"/> is the real set.
/// </summary>
public sealed record TerminalTables(
    IReadOnlyList<string> TerminalClasses,
    IReadOnlyList<TerminalProgramRow> TerminalPrograms,
    IReadOnlyList<AiProgramRow> AiPrograms,
    IReadOnlyList<HelperProgramRow> HelperPrograms,
    IReadOnlyDictionary<string, HelperColor> HelperColors)
{
    /// <summary>
    /// The real tables. The terminal names are those of <c>PickSuggest.TerminalExes</c> (copied: that list is private) plus three more
    /// (Claude, unconfirmed). The AI programs start from <see cref="StarterPicks.Programs"/>; every name in it, and the whole of
    /// ChatGPT's and Windsurf's rows, is UNVERIFIED: no official page was read for it (Research/agent-sessions.md could not confirm any
    /// of them), so an AI program installed under another name does not appear until its name is added here.
    /// </summary>
    public static TerminalTables Default { get; } = Build();

    public static TerminalTables Empty { get; } = new([], [], [], [], new Dictionary<string, HelperColor>());

    private static TerminalTables Build() => new(
        [TerminalConstants.WindowsTerminalClass, TerminalConstants.ClassicConsoleClass],
        [
            new("WindowsTerminal.exe", "Windows Terminal"),
            new("wt.exe", "Windows Terminal"),
            new("cmd.exe", "Command Prompt"),
            new("powershell.exe", "PowerShell"),
            new("pwsh.exe", "PowerShell"),
            new("wsl.exe", "WSL"),
            new("bash.exe", "Bash"),
            new("conhost.exe", "Console"),
            new("wezterm-gui.exe", "WezTerm", Unconfirmed: true), // (Claude, unconfirmed)
            new("alacritty.exe", "Alacritty", Unconfirmed: true), // (Claude, unconfirmed)
            new("mintty.exe", "Mintty", Unconfirmed: true), // (Claude, unconfirmed)
        ],
        [
            FromStarter("Claude"),
            new("ChatGPT", ["ChatGPT.exe"], ["OpenAI.ChatGPT-Desktop", "OpenAI.Codex"], Unconfirmed: true), // UNVERIFIED (Claude)
            FromStarter("Antigravity"),
            FromStarter("Cursor"),
            new("Windsurf", ["Windsurf.exe"], [], Unconfirmed: true), // UNVERIFIED (Claude)
        ],
        [
            new("claude.exe", TerminalConstants.ClaudeCode),
            new("codex.exe", TerminalConstants.Codex),
            new("agy.exe", TerminalConstants.Antigravity, Unconfirmed: true), // (Claude, unconfirmed)
        ],
        new Dictionary<string, HelperColor>(StringComparer.OrdinalIgnoreCase)
        {
            [TerminalConstants.ClaudeCode] = TerminalConstants.ClaudeCodeColor,
            [TerminalConstants.Codex] = TerminalConstants.CodexColor,
            [TerminalConstants.Antigravity] = TerminalConstants.AntigravityColor,
            [TerminalConstants.Gemini] = TerminalConstants.GeminiColor,
        });

    // The candidates come from the starter list; its names were only partly confirmed, so the row is marked so.
    private static AiProgramRow FromStarter(string name)
    {
        var starter = StarterPicks.Programs.First(p => p.Name == name);
        return new AiProgramRow(name, starter.ExeCandidates, starter.PackageHints, Unconfirmed: true);
    }

    /// <summary>True when the window class is one of the terminal classes (case-insensitive).</summary>
    public bool IsTerminalClass(string? className) =>
        !string.IsNullOrEmpty(className) && TerminalClasses.Contains(className, StringComparer.OrdinalIgnoreCase);

    public TerminalProgramRow? TerminalProgramOf(string? exeName) =>
        string.IsNullOrEmpty(exeName) ? null : TerminalPrograms.FirstOrDefault(r => string.Equals(r.FileName, exeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The AI program a window's own program file name, or its package name's beginning, belongs to; null when none.</summary>
    public AiProgramRow? AiProgramOf(string? exeName, string? packageFamily)
    {
        foreach (var row in AiPrograms)
        {
            if (!string.IsNullOrEmpty(exeName) && row.ExeNames.Contains(exeName, StringComparer.OrdinalIgnoreCase)) return row;
            if (!string.IsNullOrEmpty(packageFamily) && row.PackagePrefixes.Any(p => p.Length > 0 && packageFamily.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return row;
        }

        return null;
    }

    /// <summary>The helper whose process has this file name; null when none.</summary>
    public HelperProgramRow? HelperOf(string? exeName) =>
        string.IsNullOrEmpty(exeName) ? null : HelperPrograms.FirstOrDefault(r => string.Equals(r.FileName, exeName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The disc colour of a helper; <see cref="TerminalConstants.UnknownHelperColor"/> when the table has none.</summary>
    public HelperColor ColorOf(string? helperName) =>
        helperName is not null && HelperColors.TryGetValue(helperName, out var c) ? c : TerminalConstants.UnknownHelperColor;
}
