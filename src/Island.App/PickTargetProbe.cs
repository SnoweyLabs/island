using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// Is a hand-added place still there? Reads the disk (File.Exists, Directory.Exists) and nothing else; called off the drawing thread by <see cref="PickPages.StartTargetProbe"/>.
/// The path is in memory only and never reaches a log. A folder is a directory; a file or a program (or its shortcut) is a file.
/// </summary>
internal sealed class PickTargetProbe : IPickTargetProbe
{
    public bool Exists(PickKind kind, string path)
    {
        try
        {
            return kind == PickKind.Folder ? Directory.Exists(path) : File.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
