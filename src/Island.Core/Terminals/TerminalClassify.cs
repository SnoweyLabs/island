using System.Text;

namespace Island.Core.Terminals;

/// <summary>Which windows are on the Terminals page, and how a window's title is made fit to draw (WORK-ORDER-11 section 1).</summary>
public static class TerminalClassify
{
    /// <summary>
    /// A window that is a terminal by its class is a terminal, whatever its program is called; otherwise a terminal by its program's file
    /// name; otherwise a window of an AI program by file name or package name's beginning; otherwise it is not on the page. One window, one role.
    /// </summary>
    public static WindowRole RoleOf(TermWindowFact window, TerminalTables tables)
    {
        if (tables.IsTerminalClass(window.ClassName)) return WindowRole.Terminal;
        if (tables.TerminalProgramOf(window.ExeName) is not null) return WindowRole.Terminal;
        if (tables.AiProgramOf(window.ExeName, window.PackageFamily) is not null) return WindowRole.AiProgram;
        return WindowRole.None;
    }

    /// <summary>
    /// The window's title as a tile shows it: cleaned like a project's name (<see cref="AgentText.Clean"/>), leading characters that are neither
    /// letters nor digits dropped, cut to <see cref="TerminalConstants.MaxTitleChars"/>. "" when nothing readable is left. Never throws.
    /// </summary>
    public static string CleanTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        var cleaned = AgentText.Clean(AgentText.Head(title, TerminalConstants.TitleReadLimit));
        var start = 0;
        foreach (var rune in cleaned.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) break;
            start += rune.Utf16SequenceLength;
        }

        var cut = AgentText.Head(cleaned[start..], TerminalConstants.MaxTitleChars).TrimEnd();
        return BlankText.HasVisible(cut) ? cut : "";
    }

    /// <summary>A name that arrives from outside (a project's name), cleaned and cut like a project's name. "" when it draws as nothing.</summary>
    public static string CleanName(string? name)
    {
        var cut = AgentText.Head(AgentText.Clean(AgentText.Head(name ?? "", TerminalConstants.TitleReadLimit)), ProjectName.MaxChars).Trim();
        return BlankText.HasVisible(cut) ? cut : "";
    }
}

/// <summary>The windows of one reading that are on the page, kept so that nothing is classified twice.</summary>
public sealed class PageWindows
{
    private readonly Dictionary<int, List<TermWindowFact>> byOwner = [];

    private PageWindows(IReadOnlyList<TermWindowFact> all, IReadOnlyDictionary<long, WindowRole> roles, IReadOnlyList<TermWindowFact> terminals, IReadOnlyList<TermWindowFact> aiWindows)
    {
        All = all;
        Roles = roles;
        Terminals = terminals;
        AiWindows = aiWindows;
        foreach (var w in all)
        {
            if (!byOwner.TryGetValue(w.OwnerProcessId, out var list)) byOwner[w.OwnerProcessId] = list = [];
            list.Add(w);
        }
    }

    /// <summary>Every window on the page, in the order the reading gave them; a handle that appears twice counts once (the first).</summary>
    public IReadOnlyList<TermWindowFact> All { get; }

    public IReadOnlyDictionary<long, WindowRole> Roles { get; }

    public IReadOnlyList<TermWindowFact> Terminals { get; }

    public IReadOnlyList<TermWindowFact> AiWindows { get; }

    public static PageWindows From(IReadOnlyList<TermWindowFact>? windows, TerminalTables tables)
    {
        var all = new List<TermWindowFact>();
        var roles = new Dictionary<long, WindowRole>();
        var terminals = new List<TermWindowFact>();
        var ai = new List<TermWindowFact>();
        foreach (var w in windows ?? [])
        {
            if (w is null || roles.ContainsKey(w.Handle)) continue;
            var role = TerminalClassify.RoleOf(w, tables);
            if (role == WindowRole.None) continue;
            roles[w.Handle] = role;
            all.Add(w);
            (role == WindowRole.Terminal ? terminals : ai).Add(w);
        }

        return new PageWindows(all, roles, terminals, ai);
    }

    /// <summary>The windows of the page that this process owns; empty when none.</summary>
    public IReadOnlyList<TermWindowFact> OwnedBy(int processId) => byOwner.TryGetValue(processId, out var list) ? list : [];

    /// <summary>The terminal window with this handle, or null.</summary>
    public TermWindowFact? TerminalByHandle(long handle) =>
        Roles.TryGetValue(handle, out var role) && role == WindowRole.Terminal ? Terminals.FirstOrDefault(w => w.Handle == handle) : null;
}
