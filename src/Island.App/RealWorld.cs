using Island.Bridge;
using Island.Core;
using Island.Sources.Folders;
using Island.Sources.Media;
using Island.Sources.Programs;

namespace Island.App;

/// <summary>
/// No browser tabs: what the island sees when the add-on is not connected (or not yet fitted in). Site picks then
/// cannot know whether their site is open.
/// </summary>
internal sealed class NoTabs : ITabSource, ITabControl
{
    public bool Connected => false;

    public IReadOnlyList<TabInfo> Tabs => [];

    public byte[]? IconPng(string tabKey) => null;

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public bool Activate(string tabKey) => false;

    public bool Close(string tabKey) => false;

    public bool Media(string tabKey, MediaCommand command) => false;
}

/// <summary>
/// The real readers of this laptop, started once: windows and programs, installed programs, icons, File Explorer
/// windows and Windows' media sessions, with the one outside door of the programs source. Each runs on its own
/// thread and raises its events there; <see cref="AppWorld"/> passes them on and the app marshals them to the UI thread.
/// </summary>
internal sealed class RealWorld : IDisposable
{
    private RealWorld(AppWorld world, InstalledProgramCatalog catalog, WindowLister windows, ProgramIcons icons, ExplorerWindowReader folders, MediaSessionReader media, TabBridge? tabs)
    {
        Tabs = tabs;
        World = world;
        Catalog = catalog;
        Windows = windows;
        Icons = icons;
        Folders = folders;
        Media = media;
    }

    public AppWorld World { get; }
    public InstalledProgramCatalog Catalog { get; }
    public WindowLister Windows { get; }
    public ProgramIcons Icons { get; }
    public ExplorerWindowReader Folders { get; }
    public MediaSessionReader Media { get; }

    /// <summary>The listener for the browser add-on; null when it was not started (the self-test never opens the real ports).</summary>
    public TabBridge? Tabs { get; }

    /// <param name="listenForAddon">Start the loopback listener of the browser add-on. Off under the self-test, so Dan's real add-on can never connect to a test run.</param>
    /// <param name="log">The island's log line writer; the listener says "on" or "off" once and then only when an add-on connects, leaves or is refused.</param>
    public static RealWorld Create(bool listenForAddon, Action<string>? log = null)
    {
        TabBridge? bridge = null;
        if (listenForAddon)
        {
            bridge = new TabBridge();
            if (log is not null) bridge.AddonHappened += e => log(AddonLog.Line(e));
            bridge.Start();
        }
        else log?.Invoke(AddonLog.Line(new AddonEvent(AddonEventKind.ListenerOff)));

        var catalog = new InstalledProgramCatalog();
        var windows = new WindowLister();
        var icons = new ProgramIcons(catalog);
        var folders = new ExplorerWindowReader();
        folders.Start();
        var media = new MediaSessionReader();
        media.Start();

        var none = new NoTabs();
        var world = new AppWorld
        {
            Windows = windows,
            Catalog = catalog,
            Icons = icons,
            Folders = folders,
            Media = media,
            MediaControl = new OutsideMedia(media),
            Tabs = bridge is null ? none : bridge,
            TabControl = bridge is null ? none : new OutsideTabs(bridge),
            Outside = new OutsideActions(catalog),
        };
        world.Listen();
        return new RealWorld(world, catalog, windows, icons, folders, media, bridge);
    }

    public void Dispose()
    {
        Windows.Dispose();
        Icons.Dispose();
        Folders.Dispose();
        Media.Dispose();
        Tabs?.Dispose();
    }
}
