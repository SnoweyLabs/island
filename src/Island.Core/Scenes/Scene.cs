namespace Island.Core;

/// <summary>
/// A scene: a name, an ordered list of things, and optionally a key. The things are copies of picks (kind plus
/// the name-only identity: a program is a name and an executable file name or package family, a folder a
/// known-folder name, a site a host), so a pick that is later removed from the island stays in the scene. A
/// thing carries <see cref="Scenes.ThingPage"/> as its page: a scene has no pages. Nothing here is a path and
/// nothing says what is open on this machine. The key is only carried: its rules (EVALS K2 to K9) belong to the
/// settings editor.
/// </summary>
public sealed record Scene(string Id, string Name, IReadOnlyList<Pick> Things, HotkeyCombo? Key = null);

/// <summary>The outcome of one scene edit: the store after it (the same store when refused), the scene concerned, and the refusal reason.</summary>
public sealed record SceneEdit(SceneStore Store, Scene? Scene, string? Refusal)
{
    public bool Refused => Refusal is not null;
}

/// <summary>The bounds of a scene store. What is saved can always be loaded again, and a hostile file cannot make the app hold more.</summary>
public static class Scenes
{
    public const int MaxScenes = 100;
    public const int MaxThingsPerScene = 200;
    public const int MaxNameLength = 24;

    /// <summary>The page every thing of a scene carries: <see cref="Pick"/> insists on one, a scene has none.</summary>
    public const string ThingPage = "scene";

    /// <summary>A copy of the pick as a scene keeps it, or null when it cannot be stored (it would hold a path, say).</summary>
    public static Pick? ThingFrom(Pick pick)
    {
        var thing = pick with { PageId = ThingPage };
        return thing.IsStorable(out _) ? thing : null;
    }
}

/// <summary>The words the editor shows when a scene edit is refused.</summary>
public static class SceneText
{
    public const string NoSuchScene = "That scene no longer exists.";
    public const string NameEmpty = "A scene needs a name.";
    public const string NameBad = "A scene name can hold letters, digits and ordinary punctuation, but no hidden or control characters.";
    public const string SceneLimit = "There can be at most 100 scenes.";
    public const string ThingLimit = "A scene can hold at most 200 things.";
    public const string ThingNotStorable = "That thing cannot be kept in a scene.";

    public static string NameTooLong => $"A scene name can have at most {Scenes.MaxNameLength} characters.";

    public static string NameTaken(string name) => $"There is already a scene called \"{name}\".";
}
