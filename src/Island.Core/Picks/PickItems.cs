using System.Text;

namespace Island.Core;

/// <summary>Turns the rows of a page (picks and what is open) into the round items the island draws.</summary>
public static class PickItems
{
    /// <summary>The empty page: its one text line says so.</summary>
    public const string NothingHere = "Nothing here yet";

    /// <summary>The Terminals page with no tile (WORK-ORDER-11 section 1).</summary>
    public const string NoTerminal = "No terminal is open";

    /// <summary>Two letters for the tile: the initials of the first two words, or the first two letters of a single word ("Sp", "YM").</summary>
    public static string Mark(string name)
    {
        var words = name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";

        // Whole text elements, never half a character (a letter outside the basic plane is two chars).
        static string First(string w) => System.Globalization.StringInfo.GetNextTextElement(w);
        static string Second(string w) => w.Length > First(w).Length ? System.Globalization.StringInfo.GetNextTextElement(w, First(w).Length) : string.Empty;

        if (words.Length == 1)
        {
            var w = words[0];
            return First(w).ToUpperInvariant() + Second(w).ToLowerInvariant();
        }

        return First(words[0]).ToUpperInvariant() + First(words[1]).ToUpperInvariant();
    }

    /// <summary>A stable hue (0..359) from the id of a pick, so a tile keeps its colour from one start to the next.</summary>
    public static double Hue(string id)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(id)) hash = (hash ^ b) * 16777619u;
        return hash % 360;
    }

    /// <summary>The second line under the name of a pick: what state it is in.</summary>
    public static string Subtitle(Pick pick, PickStatus status)
    {
        if (status.Missing) return "not found";
        if (pick.Kind == PickKind.File) return "file"; // a file has no open or closed
        if (!status.Known) return pick.Kind == PickKind.Program ? "program" : "website"; // a site cannot be known without the add-on; a shortcut's program is not known by name
        if (!status.IsOpen) return "closed";
        if (status.Count <= 1) return "open";
        return pick.Kind == PickKind.Site ? $"{status.Count} tabs" : $"{status.Count} windows";
    }

    public static Item For(PickRow row, IconImage? icon) => new(
        row.Pick.Name,
        Subtitle(row.Pick, row.Status),
        Mark(row.Pick.Name),
        Hue(row.Pick.Id),
        PickId: row.Pick.Id,
        Icon: icon,
        IsClosed: row.Status.Missing || row.Status is { Known: true, IsOpen: false }, // grey: closed, or not found
        Count: row.Status.Count);

    /// <summary>The items of one page: one for every pick. A page with more picks than show at once slides them (<see cref="StripLayout"/>).</summary>
    public static IReadOnlyList<Item> For(IReadOnlyList<PickRow> rows, Func<Pick, IconImage?> iconOf) =>
        [.. rows.Select(r => For(r, iconOf(r.Pick)))];
}

/// <summary>How many tiles a page may show before its capsule would be wider than the screen allows.</summary>
public static class PageFit
{
    /// <summary>Largest number of tiles whose capsule still fits <paramref name="availableWidth"/> (device-independent pixels); at least 1.</summary>
    public static int MaxTiles(double availableWidth, bool isMedia)
    {
        var n = 1;
        while (n < 200 && CapsuleLayout.Width(n + 1, isMedia) <= availableWidth) n++;
        return n;
    }
}
