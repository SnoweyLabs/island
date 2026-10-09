namespace Island.Core;

/// <summary>
/// review/perf.md is rewritten by the ordinary self-test (PerfStage) but keeps the sections that other runs wrote under their own headings: the hidden cost of
/// WORK-ORDER-6 section 6 and, from WORK-ORDER-11 section 4, everything about what the open island costs (the before table, the list of causes with what each fix did, the
/// after table). Pure text: a section runs from its heading up to the next heading of the same level.
/// </summary>
public static class PerfSections
{
    /// <summary>The heading of the hidden-cost section (WORK-ORDER-6 section 6).</summary>
    public const string HiddenCost = "## What it costs while hidden (WORK-ORDER-6 section 6)";

    /// <summary>
    /// The beginning every heading of WORK-ORDER-11 section 4 has: "What the open island costs (WORK-ORDER-11 §4) — before", "— where it goes", "— after"
    /// (and any further one the section adds). All of them are kept when the file is rewritten.
    /// </summary>
    public const string OpenCostPrefix = "## What the open island costs (WORK-ORDER-11";

    /// <summary>The beginning of the heading of how quickly the island answers (WORK-ORDER-12 sections 1 and 3: "— before", "— where it goes", "— after"). All of them are kept.</summary>
    public const string SpeedPrefix = "## How quickly it answers (WORK-ORDER-12";

    /// <summary>The beginning of the heading of what the open island costs from WORK-ORDER-12 section 2 on (with the kind of moving light as a column). Kept.</summary>
    public const string OpenCostPrefix12 = "## What the open island costs (WORK-ORDER-12";

    /// <summary>The sections that a rewrite keeps, each as one block ending in one line break, in the order they stand in the text.</summary>
    public static IReadOnlyList<string> Kept(string? text)
    {
        var normal = (text ?? string.Empty).Replace("\r\n", "\n");
        var kept = new List<string>();
        var at = 0;
        while (at < normal.Length)
        {
            var start = IndexOfKeptHeading(normal, at);
            if (start < 0) break;
            var next = normal.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
            var end = next < 0 ? normal.Length : next + 1;
            kept.Add(normal[start..end].TrimEnd('\n') + "\n");
            at = end;
        }

        return kept;
    }

    /// <summary>The text of a rewrite followed by every kept section of the earlier text, each after a blank line.</summary>
    public static string WithKept(string rewritten, string? earlier)
    {
        var text = rewritten.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        foreach (var section in Kept(earlier)) text += "\n" + section;
        return text;
    }

    /// <summary>Puts a section (its first line is its heading) in place of the earlier one with the same heading, or after everything else.</summary>
    public static string Upsert(string? existing, string section)
    {
        var normal = (existing ?? string.Empty).Replace("\r\n", "\n");
        var fresh = section.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        var heading = fresh[..fresh.IndexOf('\n')];
        var start = IndexOfHeading(normal, heading);
        if (start < 0) return normal.TrimEnd('\n') + (normal.Length == 0 ? string.Empty : "\n\n") + fresh;
        var next = normal.IndexOf("\n## ", start + 3, StringComparison.Ordinal);
        var end = next < 0 ? normal.Length : next + 1;
        return normal[..start] + fresh + (end < normal.Length ? "\n" : string.Empty) + normal[end..];
    }

    private static int IndexOfHeading(string normal, string heading)
    {
        if (normal.StartsWith(heading + "\n", StringComparison.Ordinal) || normal == heading) return 0;
        var at = normal.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        if (at >= 0) return at + 1;
        return normal.EndsWith("\n" + heading, StringComparison.Ordinal) ? normal.Length - heading.Length : -1;
    }

    private static int IndexOfKeptHeading(string normal, int from)
    {
        var best = -1;
        foreach (var prefix in new[] { HiddenCost, OpenCostPrefix, SpeedPrefix, OpenCostPrefix12 })
        {
            var at = FindLineStart(normal, prefix, from);
            if (at >= 0 && (best < 0 || at < best)) best = at;
        }

        return best;
    }

    private static int FindLineStart(string normal, string prefix, int from)
    {
        var at = from;
        while (at <= normal.Length)
        {
            var found = normal.IndexOf(prefix, at, StringComparison.Ordinal);
            if (found < 0) return -1;
            if (found == 0 || normal[found - 1] == '\n') return found;
            at = found + 1;
        }

        return -1;
    }
}
