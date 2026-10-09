using System.Text.Json;

namespace Island.Core;

public enum PickStoreStatus
{
    Loaded,
    Missing,
    Unreadable,
}

public sealed record PickStoreLoad(PickStore Store, PickStoreStatus Status, string? Detail);

/// <summary>
/// The picks, loaded from and saved to <c>%APPDATA%\Island\picks.json</c>. A file that cannot be read is
/// left exactly as it was and the app runs on an empty list. Nothing in the file is a path, except the one field
/// of a pick the person added by hand: see <see cref="Pick.IsStorable"/> and <see cref="PickPath"/>.
/// </summary>
public sealed class PickStore
{
    /// <summary>
    /// The schema number of the picks file when it holds the new shape (a pick of kind "file" or a "location", WORK-ORDER-10): one more than the
    /// shape before. A file whose picks all have the old shape still carries the old number, so an older Island reads it as it always did.
    /// FileSchema.Current is one number for every file; when it is raised to this number this constant can go.
    /// </summary>
    public const int NewShapeSchema = FileSchema.CurrentPicks;

    /// <summary>The most picks a store holds; what is saved can always be loaded again.</summary>
    public const int MaxPicks = 1000;

    /// <summary>
    /// Picks with the same id are one pick (the first stays); beyond <see cref="MaxPicks"/> the rest are left out. A pick that names the Terminals page is left out as well:
    /// that page fills itself and holds no pick (WORK-ORDER-11 section 1). A file that names it is not rewritten for that at load.
    /// </summary>
    public PickStore(IEnumerable<Pick> picks) => Picks = [.. picks.Where(p => PageIds.CanHoldPicks(p.PageId)).DistinctBy(p => p.Id).Take(MaxPicks)];

    public static PickStore Empty => new([]);

    public IReadOnlyList<Pick> Picks { get; }

    public IReadOnlyList<Pick> ForPage(string pageId) => [.. Picks.Where(p => p.PageId == pageId)];

    public Pick? ById(string id) => Picks.FirstOrDefault(p => p.Id == id);

    // ---- Changing (each returns a new store; the old one is untouched) -------

    /// <summary>The store with the pick added at the end of its page. The same thing cannot be picked twice: when the id or the place is already there, the store is returned as it was and <paramref name="added"/> is false.</summary>
    public PickStore Add(Pick pick, out bool added)
    {
        added = Picks.Count < MaxPicks && PageIds.CanHoldPicks(pick.PageId) && pick.IsStorable(out _) && Find(pick) is null;
        return added ? new PickStore([.. Picks, pick]) : this;
    }

    /// <summary>The pick already on the island that is the same thing as <paramref name="pick"/> (<see cref="Pick.IsSameThing"/>), or null. Its <see cref="Pick.PageId"/> is where "it is already on page X" comes from.</summary>
    public Pick? Find(Pick pick) => Picks.FirstOrDefault(p => p.IsSameThing(pick));

    /// <summary>The store without the pick. Only the list changes: nothing that is open is closed.</summary>
    public PickStore Remove(string id) => new(Picks.Where(p => p.Id != id));

    /// <summary>The store with the pick moved to another page (a pick is on exactly one page).</summary>
    public PickStore Move(string id, string pageId) =>
        !PageIds.CanHoldPicks(pageId) ? this : new(Picks.Select(p => p.Id == id && (p with { PageId = pageId }).IsStorable(out _) ? p with { PageId = pageId } : p));

    /// <summary>The store with every pick of one page replaced and all other pages left as they are (restoring the starter list of a page).</summary>
    public PickStore ReplacePage(string pageId, IEnumerable<Pick> replacement) =>
        !PageIds.CanHoldPicks(pageId) ? this : new(Picks.Where(p => p.PageId != pageId)
            .Concat(replacement.Where(p => p.PageId == pageId && Picks.All(q => q.PageId == pageId || q.Id != p.Id))));

    // ---- Loading and saving ------------------------------------------------

