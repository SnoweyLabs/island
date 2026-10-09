namespace Island.Core;

/// <summary>
/// Which page a new pick is suggested for: a music site on Media, a terminal on Vibe coding, a browser on
/// Browser, a folder on Folders, anything else on Apps. It is only a suggestion; Dan's choice always wins.
/// </summary>
public static class PickSuggest
{
    private static readonly string[] TerminalExes = ["WindowsTerminal.exe", "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wsl.exe", "bash.exe", "conhost.exe"];
    private static readonly string[] MusicHostParts = ["youtube.", "spotify.", "soundcloud.", "twitch.", "deezer.", "tidal.", "bandcamp."];

    public static string PageFor(PickKind kind, string? exeName, string? host, string? name = null)
    {
        switch (kind)
        {
            case PickKind.Folder:
                return PageIds.Folders;
            case PickKind.Site:
                return host is not null && MusicHostParts.Any(part => host.Contains(part, StringComparison.OrdinalIgnoreCase)) ? PageIds.Media : PageIds.Browser;
            default:
                if (StarterPicks.Programs.FirstOrDefault(s =>
                        exeName is not null && s.ExeCandidates.Contains(exeName, StringComparer.OrdinalIgnoreCase)
                        || name is not null && s.NameHints.Contains(name, StringComparer.OrdinalIgnoreCase)) is { } starter)
                    return starter.PageId;
                if (exeName is not null && TerminalExes.Contains(exeName, StringComparer.OrdinalIgnoreCase)) return PageIds.Vibe;
                return PageIds.Apps;
        }
    }
}
