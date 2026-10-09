namespace Island.Core;

/// <summary>
/// What running a scene does to one thing. There is deliberately no member that closes or places a window: a scene
/// only opens and brings forward (EVALS S2), and a test lists these names to keep it so.
/// </summary>
public enum SceneAction
{
    /// <summary>The thing is closed (or cannot be told): start it, open the folder, open the site.</summary>
    Open,

    /// <summary>The thing is open: bring its newest window or tab forward.</summary>
    BringForward,

    /// <summary>The thing is closed and no longer installed: do nothing and say so.</summary>
    Skip,
}

/// <summary>
/// One step. <see cref="Click"/> is exactly what a click on the pick would do (<see cref="PickStates.Plan"/>), so the
/// app carries it out the way <c>PickPages</c> does: through <see cref="IOutsideActions"/> and the tab control, never
/// directly. It is <c>null</c> for a skipped thing.
/// </summary>
public sealed record SceneStep(Pick Thing, SceneAction Action, ClickPlan? Click);

/// <summary>The ordered steps of one run, and what the run reports. Names live in memory only.</summary>
public sealed record ScenePlan(string SceneName, IReadOnlyList<SceneStep> Steps)
{
    /// <summary>How many things the run opens: the number in the island's text, "&lt;n&gt; opened".</summary>
    public int OpenedCount => Steps.Count(s => s.Action == SceneAction.Open);

    public IReadOnlyList<string> SkippedNames => [.. Steps.Where(s => s.Action == SceneAction.Skip).Select(s => s.Thing.Name)];

    /// <summary>The island's text block for the run: the scene's name and "&lt;n&gt; opened".</summary>
    public string OpenedText => $"{OpenedCount} opened";

    /// <summary>Null when nothing was skipped.</summary>
    public Refusal? PartMissing => SceneRefusals.PartMissing(SceneName, SkippedNames);
}

/// <summary>The one refusal a scene run can end with (WORK-ORDER-7.md, REFUSAL REGISTER).</summary>
public static class SceneRefusals
{
    // Windows shows at most 255 characters in a balloon; stay clear of it with room to spare.
    public const int MaxMessageLength = 250;

    /// <summary>The register's wording with its three parts; <see cref="PartMissing"/> fills in the first.</summary>
    public static Refusal PartMissingTemplate { get; } = new(
        "SCENE_PART_MISSING",
        "<n> of the things in <scene> could not be opened: <names>.",
        "They are no longer on this computer, so Island opened the rest.",
        "Remove them from the scene in the settings, or install them again.");

    /// <summary>
    /// The refusal for the skipped names, or null when none. The text is built by putting the pieces in, never by
    /// replacing placeholders inside text that came from a name. When the names do not fit, the first ones are
    /// listed and the rest are counted ("and 3 more"); the message never passes <see cref="MaxMessageLength"/>.
    /// </summary>
    public static Refusal? PartMissing(string sceneName, IReadOnlyList<string> skipped)
    {
        if (skipped.Count == 0) return null;

        var scene = Shorten(Tidy(sceneName), Scenes.MaxNameLength);
        var head = $"{skipped.Count} of the things in {scene} could not be opened: ";
        var room = MaxMessageLength - head.Length - 1 - PartMissingTemplate.Why.Length - PartMissingTemplate.NextAction.Length - 2; // the full stop and two spaces

        var names = ListNames([.. skipped.Select(Tidy)], room);
        return PartMissingTemplate with { WhatHappened = head + names + "." };
    }

    private static string ListNames(IReadOnlyList<string> names, int room)
    {
        for (var shown = names.Count; shown >= 1; shown--)
        {
            var text = string.Join(", ", names.Take(shown)) + Rest(names.Count - shown);
            if (text.Length <= room) return text;
        }

        var rest = Rest(names.Count - 1);
        return Shorten(names[0], room - rest.Length) + rest; // even one name is too long: cut it
    }

    private static string Rest(int count) => count == 0 ? string.Empty : $" and {count} more";

    private static string Shorten(string text, int length)
    {
        if (text.Length <= length) return text;
        var cut = Math.Max(length - 1, 0);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--; // never half a character
        return text[..cut].TrimEnd() + "…";
    }

    // A name from a pick can hold a right-to-left override or a control character; in a balloon it would reorder or hide
    // the words around it, so those are left out and white space is made single.
    private static string Tidy(string text) =>
        System.Text.RegularExpressions.Regex.Replace(BlankText.WithoutHidden(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ")).Trim(), @"\s+", " "); // white space first (a line break is a space, not nothing), then what is hidden
}

/// <summary>Works out what running a scene does. Pure: it reads, it never acts.</summary>
public static class ScenePlans
{
    /// <summary>
    /// The plan for a run, one step per thing in the list's order (so the last thing is the last to come forward and
    /// ends on top). A thing that is open is brought forward, also when it is no longer in the installed list: it is
    /// plainly there, and bringing a window forward needs no installation. A closed thing is opened, unless it is a
    /// program that is no longer installed: that is skipped. Folders and sites are always available. A site cannot be
    /// told open or closed without the add-on, and is then opened, as a click on it would. The same thing twice and
    /// anything past <see cref="Scenes.MaxThingsPerScene"/> is ignored.
    /// </summary>
    /// <param name="isInstalled">Whether a program pick is still installed; asked only for a closed program.</param>
    /// <param name="context">The profile folder and what is known about hand-added places: a folder under the profile is found open, and a hand-added thing known to be gone is skipped.</param>
    public static ScenePlan For(Scene scene, OpenSnapshot open, Func<Pick, bool> isInstalled, PickContext? context = null)
    {
        var steps = scene.Things.DistinctBy(t => t.Id).Take(Scenes.MaxThingsPerScene).Select(t => Step(t, open, isInstalled, context ?? PickContext.None));
        return new ScenePlan(scene.Name, [.. steps]);
    }

    /// <summary>The program picks that <paramref name="installed"/> still contains, by executable name or package family.</summary>
    public static Func<Pick, bool> InstalledIn(IReadOnlyList<InstalledProgram> installed) => pick =>
        pick.Kind != PickKind.Program
        || pick.Location is not null // a program the person browsed to is not in the installed list: its place is its own
        || installed.Any(p => pick.ExeName is not null && string.Equals(p.ExeName, pick.ExeName, StringComparison.OrdinalIgnoreCase)
                              || pick.PackageFamily is not null && string.Equals(p.PackageFamily, pick.PackageFamily, StringComparison.OrdinalIgnoreCase));

    private static SceneStep Step(Pick thing, OpenSnapshot open, Func<Pick, bool> isInstalled, PickContext context)
    {
        // A new cycler every time: a scene always goes to the newest window, whatever the clicks have cycled through.
        var click = PickStates.Plan(thing, PickStates.For(thing, open, context), new ClickCycler());
        if (click.Kind == ClickKind.BringForward) return new SceneStep(thing, SceneAction.BringForward, click);
        if (click.Kind is ClickKind.None or ClickKind.TargetMissing || !isInstalled(thing)) return new SceneStep(thing, SceneAction.Skip, null);
        return new SceneStep(thing, SceneAction.Open, click);
    }
}