    public static PickStoreLoad Load(string path)
    {
        if (!File.Exists(path)) return new PickStoreLoad(Empty, PickStoreStatus.Missing, null);
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable("The file could not be opened."); // fixed text: the system's own message names a path
        }
    }

    public static PickStoreLoad Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Unreadable("The file must hold one JSON object.");
            if (doc.RootElement.ValueKind == JsonValueKind.Object && FileSchema.Problem(doc.RootElement, FileSchema.CurrentPicks) is { } schemaProblem) return Unreadable(schemaProblem);
            if (!doc.RootElement.TryGetProperty("picks", out var list) || list.ValueKind != JsonValueKind.Array)
                return Unreadable("\"picks\" must be a list.");
            if (list.GetArrayLength() > MaxPicks) return Unreadable("Too many picks.");

            var picks = new List<Pick>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return Unreadable("Every pick must be an object.");
                if (!TryReadPick(item, out var pick, out var problem)) return Unreadable(problem);
                if (picks.Any(p => p.Id == pick.Id)) return Unreadable("The same pick appears twice.");
                picks.Add(pick);
            }

            return new PickStoreLoad(new PickStore(picks), PickStoreStatus.Loaded, null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return Unreadable(e is JsonException ? e.Message : "The file contains text that is not valid.");
        }
    }

    private static bool TryReadPick(JsonElement item, out Pick pick, out string problem)
    {
        pick = null!;
        problem = string.Empty;

        string? Text(string name) =>
            item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        if (!EnumNames.TryParse<PickKind>(Text("kind"), out var kind))
        {
            problem = "A pick has no valid kind.";
            return false;
        }

        var location = Text("location");
        if (location is not null)
        {
            if (!PickPath.TryCanonical(location, out var canonical, out _))
            {
                problem = "A pick has a location that is not valid.";
                return false;
            }

            location = canonical; // a hand-edited spelling (forward slashes, other capitals of the profile token) is brought to the one form
        }

        var candidate = new Pick(Text("id") ?? string.Empty, kind, Text("name") ?? string.Empty, Text("page") ?? string.Empty,
            Text("exe"), Text("package"), Text("folder"), Text("host"), location);
        if (!candidate.IsStorable(out var why))
        {
            problem = $"A pick is not valid: {why}.";
            return false;
        }

        pick = candidate;
        return true;
    }

    public string ToJson()
    {
        var rows = Picks.Select(p => new Dictionary<string, string?>
        {
            ["id"] = p.Id,
            ["kind"] = p.Kind.ToString().ToLowerInvariant(),
            ["name"] = p.Name,
            ["page"] = p.PageId,
            ["exe"] = p.ExeName,
            ["package"] = p.PackageFamily,
            ["folder"] = p.KnownFolder,
            ["host"] = p.Host,
            ["location"] = p.Location,
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

        return JsonSerializer.Serialize(
            new Dictionary<string, object> { [FileSchema.Key] = SchemaToWrite(), ["picks"] = rows.ToList() },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private int SchemaToWrite() =>
        Picks.Any(p => p.Location is not null || p.Kind == PickKind.File) ? FileSchema.CurrentPicks : FileSchema.Current;

    /// <summary>
    /// Writes the picks. Refuses (false) when any pick is not storable, so a path can never reach the file,
    /// and when the path cannot be written; neither case throws. With <paramref name="profileFolder"/> given, a place that
    /// still spells out the profile folder is refused as well: it would carry the Windows account name into the file.
    /// </summary>
    public bool Save(string path, string? profileFolder = null)
    {
        if (Picks.Any(p => !p.IsStorable(out _) || PickPath.SpellsOutProfile(p.Location, profileFolder))) return false;
        try
        {
            AtomicFile.Write(path, ToJson()); // the list that was refused does not stay behind in a file of its own
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The picks at start-up. With a file: its picks. Without one, in normal running: the starter picks for the
    /// programs installed on this laptop, saved once. Without one under the self-test: an empty list and nothing
    /// written. A file that cannot be read is left untouched and the list is empty.
    /// </summary>
    public static PickStoreLoad OpenOrStart(string path, IReadOnlyList<InstalledProgram> installed, bool selfTest)
    {
        var load = Load(path);
        if (load.Status != PickStoreStatus.Missing || selfTest) return load;

        var started = new PickStore(StarterPicks.Build(installed));
        started.Save(path);
        return new PickStoreLoad(started, PickStoreStatus.Loaded, "Starter picks were applied for the first time.");
    }

    private static PickStoreLoad Unreadable(string detail) => new(Empty, PickStoreStatus.Unreadable, detail);
}
