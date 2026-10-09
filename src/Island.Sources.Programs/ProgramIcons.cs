using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Sources.Programs;

/// <summary>
/// Icons read from the programs themselves, never from the internet (EVALS I1, I3). The reading is done on one
/// STA thread of this class, so the shell is always called from the right kind of thread; a call from another
/// thread waits for that thread, so the app must ask from a background thread, never the drawing one.
/// Icons are asked for at 256 pixels and then smaller sizes until the shell gives one (it scales a program's
/// largest picture itself); for a packaged app the picture comes through the app's id in shell:AppsFolder.
/// Results are cached in memory by program.
/// </summary>
public sealed class ProgramIcons : IIconSource, IDisposable
{
    private static readonly int[] Sizes = [256, 128, 64, 48, 32, 16];
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);
    private const int FileInfoTries = 3;

    private readonly IProgramCatalog _catalog;
    private readonly BlockingCollection<Action> _work = [];
    private readonly ConcurrentDictionary<string, IconImage?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _thread;
    private IReadOnlyList<IconImage>? _genericReferences;

    public ProgramIcons(IProgramCatalog catalog)
    {
        _catalog = catalog;
        _thread = new Thread(() =>
        {
            foreach (var job in _work.GetConsumingEnumerable()) job();
        }) { IsBackground = true, Name = "Island icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Dispose() => _work.CompleteAdding();

    public IconImage? ProgramIcon(string? exeName, string? packageFamily)
    {
        var key = $"p:{exeName}|{packageFamily}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var icon = OnOwnThread(() => ReadProgramIcon(exeName, packageFamily));
        if (icon is not null) _cache[key] = icon; // a miss is not cached: the catalog may not have been read yet
        return icon;
    }

    public IconImage? FolderIcon(string knownFolder)
    {
        var key = "f:" + knownFolder;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var icon = OnOwnThread(() => KnownFolders.PathOf(knownFolder) is { } path ? ReadTarget(path) : null);
        if (icon is not null) _cache[key] = icon;
        return icon;
    }

    /// <summary>The icon of a file by its path (for checks of the reader itself).</summary>
    public IconImage? FileIcon(string path) => OnOwnThread(() => ReadTarget(path));

    /// <summary>The icon of something the person added by hand, by its real place (asked off the drawing thread; remembered by place in memory only).</summary>
    public IconImage? PlaceIcon(string realPath)
    {
        var key = "x:" + realPath;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var icon = FileIcon(realPath);
        if (icon is not null) _cache[key] = icon;
        return icon;
    }

    /// <summary>
    /// True when the picture is the generic icon Windows gives a file with no icon of its own: the plain document
    /// icon of a made-up extension, or the generic program icon. The two are asked of Windows at this machine's own
    /// sizes and compared with <see cref="IconCompare"/>.
    /// </summary>
    public bool IsGenericFileIcon(IconImage image) =>
        OnOwnThread(() => References().Any(r => IconCompare.AreAlike(image, r)));

    private T? OnOwnThread<T>(Func<T?> read)
    {
        if (_work.IsAddingCompleted) return default;
        var done = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _work.Add(() =>
            {
                try
                {
                    done.SetResult(read());
                }
                catch (Exception)
                {
                    done.SetResult(default); // fail soft: no icon, so the item shows its letters
                }
            });
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        return done.Task.Wait(Wait) ? done.Task.Result : default;
    }

    private IconImage? ReadProgramIcon(string? exeName, string? packageFamily)
    {
        var target = _catalog.Installed.FirstOrDefault(p => Matches(p, exeName, packageFamily))?.LaunchTarget
                     ?? (exeName is not null ? LocateExecutable(exeName) : null);
        return target is null ? null : ReadTarget(target);
    }

    private static bool Matches(InstalledProgram p, string? exeName, string? packageFamily) =>
        exeName is not null && string.Equals(p.ExeName, exeName, StringComparison.OrdinalIgnoreCase)
        || packageFamily is not null && string.Equals(p.PackageFamily, packageFamily, StringComparison.OrdinalIgnoreCase);

    /// <summary>A program that is not in the catalog (a system tool, say): look under Windows' own folders.</summary>
    private static string? LocateExecutable(string exeName)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new[] { windows, Path.Combine(windows, "System32") }
            .Select(dir => Path.Combine(dir, Path.GetFileName(exeName)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>A shell:AppsFolder id or a file path to its icon: the shell's image factory from large to small, then the older file-info call.</summary>
    private static IconImage? ReadTarget(string target)
    {
        if (ShellItems.FromParsingName(target) is { } item)
            foreach (var size in Sizes)
                if (IconReader.FromBitmap(ShellItems.IconBitmap(item, size)) is { } image)
                    return image;

        return target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? null : FileInfoIcon(target);
    }

    private IReadOnlyList<IconImage> References() => _genericReferences ??=
    [
        .. new[] { "generic.zzqq9", "generic.exe" }
            .SelectMany(name => ImageListSizes.Select(s => ImageListIcon(name, s)))
            .OfType<IconImage>(),
    ];

    // --- Win32 pieces -------------------------------------------------------------------------------------------------

    private static readonly int[] ImageListSizes = [4, 2, 0]; // SHIL_JUMBO (256), SHIL_EXTRALARGE (48), SHIL_LARGE (32)

    private const uint GetIconFlag = 0x100;      // SHGFI_ICON (with no SHGFI_SMALL flag: the large, 32-pixel one)
    private const uint SysIconIndexFlag = 0x4000; // SHGFI_SYSICONINDEX
    private const uint UseFileAttributesFlag = 0x10; // SHGFI_USEFILEATTRIBUTES: the file need not exist
    private const uint FileAttributeNormal = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(nint image, nint mask, out int index);

        [PreserveSig] int ReplaceIcon(int index, nint icon, out int result);

        [PreserveSig] int SetOverlayImage(int image, int overlay);

        [PreserveSig] int Replace(int index, nint image, nint mask);

        [PreserveSig] int AddMasked(nint image, int maskColor, out int index);

        [PreserveSig] int Draw(nint parameters);

        [PreserveSig] int Remove(int index);

        [PreserveSig] int GetIcon(int index, int flags, out nint icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfoW(string path, uint attributes, ref FileInfo info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int imageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList list);

    private static readonly Guid ImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>The 32-pixel icon from the older file-info call, retried because it fails now and then (Research/windows-apis.md section 2).</summary>
    private static IconImage? FileInfoIcon(string path)
    {
        for (var attempt = 0; attempt < FileInfoTries; attempt++)
        {
            var info = new FileInfo();
            if (SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<FileInfo>(), GetIconFlag) != 0
                && IconReader.FromIcon(info.Icon) is { } image)
                return image;
            Thread.Sleep(20);
        }

        return null;
    }

    /// <summary>The system image list's picture for a file name that need not exist (only its extension counts).</summary>
    private static IconImage? ImageListIcon(string fileName, int imageListSize)
    {
        var info = new FileInfo();
        if (SHGetFileInfoW(fileName, FileAttributeNormal, ref info, (uint)Marshal.SizeOf<FileInfo>(), SysIconIndexFlag | UseFileAttributesFlag) == 0) return null;
        var iid = ImageListId;
        if (SHGetImageList(imageListSize, ref iid, out var list) != 0) return null;
        return list.GetIcon(info.IconIndex, 1, out var icon) == 0 ? IconReader.FromIcon(icon) : null; // 1 = ILD_TRANSPARENT
    }
}
