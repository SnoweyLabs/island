using Island.Core;

namespace Island.App;

/// <summary>
/// Everything the island knows about the machine, in one place: the readers (programs and their windows, installed
/// programs, icons, Explorer windows, media sessions, browser tabs) and the one door to the outside world. Each
/// reader is an interface of Island.Core; before the real readers are fitted in, <see cref="Pretend"/> stands in
/// for all of them and nothing is ever started or brought forward.
/// </summary>
internal sealed class AppWorld
{
    public required IOpenWindowSource Windows { get; init; }
    public required IProgramCatalog Catalog { get; init; }
    public required IIconSource Icons { get; init; }
    public required IFolderSource Folders { get; init; }
    public required IMediaSessionSource Media { get; init; }
    public required IMediaControl MediaControl { get; init; }
    public required ITabSource Tabs { get; init; }
    public required ITabControl TabControl { get; init; }
    public required IOutsideActions Outside { get; init; }

    /// <summary>Raised (from whatever thread the reader uses) when anything the readers report has changed.</summary>
    public event Action? Changed;

    public OpenSnapshot Snapshot() => new(Windows.Windows, Folders.Windows, Tabs.Tabs, Tabs.Connected);

    /// <summary>Starts passing the readers' own change events on as <see cref="Changed"/>.</summary>
    public void Listen()
    {
        Windows.Changed += Raise;
        Folders.Changed += Raise;
        Media.Changed += Raise;
        Tabs.Changed += Raise;
    }

    public void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Raised from a reader's own thread: a mistake in a listener must not end the app.
        }
    }

    /// <summary>A world of pretend readers, for tests and for the app before the real readers are fitted in.</summary>
    public static AppWorld Pretend(PretendWorld pretend, IOutsideActions? outside = null)
    {
        var world = new AppWorld
        {
            Windows = pretend,
            Catalog = pretend,
            Icons = pretend,
            Folders = pretend,
            Media = pretend,
            MediaControl = pretend,
            Tabs = pretend,
            TabControl = pretend,
            Outside = outside ?? new RecordingOutside(),
        };
        world.Listen();
        return world;
    }
}
