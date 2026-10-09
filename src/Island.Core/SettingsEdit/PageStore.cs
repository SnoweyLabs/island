using System.Text.Json;
using System.Text.RegularExpressions;

namespace Island.Core.SettingsEdit;

public enum PageStoreStatus
{
    Loaded,
    Missing,
    Unreadable,
}

/// <param name="TerminalsNoRoom">The file holds as many pages as the island can hold, so the built-in Terminals page was not added (TERMINALS_PAGE_NO_ROOM); looked at again at every load.</param>
public sealed record PageStoreLoad(PageStore Store, PageStoreStatus Status, string? Detail, bool TerminalsNoRoom = false);

/// <summary>The outcome of one page edit: the store after it, the page concerned, a refusal reason, and a one-line warning that does not block.</summary>
public sealed record PageEdit(PageStore Store, Page? Page, string? Refusal, string? Warning)
{
    public bool Refused => Refusal is not null;
}

/// <summary>What the screen asks before a page is removed.</summary>
public sealed record DeleteQuestion(Page Page, int PickCount, string Text);

/// <summary>The outcome of removing a page: the pages and picks after it (unchanged when refused or not confirmed).</summary>
public sealed record PageDeletion(PageStore Pages, PickStore Picks, int RemovedPicks, string? Refusal);

/// <summary>
/// The list of pages: the six built-in ones (the sixth, Terminals, last when it was added to an older file) with their pinned colours, then Dan's own. A page's
/// number key is its place in the list (1 to 9), the same rule the island's machine uses. Changes return a
/// new store; the old one is untouched. Saved as JSON at a path the caller gives; a file that cannot be read
/// is left exactly as it was and the defaults are used.
/// </summary>
public sealed class PageStore
{
    public const int MaxPages = 9;
    private const string CustomGlyph = "dot";

    /// <summary>Two colours closer than this in OKLab count as "very close" (a just-noticeable difference is about 0.02).</summary>
    public const double CloseColourDistance = 0.06;

    private static readonly Regex ColourPattern = new(@"^#[0-9A-Fa-f]{6}\z", RegexOptions.Compiled);
    private static readonly Regex IdPattern = new(@"^[a-z0-9-]{1,40}\z", RegexOptions.Compiled);

    public PageStore(IEnumerable<Page> pages) => Pages = [.. pages];

    public static PageStore Default => new(Island.Core.Pages.BuiltIn);

    public IReadOnlyList<Page> Pages { get; }

    public Page? ById(string id) => Pages.FirstOrDefault(p => p.Id == id);

    /// <summary>The number key that reaches a page while the island is open (1 to 9), or null for a page beyond the ninth.</summary>
    public int? DigitFor(string pageId)
    {
        var at = Pages.ToList().FindIndex(p => p.Id == pageId);
        return at is >= 0 and < 9 ? at + 1 : null;
    }

    // ---- Changing --------------------------------------------------------

    /// <summary>A new page at the end of the list; it gets the next free number key.</summary>
    public PageEdit Create(string name, string colour)
    {
        if (Pages.Count >= MaxPages) return Refuse(SettingsText.PageLimit);
        if (CheckName(name, exceptId: null) is { } nameProblem) return Refuse(nameProblem);
        if (!TryColour(colour, out var hex)) return Refuse(SettingsText.PageColourBad);

        var page = new Page(NextId(), Tidy(name), hex, CustomGlyph, null, false);
        return new PageEdit(new PageStore([.. Pages, page]), page, null, CloseColourWarning(hex, exceptId: null));
    }

    public PageEdit Rename(string id, string name)
    {
        if (ById(id) is not { } page) return Refuse(SettingsText.NoSuchPage);
        if (CheckName(name, id) is { } problem) return Refuse(problem);
        return Replace(page with { Name = Tidy(name) }, warning: null);
    }

