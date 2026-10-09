namespace Island.Core;

/// <summary>One thing that is open right now and is not on the island: a program (with its windows), a folder, or a site (with its tabs).</summary>
/// <param name="Key">Stable while the thing stays open: "program:alpha.exe", "folder:Downloads", "site:example.org".</param>
/// <param name="Targets">The window handles or tab ordering numbers a click can go to, top-most or newest first.</param>
public sealed record PlusEntry(
    string Key,
    PickKind Kind,
    string Name,
    string? ExeName,
    string? PackageFamily,
    string? KnownFolder,
    string? Host,
    int Count,
    IReadOnlyList<long> Targets)
{
    /// <summary>The second line of the row: how many windows or tabs.</summary>
    public string Detail => Count <= 1 ? "open" : Kind == PickKind.Site ? $"{Count} tabs" : $"{Count} windows";

    /// <summary>The pick this entry would become on a page. Null when it cannot be stored (never a path).</summary>
    public Pick? ToPick(string pageId)
    {
        var pick = Kind switch
        {
            PickKind.Program => Pick.ForProgram(Name, pageId, ExeName, PackageFamily),
            PickKind.Folder when KnownFolder is not null => Pick.ForFolder(KnownFolder, pageId),
            PickKind.Site when Host is not null => Pick.ForSite(Name, Host, pageId),
            _ => null,
        };
        return pick is not null && pick.IsStorable(out _) ? pick : null;
    }

    public string SuggestedPage => PickSuggest.PageFor(Kind, ExeName, Host, Name);
}

/// <summary>
/// The list behind the + tile: everything that is open right now and is not a pick (EVALS C4), so a thing can be
/// jumped to once or added for good. Lives in memory only: nothing of it is ever written.
/// </summary>
public static class PlusList
{
    /// <summary>The most rows the list shows.</summary>
    public const int MaxRows = 8;

    /// <param name="installed">Used only to give a program the name it is known by.</param>
    /// <param name="ownExe">The island's own program: never offered.</param>
    public static IReadOnlyList<PlusEntry> Candidates(OpenSnapshot open, PickStore store, IReadOnlyList<InstalledProgram> installed, string? ownExe = null)
    {
        var entries = new List<(int Order, PlusEntry Entry)>();

        // One program is one row: windows with a file name group by it; a window with only a package name (a minimised
        // Store window has no file name) joins the group of its package when there is one, else it is a row of its own.
        var windows = open.Windows
            .Where(w => w.ExeName is not null || w.PackageFamily is not null)
            .Where(w => ownExe is null || !string.Equals(w.ExeName, ownExe, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var exeOfPackage = windows.Where(w => w.ExeName is not null && w.PackageFamily is not null)
            .GroupBy(w => w.PackageFamily!.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderBy(w => w.ZOrder).First().ExeName!.ToLowerInvariant());
        string KeyOf(OpenWindow w) =>
            w.ExeName?.ToLowerInvariant()
            ?? (exeOfPackage.TryGetValue(w.PackageFamily!.ToLowerInvariant(), out var exe) ? exe : "package:" + w.PackageFamily!.ToLowerInvariant());

        foreach (var group in windows.GroupBy(KeyOf))
        {
            var first = group.OrderBy(w => w.ZOrder).First();
            if (store.Picks.Any(p => p.Kind == PickKind.Program && PickStates.For(p, new OpenSnapshot([first], [], [], false)).IsOpen)) continue;

            var exe = group.Select(w => w.ExeName).FirstOrDefault(e => e is not null);
            var entry = new PlusEntry($"program:{(exe ?? first.PackageFamily)!.ToLowerInvariant()}", PickKind.Program, NameOf(group.FirstOrDefault(w => w.ExeName is not null) ?? first, installed),
                exe, group.Select(w => w.PackageFamily).FirstOrDefault(f => f is not null), null, null, group.Count(), [.. group.OrderBy(w => w.ZOrder).Select(w => w.Handle)]);
            if (entry.ToPick("x") is null) continue; // something that cannot be stored (a name that is a path) is not offered
            entries.Add((first.ZOrder, entry));
        }

        foreach (var group in open.Folders.Where(f => f.KnownFolder is not null).GroupBy(f => f.KnownFolder!, StringComparer.OrdinalIgnoreCase))
        {
            if (store.Picks.Any(p => p.Kind == PickKind.Folder && string.Equals(p.KnownFolder, group.Key, StringComparison.OrdinalIgnoreCase))) continue;
            var ordered = group.OrderBy(f => f.ZOrder).ToList();
            var folder = new PlusEntry($"folder:{group.Key}", PickKind.Folder, group.Key, null, null, group.Key, null, ordered.Count, [.. ordered.Select(f => f.Handle).Distinct()]);
            if (folder.ToPick("x") is null) continue; // a folder that cannot be stored is not offered
            entries.Add((ordered[0].ZOrder, folder));
        }

        if (open.TabsConnected)
        {
            foreach (var group in open.Tabs.Where(t => !string.IsNullOrEmpty(t.Host)).GroupBy(t => SiteMatch.Normalize(t.Host)))
            {
                if (store.Picks.Any(p => p.Kind == PickKind.Site && p.Host is not null && SiteMatch.Matches(p.Host, group.Key))) continue;
                var newest = group.OrderByDescending(t => t.LastActiveOrder).ToList();
                var site = new PlusEntry($"site:{group.Key}", PickKind.Site, group.Key, null, null, null, group.Key, newest.Count, [.. newest.Select(t => t.LastActiveOrder)]);
                if (site.ToPick("x") is null || System.Net.IPAddress.TryParse(group.Key.Trim('[', ']'), out _)) continue; // a host that cannot be a pick (localhost, an IP address) is not offered
                entries.Add((int.MaxValue / 2 - (int)Math.Min(newest[0].LastActiveOrder, int.MaxValue / 4), site));
            }
        }

        return [.. entries.OrderBy(e => e.Order).Select(e => e.Entry)];
    }

    private static string NameOf(OpenWindow window, IReadOnlyList<InstalledProgram> installed)
    {
        var known = installed.FirstOrDefault(p =>
            window.ExeName is not null && string.Equals(p.ExeName, window.ExeName, StringComparison.OrdinalIgnoreCase)
            || window.PackageFamily is not null && string.Equals(p.PackageFamily, window.PackageFamily, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known.Name;

        var stem = Path.GetFileNameWithoutExtension(window.ExeName ?? window.PackageFamily ?? "program");
        return stem.Length == 0 ? "Program" : char.ToUpperInvariant(stem[0]) + stem[1..];
    }
}
