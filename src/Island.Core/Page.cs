namespace Island.Core;

/// <summary>
/// One round item on a page. Hue is the item's hue in degrees (0..360). A placeholder has only the first four
/// values; a row built from a pick also carries the pick it stands for, the program's or folder's own icon
/// (when Windows gave one), whether the thing is closed, and how many windows or tabs it has. The + tile
/// (<see cref="IsPlus"/>) opens the second row. A tile of the Terminals page (WORK-ORDER-11) has no pick: its identity is its window
/// (<see cref="WindowKey"/>, in memory only), and it may carry a state ring (<see cref="Ring"/>, idle = none) and a disc colour of its own (<see cref="Disc"/>). A page with more picks than fit slides them (WORK-ORDER-5 §4).
/// </summary>
public sealed record Item(
    string Title,
    string Subtitle,
    string Mark,
    double Hue,
    string? PickId = null,
    IconImage? Icon = null,
    bool IsClosed = false,
    int Count = 0,
    bool IsPlus = false,
    long? WindowKey = null,
    Island.Core.Terminals.HelperState Ring = Island.Core.Terminals.HelperState.Idle,
    Island.Core.Terminals.HelperColor? Disc = null);

/// <summary>
/// A page of the island (what the preview calls a category). Pages are data, not a fixed list of
/// five: Dan will later create his own. Colour is #RRGGBB, keybind is a suggested combination as
/// text (or null for none; <see cref="Settings.Defaults"/> no longer uses it: since 6 Oct 2026 no page
/// has a global keybind by default), and the five built-in pages cannot be deleted.
/// </summary>
public sealed record Page(string Id, string Name, string Color, string Glyph, string? Keybind, bool IsBuiltIn)
{
    /// <summary>The Media page has previous, play/pause and next controls; every other page has one close button.</summary>
    public bool IsMedia => Id == PageIds.Media;
}

/// <summary>A page together with the rows it shows. Placeholders for now; picks later.</summary>
public sealed record PageContents(Page Page, IReadOnlyList<Item> Items);

/// <summary>Ids of the six built-in pages.</summary>
public static class PageIds
{
    public const string Media = "media";
    public const string Folders = "folders";
    public const string Apps = "apps";
    public const string Vibe = "vibe";
    public const string Browser = "browser";

    /// <summary>The page that fills itself with the terminals and AI programs that are open (WORK-ORDER-11 section 1). No pick can live on it.</summary>
    public const string Terminals = "terminals";

    /// <summary>A pick can be put on every page but the one that fills itself.</summary>
    public static bool CanHoldPicks(string? pageId) => pageId != Terminals;
}

/// <summary>The six built-in pages with their pinned colours, and the hard-coded placeholder items copied from CATS in the reference.</summary>
public static class Pages
{
    public static IReadOnlyList<Page> BuiltIn { get; } =
    [
        new(PageIds.Media, "Media", LookConstants.MediaColor, "play", "Ctrl+Alt+Shift+1", true),
        new(PageIds.Folders, "Folders", LookConstants.FoldersColor, "folder", "Ctrl+Alt+Shift+2", true),
        new(PageIds.Apps, "Apps", LookConstants.AppsColor, "grid", "Ctrl+Alt+Shift+3", true),
        new(PageIds.Vibe, "Vibe coding", LookConstants.VibeColor, "term", "Ctrl+Alt+Shift+4", true),
        new(PageIds.Browser, "Browser", LookConstants.BrowserColor, "globe", "Ctrl+Alt+Shift+5", true),
        new(PageIds.Terminals, "Terminals", LookConstants.TerminalsColor, "terminal", null, true),
    ];

    // Invented sample rows for the placeholder look (the self-test and the approved previews): names of nothing real, no program, site or folder of anyone.
    private static readonly Dictionary<string, Item[]> Placeholders = new()
    {
        [PageIds.Media] =
        [
            new("Lo-fi mix", "Music · tab", "Mx", 350),
            new("Daily mix", "Player", "Dm", 140),
            new("Tutorial clip", "Video · tab", "Tc", 8),
        ],
        [PageIds.Folders] =
        [
            new("Projects", "Documents", "Pr", 40),
            new("Downloads", "This PC", "Dl", 210),
            new("Clips", "Videos", "Cl", 285),
            new("Screenshots", "Pictures", "Sc", 170),
        ],
        [PageIds.Apps] =
        [
            new("Editor", "clip one", "Ed", 265),
            new("Chat", "2 windows", "Ch", 235),
            new("Messages", "Alpha", "Ms", 200),
            new("Notepad", "notes.txt", "Np", 185),
            new("Settings", "Display", "Se", 220),
        ],
        [PageIds.Vibe] =
        [
            new("Assistant", "Desktop app", "As", 22),
            new("Coder", "project · main", "Co", 215),
            new("Terminal", "PowerShell", "PS", 228),
            new("Terminal", "Linux · shell", "Li", 30),
        ],
        // The Terminals page fills itself in the app; these invented rows are only what a stage without a world draws.
        [PageIds.Terminals] =
        [
            new("Shell", "terminal", "Sh", 90),
            new("Coder", "Helper · working", "Co", 215),
            new("Notes", "terminal", "No", 30),
        ],
        [PageIds.Browser] =
        [
            new("Templates", "example.org", "Te", 205),
            new("Project repo", "example.net", "Pr", 250),
            new("Docs", "example.com", "Dc", 130),
            new("Inbox", "mail.example", "Ml", 275),
            new("Studio", "example.org", "St", 340),
        ],
    };

    public static Page Get(string id) =>
        BuiltIn.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"No built-in page '{id}'.");

    /// <summary>The placeholder rows of a built-in page; any other page has none yet.</summary>
    public static IReadOnlyList<Item> PlaceholderItems(Page page) =>
        Placeholders.TryGetValue(page.Id, out var items) ? items : [];

    public static PageContents Placeholder(Page page) => new(page, PlaceholderItems(page));

    public static PageContents Placeholder(string id) => Placeholder(Get(id));

    /// <summary>Every built-in page with its placeholder rows.</summary>
    public static IReadOnlyList<PageContents> AllPlaceholders { get; } = [.. BuiltIn.Select(Placeholder)];
}
