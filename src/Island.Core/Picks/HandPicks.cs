using System.Globalization;
using System.Security.Cryptography;

namespace Island.Core;

/// <summary>What the app supplies once: the profile folder, the paths of the known folders, and (for tests) the source of the random ids.</summary>
/// <param name="KnownFolderPaths">Pairs of a known-folder name ("Downloads") and one way to spell its real path; the same list the folder reader uses.</param>
public sealed record HandContext(string? ProfileFolder, IReadOnlyList<(string Name, string Path)> KnownFolderPaths, Func<ulong>? Random = null);

/// <summary>A pick made by hand, or the reason there is none. <see cref="Code"/> is a register code (NOT_A_SITE) or PATH_REFUSED; <see cref="Message"/> is plain text that names no path.</summary>
public sealed record HandPickResult(Pick? Pick, string? Code, string? Message)
{
    public bool Ok => Pick is not null;

    internal static HandPickResult Made(Pick pick) =>
        pick.IsStorable(out var why) ? new HandPickResult(pick, null, null) : Refused("PATH_REFUSED", "That could not be added: " + why + ".");

    internal static HandPickResult Refused(string code, string message) => new(null, code, message);
}

/// <summary>
/// WORK-ORDER-10 section 3: making a pick by hand. Pure logic: nothing here reads the disk, opens a window or throws for odd input.
/// A program from the installed list is an ordinary program pick with no place. A program chosen with "Browse", a folder that is not a known
/// folder and a file keep their place in the one field made for it. The id of a pick made by hand is its kind, a colon and 16 random hex digits,
/// never made from the place. For a shortcut (.lnk) the stored pick is the shortcut's own place; its target is not read here (see the report).
/// </summary>
public static class HandPicks
{
    private const int MaxNameChars = 100; // (Claude) the name a pick gets from a file's name, cut so it always fits the pick's own limit

    public static HandPickResult FromInstalledProgram(InstalledProgram program, string pageId) =>
        HandPickResult.Made(Pick.ForProgram(program.Name, pageId, program.ExeName, program.PackageFamily));

    /// <param name="path">What the chooser returned: a program, or a shortcut to one.</param>
    /// <param name="targetExeName">The executable's file name when the app has read a shortcut's target; null when it did not (the pick then has no open or closed).</param>
    public static HandPickResult BrowsedProgram(string path, string pageId, HandContext context, string? targetExeName = null)
    {
        if (Place(path, context) is not { } place) return PathRefused(path);
        var leaf = PickPath.LeafName(place.Stored);
        if (leaf is null) return Refused("a program is a file, not a drive");

        var exe = leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? leaf : targetExeName;
        var pick = new Pick(NewId(PickKind.Program, context), PickKind.Program, ProgramName(Path.GetFileNameWithoutExtension(leaf)), pageId, ExeName: exe, Location: place.Stored);
        return HandPickResult.Made(pick);
    }

    /// <summary>A known folder becomes the ordinary folder pick with no place; any other folder keeps its place.</summary>
    public static HandPickResult Folder(string path, string pageId, HandContext context)
    {
        if (Place(path, context) is not { } place) return PathRefused(path);
        if (new FolderMatch(context.KnownFolderPaths).Resolve(place.Real) is { } known) return HandPickResult.Made(Pick.ForFolder(known, pageId));

        var leaf = PickPath.LeafName(place.Stored);
        // The profile folder's own name is the account's name, never used: it is "Home folder". A drive is "Drive C".
        var name = leaf is not null ? NameFrom(leaf, "Folder") : place.Stored == PickPath.ProfileToken ? "Home folder" : $"Drive {place.Real[0]}";
        return HandPickResult.Made(new Pick(NewId(PickKind.Folder, context), PickKind.Folder, name, pageId, Location: place.Stored));
    }

    public static HandPickResult File(string path, string pageId, HandContext context)
    {
        if (Place(path, context) is not { } place) return PathRefused(path);
        var leaf = PickPath.LeafName(place.Stored);
        if (leaf is null) return Refused("a file is not a drive");

        return HandPickResult.Made(new Pick(NewId(PickKind.File, context), PickKind.File, NameFrom(leaf, "File"), pageId, Location: place.Stored));
    }

    /// <summary>A website from typed text: only the host is kept; the name of the pick is the host. Text that is not a site's name gives NOT_A_SITE.</summary>
    public static HandPickResult Site(string? typed, string pageId) =>
        TypedSite.Understand(typed) is { Host: { } host } ? HandPickResult.Made(Pick.ForSite(host, host, pageId)) : HandPickResult.Refused(HandPickRefusals.NotASite.Code, HandPickRefusals.NotASite.Message);

    /// <summary>A hand-made pick's id: its kind, a colon and 16 random hex digits. <paramref name="random"/> is injectable for tests.</summary>
    public static string NewId(PickKind kind, Func<ulong>? random = null) =>
        kind.ToString().ToLowerInvariant() + ":" + (random ?? RandomValue)().ToString("x16", CultureInfo.InvariantCulture);

    private static string NewId(PickKind kind, HandContext context) => NewId(kind, context.Random);

    private static ulong RandomValue() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));

    private static (string Real, string Stored)? Place(string? path, HandContext context) =>
        PickPath.TryNormalize(path, out var real, out _) && PickPath.Compress(real, context.ProfileFolder) is { } stored ? (real, stored) : null;

    // The reason names no path: it is shown on a screen that must never show one.
    private static HandPickResult PathRefused(string? path) =>
        HandPickResult.Refused("PATH_REFUSED", PickPath.IsAcceptable(path, out var why) ? "That place could not be used." : $"That place cannot be used: {why}.");

    private static HandPickResult Refused(string why) => HandPickResult.Refused("PATH_REFUSED", $"That place cannot be used: {why}.");

    // A program's name is its file name with a capital, as the + list names an unknown program.
    private static string ProgramName(string stem) =>
        NameFrom(stem, "Program") is var name && char.IsAsciiLetterLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : NameFrom(stem, "Program");

    /// <summary>A file name Windows allows can hold a lone surrogate; a pick's name cannot (it would be saved as another character): the half character is left out.</summary>
    private static string DropHalfCharacters(string text) =>
        BlankText.HasBrokenCharacter(text) ? new string(text.Where((c, i) => !char.IsSurrogate(c) || (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) || (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]))).ToArray()) : text;

    /// <summary>A file name Windows allows can hold characters that draw as nothing or turn the text round (a right-to-left override, zero-width characters): they are left out of the pick name.</summary>
    private static string DropHidden(string text) => BlankText.WithoutHidden(text).Replace('\u2028', ' ').Replace('\u2029', ' '); // a line or paragraph separator would break the tile's title

    private static string NameFrom(string text, string fallback)
    {
        var trimmed = DropHidden(DropHalfCharacters(text.Trim())).Trim();
        if (trimmed.Length == 0) return fallback;
        var name = trimmed.Length > MaxNameChars ? trimmed[..MaxNameChars] : trimmed;
        if (char.IsHighSurrogate(name[^1])) name = name[..^1]; // never half a character
        return name.Length == 0 || !BlankText.HasVisible(name) ? fallback : name;
    }
}