    /// <summary>Any #RRGGBB. A colour very close to another page's is allowed with a warning.</summary>
    public PageEdit Recolour(string id, string colour)
    {
        if (ById(id) is not { } page) return Refuse(SettingsText.NoSuchPage);
        if (!TryColour(colour, out var hex)) return Refuse(SettingsText.PageColourBad);
        return Replace(page with { Color = hex }, CloseColourWarning(hex, id));
    }

    /// <summary>The question to ask before removing a page, or null when the page does not exist or is built in.</summary>
    public DeleteQuestion? AskDelete(string id, PickStore picks)
    {
        if (ById(id) is not { IsBuiltIn: false } page) return null;
        var count = picks.ForPage(id).Count;
        return new DeleteQuestion(page, count, SettingsText.DeletePageQuestion(page.Name, count));
    }

    /// <summary>
    /// Removes a custom page and the picks on it (nothing that is open is closed). Without
    /// <paramref name="confirmed"/> nothing changes; a built-in page is never removed.
    /// </summary>
    public PageDeletion Delete(string id, PickStore picks, bool confirmed)
    {
        var unchanged = new PageDeletion(this, picks, 0, null);
        if (ById(id) is not { } page) return unchanged with { Refusal = SettingsText.NoSuchPage };
        if (page.IsBuiltIn) return unchanged with { Refusal = SettingsText.BuiltInPageCannotBeDeleted };
        if (!confirmed) return unchanged;

        var removed = picks.ForPage(id);
        var remaining = removed.Aggregate(picks, (store, pick) => store.Remove(pick.Id));
        return new PageDeletion(new PageStore(Pages.Where(p => p.Id != id)), remaining, removed.Count, null);
    }

    private PageEdit Replace(Page page, string? warning) =>
        new(new PageStore(Pages.Select(p => p.Id == page.Id ? page : p)), page, null, warning);

    private PageEdit Refuse(string reason) => new(this, null, reason, null);

    private string? CheckName(string? name, string? exceptId)
    {
        var trimmed = Tidy(name);
        if (trimmed.Length == 0) return SettingsText.PageNameEmpty;
        if (BlankText.CountCharacters(trimmed) > SettingsText.MaxPageNameLength) return SettingsText.PageNameTooLong;
        if (BlankText.HasHiddenCharacters(trimmed) || !BlankText.HasVisible(trimmed)) return SettingsText.PageNameBad; // the rule scenes keep (WORK-ORDER-7): a name that draws as nothing, reverses the text or is not kept as typed
        var clash = Pages.FirstOrDefault(p => p.Id != exceptId && (string.Equals(Same(p.Name), Same(trimmed), StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase))); // names that draw the same are the same (as scenes compare them), and so are the names the loader reads as one: what is accepted is read back
        return clash is null ? null : SettingsText.PageNameTaken(clash.Name);
    }

    private static string Same(string name) => BlankText.SameName(name);

    /// <summary>A page name as it is kept and compared: trimmed, with every run of white space made one space.</summary>
    private static string Tidy(string? name) => System.Text.RegularExpressions.Regex.Replace(name?.Trim() ?? string.Empty, @"\s+", " ");

    private static bool TryColour(string? text, out string hex)
    {
        var candidate = (text ?? string.Empty).Trim();
        if (candidate.Length == 6) candidate = "#" + candidate;
        hex = candidate.ToUpperInvariant();
        return ColourPattern.IsMatch(hex);
    }

    private string? CloseColourWarning(string hex, string? exceptId)
    {
        var mine = ColorMath.ToOkLab(Rgb.FromHex(hex));
        var near = Pages.Where(p => p.Id != exceptId).FirstOrDefault(p => Distance(mine, ColorMath.ToOkLab(Rgb.FromHex(p.Color))) < CloseColourDistance);
        return near is null ? null : SettingsText.PageColourClose(near.Name);
    }

