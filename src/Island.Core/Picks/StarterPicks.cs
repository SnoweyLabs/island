namespace Island.Core;

/// <summary>
/// One program the starter list would like to have. Windows' executable names are only partly known
/// (Research/windows-apis.md section 9), so each program lists several candidates; the one actually found among
/// the programs installed on this laptop is what the pick stores.
/// </summary>
public sealed record StarterProgram(string Name, string PageId, string[] ExeCandidates, string[] PackageHints, string[] NameHints);

/// <summary>The starter list, applied once when no picks file exists, never under the self-test, never again.</summary>
public static class StarterPicks
{
    public static IReadOnlyList<StarterProgram> Programs { get; } =
    [
        new("Spotify", PageIds.Media, ["Spotify.exe"], ["SpotifyAB.SpotifyMusic"], ["Spotify"]),

        new("Claude", PageIds.Vibe, ["Claude.exe"], ["Claude_pzs8sxrjxfjjc", "AnthropicPBC.Claude"], ["Claude"]),
        new("Antigravity", PageIds.Vibe, ["Antigravity.exe"], [], ["Antigravity"]),
        new("Windows Terminal", PageIds.Vibe, ["WindowsTerminal.exe", "wt.exe"], ["Microsoft.WindowsTerminal"], ["Windows Terminal", "Terminal"]),
        new("Visual Studio Code", PageIds.Vibe, ["Code.exe"], [], ["Visual Studio Code"]),
        new("Cursor", PageIds.Vibe, ["Cursor.exe"], [], ["Cursor"]),

        new("Discord", PageIds.Apps, ["Discord.exe"], [], ["Discord"]),
        new("Telegram", PageIds.Apps, ["Telegram.exe"], ["TelegramMessengerLLP.TelegramDesktop"], ["Telegram"]),
        new("Premiere Pro", PageIds.Apps, ["Adobe Premiere Pro.exe"], [], ["Premiere Pro"]),
        new("After Effects", PageIds.Apps, ["AfterFX.exe"], [], ["After Effects"]),
        new("OBS Studio", PageIds.Apps, ["obs64.exe", "obs32.exe"], [], ["OBS Studio"]),
        new("Notepad", PageIds.Apps, ["notepad.exe"], ["Microsoft.WindowsNotepad"], ["Notepad"]),

        new("Chrome", PageIds.Browser, ["chrome.exe"], [], ["Google Chrome", "Chrome"]),
        new("Edge", PageIds.Browser, ["msedge.exe"], [], ["Microsoft Edge", "Edge"]),
        new("Firefox", PageIds.Browser, ["firefox.exe"], [], ["Firefox"]),
        new("Brave", PageIds.Browser, ["brave.exe"], [], ["Brave"]),
    ];

    public static IReadOnlyList<(string Name, string Host)> MediaSites { get; } =
    [
        ("YouTube", "youtube.com"),
        ("YouTube Music", "music.youtube.com"),
        ("Twitch", "twitch.tv"),
        ("SoundCloud", "soundcloud.com"),
    ];

    public static IReadOnlyList<string> Folders { get; } = ["Downloads", "Documents", "Desktop"];

    /// <summary>The installed program that a starter program means, or null when it is not installed (it is then left out).</summary>
    public static InstalledProgram? Find(StarterProgram starter, IReadOnlyList<InstalledProgram> installed)
    {
        foreach (var exe in starter.ExeCandidates)
            if (installed.FirstOrDefault(p => string.Equals(p.ExeName, exe, StringComparison.OrdinalIgnoreCase)) is { } byExe)
                return byExe;

        foreach (var hint in starter.PackageHints)
            if (installed.FirstOrDefault(p => p.PackageFamily is { } f && f.StartsWith(hint, StringComparison.OrdinalIgnoreCase)) is { } byPackage)
                return byPackage;

        foreach (var hint in starter.NameHints)
            if (installed.FirstOrDefault(p => string.Equals(p.Name, hint, StringComparison.OrdinalIgnoreCase)) is { } byName)
                return byName;

        return null;
    }

    /// <summary>
    /// The starter picks for this laptop: the starter programs that are installed (each with the name it was
    /// actually found under), the media sites, and the three known folders. Programs that are not installed are left out.
    /// </summary>
    public static IReadOnlyList<Pick> Build(IReadOnlyList<InstalledProgram> installed)
    {
        var picks = new List<Pick>();

        foreach (var starter in Programs)
        {
            if (Find(starter, installed) is not { } found) continue;
            var pick = Pick.ForProgram(starter.Name, starter.PageId, found.ExeName, found.PackageFamily);
            if (pick.IsStorable(out _) && picks.All(p => p.Id != pick.Id)) picks.Add(pick);
        }

        foreach (var (name, host) in MediaSites) picks.Add(Pick.ForSite(name, host, PageIds.Media));
        foreach (var folder in Folders) picks.Add(Pick.ForFolder(folder, PageIds.Folders));

        // The page order of the island: Media, Folders, Apps, Vibe coding, Browser.
        var order = new[] { PageIds.Media, PageIds.Folders, PageIds.Apps, PageIds.Vibe, PageIds.Browser };
        return [.. picks.OrderBy(p => Array.IndexOf(order, p.PageId))];
    }

    /// <summary>How many starter programs were found among <paramref name="installed"/>.</summary>
    public static int CountFound(IReadOnlyList<InstalledProgram> installed) =>
        Programs.Count(p => Find(p, installed) is not null);
}
