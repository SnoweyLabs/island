using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Sources.Programs;

/// <summary>
/// One full pass over the open top-level windows: EnumWindows, then <see cref="WindowRules"/>, then the program
/// each listed window belongs to. Only the file name of the program is kept, never its path. Runs on whatever
/// thread calls it; <see cref="WindowLister"/> calls it from its own thread.
/// </summary>
internal static class WindowReader
{
    public static IReadOnlyList<OpenWindow> Read()
    {
        var handles = new List<nint>();
        Native.EnumWindows((h, _) =>
        {
            handles.Add(h);
            return true;
        }, 0);

        var programs = new Dictionary<uint, (string? Exe, string? Family)>();
        var result = new List<OpenWindow>();
        foreach (var hwnd in handles)
        {
            if (!SafeFacts(hwnd, out var facts) || !WindowRules.IsListed(facts)) continue;
            var (exe, family) = ProgramOf(hwnd, programs);
            Native.GetWindowThreadProcessId(hwnd, out var ownerPid);
            result.Add(new OpenWindow(hwnd, exe, family, facts.Title, result.Count, facts.ClassName, (int)ownerPid));
        }

        return result;
    }

    private static bool SafeFacts(nint hwnd, out WindowFacts facts)
    {
        facts = null!;
        if (!Native.IsWindowVisible(hwnd)) return false;
        var style = Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        Native.GetWindowRect(hwnd, out var rect);
        Native.DwmGetWindowAttribute(hwnd, Native.DwmaCloaked, out var cloak, sizeof(int));
        facts = new WindowFacts(
            Visible: true,
            HasOwner: Native.GetWindow(hwnd, Native.GwOwner) != 0,
            IsToolWindow: (style & Native.WsExToolWindow) != 0,
            IsAppWindow: (style & Native.WsExAppWindow) != 0,
            MarkedDeletedFromTaskList: Native.GetPropW(hwnd, "ITaskList_Deleted") != 0,
            ClassName: Native.ClassNameOf(hwnd),
            Title: Native.TitleOf(hwnd),
            Width: rect.Right - rect.Left,
            Height: rect.Bottom - rect.Top,
            CloakFlags: cloak,
            OnOtherDesktop: cloak == 2 && VirtualDesktops.IsOnOtherDesktop(hwnd));
        return true;
    }

    private static (string? Exe, string? Family) ProgramOf(nint hwnd, Dictionary<uint, (string?, string?)> cache)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var own = OfProcess(pid, cache);
        if (!WindowRules.IsUwpFrameHost(own.Exe)) return own;

        // A UWP window belongs to the frame host; the real program is behind its "Windows.UI.Core.*" child.
        var children = new List<(long, string)>();
        Native.EnumChildWindows(hwnd, (h, _) =>
        {
            children.Add((h, Native.ClassNameOf(h)));
            return true;
        }, 0);

        if (WindowRules.PickUwpChild(children) is { } child)
        {
            Native.GetWindowThreadProcessId((nint)child, out var childPid);
            return OfProcess(childPid, cache);
        }

        // Minimised: the child is gone, but the window still carries the app's id ("Family!App").
        return (null, WindowRules.PackageFamilyOf(AppUserModelIds.Of(hwnd)));
    }

    private static (string? Exe, string? Family) OfProcess(uint pid, Dictionary<uint, (string?, string?)> cache)
    {
        if (cache.TryGetValue(pid, out var known)) return known;
        var found = (Native.ExeNameOf(pid), Native.PackageFamilyOf(pid));
        cache[pid] = found;
        return found;
    }
}

/// <summary>The app user model id a window carries, read through its property store.</summary>
internal static class AppUserModelIds
{
    private static readonly Guid PropertyStoreId = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    // System.AppUserModel.ID: {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5 (propkey.h).
    private static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public nint Value;
        public nint Value2;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetAt(uint index, out PropertyKey key);

        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("propsys.dll")]
    private static extern int PropVariantToStringAlloc(ref PropVariant value, out nint text);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    public static string? Of(nint hwnd)
    {
        try
        {
            var iid = PropertyStoreId;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null) return null;
            try
            {
                var key = AppUserModelId;
                if (store.GetValue(ref key, out var value) != 0) return null;
                try
                {
                    if (PropVariantToStringAlloc(ref value, out var text) != 0 || text == 0) return null;
                    var id = Marshal.PtrToStringUni(text);
                    Marshal.FreeCoTaskMem(text);
                    return id;
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or MarshalDirectiveException)
        {
            return null;
        }
    }
}

/// <summary>Whether a window sits on another virtual desktop (IVirtualDesktopManager, a documented shell interface).</summary>
internal static class VirtualDesktops
{
    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerClass;

    private static readonly Lazy<IVirtualDesktopManager?> Manager = new(Create);

    private static IVirtualDesktopManager? Create()
    {
        try
        {
            return (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return null;
        }
    }

    public static bool IsOnOtherDesktop(nint hwnd)
    {
        try
        {
            return Manager.Value is { } m && m.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent) == 0 && !onCurrent;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return false;
        }
    }
}
