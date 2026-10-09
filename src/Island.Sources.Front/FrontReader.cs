using Island.Core;

namespace Island.Sources.Front;

/// <summary>What is in front, and the executable file name of the program in front when it is a fullscreen program (memory only, never a path).</summary>
public sealed record FrontReading(FrontState State, string? ExeFileName)
{
    public static FrontReading Clear { get; } = new(FrontState.Clear, null);
}

/// <summary>
/// Reads what is in front of the person (WORK-ORDER-6 section 2). Read-only: no window is shown, moved or asked
/// for, and nothing it reads is written anywhere. Any failure to read gives Clear and never throws. Each call is a
/// handful of quick Windows calls with no waiting; call it once, at the moment something is about to appear, never
/// on a timer. The decision itself is <see cref="FrontClassifier"/> in Island.Core.
/// </summary>
public static class FrontReader
{
    /// <summary>The handle of the window in front, 0 when there is none. A handle is only a number; nothing is read from it here.</summary>
    public static nint ForegroundWindow() => FrontNative.GetForegroundWindow();

    /// <summary>The real foreground window, its own monitor's rectangle, and Windows' own answer.</summary>
    /// <param name="isIslandWindow">True for the handle of one of the island's own windows (capsule, shadow, glass layer, settings).</param>
    public static FrontReading ReadForeground(Func<nint, bool>? isIslandWindow = null)
    {
        try
        {
            var window = FrontNative.GetForegroundWindow();
            return Read(window, ScreenOf(window), FrontNative.NotificationState(), isIslandWindow);
        }
        catch (Exception)
        {
            return FrontReading.Clear;
        }
    }

    /// <summary>
    /// A function of the window and the screen rectangle it is HANDED, plus the notification state it is handed
    /// (null: unknown), so a self-test can hand it its own test window and that window's own rectangle.
    /// </summary>
    public static FrontReading Read(nint window, FrontRect? screen, int? notificationState, Func<nint, bool>? isIslandWindow = null)
    {
        try
        {
            var state = FrontClassifier.Combine(notificationState, Facts(window, screen, isIslandWindow));
            var exe = state == FrontState.FullscreenProgram ? FrontNative.ExeFileNameOf(window) : null;
            return new FrontReading(state, exe);
        }
        catch (Exception)
        {
            return FrontReading.Clear;
        }
    }

    /// <summary>The rectangle test alone (no notification state), for a self-test that must not depend on what else is running.</summary>
    public static FrontState ClassifyWindow(nint window, FrontRect? screen, Func<nint, bool>? isIslandWindow = null)
    {
        try
        {
            return FrontClassifier.Classify(Facts(window, screen, isIslandWindow));
        }
        catch (Exception)
        {
            return FrontState.Clear;
        }
    }

    /// <summary>Windows' own answer (QUERY_USER_NOTIFICATION_STATE as a number), or null when the call fails.</summary>
    public static int? ReadNotificationState()
    {
        try
        {
            return FrontNative.NotificationState();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The full rectangle of the monitor that holds most of the window, or null when it cannot be read.</summary>
    public static FrontRect? ScreenOf(nint window)
    {
        try
        {
            if (!FrontNative.TryWindowRect(window, out var rect)) return null;
            return FrontNative.MonitorOf(rect) is { } monitor ? ToFrontRect(monitor) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FrontWindowFacts? Facts(nint window, FrontRect? screen, Func<nint, bool>? isIslandWindow)
    {
        if (screen is not { } screenRect || !FrontNative.TryWindowRect(window, out var rect)) return null;
        var style = FrontNative.Style(window);
        var exStyle = FrontNative.ExStyle(window);
        return new FrontWindowFacts(
            ToFrontRect(rect),
            screenRect,
            HasTitleBar: (style & FrontNative.WsDlgFrame) != 0,
            HasSizingBorder: (style & FrontNative.WsThickFrame) != 0,
            ClassName: FrontNative.ClassOf(window),
            IsIslandWindow: isIslandWindow?.Invoke(window) ?? false,
            IsDesktopOrShell: FrontNative.IsDesktopOrShell(window),
            IsToolWindow: (exStyle & FrontNative.WsExToolWindow) != 0);
    }

    private static FrontRect ToFrontRect(FrontNative.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
