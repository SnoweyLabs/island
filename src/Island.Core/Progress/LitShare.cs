namespace Island.Core;

/// <summary>The part of the pill's edge that is still to play (EVALS N10, N11).</summary>
public static class LitShare
{
    /// <summary>
    /// The lit share of the edge, 0 to 1, as of <paramref name="now"/>; null means "no progress": the pill then shows the
    /// approved moving light, never a made-up ring (N11). Null when the length is unknown, zero or not finite, when
    /// Windows reports no timeline (zero date), when the reported position is negative, not finite or past the length,
    /// or when the speed is not finite.
    /// The position between reports is <c>position + (now - reportedAt) x speed</c>, only while playing; a paused item
    /// stands still. Time that did not pass (a report from the future, a clock that went backwards) counts as none.
    /// The result is clamped, so a stale report of a playing item ends at 0 and never beyond. A negative speed runs
    /// backwards (rewind) and stops at the start.
    /// </summary>
    public static double? Of(ProgressReport report, DateTimeOffset now)
    {
        if (report.PositionSeconds is not { } position || report.LengthSeconds is not { } length) return null;
        if (report.ReportedAt <= ProgressReport.WindowsZeroDate) return null;
        if (!double.IsFinite(position) || !double.IsFinite(length) || !double.IsFinite(report.Speed)) return null;
        if (length <= 0 || position < 0 || position > length) return null;

        var elapsed = report.IsPlaying ? Math.Max(0, (now - report.ReportedAt).TotalSeconds) : 0;
        var worked = Math.Clamp(position + elapsed * report.Speed, 0, length);
        return Math.Clamp(1 - worked / length, 0, 1);
    }
}
