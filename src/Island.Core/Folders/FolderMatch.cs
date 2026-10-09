namespace Island.Core;

/// <summary>
/// Tells which of <see cref="Pick.KnownFolders"/> a folder held in memory is. Only the folder itself counts:
/// a subfolder of Downloads is not Downloads. The real paths of the known folders are passed in (the reader
/// reads them once from Windows), so this logic is testable with made-up paths.
/// </summary>
public sealed class FolderMatch
{
    private readonly Dictionary<string, string> _nameByKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="lookup"/> pairs a known-folder name with one way to spell it: its real path, or its shell
    /// parsing name ("::{GUID}"). A name may appear several times. Pairs whose name is not a known folder are ignored.
    /// </summary>
    public FolderMatch(IEnumerable<(string Name, string Path)> lookup)
    {
        foreach (var (name, path) in lookup)
        {
            var known = Pick.KnownFolders.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            var key = Key(path);
            if (known is not null && key is not null) _nameByKey[key] = known;
        }
    }

    /// <summary>The known-folder name of <paramref name="folderPath"/> (a path or a "::{GUID}" parsing name), or null.</summary>
    public string? Resolve(string? folderPath) =>
        Key(folderPath) is { } key && _nameByKey.TryGetValue(key, out var name) ? name : null;

    // Case, slash direction and trailing slashes do not matter. Comparison is ordinal-ignore-case: Windows paths.
    private static string? Key(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var key = path.Trim().Replace('/', '\\').TrimEnd('\\');
        return key.Length == 0 ? null : key;
    }
}
