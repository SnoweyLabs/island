namespace Island.Core;

/// <summary>Which sources may take the "Now playing" line.</summary>
/// <param name="PickedHosts">The sites picked on the Media page; a tab counts only when its host is one of these (EVALS N7).</param>
/// <param name="DesktopPlayersCount">Desktop players (Spotify's program and the like) are sources.</param>
/// <param name="BrowserSessionCounts">
/// Windows' one session for the whole browser counts as a source. With no add-on connected it is the source. With the add-on connected the tabs
/// speak for the browser and its session is ignored (it would only duplicate them), with one exception (WORK-ORDER-9 section 3): exactly one browser
/// session says it is playing, and a tab on a picked site is making sound and has sent no report at all: then Windows' words fill the line.
/// </param>
public sealed record NowPlayingPolicy(IReadOnlyCollection<string> PickedHosts, bool DesktopPlayersCount, bool BrowserSessionCounts)
{
    /// <summary>
    /// Tonight's live rule: desktop players count, and a browser session counts whenever the Media page has at
    /// least one site pick (whether that site is the one playing cannot be known without the add-on).
    /// </summary>
    public static NowPlayingPolicy ForMediaPagePicks(IEnumerable<Pick> mediaPagePicks)
    {
        var hosts = mediaPagePicks.Where(p => p.Kind == PickKind.Site && p.Host is not null).Select(p => p.Host!).ToList();
        return new NowPlayingPolicy(hosts, DesktopPlayersCount: true, BrowserSessionCounts: hosts.Count > 0);
    }
}

public enum MediaTargetKind
{
    /// <summary>A Windows media session; <see cref="MediaTarget.Id"/> is its session id (send with IMediaControl).</summary>
    Session,

    /// <summary>A browser tab reported by the add-on; <see cref="MediaTarget.Id"/> is its tab key (send with ITabControl).</summary>
    Tab,
}

/// <summary>How to address the item for a command.</summary>
public sealed record MediaTarget(MediaTargetKind Kind, string Id);

/// <summary>What the "Now playing" block shows. Lives in memory only.</summary>
/// <param name="Title">The title; the place it plays when the source gave none.</param>
/// <param name="Where">"Spotify", "YouTube", "Chrome" ...</param>
/// <param name="SecondLine">The line under the title: <see cref="Where"/>, with " - paused" added when it is paused.</param>
/// <param name="IsPaused">Not playing: the middle button shows play and resumes this item.</param>
/// <param name="Progress">0 to 1; null when the length is not known (EVALS N11): progress is never invented.</param>
/// <param name="CanControl">False while the source is gone (between tracks); the buttons then do nothing.</param>
/// <param name="SourceApp">The session's app id, for bringing its window forward; null for a tab.</param>
/// <param name="Host">The tab's host; null for a session.</param>
/// <param name="IsBrowserSession">The item is "the whole browser", not one tab.</param>
/// <param name="Report">The raw report the small pill's edge is worked out from (<see cref="LitShare.Of"/>); null when the source gave none.</param>
/// <param name="FallbackTabKey">
/// WORK-ORDER-9 section 3: set when the words are Windows' own for the browser's session (a tab on a picked site is making sound and has said nothing), shown for
/// that tab: <see cref="Host"/> is the tab's site, a click on the line goes to the tab, and the buttons still go to the session. Null otherwise.
/// </param>
public sealed record NowPlayingView(
    string Title,
    string? Artist,
    string Where,
    string SecondLine,
    PlaybackState State,
    bool IsPaused,
    double? PositionSeconds,
    double? LengthSeconds,
    double? Progress,
    MediaTarget Target,
    bool CanControl,
    string? SourceApp,
    string? Host,
    bool IsBrowserSession,
    ProgressReport? Report = null,
    string? FallbackTabKey = null);

/// <summary>Which command goes to which target. Built by <see cref="NowPlaying.PlanFor"/>; sending it is the caller's job.</summary>
public sealed record MediaCommandPlan(MediaTarget Target, MediaCommand Command)
{
    /// <summary>Sends the plan through the session door or the tab door, whichever its target needs.</summary>
    public bool Send(IMediaControl sessions, ITabControl tabs) =>
        Target.Kind == MediaTargetKind.Tab ? tabs.Media(Target.Id, Command) : sessions.Send(Target.Id, Command);
}
