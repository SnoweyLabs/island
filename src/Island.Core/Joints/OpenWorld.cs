namespace Island.Core;

// The shapes of "what is open right now" that every real reader plugs into (NIGHT-PLAN.md, phase A).
// Everything here lives in memory only: no window title, program name found running, folder path, site,
// track title or artist is ever written to a file.

/// <summary>An open top-level window, as Alt+Tab would list it. <see cref="ZOrder"/> 0 is the top-most.</summary>
/// <param name="ClassName">The window's class, read for the rules and kept for the Terminals page (WORK-ORDER-11); null where it was not read (tests).</param>
/// <param name="OwnerProcessId">The process that owns the window; 0 where it was not read.</param>
public sealed record OpenWindow(long Handle, string? ExeName, string? PackageFamily, string Title, int ZOrder, string? ClassName = null, int OwnerProcessId = 0);

/// <summary>
/// A program installed on this laptop. <see cref="LaunchTarget"/> is how to start it (a path or an app id)
/// and is held in memory only; it is never saved in a pick.
/// </summary>
public sealed record InstalledProgram(string Name, string? ExeName, string? PackageFamily, string LaunchTarget);

/// <summary>An icon as 32-bit BGRA pixels, row after row, <c>Width * Height * 4</c> bytes.</summary>
public sealed record IconImage(int Width, int Height, byte[] Bgra);

/// <summary>An open File Explorer window (or one tab of it). <see cref="KnownFolder"/> is set when it shows one of Pick.KnownFolders.</summary>
public sealed record FolderWindow(long Handle, string? KnownFolder, string? PathInMemory, int ZOrder);

public enum PlaybackState
{
    Stopped,
    Paused,
    Playing,
}

public enum MediaCommand
{
    PlayPause,
    Next,
    Previous,
}

/// <summary>One of Windows' media sessions (a desktop player, or "the browser" as a whole).</summary>
public sealed record MediaSessionInfo(
    string SessionId,
    string SourceApp,
    bool IsBrowser,
    string? Title,
    string? Artist,
    PlaybackState State,
    double? PositionSeconds,
    double? LengthSeconds,
    ProgressReport? Timeline = null);

/// <summary>
/// What a tab's page reports about what it is playing (from the add-on). <paramref name="Rate"/> is the page player's playing speed
/// (1 is normal; null means 1) and <paramref name="ReadAtMs"/> the add-on's clock (milliseconds since 1970 UTC) when the position was
/// read (null: as it arrived); both let the island work the position out between reports (WORK-ORDER-7 section 2).
/// </summary>
public sealed record TabMedia(string? Title, string? Artist, PlaybackState State, double? PositionSeconds, double? LengthSeconds, double? Rate = null, double? ReadAtMs = null);

/// <summary>
/// A browser tab as the add-on reports it. <see cref="Key"/> is unique across browsers and profiles
/// (connection plus tab id). <see cref="Host"/> is the normalized host.
/// </summary>
public sealed record TabInfo(string Key, int WindowId, int TabId, string Title, string Host, bool Audible, bool Active, long LastActiveOrder, TabMedia? Media);

/// <summary>Everything that is open at one moment, as the readers report it.</summary>
public sealed record OpenSnapshot(
    IReadOnlyList<OpenWindow> Windows,
    IReadOnlyList<FolderWindow> Folders,
    IReadOnlyList<TabInfo> Tabs,
    bool TabsConnected)
{
    public static OpenSnapshot Empty { get; } = new([], [], [], false);
}

public interface IOpenWindowSource
{
    IReadOnlyList<OpenWindow> Windows { get; }

    event Action? Changed;
}

public interface IProgramCatalog
{
    IReadOnlyList<InstalledProgram> Installed { get; }
}

public interface IIconSource
{
    /// <summary>The program's icon, as large as the program offers; null when none can be read.</summary>
    IconImage? ProgramIcon(string? exeName, string? packageFamily);

    /// <summary>The folder's own icon from Windows; null when none can be read.</summary>
    IconImage? FolderIcon(string knownFolder);

    /// <summary>
    /// The icon of a thing at a real place (a program, a shortcut, a folder or a file the person added by hand, WORK-ORDER-10 §3): the icon of its kind, never a preview of
    /// what is in it. Null when none can be read. The path is in memory only.
    /// </summary>
    IconImage? PlaceIcon(string realPath);
}

