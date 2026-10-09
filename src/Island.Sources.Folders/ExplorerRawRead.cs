using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Sources.Folders;

/// <summary>One look at File Explorer through the shell's window collection. Must run on an STA thread.</summary>
internal static class ExplorerRawRead
{
    public sealed record Result(IReadOnlyList<RawFolderEntry> Entries, IReadOnlyDictionary<long, IReadOnlyList<long>> TabsByFrame)
    {
        public static Result Empty { get; } = new([], new Dictionary<long, IReadOnlyList<long>>());
    }

    /// <summary>Cheap check: is any File Explorer frame open? No COM involved.</summary>
    public static bool AnyFrame() => ShellNative.FindWindowExW(0, 0, ShellNative.FrameClass, null) != 0;

    /// <summary>The File Explorer frames, top-most first.</summary>
    public static List<nint> Frames()
    {
        var frames = new List<nint>();
        ShellNative.EnumWindows((hwnd, _) =>
        {
            if (ShellNative.ClassOf(hwnd) == ShellNative.FrameClass) frames.Add(hwnd);
            return true;
        }, 0);
        return frames;
    }

    /// <summary>Null when the shell's collection cannot be had at all (the caller keeps its last good list).</summary>
    public static Result? Read()
    {
        var frames = Frames();
        if (frames.Count == 0) return Result.Empty;

        var rank = frames.Select((h, i) => (h, i)).ToDictionary(x => (long)x.h, x => x.i);
        var entries = ReadEntries(rank);
        if (entries is null) return null;

        var tabsByFrame = entries.Select(e => e.FrameHandle).Distinct()
            .ToDictionary(f => f, f => TabWindows((nint)f));
        return new Result(entries, tabsByFrame);
    }

    private static List<RawFolderEntry>? ReadEntries(Dictionary<long, int> frameRank)
    {
        var type = Type.GetTypeFromCLSID(ShellNative.ClsidShellWindows);
        if (type is null || Activator.CreateInstance(type) is not { } shell) return null;
        try
        {
            dynamic windows = shell;
            int count = windows.Count;
            var entries = new List<RawFolderEntry>(count);
            for (var i = 0; i < count; i++)
            {
                if (ReadItem(windows, i, frameRank) is { } entry) entries.Add(entry);
            }

            return entries;
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    // A window that disappears or answers badly while being read is skipped; the next poll sees the truth.
    private static RawFolderEntry? ReadItem(dynamic windows, int index, Dictionary<long, int> frameRank)
    {
        object? item = null;
        try
        {
            item = windows.Item(index);
            if (item is null) return null;
            dynamic d = item;
            long frame = Convert.ToInt64((object)d.HWND);
            if (!frameRank.TryGetValue(frame, out var rank)) return null; // not a File Explorer frame
            return new RawFolderEntry(frame, TabWindow(item), PathOf(d), rank);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or FormatException or OverflowException)
        {
            return null;
        }
        finally
        {
            if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }

    // The address first (cheap); the folder object only when the address says nothing, e.g. for special folders.
    private static string? PathOf(dynamic item)
    {
        if (FolderLocation.FromUrl((string?)item.LocationURL) is { } fromUrl) return fromUrl;
        try { return (string?)item.Document.Folder.Self.Path; }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { return null; }
    }

    // The per-tab identity is the window of the tab's own browser; 0 when it cannot be had.
    private static long TabWindow(object item)
    {
        if (item is not ShellNative.IShellServiceProvider provider) return 0;
        var service = ShellNative.SidTopLevelBrowser;
        var iid = ShellNative.IidShellBrowser;
        if (provider.QueryService(ref service, ref iid, out var raw) != 0 || raw == 0) return 0;
        try
        {
            var browser = (ShellNative.IShellBrowser)Marshal.GetObjectForIUnknown(raw);
            try { return browser.GetWindow(out var hwnd) == 0 ? hwnd : 0; }
            finally { Marshal.ReleaseComObject(browser); }
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    // FindWindowEx walks the children in z-order, top first: the first one is the active tab.
    private static IReadOnlyList<long> TabWindows(nint frame)
    {
        var tabs = new List<long>();
        for (var tab = ShellNative.FindWindowExW(frame, 0, ShellNative.TabClass, null); tab != 0; tab = ShellNative.FindWindowExW(frame, tab, ShellNative.TabClass, null))
        {
            tabs.Add(tab);
            if (tabs.Count > 200) break; // a shell that loops would otherwise never end
        }

        return tabs;
    }
}
