using System.Runtime.InteropServices;
using Island.Core;
using Microsoft.Win32;

namespace Island.Sources.Programs;

/// <summary>
/// The programs installed on this laptop, Store apps included: what Windows' own "all apps" folder lists
/// (shell:AppsFolder, which covers Start-menu programs and packaged apps), plus the executables registered under
/// App Paths. Read once on a thread of its own, and again on <see cref="Refresh"/>. Names, executable file names
/// and launch targets live in memory only; the launch target is never saved in a pick.
/// </summary>
public sealed class InstalledProgramCatalog : IProgramCatalog
{
    private const string AppsFolder = "shell:AppsFolder";
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private readonly object _gate = new();
    private readonly ManualResetEventSlim _loaded = new(false);
    private volatile IReadOnlyList<InstalledProgram> _installed = [];
    private bool _reading;

    public InstalledProgramCatalog() => Refresh();

    public IReadOnlyList<InstalledProgram> Installed => _installed;

    /// <summary>Raised on the reading thread each time a read finishes.</summary>
    public event Action? Loaded;

    /// <summary>Starts a new read in the background (does nothing when one is already running).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (_reading) return;
            _reading = true;
        }

        var thread = new Thread(Read) { IsBackground = true, Name = "Island programs" };
        thread.SetApartmentState(ApartmentState.STA); // the shell's folders must be walked from an STA thread
        thread.Start();
    }

    /// <summary>For a console check: waits until the first read has finished.</summary>
    public bool WaitForLoad(TimeSpan timeout) => _loaded.Wait(timeout);

    private void Read()
    {
        try
        {
            var programs = new List<InstalledProgram>();
            programs.AddRange(FromAppsFolder());
            programs.AddRange(FromAppPaths(programs));
            _installed = programs;
        }
        catch (Exception)
        {
            // Fail soft: keep what was read before (an empty list on the first try).
        }
        finally
        {
            lock (_gate) _reading = false;
            _loaded.Set();
            Loaded?.Invoke();
        }
    }

    private static List<InstalledProgram> FromAppsFolder()
    {
        var found = new List<InstalledProgram>();
        try
        {
            if (ShellItems.FromParsingName(AppsFolder) is not { } folder) return found;
            foreach (var item in ShellItems.Children(folder))
                if (FromItem(item) is { } program) found.Add(program);
        }
        catch (COMException)
        {
            // A half-read list is better than none.
        }

        return found;
    }

    private static InstalledProgram? FromItem(IShellItem item)
    {
        var name = ShellItems.Display(item, ShellItems.NormalDisplay);
        var id = ShellItems.Display(item, ShellItems.DesktopAbsoluteParsing);
        if (name is null || id is null) return null;

        var launch = AppsFolder + "\\" + id;
        var family = WindowRules.PackageFamilyOf(id);
        if (family is not null) return new InstalledProgram(name, null, family, launch);

        var exe = ExecutablePath(item, id);
        if (exe is not null && Path.GetFileName(exe).Equals("Update.exe", StringComparison.OrdinalIgnoreCase)) exe = SquirrelProgram(exe, name);
        return new InstalledProgram(name, exe is null ? null : Path.GetFileName(exe), null, launch);
    }

    /// <summary>
    /// Programs installed by Squirrel (several chat and editor apps) have a Start-menu shortcut to a shared
    /// Update.exe, not to the program that shows windows. The program sits in the newest "app-*" folder next to
    /// it, normally named after the app. Returns null when it is not found, so a pick never stores "Update.exe".
    /// </summary>
    private static string? SquirrelProgram(string updaterPath, string displayName)
    {
        try
        {
            var root = Path.GetDirectoryName(updaterPath);
            var newest = root is null ? null : Directory.EnumerateDirectories(root, "app-*").OrderDescending().FirstOrDefault();
            var wanted = displayName.Replace(" ", string.Empty) + ".exe";
            return newest is null ? null : Directory.EnumerateFiles(newest, "*.exe").FirstOrDefault(f => Path.GetFileName(f).Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The program file behind a Start-menu entry: its shortcut's target, or the "{known folder id}\relative path" form of its id.</summary>
    private static string? ExecutablePath(IShellItem item, string id)
    {
        var target = ShellItems.ShortcutTarget(item);
        if (IsExe(target)) return target;

        var close = id.IndexOf('}');
        if (id.StartsWith('{') && close > 1 && Guid.TryParse(id[..(close + 1)], out var folderId)
            && KnownFolders.PathOf(folderId) is { } root)
        {
            var path = Path.Combine(root, id[(close + 1)..].TrimStart('\\'));
            if (IsExe(path)) return path;
        }

        return null;
    }

    private static bool IsExe(string? path) => path is not null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static List<InstalledProgram> FromAppPaths(List<InstalledProgram> already)
    {
        var known = already.Select(p => p.ExeName).Where(n => n is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<InstalledProgram>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var root = hive.OpenSubKey(AppPathsKey);
            if (root is null) continue;
            foreach (var exe in root.GetSubKeyNames().Where(IsExe))
            {
                if (known.Contains(exe) || PathOf(root, exe) is not { } path) continue;
                known.Add(exe);
                added.Add(new InstalledProgram(Path.GetFileNameWithoutExtension(exe), exe, null, path));
            }
        }

        return added;
    }

    private static string? PathOf(RegistryKey root, string exe)
    {
        using var key = root.OpenSubKey(exe);
        var path = (key?.GetValue(null) as string)?.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path);
        return File.Exists(path) ? path : null;
    }
}
