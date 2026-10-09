namespace Island.Core;

/// <summary>
/// The one pure decision of WORK-ORDER-7 section 1 (its table, whose Vibe column is WORK-ORDER-6 section 2):
/// may this thing appear now? No Windows call, no clock. A value outside the enums answers StayAway.
/// </summary>
public static class ShowDecision
{
    public static ShowAnswer Decide(FrontState front, Appearer thing, ShowOrigin origin, Mode mode)
    {
        if (!Enum.IsDefined(front) || !Enum.IsDefined(thing) || !Enum.IsDefined(origin) || !Enum.IsDefined(mode))
            return ShowAnswer.StayAway;

        // A game in exclusive fullscreen: nothing, in every mode, however it was asked for.
        if (front == FrontState.ExclusiveFullscreen) return ShowAnswer.StayAway;

        var allowed = mode switch
        {
            Mode.DND => front == FrontState.Clear && origin == ShowOrigin.Asked,
            Mode.Focus => FocusAllows(front, thing, origin),
            Mode.Vibe => front == FrontState.Clear || origin == ShowOrigin.Asked,
            _ => false,
        };
        return allowed ? ShowAnswer.Show : ShowAnswer.StayAway;
    }

    // Focus is Vibe, plus: over a fullscreen program (not a presentation) the notice shows by itself.
    private static bool FocusAllows(FrontState front, Appearer thing, ShowOrigin origin) =>
        front == FrontState.Clear
        || origin == ShowOrigin.Asked
        || (front == FrontState.FullscreenProgram && thing == Appearer.Notice);
}
