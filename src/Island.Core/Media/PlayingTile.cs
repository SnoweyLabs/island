namespace Island.Core;

/// <summary>
/// WORK-ORDER-5 §7: which pick's tile dances, and what the text block says under the title. What is playing belongs to a pick of the
/// Media page when it is a tab of a site that is picked, or a desktop player that is picked; the one session of the whole browser
/// (no add-on) and a tab of a site nobody picked belong to no pick: no tile dances for them, the text block still shows them.
/// </summary>
public static class PlayingTile
{
    /// <summary>The id of the pick the playing item belongs to, or null.</summary>
    public static string? PickIdFor(NowPlayingView? view, IReadOnlyList<Pick> mediaPicks)
    {
        if (view is null) return null;

        // The browser's whole session belongs to no pick, except when Windows' words are shown for a tab of a picked site (the fallback).
        if (view.IsBrowserSession)
            return view.FallbackTabKey is not null && view.Host is { } fallbackHost
                ? mediaPicks.FirstOrDefault(p => p.Kind == PickKind.Site && p.Host is not null && SiteMatch.Matches(p.Host, fallbackHost))?.Id
                : null;

        if (view.Target.Kind == MediaTargetKind.Tab)
        {
            return view.Host is null
                ? null
                : mediaPicks.FirstOrDefault(p => p.Kind == PickKind.Site && p.Host is not null && SiteMatch.Matches(p.Host, view.Host))?.Id;
        }

        var app = view.SourceApp;
        if (string.IsNullOrEmpty(app)) return null;
        return mediaPicks.FirstOrDefault(p => p.Kind == PickKind.Program && IsPlayerOf(p, app))?.Id;
    }

    // The same matching the line's click uses to find the player's window: the session names the file, the file's stem, or a Store app id that begins with the package name.
    private static bool IsPlayerOf(Pick pick, string app) =>
        pick.ExeName is { } exe && (string.Equals(exe, app, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(Path.GetFileNameWithoutExtension(exe), Path.GetFileNameWithoutExtension(app), StringComparison.OrdinalIgnoreCase))
        || pick.PackageFamily is { } package && StoreAppOf(package, app);

    // A Store app id is the package name, then '_' and its publisher, then '!' and the app: the package name must be all of the beginning, with a boundary after it.
    private static bool StoreAppOf(string packageFamily, string app)
    {
        var name = packageFamily.Split('_')[0];
        if (name.Length < 4 || !app.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return false;
        return app.Length == name.Length || app[name.Length] is '_' or '!' or '.';
    }

    /// <summary>The line under the title: where it plays and what kind of thing it is — "YouTube · tab" or "Spotify · app" — with "· paused" added while it is paused.</summary>
    public static string SecondLine(NowPlayingView view)
    {
        var kind = view.Target.Kind == MediaTargetKind.Tab || view.FallbackTabKey is not null ? "tab" : "app";
        var line = $"{view.Where} · {kind}";
        return view.IsPaused ? line + " · paused" : line;
    }

    /// <summary>
    /// The same line, cut so that it fits <paramref name="room"/> (Dan's P18, WORK-ORDER-13): when it is too long the NAME is shortened with an ellipsis, never the end, so that " · app" or " · tab" and
    /// " · paused" are always there. <paramref name="width"/> measures a text as the tile draws it.
    /// </summary>
    public static string FittedSecondLine(NowPlayingView view, double room, Func<string, double> width)
    {
        var full = SecondLine(view);
        if (width(full) <= room) return full;
        var kind = view.Target.Kind == MediaTargetKind.Tab || view.FallbackTabKey is not null ? "tab" : "app";
        var suffix = $" · {kind}" + (view.IsPaused ? " · paused" : string.Empty);
        var name = view.Where ?? string.Empty;
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        string Candidate(int kept) => name[..(kept < starts.Length ? starts[kept] : name.Length)].TrimEnd() + "…" + suffix;
        int low = 0, high = starts.Length; // how many characters of the name stay
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (width(Candidate(mid)) <= room) low = mid;
            else high = mid - 1;
        }

        return Candidate(low);
    }
}

/// <summary>
/// The three bars on the playing tile. Each bar's height goes from 5 to 17 and back in 0.9 s, easing in and out; the second bar runs
/// 0.2 s behind the first, the third 0.45 s behind. They depend only on the time, like the rim light, and never read the sound.
/// Paused, the three bars stand still at 5.
/// </summary>
public static class Equalizer
{
    /// <summary>Picture: keyframes 0% and 100% are 5 high, 50% is 17.</summary>
    public const double Lowest = 5;

    public const double Highest = 17;

    /// <summary>Picture: animation .9s ease-in-out infinite.</summary>
    public const double Seconds = 0.9;

    /// <summary>Picture: animation-delay of the second and third bar.</summary>
    public static IReadOnlyList<double> Delays { get; } = [0, 0.2, 0.45];

    /// <summary>Picture: each bar is 3 wide with round ends, 2.5 apart; the disc under them is black at 45%.</summary>
    public const double BarWidth = 3;

    public const double BarGap = 2.5;
    public const double DiscAlpha = 0.45;

    /// <summary>The heights of the three bars at this moment, in the island's own clock (seconds).</summary>
    public static double[] Heights(double timeSeconds, bool playing)
    {
        var heights = new double[Delays.Count];
        for (var i = 0; i < heights.Length; i++)
        {
            if (!playing || !double.IsFinite(timeSeconds)) heights[i] = Lowest;
            else heights[i] = HeightOf(timeSeconds - Delays[i]);
        }

        return heights;
    }

    private static double HeightOf(double local)
    {
        if (local <= 0) return Lowest; // a bar that has not started yet waits at the low end
        var phase = local % Seconds / Seconds;
        var half = phase < 0.5 ? phase * 2 : (1 - phase) * 2; // up for the first half, down for the second
        return Lowest + (Highest - Lowest) * Easing.CubicBezier(0.42, 0, 0.58, 1, half); // CSS ease-in-out
    }
}
