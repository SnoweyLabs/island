namespace Island.Core;

/// <summary>
/// Reads the screens and the pointer. Reading only: it moves nothing. Safe to read from any thread; never throws.
/// The lists are replaced whole, so a caller holding one sees a consistent set.
/// </summary>
public interface IScreenSource
{
    /// <summary>The screens as last read; empty when none could be read.</summary>
    IReadOnlyList<ScreenInfo> Screens { get; }

    /// <summary>Reads the pointer now (a call that takes microseconds); null when Windows will not say.</summary>
    ScreenPoint? Pointer { get; }

    /// <summary>Raised on a background thread, debounced, when the screens or a work area really changed (not for a repeated identical reading).</summary>
    event Action? Changed;
}

/// <summary>A pretend source for tests and for building screens before the real reader is wired in.</summary>
public sealed class PretendScreens : IScreenSource
{
    public IReadOnlyList<ScreenInfo> Screens { get; set; } = [];

    public ScreenPoint? Pointer { get; set; }

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
