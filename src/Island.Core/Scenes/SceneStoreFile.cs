using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Island.Core;

public enum SceneStoreStatus
{
    Loaded,
    Missing,
    Unreadable,
}

/// <summary>
/// What loading gave. <see cref="Detail"/> says why a file is <see cref="SceneStoreStatus.Unreadable"/>, or, when it was
/// loaded with something left out, what (counts only, never a name). An unreadable file is left exactly as it was.
/// </summary>
public sealed record SceneStoreLoad(SceneStore Store, SceneStoreStatus Status, string? Detail);

public sealed partial class SceneStore
{

    /// <summary>A larger file is not read (100 full scenes of 200 things are about 3 MB; a hostile file of 100,000 things, about 13 MB, is still read and cut).</summary>
    public const int MaxFileChars = 16_000_000;

    private static readonly Regex IdPattern = new(@"^[a-z0-9-]{1,40}\z", RegexOptions.Compiled);

    // ---- Loading -----------------------------------------------------------

    public static SceneStoreLoad Load(string path)
    {
        if (!File.Exists(path)) return new SceneStoreLoad(Empty, SceneStoreStatus.Missing, null);
        try
        {
            if (new FileInfo(path).Length > MaxFileChars * 4L) return Unreadable("The file is too large."); // UTF-8 bytes, a generous bound
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable("The file could not be opened."); // fixed text: the system's own message names a path
        }
    }

    /// <summary>
    /// Reads the text. Structure that is wrong (a bad name, a thing that looks like a path, a repeated name or id, a
    /// newer version) makes the whole file unreadable, so nothing is half-read and then overwritten. Too many scenes or
    /// things are the exception: the first ones are kept and the rest are left out, because a file of that size is
    /// never one Island wrote. A key that is not a valid key, or is already some earlier scene's, is dropped.
    /// </summary>
    public static SceneStoreLoad Parse(string json)
    {
        if (json.Length > MaxFileChars) return Unreadable("The file is too large.");
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unreadable("The file must hold one JSON object.");
            if (FileSchema.Problem(root) is { } versionProblem) return Unreadable(versionProblem);
            if (!root.TryGetProperty("scenes", out var list) || list.ValueKind != JsonValueKind.Array) return Unreadable("\"scenes\" must be a list.");

            var notes = new Notes();
            var scenes = new List<Scene>();
            foreach (var item in list.EnumerateArray().Take(Scenes.MaxScenes))
            {
                if (!TryReadScene(item, scenes, notes, out var scene, out var problem)) return Unreadable(problem);
                scenes.Add(scene);
            }

            notes.ScenesLeftOut = Math.Max(0, list.GetArrayLength() - Scenes.MaxScenes);
            return new SceneStoreLoad(new SceneStore(scenes), SceneStoreStatus.Loaded, notes.ToText());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return Unreadable(e is JsonException ? e.Message : "The file contains text that is not valid.");
        }
    }

    private static bool TryReadScene(JsonElement item, List<Scene> earlier, Notes notes, out Scene scene, out string problem)
    {
        scene = null!;
        problem = string.Empty;
        if (item.ValueKind != JsonValueKind.Object) return Fail("Every scene must be an object.", out problem);

        string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        var id = Text("id");
        if (!IdPattern.IsMatch(id) || earlier.Any(s => s.Id == id)) return Fail("A scene has no valid id, or the same id twice.", out problem);

        // The rule of a name that is being ADDED is not held against a file saved before it: a twin of an earlier name is kept (the person renames it); a name that is empty, too long or hidden is not.
        if (new SceneStore([]).CheckName(Text("name"), exceptId: null, out var name) is not null)
            return Fail("A scene has a name that is empty, too long or hidden.", out problem);
        if (earlier.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Fail("A scene has a name that is already used.", out problem);

        if (!TryReadThings(item, notes, out var things, out problem)) return false;

        scene = new Scene(id, name, things, ReadKey(Text("key"), earlier, notes));
        return true;
    }

    private static bool TryReadThings(JsonElement item, Notes notes, out IReadOnlyList<Pick> things, out string problem)
    {
        things = [];
        problem = string.Empty;
        if (!item.TryGetProperty("things", out var list) || list.ValueKind != JsonValueKind.Array) return Fail("\"things\" must be a list.", out problem);

        // The one reader of picks is PickStore's: what it accepts is what a scene may keep.
        var kept = list.EnumerateArray().Take(Scenes.MaxThingsPerScene).Select(e => e.GetRawText());
        var load = PickStore.Parse("{\"picks\":[" + string.Join(',', kept) + "]}");
        if (load.Status != PickStoreStatus.Loaded) return Fail("A scene has a thing that is not valid.", out problem);

        notes.ThingsLeftOut += Math.Max(0, list.GetArrayLength() - Scenes.MaxThingsPerScene);
        things = [.. load.Store.Picks.Select(p => p with { PageId = Scenes.ThingPage })];
        return true;
    }

    private static HotkeyCombo? ReadKey(string text, List<Scene> earlier, Notes notes)
    {
        if (text.Length == 0) return null;
        if (!HotkeyCombo.TryParse(text, out var combo, out _) || earlier.Any(s => s.Key == combo))
        {
            notes.KeysDropped++;
            return null;
        }

        return combo;
    }

    private static bool Fail(string why, out string problem)
    {
        problem = why;
        return false;
    }

    private sealed class Notes
    {
        public int ScenesLeftOut;
        public int ThingsLeftOut;
        public int KeysDropped;

        public string? ToText()
        {
            var parts = new List<string>();
            if (ScenesLeftOut > 0) parts.Add($"{ScenesLeftOut} scenes beyond the limit of {Scenes.MaxScenes} were left out.");
            if (ThingsLeftOut > 0) parts.Add($"{ThingsLeftOut} things beyond the limit of {Scenes.MaxThingsPerScene} per scene were left out.");
            if (KeysDropped > 0) parts.Add($"{KeysDropped} keys were not valid or used twice, so those scenes have no key.");
            return parts.Count == 0 ? null : string.Join(' ', parts);
        }
    }

    // ---- Saving ------------------------------------------------------------

    public string ToJson()
    {
        var rows = new JsonArray();
        foreach (var scene in Items)
        {
            var row = new JsonObject { ["id"] = scene.Id, ["name"] = scene.Name };
            if (scene.Key is { } key) row["key"] = key.ToString();
            row["things"] = JsonNode.Parse(new PickStore(scene.Things).ToJson())!["picks"]!.DeepClone(); // PickStore's own writer
            rows.Add(row);
        }

        return new JsonObject { [FileSchema.Key] = FileSchema.Current, ["scenes"] = rows }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Writes the scenes (a temporary file, then a move). False when a thing is not storable (so a path can never reach
    /// the file) or the path cannot be written; neither case throws. Not for a file that could not be read.
    /// </summary>
    public bool Save(string path)
    {
        if (Items.Any(s => s.Things.Any(t => !t.IsStorable(out _)))) return false;
        try
        {
            AtomicFile.Write(path, ToJson());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static SceneStoreLoad Unreadable(string detail) => new(Empty, SceneStoreStatus.Unreadable, detail);
}
