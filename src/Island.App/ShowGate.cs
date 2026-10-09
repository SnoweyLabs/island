using Island.Core;
using Island.Sources.Front;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 §2: the island looks at what is in front before it shows itself. The reading happens once, at the moment something
/// is about to appear, for the foreground window; Island.Core decides (<see cref="ShowDecision"/>). Over a game in exclusive
/// fullscreen the island never appears; over a fullscreen program or a presentation it appears when asked by a key or a click and not
/// by itself. Stay away after a key press: no sound, no message; the log gets one line with the kind of reason, never a program's name.
/// Under the self-test the reading is a scripted Clear (a film Dan is watching fullscreen would otherwise fail every stage), except
/// where a stage hands its own reading.
/// </summary>
internal sealed class ShowGate(Action<string> log)
{
    /// <summary>The mode: one of Focus, Vibe, DND. Until WORK-ORDER-7 there are no modes and the island behaves as Vibe does.</summary>
    public Mode Mode { get; set; } = Mode.Vibe;

    /// <summary>Programs that count as an exclusive fullscreen game whenever they are in front and fullscreen (a name and a file name, never a path).</summary>
    public NeverOverList NeverOver { get; set; } = NeverOverList.Empty;

    /// <summary>A reading handed in by a test; null reads the real foreground window (or a scripted Clear under the self-test).</summary>
    public Func<FrontReading>? Reading { get; set; }

    /// <summary>What the last decision looked at (for the self-test: a kind only).</summary>
    public FrontState LastState { get; private set; } = FrontState.Clear;

    public bool MayAppear(Appearer thing, ShowOrigin origin)
    {
        if (Check(thing, origin)) return true;
        log($"the {thing.ToString().ToLowerInvariant()} stays away: {LastState}");
        return false;
    }

    /// <summary>
    /// The same question, asked again whenever the foreground window or the mode changes while the pill is up (WORK-ORDER-7 section 1): nothing is
    /// logged, because it is asked every time and a line each time would be a flood.
    /// </summary>
    public bool Check(Appearer thing, ShowOrigin origin)
    {
        var reading = Read();
        var state = NeverOver.Apply(reading.State, reading.ExeFileName);
        LastState = state;
        return ShowDecision.Decide(state, thing, origin, Mode) == ShowAnswer.Show;
    }

    private FrontReading Read()
    {
        if (Reading is { } handed) return handed();
        if (OutsideGate.Current.SelfTest) return FrontReading.Clear;
        try
        {
            return FrontReader.ReadForeground(IsOwnWindow);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return FrontReading.Clear; // anything that cannot be read counts as clear
        }
    }

    /// <summary>One of this program's own windows (the island's two, the blur layer, the settings screen): never "a fullscreen program in front".</summary>
    private static bool IsOwnWindow(nint window)
    {
        Native.GetWindowThreadProcessId(window, out var pid);
        return pid == (uint)Environment.ProcessId;
    }
}
