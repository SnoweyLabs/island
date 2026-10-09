using System.Runtime.InteropServices;

namespace Island.Sources.Programs;

// The shell's item interfaces (shobjidl_core.h), declared by hand because no package may be added.
// The order of the methods is the order of the vtable; do not reorder.

[StructLayout(LayoutKind.Sequential)]
internal struct ShellPropertyKey(Guid format, uint id)
{
    public Guid Format = format;
    public uint Id = id;
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint bindContext, ref Guid handlerId, ref Guid interfaceId, out nint result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IShellItem other, uint hint, out int order);
}

// Declared on its own with the five IShellItem methods repeated first, not as a C# child of IShellItem:
// as a child, the vtable slots did not line up on this machine and the first call crashed the process.
[ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    void BindToHandler(nint bindContext, ref Guid handlerId, ref Guid interfaceId, out nint result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IShellItem other, uint hint, out int order);

    [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out nint store);

    [PreserveSig] int GetPropertyStoreWithCreateObject(int flags, nint createObject, ref Guid riid, out nint store);

    [PreserveSig] int GetPropertyStoreForKeys(nint keys, uint count, int flags, ref Guid riid, out nint store);

    [PreserveSig] int GetPropertyDescriptionList(ref ShellPropertyKey key, ref Guid riid, out nint list);

    [PreserveSig] int Update(nint bindContext);

    [PreserveSig] int GetProperty(ref ShellPropertyKey key, nint value);

    [PreserveSig] int GetCLSID(ref ShellPropertyKey key, out Guid clsid);

    [PreserveSig] int GetFileTime(ref ShellPropertyKey key, out long fileTime);

    [PreserveSig] int GetInt32(ref ShellPropertyKey key, out int value);

    [PreserveSig] int GetString(ref ShellPropertyKey key, out nint value); // a raw pointer: it is only valid when the call succeeded
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);

    [PreserveSig] int Skip(uint count);

    [PreserveSig] int Reset();
}

[StructLayout(LayoutKind.Sequential)]
internal struct ShellSize(int width, int height)
{
    public int Width = width;
    public int Height = height;
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(ShellSize size, int flags, out nint bitmap);
}

internal static class ShellItems
{
    public const uint NormalDisplay = 0x00000000;
    public const uint DesktopAbsoluteParsing = 0x80028000;
    public const int IconOnly = 0x4; // SIIGBF_ICONONLY: the icon, never a thumbnail of the content

    private static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid EnumItemsHandler = new("94f60519-2850-4924-aa5a-d15e84868039");
    private static readonly Guid EnumShellItemsId = new("70629033-e363-4a28-a567-0db78006e6d7");

    // System.Link.TargetParsingPath: {B9B4B3FC-2B51-4A42-B5D8-324146AFCF25}, 2 (propkey.h).
    private static ShellPropertyKey TargetParsingPath => new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string name, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    public static IShellItem? FromParsingName(string name)
    {
        var iid = ShellItemId;
        return SHCreateItemFromParsingName(name, 0, ref iid, out var item) == 0 ? item : null;
    }

    /// <summary>The items inside a shell folder item (shell:AppsFolder for the installed programs).</summary>
    public static IEnumerable<IShellItem> Children(IShellItem folder)
    {
        var handler = EnumItemsHandler;
        var iid = EnumShellItemsId;
        folder.BindToHandler(0, ref handler, ref iid, out var raw);
        if (raw == 0) yield break;

        var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(raw);
        Marshal.Release(raw);
        while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1) yield return item;
    }

    public static string? Display(IShellItem item, uint sigdn)
    {
        try
        {
            item.GetDisplayName(sigdn, out var name);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>The path of the program a Start-menu shortcut points at; null when the item has none (a packaged app).</summary>
    public static string? ShortcutTarget(IShellItem item)
    {
        if (item is not IShellItem2 item2) return null;
        var key = TargetParsingPath;
        if (item2.GetString(ref key, out var raw) != 0 || raw == 0) return null;
        var target = Marshal.PtrToStringUni(raw);
        Marshal.FreeCoTaskMem(raw);
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    /// <summary>The item's icon as a GDI bitmap handle at the requested size; 0 when the shell cannot make one. The caller deletes it.</summary>
    public static nint IconBitmap(IShellItem item, int size) =>
        item is IShellItemImageFactory factory && factory.GetImage(new ShellSize(size, size), IconOnly, out var bitmap) == 0 ? bitmap : 0;
}
