namespace Island.Core;

/// <summary>
/// How every file the app owns is written (WORK-ORDER-12 section 4, CODE): the whole text goes into a temporary file beside it and the temporary file replaces the live one in a single move, so a
/// reader (an editor, a backup or sync program, the next start after a power cut) meets the old complete file or the new complete one, never a cut one. A move that fails (a program has the file open for a moment)
/// is tried again a few times; when it still fails the temporary file is deleted, so a list that was refused does not stay behind in a file of its own, and the exception goes to the caller.
/// </summary>
public static class AtomicFile
{
    /// <summary>How many times the move is tried (Claude).</summary>
    public const int Tries = 6;

    /// <summary>How long to wait between two tries, in milliseconds (Claude): long enough for a scanner or an indexer to let go (a quarter of a second in all), short enough that a save does not feel slow.</summary>
    public const int WaitMs = 50;

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> atomically; creates the folder. Throws what the file system throws, after the temporary file is gone.</summary>
    public static void Write(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        var liveExisted = File.Exists(path);
        try
        {
            File.WriteAllText(temp, text);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // ReplaceFile (File.Replace) puts the new file in place of the live one even while a reader that allows deleting has the live one open; the reader goes on reading the old file.
                    if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else File.Move(temp, path);
                    return;
                }
                catch (Exception e) when (attempt < Tries && e is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(WaitMs);
                }
            }
        }
        catch (Exception)
        {
            try
            {
                // After a replace that failed half way (ERROR_UNABLE_TO_MOVE_REPLACEMENT) the live file may be gone and the temporary file is the only copy: it is then put in place, not deleted.
                if (File.Exists(temp) && !File.Exists(path) && !Directory.Exists(path) && liveExisted) File.Move(temp, path);
                else File.Delete(temp);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // nothing more can be done
            }

            throw;
        }
    }
}
