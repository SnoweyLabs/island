namespace Island.Core;

/// <summary>A rectangle in real pixels. <see cref="Right"/> and <see cref="Bottom"/> are exclusive, as Windows' RECT; coordinates can be negative.</summary>
public readonly record struct FrontRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;
}

/// <summary>
/// What Windows says about the window in front, as plain facts (WORK-ORDER-6 section 2).
/// <see cref="HasTitleBar"/> is the dialog-frame bit of the style (set by a title bar too), the bit Chromium tests;
/// <see cref="HasSizingBorder"/> is the sizing-border bit; <see cref="IsToolWindow"/> the tool-window extended style.
/// <see cref="IsDesktopOrShell"/> is true for the desktop window and the shell's own window.
/// <see cref="IsIslandWindow"/> is true for the island's own windows, whatever their size.
/// </summary>
public sealed record FrontWindowFacts(
    FrontRect Window,
    FrontRect Screen,
    bool HasTitleBar,
    bool HasSizingBorder,
    string? ClassName,
    bool IsIslandWindow,
    bool IsDesktopOrShell = false,
    bool IsToolWindow = false);

/// <summary>
/// "Is this window a fullscreen program?" and the mapping of Windows' own notification state, as pure functions.
/// The rectangle test is Chromium's (ui/base/fullscreen_win.cc) re-implemented; the class list is PowerToys'
/// (window.h, WindowsInteropHelper.cs), re-implemented. Windows' "busy" answer is not used: other programs'
/// overlays cause it (Research/windows-apis-2.md section B).
/// </summary>
public static class FrontClassifier
{
    // SHQueryUserNotificationState values (QUERY_USER_NOTIFICATION_STATE on Microsoft Learn).
    public const int QunsRunningD3dFullScreen = 3;
    public const int QunsPresentationMode = 4;

    // The desktop, the taskbars and the 3D window switcher are never a program in front, however big.
    private static readonly string[] NeverFullscreenClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SysListView32", "Flip3D"];

    /// <summary>FullscreenProgram when the window's rectangle is exactly its screen's and it has neither title bar nor sizing border; Clear otherwise.</summary>
    public static FrontState Classify(FrontWindowFacts? facts)
    {
        if (facts is null) return FrontState.Clear;
        if (facts.IsIslandWindow || facts.IsDesktopOrShell || facts.IsToolWindow) return FrontState.Clear;
        if (IsSystemClass(facts.ClassName)) return FrontState.Clear;
        if (facts.HasTitleBar || facts.HasSizingBorder) return FrontState.Clear;
        if (facts.Window.IsEmpty || facts.Screen.IsEmpty) return FrontState.Clear;
        return facts.Window == facts.Screen ? FrontState.FullscreenProgram : FrontState.Clear;
    }

    /// <summary>
    /// Windows' own answer: exclusive Direct3D fullscreen and presentation settings are hard states; every other
    /// value (including "busy", "not present" and an unknown number) is not one and gives null.
    /// </summary>
    public static FrontState? FromNotificationState(int value) => value switch
    {
        QunsRunningD3dFullScreen => FrontState.ExclusiveFullscreen,
        QunsPresentationMode => FrontState.Presentation,
        _ => null,
    };

    /// <summary>Windows' hard states win; otherwise the rectangle test decides. A failed reading (null) of either gives Clear.</summary>
    public static FrontState Combine(int? notificationState, FrontWindowFacts? facts) =>
        (notificationState is { } value ? FromNotificationState(value) : null) ?? Classify(facts);

    private static bool IsSystemClass(string? className) =>
        !string.IsNullOrEmpty(className) && NeverFullscreenClasses.Contains(className, StringComparer.OrdinalIgnoreCase);
}
