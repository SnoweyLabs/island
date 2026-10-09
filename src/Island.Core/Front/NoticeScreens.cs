namespace Island.Core;

/// <summary>
/// Which screen a notice goes to when the table says the island stays away from the screen it would appear on (Dan's Q2, WORK-ORDER-13; WORK-ORDER-7 section 1: "a second monitor if there is one").
/// Pure. The screen with the fullscreen program in front is the one the foreground window is on; any other usable screen has no fullscreen program on it as far as the island can tell (it reads the
/// foreground window only).
/// </summary>
public static class NoticeScreens
{
    /// <summary>
    /// The index of the screen to show the notice on, or -1 when there is none: fewer than two usable screens, or the screen of the foreground window is not known. The main screen is preferred when
    /// it is not the one in front; else the first other one.
    /// </summary>
    public static int OtherScreenIndex(IReadOnlyList<ScreenInfo>? screens, PixelRect? frontScreen)
    {
        if (screens is null || frontScreen is not { } front) return -1;
        var count = Math.Min(screens.Count, ScreenChooser.MaxScreens);
        var usable = 0;
        var frontIndex = -1;
        for (var i = 0; i < count; i++)
        {
            if (!screens[i].IsUsable) continue;
            usable++;
            if (screens[i].Full == front && frontIndex < 0) frontIndex = i;
        }

        if (usable < 2 || frontIndex < 0) return -1;
        var first = -1;
        for (var i = 0; i < count; i++)
        {
            if (i == frontIndex || !screens[i].IsUsable || screens[i].Full == front) continue;
            if (screens[i].IsPrimary) return i;
            if (first < 0) first = i;
        }

        return first;
    }

    /// <summary>The point at the middle of a screen: where the pointer would be if it were on that screen, which is how the placer chooses a screen.</summary>
    public static ScreenPoint CentreOf(PixelRect screen) => new((int)(((long)screen.Left + screen.Right) / 2), (int)(((long)screen.Top + screen.Bottom) / 2));
}
