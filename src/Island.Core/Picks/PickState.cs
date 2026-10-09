namespace Island.Core;

/// <summary>
/// Whether a pick is open and how many windows or tabs it has. <see cref="Known"/> is false when that cannot be
/// known: a website pick while no add-on is connected. <see cref="Targets"/> are the window handles or tab keys'
/// numbers a click can go to, newest first. <see cref="Missing"/> is set for a thing added by hand whose place is known not to be there any
/// more (<see cref="PickTargetCache"/>): the tile is grey, says "not found", and a click answers PICK_TARGET_MISSING.
/// </summary>
public sealed record PickStatus(bool Known, bool IsOpen, int Count, IReadOnlyList<long> Targets, IReadOnlyList<string>? Identities = null, bool Missing = false)
{
    public static PickStatus Closed { get; } = new(true, false, 0, []);

    public static PickStatus Unknown { get; } = new(false, false, 0, []);
}

public enum ClickKind
{
    None,
    BringForward,
    Start,
    OpenFolder,
    OpenSite,

    /// <summary>A folder added by hand: open it at its <see cref="Pick.Location"/> (expanded in memory).</summary>
    OpenFolderAt,

    /// <summary>A file: ask Windows to open it with whatever opens it.</summary>
    OpenFile,

    /// <summary>The place is not there any more: nothing is opened, the answer is PICK_TARGET_MISSING (<see cref="HandPickRefusals.ForMissing"/>).</summary>
    TargetMissing,
}

/// <summary>What a click on a pick does. <see cref="Target"/> is the window handle for <see cref="ClickKind.BringForward"/>.</summary>
public sealed record ClickPlan(ClickKind Kind, long Target = 0);

public static class PickStates
{
    /// <summary>Open or closed, with the count, for one pick, from a snapshot of what is open.</summary>
    public static PickStatus For(Pick pick, OpenSnapshot open) => For(pick, open, PickContext.None);

    /// <summary>
    /// The same, knowing the profile folder (to put a stored place back in memory) and what is known about whether places are there.
    /// Never touches the disk: a place that was not asked about yet counts as present.
    /// </summary>
    public static PickStatus For(Pick pick, OpenSnapshot open, PickContext context)
    {
        var status = Read(pick, open, context);
        return !status.IsOpen && pick.Location is not null && context.Targets?.Presence(pick, context.ProfileFolder) == TargetPresence.Missing
            ? status with { Missing = true }
            : status;
    }

    private static PickStatus Read(Pick pick, OpenSnapshot open, PickContext context)
    {
        switch (pick.Kind)
        {
            case PickKind.Program when pick.ExeName is null && pick.PackageFamily is null:
                return PickStatus.Unknown; // a shortcut chosen with "Browse": its program is not known by name, so open or closed cannot be told

            case PickKind.Program:
                return FromTargets(open.Windows
                    .Where(w => ProgramMatches(pick, w))
                    .OrderBy(w => w.ZOrder)
                    .Select(w => w.Handle));

            case PickKind.File:
                return PickStatus.Unknown; // a file has no open or closed: always bright

            case PickKind.Folder when pick.Location is not null:
            {
                // A folder added by hand is open when an Explorer window's location is its place; both sides are compared in memory.
                var key = PickPath.RealKey(PickPath.Expand(pick.Location, context.ProfileFolder));
                var entries = open.Folders.Where(f => key is not null && PickPath.RealKey(f.PathInMemory) == key).OrderBy(f => f.ZOrder).ToList();
                var frames = entries.Select(f => f.Handle).Distinct().ToList();
                return new PickStatus(true, entries.Count > 0, entries.Count, frames, [.. frames.Select(h => "w" + h)]);
            }

            case PickKind.Folder:
            {
                // One entry per tab: the count is the number of tabs and windows, but a click goes to a frame, so a
                // frame that has several tabs is one target.
                var entries = open.Folders
                    .Where(f => pick.KnownFolder is not null && string.Equals(f.KnownFolder, pick.KnownFolder, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.ZOrder)
                    .ToList();
                var frames = entries.Select(f => f.Handle).Distinct().ToList();
                return new PickStatus(true, entries.Count > 0, entries.Count, frames, [.. frames.Select(h => "w" + h)]);
            }

            case PickKind.Site:
                if (!open.TabsConnected) return PickStatus.Unknown;
            {
                // A tab is told apart by its key: its ordering number changes every time it is activated.
                var tabs = open.Tabs
                    .Where(t => pick.Host is not null && SiteMatch.Matches(pick.Host, t.Host))
                    .OrderByDescending(t => t.LastActiveOrder)
                    .ToList();
                return new PickStatus(true, tabs.Count > 0, tabs.Count, [.. tabs.Select(t => t.LastActiveOrder)], [.. tabs.Select(t => "t" + t.Key)]);
            }

            default:
                return PickStatus.Closed;
        }
    }

    /// <summary>What a click does: bring the next window forward (newest first, cycling), or start / open the pick when it is closed or unknown.</summary>
    public static ClickPlan Plan(Pick pick, PickStatus status, ClickCycler cycler)
    {
        if (status.Missing)
        {
            cycler.Reset(pick.Id);
            return new ClickPlan(ClickKind.TargetMissing);
        }

        if (pick.Kind == PickKind.File) return new ClickPlan(ClickKind.OpenFile);

        if (status is { Known: true, IsOpen: true } && cycler.Next(pick.Id, status.Targets, status.Identities ?? [.. status.Targets.Select(t => "w" + t)]) is { } target)
            return new ClickPlan(ClickKind.BringForward, target);

        cycler.Reset(pick.Id);
        return pick.Kind switch
        {
            PickKind.Program => new ClickPlan(ClickKind.Start),
            PickKind.Folder => new ClickPlan(pick.Location is not null ? ClickKind.OpenFolderAt : ClickKind.OpenFolder),
            PickKind.Site => new ClickPlan(ClickKind.OpenSite),
            _ => new ClickPlan(ClickKind.None),
        };
    }

    private static bool ProgramMatches(Pick pick, OpenWindow w) =>
        pick.ExeName is not null && string.Equals(pick.ExeName, w.ExeName, StringComparison.OrdinalIgnoreCase)
        || pick.PackageFamily is not null && string.Equals(pick.PackageFamily, w.PackageFamily, StringComparison.OrdinalIgnoreCase);

    private static PickStatus FromTargets(IEnumerable<long> targets)
    {
        var list = targets.ToList();
        return new PickStatus(true, list.Count > 0, list.Count, list, [.. list.Select(h => "w" + h)]);
    }
}

/// <summary>A row of a page: one pick and its state.</summary>
public sealed record PickRow(Pick Pick, PickStatus Status);

public static class PickList
{
    /// <summary>
    /// The rows of one page: the picks of that page and nothing else. Whatever else is open on the laptop
    /// is not listed (EVALS C1).
    /// </summary>
    public static IReadOnlyList<PickRow> RowsFor(string pageId, PickStore store, OpenSnapshot open) => RowsFor(pageId, store, open, PickContext.None);

    /// <summary>The same, with the profile folder and what is known about whether things added by hand are still there.</summary>
    public static IReadOnlyList<PickRow> RowsFor(string pageId, PickStore store, OpenSnapshot open, PickContext context) =>
        [.. store.Picks.Where(p => p.PageId == pageId).Select(p => new PickRow(p, PickStates.For(p, open, context)))];
}
