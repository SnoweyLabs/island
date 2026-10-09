namespace Island.Core;

/// <summary>
/// What Windows says about one top-level window, reduced to plain facts so the Alt+Tab filter can be a pure
/// function. <see cref="CloakFlags"/> is DWMWA_CLOAKED (0 none, 1 app, 2 shell, 4 inherited);
/// <see cref="OnOtherDesktop"/> is true when the window sits on another virtual desktop.
/// </summary>
public sealed record WindowFacts(
    bool Visible,
    bool HasOwner,
    bool IsToolWindow,
    bool IsAppWindow,
    bool MarkedDeletedFromTaskList,
    string ClassName,
    string Title,
    int Width,
    int Height,
    int CloakFlags,
    bool OnOtherDesktop);

/// <summary>
/// Which windows Alt+Tab would list (Research/windows-apis.md section 1: the PowerToys Window Walker filter, re-implemented,
/// plus the empty-title and tiny-size rules of window-switcher), and how the real program behind a UWP window is found.
/// </summary>
public static class WindowRules
{
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    public const string UwpChildClassPrefix = "Windows.UI.Core.";
    public const string UwpFrameHostExe = "ApplicationFrameHost.exe";

    /// <summary>Chosen by B1 (window-switcher uses 120x90, which would hide small real tools): below this a window is a helper, not something to switch to.</summary>
    public const int MinWidth = 50;

    public const int MinHeight = 30;

    private const int CloakedByShell = 2;

    public static bool IsListed(WindowFacts w) =>
        w.Visible
        && PassesOwnerRule(w)
        && !w.MarkedDeletedFromTaskList
        && !string.Equals(w.ClassName, CoreWindowClass, StringComparison.Ordinal)
        && PassesCloakRule(w)
        && !string.IsNullOrWhiteSpace(w.Title)
        && w.Width >= MinWidth && w.Height >= MinHeight;

    /// <summary>An app window is listed whatever its owner; otherwise it must have no owner and not be a tool window.</summary>
    public static bool PassesOwnerRule(WindowFacts w) => w.IsAppWindow || (!w.HasOwner && !w.IsToolWindow);

    /// <summary>Not cloaked, or cloaked only because it is on another virtual desktop (the program is still open).</summary>
    public static bool PassesCloakRule(WindowFacts w) =>
        w.CloakFlags == 0 || (w.CloakFlags == CloakedByShell && w.OnOtherDesktop);

    /// <summary>A UWP window is owned by ApplicationFrameHost.exe; the real program is behind one of its child windows.</summary>
    public static bool IsUwpFrameHost(string? exeName) =>
        string.Equals(exeName, UwpFrameHostExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>The child that holds the real UWP program: the first whose class starts "Windows.UI.Core.". Null when there is none (a minimised UWP window has none).</summary>
    public static long? PickUwpChild(IEnumerable<(long Handle, string ClassName)> children)
    {
        foreach (var (handle, className) in children)
            if (className.StartsWith(UwpChildClassPrefix, StringComparison.Ordinal))
                return handle;
        return null;
    }

    /// <summary>
    /// The package family name inside an app user model id ("Family!App" gives "Family"); null when the id
    /// is not packaged-looking. Used when the UWP child is missing.
    /// </summary>
    public static string? PackageFamilyOf(string? appUserModelId)
    {
        if (string.IsNullOrEmpty(appUserModelId)) return null;
        var bang = appUserModelId.IndexOf('!');
        return bang > 0 && appUserModelId.Contains('_') ? appUserModelId[..bang] : null;
    }
}
