using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Island.Core;

/// <summary>
/// The scenes. No scene is shipped: <see cref="Empty"/> is the store of a new install (EVALS S1). Changes return
/// a new store and leave the old one untouched. Loading and saving are in <c>SceneStoreFile.cs</c>.
/// </summary>
public sealed partial class SceneStore
{
    /// <summary>Scenes with the same id are one scene (the first stays); beyond <see cref="Scenes.MaxScenes"/> the rest are left out.</summary>
    public SceneStore(IEnumerable<Scene> scenes) => Items = [.. scenes.DistinctBy(s => s.Id).Take(Scenes.MaxScenes)];

    public static SceneStore Empty => new([]);

    public IReadOnlyList<Scene> Items { get; }

    public Scene? ById(string id) => Items.FirstOrDefault(s => s.Id == id);

    /// <summary>The scenes that have a key, for the settings list and for registering keys.</summary>
    public IReadOnlyList<Scene> WithKeys => [.. Items.Where(s => s.Key is not null)];

    // ---- Changing ----------------------------------------------------------

    /// <summary>A new, empty scene at the end of the list.</summary>
    /// <param name="name">The scene's name.</param>
    /// <param name="idTaken">Ids that must not be handed out although no scene has them (a key left behind by a scene that is gone belongs to its id).</param>
    public SceneEdit Create(string name, Func<string, bool>? idTaken = null)
    {
        if (Items.Count >= Scenes.MaxScenes) return Refuse(SceneText.SceneLimit);
        if (CheckName(name, exceptId: null, out var tidy) is { } problem) return Refuse(problem);

        var scene = new Scene(NextId(idTaken), tidy, []);
        return new SceneEdit(new SceneStore([.. Items, scene]), scene, null);
    }

    public SceneEdit Rename(string id, string name)
    {
        if (ById(id) is not { } scene) return Refuse(SceneText.NoSuchScene);
        if (CheckName(name, id, out var tidy) is { } problem) return Refuse(problem);
        return Replace(scene with { Name = tidy });
    }

    /// <summary>The store without the scene. Nothing that is open is closed; the editor gives the scene's key back to Windows.</summary>
    public SceneStore Delete(string id) => new(Items.Where(s => s.Id != id));

    /// <summary>Carries the key (or none). Whether the key is allowed is not decided here.</summary>
    public SceneEdit SetKey(string id, HotkeyCombo? key) =>
        ById(id) is { } scene ? Replace(scene with { Key = key }) : Refuse(SceneText.NoSuchScene);

    /// <summary>
    /// Replaces the list of things: each is copied from the pick given, the same thing twice is one thing (the first
    /// stays), the order is the order given. Refused as a whole when one cannot be stored or there are too many.
    /// </summary>
    public SceneEdit SetThings(string id, IEnumerable<Pick> picks)
    {
        if (ById(id) is not { } scene) return Refuse(SceneText.NoSuchScene);

        var things = new List<Pick>();
        foreach (var pick in picks)
        {
            if (Scenes.ThingFrom(pick) is not { } thing) return Refuse(SceneText.ThingNotStorable);
            if (things.All(t => t.Id != thing.Id)) things.Add(thing);
            if (things.Count > Scenes.MaxThingsPerScene) return Refuse(SceneText.ThingLimit);
        }

        return Replace(scene with { Things = things });
    }

    /// <summary>The thing added at the end; already there means no change and no refusal.</summary>
    public SceneEdit AddThing(string id, Pick pick) =>
        ById(id) is { } scene ? SetThings(id, [.. scene.Things, pick]) : Refuse(SceneText.NoSuchScene);

    /// <summary>The thing taken out of the scene only; the pick on the island, and anything open, is untouched.</summary>
    public SceneEdit RemoveThing(string id, string thingId) =>
        ById(id) is { } scene ? Replace(scene with { Things = [.. scene.Things.Where(t => t.Id != thingId)] }) : Refuse(SceneText.NoSuchScene);

    /// <summary>The thing moved to a place in the list (0 is first); a place beyond the ends is the nearest end, an unknown thing changes nothing.</summary>
    public SceneEdit MoveThing(string id, string thingId, int toIndex)
    {
        if (ById(id) is not { } scene) return Refuse(SceneText.NoSuchScene);
        if (scene.Things.FirstOrDefault(t => t.Id == thingId) is not { } thing) return Replace(scene);

        var rest = scene.Things.Where(t => t.Id != thingId).ToList();
        rest.Insert(Math.Clamp(toIndex, 0, rest.Count), thing);
        return Replace(scene with { Things = rest });
    }

    private SceneEdit Replace(Scene scene) =>
        new(new SceneStore(Items.Select(s => s.Id == scene.Id ? scene : s)), scene, null);

    private SceneEdit Refuse(string reason) => new(this, null, reason);

    // ---- Names -------------------------------------------------------------

    /// <summary>A scene name as it is kept and compared: trimmed, every run of white space one space (the way PageStore tidies a page name).</summary>
    internal static string Tidy(string? name) => Regex.Replace(name?.Trim() ?? string.Empty, @"\s+", " ");

    /// <summary>Null when the name is good; <paramref name="tidy"/> is the name as it will be kept.</summary>
    internal string? CheckName(string? name, string? exceptId, out string tidy)
    {
        tidy = Tidy(name);
        if (tidy.Length == 0) return SceneText.NameEmpty;
        if (BlankText.CountCharacters(tidy) > Scenes.MaxNameLength) return SceneText.NameTooLong;
        if (HasHiddenCharacters(tidy) || !BlankText.HasVisible(tidy)) return SceneText.NameBad;

        var wanted = tidy;
        var clash = Items.FirstOrDefault(s => s.Id != exceptId && string.Equals(Same(s.Name), Same(wanted), StringComparison.OrdinalIgnoreCase));
        return clash is null ? null : SceneText.NameTaken(clash.Name);
    }

    // Names that draw the same are the same: the precomposed and the combining spelling of one letter are one name.
    private static string Same(string name) => BlankText.SameName(name);

    // The name goes into the island's text and a balloon: a right-to-left override, a zero-width character or a
    // control character could make it say something else or nothing at all.
    private static bool HasHiddenCharacters(string text) => BlankText.HasHiddenCharacters(text);

    private string NextId(Func<string, bool>? idTaken)
    {
        var n = 1;
        while (Items.Any(s => s.Id == $"scene-{n}") || idTaken?.Invoke($"scene-{n}") == true) n++;
        return $"scene-{n}";
    }
}
