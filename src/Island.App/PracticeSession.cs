using Island.Core;

namespace Island.App;

/// <summary>
/// The practice of the first start on the real island (Dan's tutorial, WORK-ORDER-13): while it runs a balloon says what to press, the island takes the keys as always but starts, adds and removes nothing
/// (<see cref="IslandController.Practice"/>), and it stays open until the person closes it. It ends when the last step is done, or "Skip tutorial" is pressed, or the island is closed on the last step; the
/// island is then closed if it is still up, everything is put back, and <see cref="Ended"/> tells the host to bring the settings screen back.
/// </summary>
internal sealed class PracticeSession : IDisposable
{
    private readonly IslandRuntime _runtime;
    private readonly TutorialRun _run;
    private readonly TutorialBalloon _balloon;
    private bool _over;

    private readonly string _mainKey;

    public PracticeSession(IslandRuntime runtime, string mainKeyText)
    {
        _mainKey = mainKeyText;
        _runtime = runtime;
        _run = new TutorialRun(mainKeyText);
        _balloon = new TutorialBalloon(_run);
    }

    public TutorialRun Run => _run;

    public TutorialBalloon Balloon => _balloon;

    /// <summary>Raised once, on the UI thread, when the practice is over for whatever reason and everything has been put back.</summary>
    public event Action? Ended;

    public void Start()
    {
        var controller = _runtime.Controller;
        controller.Practice = true;
        _runtime.Machine.HoldOpen = true;
        controller.Practiced += OnPracticed;
        controller.Summoned += OnSummoned;
        controller.Left += OnLeft;
        _run.Changed += OnRunChanged;
        PlaceBalloon();
        OnRunChanged();
    }

    private void OnPracticed(PracticeEvent what) => _run.Feed(what);

    private void OnSummoned()
    {
        _balloon.Nudge(null);
        _run.Feed(PracticeEvent.Summoned);
    }

    private void OnLeft()
    {
        // the island closed: if that was not the step, the person is told how to bring it back (the step waits)
        if (!_run.Feed(PracticeEvent.Left) && !_run.IsDone) _balloon.Nudge($"The island is closed. Press {_mainKey} to bring it back.");
    }

    private void OnRunChanged()
    {
        if (!_run.IsDone) return;
        End();
    }

    /// <summary>Under the island: on the screen the island appears on, below the capsule.</summary>
    private void PlaceBalloon()
    {
        var work = _runtime.Placer is { } placer && ScreenChooser.Choose(placer.Reader.Pointer, placer.Reader.Screens, 1, 1) is { IsFallback: false } placement
            ? (placement.Screen.SafeWork, placement.Screen.SafeScale)
            : (new PixelRect(0, 0, 1920, 1040), 1.0);
        _balloon.PlaceUnder(work.Item1, work.Item2, LookConstants.TopGap + LookConstants.CapsuleHeight + 70);
    }

    private void End()
    {
        if (_over) return;
        _over = true;
        var controller = _runtime.Controller;
        controller.Practiced -= OnPracticed;
        controller.Summoned -= OnSummoned;
        controller.Left -= OnLeft;
        _run.Changed -= OnRunChanged;
        controller.Practice = false;
        _runtime.Machine.HoldOpen = false;
        _balloon.Close();
        if (_runtime.Machine.Phase is IslandPhase.FlyingIn or IslandPhase.Open) controller.ShowHide(); // skipped with the island up: it is closed, as the main key does
        Ended?.Invoke();
    }

    public void Dispose()
    {
        if (!_over) End();
    }
}
