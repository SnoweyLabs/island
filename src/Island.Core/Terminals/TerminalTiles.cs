namespace Island.Core.Terminals;

/// <summary>
/// WORK-ORDER-11 sections 1 and 2: from one reading to the tiles of the Terminals page, texts, faces, rings and dots. Pure; the order of
/// the tiles is the reading's own (<see cref="TerminalPage"/> puts them in first-seen order).
/// </summary>
public static class TerminalTiles
{
    /// <summary>
    /// The tiles of one reading, in the order the windows came in. <paramref name="helperOrder"/> says which of several helpers in one window
    /// was seen first (null: the order the reading gives them).
    /// </summary>
    public static IReadOnlyList<TerminalTile> Build(TerminalReading? reading, TerminalTables tables, FirstSeen<long>? helperOrder = null)
    {
        reading ??= TerminalReading.Empty;
        var page = PageWindows.From(reading.Windows, tables);
        var helpers = HelperFinder.Collect(reading, page, tables);
        var rank = RankOf(helpers, helperOrder);

        var inWindow = new Dictionary<long, List<HelperInstance>>();
        foreach (var h in helpers)
        {
            if (HelperFinder.WindowOf(h.Chain, h.ProjectName, h.FromHook, reading.ConsoleWindows, page) is not { } handle) continue; // no window, no tile
            if (!inWindow.TryGetValue(handle, out var list)) inWindow[handle] = list = [];
            list.Add(h);
        }

        return [.. page.All.Select(w => TileOf(w, page.Roles[w.Handle], tables, inWindow.GetValueOrDefault(w.Handle), rank))];
    }

    private static Dictionary<long, int> RankOf(IReadOnlyList<HelperInstance> helpers, FirstSeen<long>? helperOrder)
    {
        var keys = helperOrder is null ? [.. helpers.Select(h => h.Key)] : helperOrder.Order(helpers.Select(h => h.Key));
        var rank = new Dictionary<long, int>();
        foreach (var k in keys) rank[k] = rank.Count;
        return rank;
    }

    private static TerminalTile TileOf(TermWindowFact w, WindowRole role, TerminalTables tables, List<HelperInstance>? helpers, Dictionary<long, int> rank)
    {
        var title = TerminalClassify.CleanTitle(w.Title);
        var programName = role == WindowRole.AiProgram ? tables.AiProgramOf(w.ExeName, w.PackageFamily)?.Name ?? ProgramNameOf(w, tables) : ProgramNameOf(w, tables);
        var plainFace = new TileFace(FaceKind.ProgramIcon, w.ExeName, w.PackageFamily, PickItems.Mark(programName), null);

        var chosen = helpers is { Count: > 0 }
            ? helpers.OrderByDescending(h => TerminalConstants.UrgencyOf(h.State)).ThenBy(h => rank.GetValueOrDefault(h.Key, int.MaxValue)).First()
            : null;
        var dots = WindowDots.For(helpers?.Count ?? 0);

        if (role == WindowRole.AiProgram)
        {
            // A helper reporting from inside an AI program's window only gains the ring and the dots.
            return new TerminalTile(w.Handle, role, programName, title.Length > 0 ? title : programName, plainFace, chosen?.State ?? HelperState.Idle, dots, null);
        }

        if (chosen is null)
            return new TerminalTile(w.Handle, role, title.Length > 0 ? title : programName, TerminalConstants.TerminalWord, plainFace, HelperState.Idle, 0, null);

        var hasProject = chosen.ProjectName.Length > 0;
        var first = hasProject ? chosen.ProjectName : chosen.HelperName;
        // With a project the line is the helper's name and the state words (short). Without one it is the window's title, which can be long and is cut at its end by the tile: the state words come FIRST then
        // so that they are never the part that is cut (Dan's P22, WORK-ORDER-13).
        var second = hasProject
            ? chosen.HelperName + TerminalConstants.WordsOf(chosen.State)
            : TerminalConstants.WordsOf(chosen.State) is { Length: > 0 } words ? words.TrimStart(' ', '·') + " · " + (title.Length > 0 ? title : programName) : title.Length > 0 ? title : programName;
        var face = new TileFace(FaceKind.HelperDisc, w.ExeName, w.PackageFamily, PickItems.Mark(first), tables.ColorOf(chosen.HelperName));
        return new TerminalTile(w.Handle, role, first, second, face, chosen.State, dots, chosen.HelperName);
    }

    /// <summary>The name of a window's program: its name in the table, else its file name without ".exe", else "Terminal".</summary>
    private static string ProgramNameOf(TermWindowFact w, TerminalTables tables)
    {
        if (tables.TerminalProgramOf(w.ExeName) is { } row) return row.Name;
        var file = w.ExeName ?? "";
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
        var clean = TerminalClassify.CleanName(file);
        return clean.Length > 0 ? clean : TerminalConstants.UnknownTerminalName;
    }
}
