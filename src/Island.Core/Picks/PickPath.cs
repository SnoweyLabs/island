namespace Island.Core;

/// <summary>
/// WORK-ORDER-10 "THE RULE ABOUT PATHS": the one place that decides what a path a person points at may look like, how it is written into the
/// picks file and how it is compared. Pure text: nothing here touches the disk, so a link is never followed and a path is never "resolved".
/// Only a path on a lettered drive is accepted (never a network place, a device path or an address). A path under the profile folder is stored with
/// the folder written as the literal text <see cref="ProfileToken"/>, so the Windows account name reaches no file; it is expanded in memory only.
/// The profile folder is always passed in (the app reads it once from Windows; tests use an invented one).
/// </summary>
public static class PickPath
{
    /// <summary>What stands for the profile folder in a stored path. Never expanded in the file.</summary>
    public const string ProfileToken = "%USERPROFILE%";

    /// <summary>
    /// Windows' own classic limit: MAX_PATH is 260 characters INCLUDING the terminating null, so a path holds at most 259
    /// (Microsoft Learn, "Maximum Path Length Limitation", page dated 2024-07-15, read 2026-10-07). The longer-path behaviour needs an opt-in the
    /// app does not make, so the classic limit is the one that holds.
    /// </summary>
    public const int MaxPathChars = 259;

    /// <summary>What a stored path may hold: the real limit plus the token that stands for the profile folder.</summary>
    public const int MaxStoredChars = MaxPathChars + 13; // 13 = ProfileToken.Length, pinned by a test

    /// <summary>(Claude) Text longer than this is refused before it is even looked at: a megabyte typed into a field costs nothing.</summary>
    public const int MaxTypedChars = 4096;

    private static readonly char[] InvalidNameChars = ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>True when a person may point at <paramref name="path"/> (a real path, not yet compressed). <paramref name="reason"/> is plain text that names no path.</summary>
    public static bool IsAcceptable(string? path, out string reason) => TryNormalize(path, out _, out reason);

