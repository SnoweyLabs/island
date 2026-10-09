namespace Island.Core;

public enum NoticePhase
{
    Idle,
    Waiting,
}

/// <summary>What the app does after one step of the fallback. The planner only names it; the app shows, plays and waits.</summary>
public enum NoticeAction
{
    /// <summary>Nothing waits: nothing to do.</summary>
    None,

    /// <summary>The table allows the notice where the island would appear: show it.</summary>
    ShowHere,

    /// <summary>Show it on the second screen, which has no fullscreen program on it.</summary>
    ShowOnOtherScreen,

    /// <summary>Play the one short system sound (Focus only); the notice keeps waiting.</summary>
    PlaySound,

    /// <summary>Nothing to do now; the notice keeps waiting.</summary>
    Wait,

    /// <summary>Forget the notice (DND, stale, or nothing sensible to do).</summary>
    Drop,
}

/// <summary>
/// One notice's progress through the fallback. Immutable. <see cref="Since"/> is when the notice arrived, in the
/// clock the caller gives; <see cref="SoundPlayed"/> keeps the sound to one per notice.
/// </summary>
public sealed record NoticeWait(NoticePhase Phase, DateTimeOffset Since, bool SoundPlayed)
{
    public static NoticeWait Idle { get; } = new(NoticePhase.Idle, default, false);

    /// <summary>A notice arrives. A notice that arrives while another waits replaces it.</summary>
    public static NoticeWait Arrive(DateTimeOffset now) => new(NoticePhase.Waiting, now, false);

    /// <summary>True only while a notice waits: the 2-second timer exists exactly then.</summary>
    public bool NeedsTimer => Phase == NoticePhase.Waiting;
}

/// <param name="Mode">The mode now.</param>
/// <param name="Front">What is in front where the island appears (the never-over list already applied).</param>
/// <param name="SecondScreenWithoutFullscreen">There is a second screen and no fullscreen program is on it.</param>
/// <param name="Now">The clock, as a plain value.</param>
public sealed record NoticeFacts(Mode Mode, FrontState Front, bool SecondScreenWithoutFullscreen, DateTimeOffset Now);

public sealed record NoticeStep(NoticeWait State, NoticeAction Action)
{
    public bool NeedsTimer => State.NeedsTimer;
}

/// <summary>
/// When a notice may not show (WORK-ORDER-7 section 1, EVALS X8), as a pure step function: given what is known and
/// the clock, it returns what to do and the next state. In order: the table; DND drops; a second screen with no
/// fullscreen program; one short sound in Focus (none in Vibe); then waiting until the table allows it, dropped when
/// stale. While a notice waits, and only then, the app asks again every <see cref="RecheckEvery"/>.
/// </summary>
public static class NoticeFallback
{
    /// <summary>Chosen by Claude (WORK-ORDER-7 section 1): a notice older than this is dropped. Exactly this old is stale.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Chosen by Claude (WORK-ORDER-7 section 1): how often the table is asked again while a notice waits.</summary>
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromSeconds(2);

    public static NoticeStep Next(NoticeWait state, NoticeFacts facts)
    {
        if (state.Phase != NoticePhase.Waiting) return new(NoticeWait.Idle, NoticeAction.None);

        // Unknown mode: nothing sensible to do, and above all no sound.
        if (!Enum.IsDefined(facts.Mode)) return Done(NoticeAction.Drop);

        // Unknown front state: the same, no sound for something the table does not know.
        if (!Enum.IsDefined(facts.Front)) return Done(NoticeAction.Drop);

        // A clock that went backwards is never stale.
        if (facts.Now - state.Since >= StaleAfter) return Done(NoticeAction.Drop);

        if (ShowDecision.Decide(facts.Front, Appearer.Notice, ShowOrigin.ByItself, facts.Mode) == ShowAnswer.Show)
            return Done(NoticeAction.ShowHere);

        // The table says no. In DND that is the end: the notice is dropped, nothing more.
        if (facts.Mode == Mode.DND) return Done(NoticeAction.Drop);

        if (facts.SecondScreenWithoutFullscreen) return Done(NoticeAction.ShowOnOtherScreen);

        if (facts.Mode == Mode.Focus && !state.SoundPlayed)
            return new(state with { SoundPlayed = true }, NoticeAction.PlaySound);

        return new(state, NoticeAction.Wait);
    }

    private static NoticeStep Done(NoticeAction action) => new(NoticeWait.Idle, action);
}
