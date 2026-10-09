using System.Globalization;
using System.Text;

namespace Island.Core.Speed;

/// <summary>One figure of the speed table: the middle of the readings, their spread (highest minus lowest) and every reading.</summary>
public sealed record SpeedRow(string Id, string What, double Median, double Spread, IReadOnlyList<double> Readings, string Unit = "ms", string Note = "")
{
    /// <summary>The middle reading of an odd number of readings (the middle two averaged for an even number); 0 for none.</summary>
    public static double MiddleOf(IReadOnlyList<double> readings)
    {
        if (readings.Count == 0) return 0;
        var sorted = readings.OrderBy(x => x).ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    public static SpeedRow Of(string id, string what, IReadOnlyList<double> readings, string unit = "ms", string note = "") =>
        new(id, what, MiddleOf(readings), readings.Count == 0 ? 0 : readings.Max() - readings.Min(), readings, unit, note);
}

/// <summary>The text of a speed table for review/perf.md (WORK-ORDER-12 sections 1 and 3).</summary>
public static class SpeedTable
{
    public static string Heading(string label) => $"{PerfSections.SpeedPrefix}) — {label}";

    public static string Markdown(string label, string measured, IReadOnlyList<SpeedRow> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(Heading(label));
        sb.AppendLine();
        sb.AppendLine(measured);
        sb.AppendLine();
        sb.AppendLine("| # | What | Middle of the readings | Spread | The readings | Note |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Id} | {r.What} | {r.Median.ToString("0.0", inv)} {r.Unit} | {r.Spread.ToString("0.0", inv)} | {string.Join(", ", r.Readings.Select(x => x.ToString("0.0", inv)))} | {r.Note} |");
        return sb.ToString();
    }
}