public interface IFolderSource
{
    IReadOnlyList<FolderWindow> Windows { get; }

    event Action? Changed;
}

public interface IMediaSessionSource
{
    IReadOnlyList<MediaSessionInfo> Sessions { get; }

    event Action? Changed;
}

public interface IMediaControl
{
    /// <summary>Sends the command to one session. False when it could not be sent (or the outside gate refused it).</summary>
    bool Send(string sessionId, MediaCommand command);
}

public interface ITabSource
{
    /// <summary>True while at least one add-on connection is announced and open.</summary>
    bool Connected { get; }

    IReadOnlyList<TabInfo> Tabs { get; }

    /// <summary>The tab's stored icon as PNG bytes, when the add-on sent one.</summary>
    byte[]? IconPng(string tabKey);

    event Action? Changed;
}

public interface ITabControl
{
    /// <summary>Asks the add-on to switch to the tab. False when it could not be sent.</summary>
    bool Activate(string tabKey);

    /// <summary>Asks the add-on to press play/pause, next or previous in the tab's page.</summary>
    bool Media(string tabKey, MediaCommand command);

    /// <summary>Asks the add-on to close the tab (the close button, WORK-ORDER-6 section 5). False when it could not be sent.</summary>
    bool Close(string tabKey);
}

/// <summary>
/// The only way the app reaches out to the rest of the machine: start a program, open a folder or an
/// address, bring a window forward, send a media command. The real one is a class in a file whose name
/// begins "Outside" and asks <see cref="OutsideGate"/> first; tests and screens built before the real
/// readers exist use <see cref="RecordingOutside"/>.
/// </summary>
public interface IOutsideActions
{
    bool BringForward(long windowHandle);

    bool StartProgram(Pick pick);

    bool OpenFolder(string knownFolder);

    /// <summary>Opens a folder the person added by hand, by its real place (WORK-ORDER-10 §3). The place is in memory only.</summary>
    bool OpenFolderAt(string realPath);

    /// <summary>Asks Windows to open a file the person added by hand with whatever program opens it (WORK-ORDER-10 §3).</summary>
    bool OpenFile(string realPath);

    /// <summary>Hands the site's address to the default browser.</summary>
    bool OpenSite(string host);

    /// <summary>Hands a service's own results page for the text to the default browser (search, WORK-ORDER-7 section 3). Never written anywhere.</summary>
    bool OpenSearch(SearchService service, string text);
}

/// <summary>One frame of the shape the moving light follows, and how it looks (WORK-ORDER-12 section 2). Device-independent pixels from the island's window's top-left.</summary>
/// <param name="RadiusX">The corner's horizontal radius when the shape is stretched into an ellipse (the fly-in, Dan's P30); NaN means <paramref name="Radius"/>.</param>
/// <param name="RadiusY">The corner's vertical radius, likewise.</param>
public readonly record struct LightFrame(double Left, double Top, double Width, double Height, double Radius, Rgb Colour, ModeMark.Look Look, bool Breathing, double NowMs, double RadiusX = double.NaN, double RadiusY = double.NaN);

/// <summary>
/// The moving light drawn by the system's compositor, in windows of their own (WORK-ORDER-12 section 2): the glow directly beneath the island's window and the lit rim directly above
/// it. The app only moves the shape and changes the look; the light itself goes round without the app.
/// </summary>
public interface IMovingLight
{
    /// <summary>False when the compositor or the windows could not be made here: the island then draws the light the lighter way.</summary>
    bool IsAvailable { get; }

    /// <summary>Null when available; otherwise a short code (never a message).</summary>
    string? UnavailableReason { get; }

    bool IsShown { get; }

    /// <summary>Shows both windows over the island's window, in their places in the z-order, and (re)starts the light where the clock says.</summary>
    void Show(double nowMs);

    void Hide();

    /// <summary>The shape and look this frame. Cheap when nothing changed: the light then goes round by itself.</summary>
    void Follow(in LightFrame frame);
}

public interface IGlassLayer
{
    /// <summary>False when blur cannot be had on this machine (transparency off, layer not creatable).</summary>
    bool IsAvailable { get; }

    void Show();

    /// <summary>Makes the blurred shape follow the capsule: left, top, width and height in device-independent pixels from the window's top-left, and the corner radius.</summary>
    void Follow(double left, double top, double width, double height, double cornerRadius);

    void Hide();
}
