namespace Island.Core;

/// <summary>
/// What the settings screen asks about the browser add-on (WORK-ORDER-9 section 2): how many browsers are connected right now, and a word when that changes.
/// The bridge implements it. Nothing about a browser, a tab or a site goes through it: a number only.
/// </summary>
public interface IAddonStatus
{
    /// <summary>How many browsers (or profiles) have said hello and are still open.</summary>
    int Browsers { get; }

    /// <summary>Raised on a pool thread when a browser connects or leaves.</summary>
    event Action? Changed;
}

/// <summary>The words of the settings row "Browser add-on".</summary>
public static class AddonText
{
    public const string Title = "Browser add-on";

    /// <summary>The automation id of the word that says whether the add-on is connected (the self-test reads it by this).</summary>
    public const string StatusId = "general:addon-status";

    public const string Hint = "Connects by itself within half a minute of the island starting. To add it to your browser: open the browser's extensions page, turn on developer mode, press Load unpacked and choose the add-on's folder. The button opens that folder.";

    public const string OpenFolderButton = "Open the add-on folder";

    public const string OpenFolderId = "general:addon-folder";

    /// <summary>The refusal when the folder cannot be opened: three parts (what, why, what to do), in the register's way.</summary>
    public static Refusal FolderNotOpened { get; } = new(
        "ADDON_FOLDER_NOT_OPENED",
        "Island could not open the add-on's folder.",
        "It looks for a folder named extension beside the island or above it, and either did not find it or Windows would not open it.",
        "Look for the extension folder next to the program, or open it by hand from its place.");

    /// <summary>"Not connected", "Connected", and with more than one browser "Connected (2)".</summary>
    public static string Status(int browsers) => browsers <= 0 ? "Not connected" : browsers == 1 ? "Connected" : $"Connected ({browsers})";
}

/// <summary>What happened to the add-on's listener, in the words of the bridge: a kind and a number, never a name.</summary>
public enum AddonEventKind
{
    ListenerOn,
    ListenerOff,
    Connected,
    Left,

    /// <summary>The first connection refused since the listener started (<see cref="AddonEvent.Count"/> is 1).</summary>
    Refused,

    /// <summary>The listener stopped after more than one refusal (<see cref="AddonEvent.Count"/> is the total).</summary>
    Stopped,
}

/// <summary>An event of the bridge: the kind and, for refusals, a number. The bridge writes nothing itself; the app turns this into a log line (<see cref="AddonLog.Line"/>).</summary>
public readonly record struct AddonEvent(AddonEventKind Kind, int Count = 0);

/// <summary>
/// The lines the island writes to its log about the add-on (WORK-ORDER-9 section 2): a kind, and for a refusal a number. Constants and numbers only. A browser's
/// name, a tab's title, a site or an address never reach a log line (GuardTests.Addon_Log_Never_Names_A_Browser_Or_A_Tab). Refusals: the first one since the
/// listener started, and the total when it stops, never one line for each: any web page can knock on that port as often as it likes.
/// </summary>
public static class AddonLog
{
    public static string Line(AddonEvent e) => e.Kind switch
    {
        AddonEventKind.ListenerOn => "add-on listener on",
        AddonEventKind.ListenerOff => "add-on listener off",
        AddonEventKind.Connected => "add-on connected",
        AddonEventKind.Left => "add-on left",
        AddonEventKind.Refused => $"add-on refused {e.Count}",
        AddonEventKind.Stopped => $"add-on refused {e.Count} in total",
        _ => "add-on event",
    };
}
