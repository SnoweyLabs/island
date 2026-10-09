namespace Island.Core;

/// <summary>Finds the browser add-on's folder (Dan's P11, WORK-ORDER-13): the folder named <c>extension</c>, with a <c>manifest.json</c> in it, beside the island or in one of the folders above it.</summary>
public static class AddonFolder
{
    public const string Name = "extension";

    /// <summary>How many folders above the island are looked in (Claude): the island runs from dist\Island inside the repository.</summary>
    public const int Levels = 5;

    public static string? Find(string baseDirectory, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory)) return null;
        var dir = baseDirectory;
        for (var level = 0; level <= Levels && dir is { Length: > 0 }; level++)
        {
            var candidate = Path.Combine(dir, Name);
            if (fileExists(Path.Combine(candidate, "manifest.json"))) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
    }
}