    /// <summary>
    /// The path as it is compared and kept: a drive letter in capitals, backslashes only, no doubled or trailing separator (the root keeps its one),
    /// "." and ".." resolved in the text, trailing dots and spaces of a name dropped as Windows does. Forward slashes are accepted and become backslashes (Claude).
    /// </summary>
    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(text[i])) return true;
        }

        return false;
    }

    public static bool TryNormalize(string? path, out string normalized, out string reason)
    {
        normalized = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return Refuse("no place was given", out reason);
        if (path.Length > MaxTypedChars) return Refuse("that is longer than Windows allows for a path", out reason);
        if (path.Any(char.IsControl)) return Refuse("a path cannot hold a control character", out reason);
        if (HasLoneSurrogate(path)) return Refuse("a path cannot hold half of a character", out reason); // it would be saved as another place

        var text = path.Trim();
        if (text.StartsWith("\\\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal)
            || text.StartsWith("\\/", StringComparison.Ordinal) || text.StartsWith("/\\", StringComparison.Ordinal))
            return Refuse("a network place or a device path cannot be used, only a place on a drive such as C:\\", out reason);
        if (text.Contains("://", StringComparison.Ordinal)) return Refuse("that is an address, not a place on a drive", out reason);
        if (text.Length < 2 || !char.IsAsciiLetter(text[0]) || text[1] != ':') return Refuse("a place must begin with a drive letter such as C:\\", out reason);
        if (text.Length == 2 || !IsSeparator(text[2])) return Refuse("a drive letter must be followed by a backslash", out reason);

        if (!TrySegments(text, 3, out var segments, out reason)) return false;
        var result = $"{char.ToUpperInvariant(text[0])}:\\" + string.Join('\\', segments);
        if (result.Length > MaxPathChars) return Refuse($"that is longer than Windows allows ({MaxPathChars + 1} characters with the end mark)", out reason);

        normalized = result;
        return true;
    }

    /// <summary>
    /// The form of a path that is kept in the file: <see cref="TryNormalize"/>'s, or the same with the profile folder written as
    /// <see cref="ProfileToken"/>. Reading a file accepts any capitals of the token and brings it to the one spelling.
    /// </summary>
    public static bool TryCanonical(string? stored, out string canonical, out string reason)
    {
        canonical = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(stored)) return Refuse("no place was given", out reason);
        if (stored.Length > MaxStoredChars) return Refuse("that is longer than Windows allows for a path", out reason);
        var text = stored.Trim();
        if (!text.StartsWith(ProfileToken, StringComparison.OrdinalIgnoreCase)) return TryNormalize(text, out canonical, out reason);

        if (text.Any(char.IsControl)) return Refuse("a path cannot hold a control character", out reason);
        if (HasLoneSurrogate(text)) return Refuse("a path cannot hold half of a character", out reason);
        if (text.Length > ProfileToken.Length && !IsSeparator(text[ProfileToken.Length])) return Refuse("the profile folder must be followed by a backslash", out reason);
        if (!TrySegments(text, ProfileToken.Length, out var segments, out reason)) return false;
        canonical = segments.Count == 0 ? ProfileToken : ProfileToken + "\\" + string.Join('\\', segments);
        if (canonical.Length > MaxStoredChars) return Refuse("that is longer than Windows allows for a path", out reason);
        return true;
    }

    /// <summary>True when <paramref name="stored"/> is already in the one canonical form: the only thing a pick may hold in its path field.</summary>
    public static bool IsStorableForm(string stored, out string reason) =>
        TryCanonical(stored, out var canonical, out reason) && (canonical == stored || Refuse("the path is not written in its one plain form", out reason));

    /// <summary>The form to store for a real path, with the profile folder written as the token; null when the path cannot be used.</summary>
    public static string? Compress(string? path, string? profileFolder)
    {
        if (!TryNormalize(path, out var real, out _)) return null;
        if (!TryProfile(profileFolder, out var profile)) return real;
        if (real.Equals(profile, StringComparison.OrdinalIgnoreCase)) return ProfileToken;
        return real.Length > profile.Length && real[profile.Length] == '\\' && real.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
            ? ProfileToken + real[profile.Length..]
            : real;
    }

    /// <summary>The real path of a stored one, in memory only; null when it cannot be had (an unusable path, or a token with no profile folder to put in its place).</summary>
    public static string? Expand(string? stored, string? profileFolder)
    {
        if (!TryCanonical(stored, out var canonical, out _)) return null;
        if (!canonical.StartsWith(ProfileToken, StringComparison.Ordinal)) return canonical;
        if (!TryProfile(profileFolder, out var profile)) return null;
        var real = profile + canonical[ProfileToken.Length..];
        return real.Length <= MaxPathChars ? real : null;
    }

    /// <summary>What two stored paths are compared by: the full path, no trailing separator, capitals ignored. Null for a path that cannot be used.</summary>
    public static string? Key(string? stored) =>
        TryCanonical(stored, out var canonical, out _) ? canonical.ToUpperInvariant() : null;

    /// <summary>The same for a REAL path (the location an Explorer window reports); null for anything that is not a plain place on a drive.</summary>
    public static string? RealKey(string? real) =>
        TryNormalize(real, out var normalized, out _) ? normalized.ToUpperInvariant() : null;

    /// <summary>True when the stored path still spells out the profile folder (it would carry the account name into the file).</summary>
    public static bool SpellsOutProfile(string? stored, string? profileFolder)
    {
        if (!TryCanonical(stored, out var canonical, out _) || canonical.StartsWith(ProfileToken, StringComparison.Ordinal)) return false;
        if (!TryProfile(profileFolder, out var profile)) return false;
        return canonical.Equals(profile, StringComparison.OrdinalIgnoreCase)
            || canonical.Length > profile.Length && canonical[profile.Length] == '\\' && canonical.StartsWith(profile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The last name of a path ("notes.txt", "Alpha"), or null for a drive's root. For a stored or a real path.</summary>
    public static string? LeafName(string? path)
    {
        if (!TryCanonical(path, out var canonical, out _)) return null;
        var cut = canonical.LastIndexOf('\\');
        var leaf = canonical[(cut + 1)..];
        return leaf.Length == 0 || leaf == ProfileToken ? null : leaf;
    }

    // A profile folder that is a drive's root is not a profile: compressing every path of the drive would hide the place.
    private static bool TryProfile(string? profileFolder, out string profile) =>
        TryNormalize(profileFolder, out profile, out _) && profile.Length > 3;

    private static bool TrySegments(string text, int start, out List<string> segments, out string reason)
    {
        segments = [];
        reason = string.Empty;
        foreach (var raw in text[start..].Split('\\', '/'))
        {
            if (raw.Length == 0 || raw == ".") continue;
            if (raw == "..")
            {
                if (segments.Count == 0) return Refuse("a path cannot go above the top of its drive", out reason);
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            var name = raw.TrimEnd('.', ' ');
            if (name.Length == 0) return Refuse("a path has a name made only of dots or spaces", out reason);
            if (name.IndexOfAny(InvalidNameChars) >= 0) return Refuse("a path has a character that Windows does not allow in a name", out reason);
            var stem = name.Split('.')[0].TrimEnd(' ');
            if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return Refuse("a path uses a name that Windows keeps for devices", out reason);
            segments.Add(name);
        }

        return true;
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';

    private static bool Refuse(string why, out string reason)
    {
        reason = why;
        return false;
    }
}
