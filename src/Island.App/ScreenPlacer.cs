using System.Windows.Media;
using Island.Core;
using Island.Sources.Screens;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 §1: the island appears on the screen that holds the pointer, centred on that screen's work area (so a bar docked at
/// the top is not covered) at its top. Island.Core chooses and computes the rectangle in real pixels; this reads the pointer and the
/// screens, moves the island's windows with the native call that neither activates a window nor changes which one is on top (never
/// through <c>Left</c> and <c>Top</c>, which are unreliable when screens differ in scaling), and reads each window's real rectangle
/// back: the framework may move or resize a window by itself when its scaling changes, so the rectangle is applied again, at most
/// three times; if it still differs the island starts anyway and the log gets the kind of difference. The island changes screen only
/// while nothing of it is visible (<see cref="ScreenPicker"/>).
/// </summary>
internal sealed class ScreenPlacer : IDisposable
{
    public const int MaxAttempts = 3;

    private readonly ScreenReader _screens = new();
    private readonly ScreenPicker _picker = new();
    private readonly IslandHost _host;
    private readonly Action<string> _log;
    private PixelRect _applied;
    private bool _everPlaced;

    public ScreenPlacer(IslandHost host, Action<string> log)
    {
        _host = host;
        _log = log;
        _screens.Start();
        host.Capsule.SystemChanged += _screens.NotifyPossibleChange;
    }

    private PixelRect? _nextScreen;

    /// <summary>The next summon from hidden comes on the screen with this rectangle instead of the pointer's (the notice, when the first screen has a fullscreen program in front). Forgotten once used.</summary>
    public void UseScreenForNextSummon(PixelRect screen) => _nextScreen = screen;

    /// <summary>The screens as last read, and the rectangle of the screen the foreground window is on (null when it cannot be told).</summary>
    public IReadOnlyList<ScreenInfo> Screens => _screens.Screens;

    /// <summary>How many screens were read (a count; never anything about them).</summary>
    public int ScreenCount => _screens.Screens.Count;

    /// <summary>The placement in force, or null before the first.</summary>
    public ScreenPlacement? Current => _picker.Current;

    /// <summary>How many times a screen was chosen.</summary>
    public int Choices => _picker.Choices;

    /// <summary>The reader, for the self-test.</summary>
    internal ScreenReader Reader => _screens;

    /// <summary>
    /// Called when the island is about to be summoned from hidden, before the ball starts. <paramref name="visible"/> is false then.
    /// Moves the windows to the chosen screen when they are not already there.
    /// </summary>
    public void PlaceForSummon(bool visible)
    {
        // Dan's Q2 (WORK-ORDER-13): a notice that goes to the second screen says which one; the pointer's screen is then not asked. Used once.
        var pointer = _nextScreen is { } wanted ? NoticeScreens.CentreOf(wanted) : _screens.Pointer;
        if (!visible) _nextScreen = null;
        var placement = _picker.Update(pointer, _screens.Screens, visible, _host.WidthDip, _host.HeightDip);
        if (visible || placement.IsFallback) return; // no usable screen: the windows stay where they are
        ApplyLimit(placement.Screen);
        if (_everPlaced && placement.Window == _applied && _host.RealRectangleMatches(placement.Window)) return;

        Apply(placement.Window);
        Visuals.TileView.PixelsPerDip = placement.Screen.SafeScale;
        _applied = placement.Window;
        _everPlaced = true;
    }

    /// <summary>
    /// A screen whose work area is narrower than the island's window shows fewer picks at once, until the capsule fits (the tile count
    /// of <see cref="ScreenFit"/> includes the + tile; the spring's overshoot is kept clear). Decided here, at the summon.
    /// </summary>
    internal static void ApplyLimit(ScreenInfo screen)
    {
        var overshoot = WindowMetrics.PeakCapsuleWidth - CapsuleLayout.Width(ChoiceConstants.MaxVisibleTiles + 1, isMedia: true);
        var tiles = ScreenFit.Tiles(screen, ChoiceConstants.MaxVisibleTiles + 1, Math.Max(0, overshoot));
        StripLayout.VisibleLimit = tiles - 1;
    }

    private void Apply(PixelRect window)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            _host.MoveTo(window);
            if (_host.RealRectangleMatches(window)) return;
        }

        // Still different after the last try: the island starts anyway; what is logged is the kind of difference, never a position.
        _log("window placement differs from the computed rectangle after " + MaxAttempts + " attempts");
    }

    public void Dispose() => _screens.Dispose();
}
