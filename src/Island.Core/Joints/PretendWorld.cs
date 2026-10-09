namespace Island.Core;

/// <summary>
/// A simple pretend version of every reader, for tests and for building screens before the real readers
/// exist. Set its lists, call <see cref="Raise"/>, and the app's code sees them as if Windows had reported them.
/// </summary>
public sealed class PretendWorld :
    IOpenWindowSource, IProgramCatalog, IIconSource, IFolderSource, IMediaSessionSource, IMediaControl, ITabSource, ITabControl
{
    private readonly List<string> _sent = [];
    private readonly Dictionary<string, byte[]> _tabIcons = [];

    public IReadOnlyList<OpenWindow> Windows { get; set; } = [];
    public IReadOnlyList<InstalledProgram> Installed { get; set; } = [];
    public IReadOnlyList<FolderWindow> FolderWindows { get; set; } = [];
    public IReadOnlyList<MediaSessionInfo> Sessions { get; set; } = [];
    public IReadOnlyList<TabInfo> Tabs { get; set; } = [];
    public bool Connected { get; set; }

    /// <summary>Icons by key: the exe name or package family of a program, or the known-folder name of a folder.</summary>
    public Dictionary<string, IconImage> Icons { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Commands sent to sessions and tabs, one line each: "media:id:PlayPause", "tab-activate:key", "tab-media:key:Next".</summary>
    public IReadOnlyList<string> Sent => _sent;

    IReadOnlyList<FolderWindow> IFolderSource.Windows => FolderWindows;

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public void SetTabIcon(string tabKey, byte[] png) => _tabIcons[tabKey] = png;

    public OpenSnapshot Snapshot() => new(Windows, FolderWindows, Tabs, Connected);

    public IconImage? ProgramIcon(string? exeName, string? packageFamily) =>
        exeName is not null && Icons.TryGetValue(exeName, out var a) ? a
        : packageFamily is not null && Icons.TryGetValue(packageFamily, out var b) ? b
        : null;

    public IconImage? FolderIcon(string knownFolder) => Icons.GetValueOrDefault(knownFolder);

    /// <summary>Icons of things at a place: the file name decides (invented names in tests: "alpha.exe").</summary>
    public IconImage? PlaceIcon(string realPath) => Icons.GetValueOrDefault(System.IO.Path.GetFileName(realPath));

    public byte[]? IconPng(string tabKey) => _tabIcons.GetValueOrDefault(tabKey);

    public bool Send(string sessionId, MediaCommand command)
    {
        _sent.Add($"media:{sessionId}:{command}");
        return Sessions.Any(s => s.SessionId == sessionId);
    }

    public bool Activate(string tabKey)
    {
        _sent.Add($"tab-activate:{tabKey}");
        return Tabs.Any(t => t.Key == tabKey);
    }

    public bool Media(string tabKey, MediaCommand command)
    {
        _sent.Add($"tab-media:{tabKey}:{command}");
        return Tabs.Any(t => t.Key == tabKey);
    }

    public bool Close(string tabKey)
    {
        _sent.Add($"tab-close:{tabKey}");
        return Tabs.Any(t => t.Key == tabKey);
    }
}
