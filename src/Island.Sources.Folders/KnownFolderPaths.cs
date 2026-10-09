namespace Island.Sources.Folders;

/// <summary>Reads where Windows keeps the known folders of the island, for this user (in memory only).</summary>
internal static class KnownFolderPaths
{
    private static readonly (string Name, Guid Id)[] Folders =
    [
        ("Downloads", new("374DE290-123F-4565-9164-39C4925E467B")),
        ("Documents", new("FDD39AD0-238F-46AF-ADB4-6C85480369C7")),
        ("Desktop", new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641")),
        ("Music", new("4BD8D571-6D19-48D3-BE97-422220080E43")),
        ("Pictures", new("33E28130-4E1E-4676-835A-98395C3BC3BB")),
        ("Videos", new("18989B1D-99B5-455B-841C-AB7C74E4DDFC")),
    ];

    /// <summary>Each folder twice: its real path and its shell parsing name, as Explorer may show either.</summary>
    public static IReadOnlyList<(string Name, string Path)> Read()
    {
        var pairs = new List<(string, string)>();
        foreach (var (name, id) in Folders)
        {
            pairs.Add((name, $"::{{{id.ToString().ToUpperInvariant()}}}"));
            if (ShellNative.KnownFolderPath(id) is { Length: > 0 } path) pairs.Add((name, path));
        }

        return pairs;
    }
}
