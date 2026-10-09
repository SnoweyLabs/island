namespace Island.Core;

/// <summary>The words resting the pointer on the island says (Dan's P8, WORK-ORDER-13): the mode's name and, in a few words, what it means.</summary>
public static class ModeTip
{
    public static string Name(Mode mode) => mode switch
    {
        Mode.Focus => "Focus",
        Mode.Vibe => "Vibe",
        Mode.DND => "Do not disturb",
        _ => mode.ToString(),
    };

    public static string Of(Mode mode) => mode switch
    {
        Mode.Focus => "Focus mode: works without interruptions",
        Mode.Vibe => "Vibe mode: the edge breathes",
        Mode.DND => "Do not disturb: nothing appears by itself",
        _ => $"{mode} mode",
    };
}
