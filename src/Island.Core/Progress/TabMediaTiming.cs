namespace Island.Core;

/// <summary>
/// How the add-on's <c>media</c> message is extended so the island can work the position out between reports.
/// Proposed fields, both optional so an older add-on still works:
/// <c>rate</c> (number, the page player's playing speed, 1 is normal; missing means 1) and
/// <c>readAt</c> (number, milliseconds since 1970-01-01 UTC from the add-on's clock at the moment it read the position;
/// missing means "as it arrived"). Example frame:
/// <c>{"type":"media","id":11,"title":"Lo-fi beats to study to","artist":"Some Channel","state":"playing","position":42.5,"length":3600,"rate":1.25,"readAt":1790000000000}</c>
/// </summary>
public static class TabMediaTiming
{
    /// <summary>
    /// The report for a tab's <c>media</c> message. A reading cannot come from the future, so a <paramref name="readAtMs"/>
    /// later than <paramref name="arrivedAt"/> counts as arrived-now; one that is missing, not finite or not positive does too.
    /// A missing or unusable <paramref name="rate"/> is 1.
    /// </summary>
    public static ProgressReport ToReport(double? position, double? length, bool isPlaying, double? rate, double? readAtMs, DateTimeOffset arrivedAt)
    {
        var readAt = FromUnixMilliseconds(readAtMs) is { } t && t < arrivedAt ? t : arrivedAt;
        var speed = rate is { } r && double.IsFinite(r) ? r : 1;
        return new ProgressReport(position, length, readAt, isPlaying, speed);
    }

    private static DateTimeOffset? FromUnixMilliseconds(double? ms) =>
        ms is { } v && double.IsFinite(v) && v > 0 && v < 253402300799000d ? DateTimeOffset.FromUnixTimeMilliseconds((long)v) : null;
}
