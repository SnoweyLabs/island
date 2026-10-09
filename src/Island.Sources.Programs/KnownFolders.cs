using System.Runtime.InteropServices;

namespace Island.Sources.Programs;

/// <summary>The folder ids behind <c>Pick.KnownFolders</c>. Where a folder lives is looked up each time and kept in memory only.</summary>
public static class KnownFolders
{
    // KNOWNFOLDERID values from knownfolders.h.
    private static readonly Dictionary<string, Guid> Ids = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
        ["Documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        ["Desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        ["Music"] = new("4BD8D571-6D19-48D3-BE97-422220080E43"),
        ["Pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        ["Videos"] = new("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
    };

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    /// <summary>The folder's current path, or null for a name that is not one of the known folders or cannot be resolved.</summary>
    public static string? PathOf(string knownFolder)
    {
        if (!Ids.TryGetValue(knownFolder, out var id)) return null;
        return SHGetKnownFolderPath(ref id, 0, 0, out var path) == 0 && Directory.Exists(path) ? path : null;
    }

    /// <summary>The path of a known-folder id given as a GUID in text ("{...}"), as Start-menu shortcuts of desktop programs use it; null when unknown.</summary>
    public static string? PathOf(Guid id) =>
        SHGetKnownFolderPath(ref id, 0, 0, out var path) == 0 ? path : null;
}
