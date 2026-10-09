namespace Island.Core;

/// <summary>
/// What is done to another program's settings file when a coding helper is connected, updated or disconnected (WORK-ORDER-7 section 4, WORK-ORDER-11 section 3), apart from knowing where
/// the file is: that stays in the one gated Outside file of each helper, which hands the path in. A copy of the file as it was is saved beside it before the first write and never
/// again; an update saves a fresh second copy, over whatever second copy there is; the new text is written whole to a temporary file beside it and moved over.
/// </summary>
public static class HelperFileEditor
{
    public const string FirstCopySuffix = ".before-island";
    public const string UpdateCopySuffix = ".before-island-update";
    public const string TemporarySuffix = ".island-tmp";

    /// <summary>The text of the file; empty when there is none. <paramref name="failed"/> when it exists and cannot be read.</summary>
    public static string ReadText(string file, out bool failed)
    {
        failed = false;
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failed = true;
            return string.Empty;
        }
    }

    /// <summary>Saves the first copy when there is a file and no first copy, a fresh second copy when asked, then writes the text whole. False when anything of it failed.</summary>
    public static bool Write(string file, string text, bool freshSecondCopy)
    {
        try
        {
            var beside = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(beside);
            var backup = file + FirstCopySuffix;
            if (File.Exists(file) && !File.Exists(backup)) File.Copy(file, backup);
            if (freshSecondCopy && File.Exists(file)) File.Copy(file, file + UpdateCopySuffix, overwrite: true);
            var temporary = file + TemporarySuffix;
            File.WriteAllText(temporary, text);
            File.Move(temporary, file, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(file + TemporarySuffix); // nothing of a refused write is left in the other program's folder
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // nothing more can be done
            }

            return false;
        }
    }

    /// <summary>True when the program the hook runs is in <paramref name="from"/> (beside the island).</summary>
    public static bool NotifyIsBeside(string from) => File.Exists(Path.Combine(from, HookInstaller.ProgramName + ".exe"));

    /// <summary>
    /// Copies the program the hook runs and the files it needs beside it from <paramref name="from"/> into <paramref name="to"/>: the library first and the program last, so that a copy that
    /// stops halfway (a hook is running and holds a file) never leaves a new program on an old library. False when the program is not in <paramref name="from"/> or a copy fails.
    /// </summary>
    public static bool CopyNotify(string from, string to)
    {
        try
        {
            if (!File.Exists(Path.Combine(from, HookInstaller.ProgramName + ".exe"))) return false;
            Directory.CreateDirectory(to);
            File.Copy(Path.Combine(from, "Island.Core.dll"), Path.Combine(to, "Island.Core.dll"), overwrite: true);
            foreach (var name in new[] { ".dll", ".deps.json", ".runtimeconfig.json", ".exe" })
                File.Copy(Path.Combine(from, HookInstaller.ProgramName + name), Path.Combine(to, HookInstaller.ProgramName + name), overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
