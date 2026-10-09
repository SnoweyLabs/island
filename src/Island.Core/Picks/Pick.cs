namespace Island.Core;

public enum PickKind
{
    Program,
    Folder,
    Site,
    File,
}

/// <summary>
/// One thing Dan keeps on a page: a program, a folder, a website or a file. A pick stores no path in any field but one: a program is
/// the name it is known by plus an executable file name and/or a package family name, a folder is a
/// known-folder name, a site is its host. Where a program lives on disk is found again at every start, in memory.
/// The one exception (WORK-ORDER-10, "THE RULE ABOUT PATHS"): a thing the person added by hand by pointing at a place (a folder that is not a known
/// folder, a file, a program chosen with "Browse") keeps that place in <see cref="Location"/>, written as <see cref="PickPath"/> says, and nowhere else.
/// </summary>
public sealed record Pick(
    string Id,
    PickKind Kind,
    string Name,
    string PageId,
    string? ExeName = null,
    string? PackageFamily = null,
    string? KnownFolder = null,
    string? Host = null,
    string? Location = null)
{
    public static readonly IReadOnlyList<string> KnownFolders = ["Downloads", "Documents", "Desktop", "Music", "Pictures", "Videos"];

    public static Pick ForProgram(string name, string pageId, string? exeName, string? packageFamily) =>
        new($"program:{Slug(exeName ?? packageFamily ?? name)}", PickKind.Program, name, pageId, ExeName: exeName, PackageFamily: packageFamily);

    public static Pick ForFolder(string knownFolder, string pageId) =>
        new($"folder:{knownFolder.ToLowerInvariant()}", PickKind.Folder, knownFolder, pageId, KnownFolder: knownFolder);

    public static Pick ForSite(string name, string host, string pageId) =>
        new($"site:{SiteMatch.Normalize(host)}", PickKind.Site, name, pageId, Host: SiteMatch.Normalize(host));

    /// <summary>
    /// Two picks are the same thing, and the second cannot be put on the island: the same id (a program's file or package name, a known folder,
    /// a site's host), or the same place (the full path, no trailing separator, capitals ignored, links not followed, nothing read from the disk).
    /// </summary>
    public bool IsSameThing(Pick other) =>
        Id == other.Id
        || Kind == PickKind.Program && other.Kind == PickKind.Program && ExeName is not null && string.Equals(ExeName, other.ExeName, StringComparison.OrdinalIgnoreCase) // windows are matched by the program's file name, so two tiles of one name would show the same windows
        || Location is not null && other.Location is not null && PickPath.Key(Location) is { } mine && mine == PickPath.Key(other.Location);

    /// <summary>
    /// True when this pick can be saved: it points at something by name only, and no field looks like a path, except that
    /// <see cref="Location"/> holds a place in its one plain form. <paramref name="reason"/> says what is wrong otherwise.
    /// </summary>
    public bool IsStorable(out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(PageId))
            return Fail("id, name and page are required", out reason);

        if (BlankText.HasBrokenCharacter(Name)) return Fail("name holds a half character", out reason); // saved as another character, it would come back as another name

        if (!PickKeysJson.IsUsableId(Id)) return Fail("id must be a kind, a colon and a name, with no spaces", out reason);

        foreach (var (field, value) in Fields())
        {
            if (value is null) continue;
            if (value.Length > 200) return Fail($"{field} is too long", out reason);
            if (value.Contains('\\') || value.Contains('\0')) return Fail($"{field} looks like a path", out reason);
            if (field is "exe" or "package" or "folder" or "host" or "id" or "page" && (value.Contains('/') || value.Contains(':') && field != "id"))
                return Fail($"{field} looks like a path or an address", out reason);
        }

        if (Location is not null && !PickPath.IsStorableForm(Location, out var whyLocation)) return Fail($"location is not valid: {whyLocation}", out reason);

        switch (Kind)
        {
            case PickKind.Program:
                if (ExeName is null && PackageFamily is null && Location is null) return Fail("a program needs an executable name, a package name or a place", out reason);
                if (ExeName is not null && !ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Fail("exe must be a file name ending in .exe", out reason);
                break;
            case PickKind.Folder:
                if (KnownFolder is not null && Location is not null) return Fail("a folder is a known folder or a place, not both", out reason);
                if (KnownFolder is null && Location is null || KnownFolder is not null && !KnownFolders.Contains(KnownFolder, StringComparer.OrdinalIgnoreCase)) return Fail("folder must be a known-folder name", out reason);
                break;
            case PickKind.File:
                if (Location is null) return Fail("a file needs a place", out reason);
                if (ExeName is not null || PackageFamily is not null || KnownFolder is not null || Host is not null) return Fail("a file holds nothing but its place", out reason);
                break;
            case PickKind.Site:
                if (Location is not null) return Fail("a site has no place", out reason);
                if (string.IsNullOrEmpty(Host) || !Host.Contains('.') || Host.Any(char.IsWhiteSpace) || Host != SiteMatch.Normalize(Host)) return Fail("host must be a plain lower-case host name", out reason);
                break;
            default:
                return Fail("unknown kind", out reason);
        }

        return true;
    }

    private IEnumerable<(string, string?)> Fields()
    {
        yield return ("id", Id);
        yield return ("name", Name);
        yield return ("page", PageId);
        yield return ("exe", ExeName);
        yield return ("package", PackageFamily);
        yield return ("folder", KnownFolder);
        yield return ("host", Host);
    }

    private static bool Fail(string why, out string reason)
    {
        reason = why;
        return false;
    }

    /// <summary>
    /// A readable, stable id part: lower case letters, digits and . _ - are kept; every other character is written as
    /// %XXXX, so two different names never give the same id (a space, an accent or a Greek letter is not dropped).
    /// </summary>
    private static string Slug(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.EndsWith(".exe", StringComparison.Ordinal)) lower = lower[..^4];
        var sb = new System.Text.StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') sb.Append(c);
            else sb.Append('%').Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