    private static double Distance((double L, double A, double B) a, (double L, double A, double B) b) =>
        Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2));

    private string NextId()
    {
        var n = 1;
        while (Pages.Any(p => p.Id == $"page-{n}")) n++;
        return $"page-{n}";
    }

    // ---- Loading and saving ----------------------------------------------

    public static PageStoreLoad Load(string path)
    {
        if (!File.Exists(path)) return new PageStoreLoad(Default, PageStoreStatus.Missing, null);
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable("The file could not be opened."); // fixed text: the system's own message names a path
        }
    }

    public static PageStoreLoad Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Unreadable("The file must hold one JSON object.");
            if (FileSchema.Problem(doc.RootElement) is { } schemaProblem) return Unreadable(schemaProblem);
            if (!doc.RootElement.TryGetProperty("pages", out var list) || list.ValueKind != JsonValueKind.Array)
                return Unreadable("\"pages\" must be a list.");
            if (list.GetArrayLength() > MaxPages) return Unreadable($"There can be at most {MaxPages} pages.");

            var pages = new List<Page>();
            foreach (var item in list.EnumerateArray())
            {
                if (!TryReadPage(item, pages, out var page, out var problem)) return Unreadable(problem);
                pages.Add(page);
            }

            var missing = Island.Core.Pages.BuiltIn.Where(b => pages.All(p => p.Id != b.Id)).ToList();
            // A file written before the sixth page has the five old ones: Terminals is added at the end, in memory (the file is not rewritten here).
            // Any other built-in page that is missing still makes the file unreadable.
            var noRoom = false;
            if (missing.Count == 1 && missing[0].Id == PageIds.Terminals)
            {
                noRoom = pages.Count >= MaxPages;
                if (!noRoom) pages.Add(missing[0] with { Name = FreeName(pages, missing[0].Name) });
                missing.Clear();
            }

            return missing.Count > 0
                ? Unreadable($"The built-in page \"{missing[0].Id}\" is missing.")
                : new PageStoreLoad(new PageStore(pages), PageStoreStatus.Loaded, null, noRoom);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return Unreadable(e is JsonException ? e.Message : "The file contains text that is not valid.");
        }
    }

    /// <summary>The name as it is, or with the next number that is free ("Terminals 2") when a page of the person's already has it (case ignored).</summary>
    private static string FreeName(List<Page> pages, string name)
    {
        var candidate = name;
        for (var n = 2; pages.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++) candidate = $"{name} {n}";
        return candidate;
    }

    private static bool TryReadPage(JsonElement item, List<Page> earlier, out Page page, out string problem)
    {
        page = null!;
        problem = string.Empty;
        if (item.ValueKind != JsonValueKind.Object)
        {
            problem = "Every page must be an object.";
            return false;
        }

        string Text(string name) =>
            item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

        var id = Text("id");
        var name = Text("name").Trim();
        var builtIn = Island.Core.Pages.BuiltIn.FirstOrDefault(b => b.Id == id);

        if (!IdPattern.IsMatch(id) || earlier.Any(p => p.Id == id)) problem = "A page has no valid id, or the same id twice.";
        else if (name.Length is 0 || BlankText.CountCharacters(name) > SettingsText.MaxPageNameLength) problem = "A page has an empty or too long name.";
        else if (earlier.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) problem = "Two pages have the same name.";
        else if (!TryColour(Text("color"), out var hex)) problem = "A page has a colour that is not #RRGGBB.";
        else
        {
            page = builtIn is null
                ? new Page(id, name, hex, CustomGlyph, null, false)
                : builtIn with { Name = name, Color = hex };
            return true;
        }

        return false;
    }

    public string ToJson()
    {
        var rows = Pages.Select(p => new Dictionary<string, string> { ["id"] = p.Id, ["name"] = p.Name, ["color"] = p.Color });
        return JsonSerializer.Serialize(
            new Dictionary<string, object> { [FileSchema.Key] = FileSchema.Current, ["pages"] = rows.ToList() },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes the pages (a temporary file, then a move). False when the path cannot be written; never throws. Not for a file that could not be read.</summary>
    public bool Save(string path)
    {
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

    private static PageStoreLoad Unreadable(string detail) => new(Default, PageStoreStatus.Unreadable, detail);
}
