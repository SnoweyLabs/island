namespace Island.Core;

/// <summary>
/// One report from a player or a tab, as the pill needs it. Pure data: no clock, nothing read from the system.
/// </summary>
/// <param name="PositionSeconds">Where the player was at <paramref name="ReportedAt"/>; null when it does not say.</param>
/// <param name="LengthSeconds">Length of the item; null, zero or negative means unknown (a live stream, a silent player).</param>
/// <param name="ReportedAt">
/// The moment the player's position was current. For a Windows session this is the timeline's LastUpdatedTime, which
/// Windows leaves at its zero date when there is no timeline; for a tab it is the time of the add-on's reading.
/// </param>
/// <param name="IsPlaying">Only a playing item moves between reports.</param>
/// <param name="Speed">Playing speed, 1 is normal. A report without one (Windows' PlaybackRate is nullable) passes 1.</param>
public readonly record struct ProgressReport(
    double? PositionSeconds,
    double? LengthSeconds,
    DateTimeOffset ReportedAt,
    bool IsPlaying,
    double Speed = 1)
{
    /// <summary>
    /// How Windows says "no timeline": every number zero and LastUpdatedTime at the epoch of its DateTime, midnight
    /// 1 January 1601 UTC (Windows.Foundation.DateTime.UniversalTime 0, which is also FILETIME 0). Confirmed on Microsoft
    /// Learn: the DateTime and FILETIME pages, and TimelineProperties.LastUpdatedTime ("the UTC time at which the timeline
    /// properties were last updated"). That a player without a timeline shows this value is from research, not from Learn.
    /// </summary>
    public static readonly DateTimeOffset WindowsZeroDate = new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A report from what <see cref="NowPlaying"/> already gives: its position is worked out as of <paramref name="now"/>,
    /// so it stands still from then on. It loses the "position beyond the length" check (NowPlaying clamps it first), so
    /// the pill is better fed from the raw report when the app has it.
    /// </summary>
    public static ProgressReport FromView(NowPlayingView view, DateTimeOffset now) =>
        new(view.PositionSeconds, view.LengthSeconds, now, IsPlaying: false);
}
