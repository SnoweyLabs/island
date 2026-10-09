using System.Text.Json;

namespace Island.Core;

/// <summary>
/// The number every file the app keeps for a person carries from now on (WORK-ORDER-8 section 1), under the name "schema". A file without it is version 1:
/// every file written before this was introduced, the owner's own included, and it loads exactly as it did. The files of picks, pages and scenes carried
/// "version": 1 before; it is read as the same thing. A file with a lower number than <see cref="Current"/> is read and brought up to date in memory; a file
/// with a higher number is not read at all and never written: the app runs on defaults, says so, and leaves the file as it is.
/// </summary>
public static class FileSchema
{
    /// <summary>The number this build writes. A change to the shape of a file raises it, adds a folder beside tests/Island.Tests/Fixtures/v1 and teaches the reader of that file to bring the old shape up to date.</summary>
    public const int Current = 1;

    /// <summary>
    /// The number the picks file carries when it holds the shape of WORK-ORDER-10 (a pick of kind "file", or a "location"): one more than before. A picks file whose picks
    /// all have the old shape keeps <see cref="Current"/>, so an older Island reads it as it always did; a build that knows this shape reads up to this number.
    /// </summary>
    public const int CurrentPicks = 2;

    public const string Key = "schema";

    /// <summary>The detail a load carries when the file is from a newer version: the app shows SETTINGS_FROM_NEWER_VERSION instead of the "could not be read" refusal.</summary>
    public const string NewerDetail = "This file was written by a newer version of Island.";

    /// <summary>The number of the file: "schema", else the old "version", else 1.</summary>
    public static int Of(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return 1;
        foreach (var name in new[] { Key, "version" })
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        }

        return 1;
    }

    /// <summary>Why the file's number cannot be used, or null: it is not a whole number from 1, or it is higher than this build knows.</summary>
    public static string? Problem(JsonElement root) => Problem(root, Current);

    /// <summary>The same, for a file whose kind has its own number (<see cref="CurrentPicks"/>).</summary>
    public static string? Problem(JsonElement root, int current)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { Key, "version" })
        {
            if (!root.TryGetProperty(name, out var v)) continue;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < 1) return "The version of the file is not a number.";
            if (n > current) return NewerDetail;
        }

        return null;
    }

    public static bool IsNewer(string? detail) => detail == NewerDetail;
}
