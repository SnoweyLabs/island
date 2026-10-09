namespace Island.Core;

/// <summary>What the person did on the real island while practising (Dan's tutorial, WORK-ORDER-13). A fact, with no name of a tile or a program.</summary>
public enum PracticeEvent
{
    /// <summary>The island came (the main key).</summary>
    Summoned,

    /// <summary>The selection moved to another tile (Left or Right).</summary>
    Moved,

    /// <summary>Enter on a tile: nothing is started while practising.</summary>
    Chose,

    /// <summary>Another page (Tab, Shift+Tab or a digit).</summary>
    PageChanged,

    /// <summary>Down: the row of what is open now.</summary>
    EnteredSecondRow,

    /// <summary>Shift+Enter on a tile of that row: nothing is added while practising.</summary>
    AddedFromRow,

    /// <summary>The first Delete on a tile: it asks.</summary>
    AskedToRemove,

    /// <summary>The second Delete: nothing is removed while practising.</summary>
    Removed,

    /// <summary>The island left (Esc, the main key, or the idle time).</summary>
    Left,
}

/// <summary>One thing to try: what to say and which event completes it.</summary>
public sealed record TutorialStep(string Text, string Hint, PracticeEvent Expects);

/// <summary>
/// The tutorial after the key is chosen (Dan, 8 Oct 2026; DECISIONS.md): a few steps on the real island, in this order, each done when the person does it, each with "Skip step", the whole with "Skip tutorial".
/// Pure: it keeps the place and answers events; the app draws the balloon, counts what the island did and keeps nothing opened, added or removed.
/// </summary>
public sealed class TutorialRun
{
    private readonly IReadOnlyList<TutorialStep> _steps;
    private int _index;

    public TutorialRun(string mainKey) => _steps = StepsFor(mainKey);

    public static IReadOnlyList<TutorialStep> StepsFor(string mainKey) =>
    [
        new($"Press {mainKey}", "The island comes.", PracticeEvent.Summoned),
        new("Press → to move to the next tile", "← goes back.", PracticeEvent.Moved),
        new("Press Enter to choose the tile", "Nothing opens while you practise.", PracticeEvent.Chose),
        new("Press Tab for the next page", "The keys 1 to 5 jump to a page.", PracticeEvent.PageChanged),
        new("Press ↓ to see what is open now", "A second row appears under the first.", PracticeEvent.EnteredSecondRow),
        new("Press Shift+Enter on a tile of that row", "It would be added to this page. Nothing is added while you practise.", PracticeEvent.AddedFromRow),
        new("Press Delete on a tile", "The island asks first.", PracticeEvent.AskedToRemove),
        new("Press Delete again", "It would take the tile off the island. Nothing is removed while you practise.", PracticeEvent.Removed),
        new("Press Esc to close the island", "The first Esc closes the second row, the next closes the island.", PracticeEvent.Left),
    ];

    public IReadOnlyList<TutorialStep> Steps => _steps;

    public int Count => _steps.Count;

    /// <summary>The step now (zero based); <see cref="Count"/> when the tutorial is over.</summary>
    public int Index => _index;

    public bool IsDone => _index >= _steps.Count;

    public TutorialStep? Current => IsDone ? null : _steps[_index];

    /// <summary>True when something changed (the step moved on or the tutorial ended): the balloon is drawn again.</summary>
    public event Action? Changed;

    /// <summary>The person did something. A thing that is not what the step waits for changes nothing. True when the step was done.</summary>
    public bool Feed(PracticeEvent what)
    {
        if (Current is not { } step || step.Expects != what) return false;
        Advance();
        return true;
    }

    /// <summary>"Skip step": the next one, or the end after the last.</summary>
    public void SkipStep()
    {
        if (IsDone) return;
        Advance();
    }

    /// <summary>"Skip tutorial": the end.</summary>
    public void SkipAll()
    {
        if (IsDone) return;
        _index = _steps.Count;
        Changed?.Invoke();
    }

    private void Advance()
    {
        _index++;
        Changed?.Invoke();
    }
}
