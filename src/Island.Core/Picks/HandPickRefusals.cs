namespace Island.Core;

/// <summary>
/// The two refusals WORK-ORDER-10 adds to the register, in the order's own words. They live here until the main session moves them into
/// <see cref="Refusals"/> (and its <c>All</c> list, which RefusalTests pins); their codes and texts must then stay exactly as they are.
/// </summary>
public static class HandPickRefusals
{
    public static Refusal PickTargetMissing { get; } = new(
        "PICK_TARGET_MISSING",
        "<name> is not where it was, so Island could not open it.",
        "It may have been moved, renamed or deleted, and guessing another place could open the wrong thing.",
        "Remove it from the island and add it again from where it is now.");

    public static Refusal NotASite { get; } = new(
        "NOT_A_SITE",
        "That does not look like the name of a website, so nothing was added.",
        "Island keeps only a site's name, such as example.org.",
        "Type the site's name and try again.");

    /// <summary>PICK_TARGET_MISSING with the pick's name in it.</summary>
    public static Refusal ForMissing(Pick pick) => PickTargetMissing.With("<name>", NameThatFits(pick.Name));

    // The message goes to a tray balloon, which has room for SceneRefusals.MaxMessageLength characters: a long name is cut, and the end of the message stays.
    private static string NameThatFits(string name)
    {
        var room = Math.Max(2, SceneRefusals.MaxMessageLength - (PickTargetMissing.Message.Length - "<name>".Length));
        if (name.Length <= room) return name;
        var cut = name[..(room - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut.TrimEnd() + "…";
    }
}
