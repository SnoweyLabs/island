using System.Windows.Threading;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// The notice "Your agent is done" in the island (WORK-ORDER-7 section 4): takes what Island.Notify told the island (through the pipe, or from the
/// self-test's own invented one), keeps it in the queue of Island.Core, asks the table of section 1 (and, where it does not allow, the fallback: one short
/// sound in Focus, none in Vibe, nothing in DND; shown once the table allows it, dropped when stale), and tells the island to draw it without the keyboard.
/// It asks again when the foreground changes, because something that appeared by itself also leaves by itself. A click brings the terminal forward: the
/// nearest program of the chain that owns a visible window, and its window whose title holds the project's name when exactly one does. The project's name
/// and the titles live in memory only.
/// </summary>
internal sealed class AgentNoticeHost : IDisposable
{
    private static readonly TimeSpan ShowingTick = TimeSpan.FromMilliseconds(500);

    private readonly IslandRuntime _runtime;
    private readonly AppWorld _world;
    private readonly NoticeQueue _queue = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private NoticeWait _wait = NoticeWait.Idle;
    private AgentNotice? _shown;
    private bool _onOtherScreen;

    /// <summary>The rectangle of the screen the last notice was sent to because the first had a fullscreen program in front (for the self-test).</summary>
    public PixelRect? LastOtherScreen { get; private set; }

    /// <summary>
    /// The screens and the rectangle of the screen the foreground window is on (Dan's Q2, WORK-ORDER-13). Null reads the real ones; the self-test hands its own, because it never reads what is in front.
    /// </summary>
    public Func<(IReadOnlyList<ScreenInfo> Screens, PixelRect? Front)>? ScreensProbe { get; set; }

    private (IReadOnlyList<ScreenInfo> Screens, PixelRect? Front) ReadScreens()
    {
        if (ScreensProbe is { } handed) return handed();
        if (OutsideGate.Current.SelfTest) return ([], null); // a self-test never reads what is in front or how many screens the computer has
        var screens = _runtime.Placer?.Screens ?? [];
        try
        {
            var front = Island.Sources.Front.FrontReader.ScreenOf(Island.Sources.Front.FrontReader.ForegroundWindow());
            return (screens, front is { } r ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return (screens, null); // what cannot be read is not guessed
        }
    }

    public AgentNoticeHost(IslandRuntime runtime, AppWorld world, double seconds = NoticeQueue.DefaultSeconds)
    {
        _runtime = runtime;
        _world = world;
        _queue.Seconds = seconds;
        _timer.Tick += (_, _) => Tick();
        runtime.Controller.NoticeClicked += Click;
        world.Windows.Changed += () => _timer.Dispatcher.BeginInvoke(() =>
        {
            if (_timer.IsEnabled || _shown is not null) Tick(); // the foreground changed: the table is asked again
        });
    }

    /// <summary>How long a notice stays, 3 to 30 seconds.</summary>
    public double Seconds
    {
        get => _queue.Seconds;
        set => _queue.Seconds = value;
    }

    /// <summary>The notice now showing (for the self-test).</summary>
    public AgentNotice? Showing => _shown;

    /// <summary>A notice arrives (on the interface thread). A newer one replaces the one showing; a repeated Stop of the same session changes nothing.</summary>
    public void Post(AgentNotice notice)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_queue.Post(notice, now)) return;
        _wait = NoticeWait.Arrive(now);
        Tick();
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        var machine = _runtime.Machine;
        var capsuleOpen = machine.Phase is IslandPhase.FlyingIn or IslandPhase.Open && !machine.ShowsPill;
        var pillUp = machine.ShowsPill && !machine.ShowsNotice;
        var pointerOver = machine.ShowsNotice && _runtime.Host.Capsule.Root.IsMouseOver;
        var state = _queue.Update(now, capsuleOpen, pillUp, pointerOver);

        var waiting = state.Waiting;
        if (state.Showing is { } notice)
        {
            var allowed = _runtime.Gate.Check(Appearer.Notice, ShowOrigin.ByItself); // also tells the fallback what is in front
            if (machine.ShowsNotice && !allowed && !_onOtherScreen)
            {
                _queue.Dismiss(); // it appeared by itself and leaves by itself
                Hide();
            }
            else if (machine.ShowsNotice)
            {
                if (!ReferenceEquals(notice, _shown)) Show(notice); // a newer one replaced it
            }
            else
            {
                var (screens, front) = ReadScreens();
                var other = NoticeScreens.OtherScreenIndex(screens, front);
                var step = NoticeFallback.Next(_wait, new NoticeFacts(_runtime.Gate.Mode, _runtime.Gate.LastState, SecondScreenWithoutFullscreen: other >= 0, now));
                _wait = step.State;
                switch (step.Action)
                {
                    case NoticeAction.ShowHere:
                        Show(notice);
                        break;
                    case NoticeAction.ShowOnOtherScreen:
                        // the first screen has a fullscreen program in front: the notice comes on another screen (WORK-ORDER-7 section 1, Dan's Q2); the table is not asked again for it
                        _runtime.Placer?.UseScreenForNextSummon(screens[other].Full);
                        LastOtherScreen = screens[other].Full;
                        _onOtherScreen = true;
                        Show(notice);
                        break;
                    case NoticeAction.PlaySound:
                        OutsideSound.PlayNotification();
                        waiting = true;
                        break;
                    case NoticeAction.Drop:
                        _queue.Dismiss();
                        break;
                    default:
                        waiting = true;
                        break;
                }
            }
        }
        else if (_shown is not null)
        {
            Hide();
        }

        // While a notice shows the time is looked at twice a second; while one waits the table is asked again every 2 seconds; with nothing there, no timer.
        if (_shown is not null && _runtime.Machine.ShowsNotice) Arm(ShowingTick);
        else if (waiting) Arm(NoticeFallback.RecheckEvery);
        else
        {
            _timer.Stop();
            if (_shown is null) _wait = NoticeWait.Idle;
        }
    }

    private void Show(AgentNotice notice)
    {
        _shown = notice;
        _runtime.Controller.NoticeText = new NoticeContent(notice.ProjectName, notice.Line);
        _runtime.Controller.SetNotice(true);
    }

    private void Hide()
    {
        _shown = null;
        _onOtherScreen = false;
        _runtime.Controller.SetNotice(false);
    }

    private void Arm(TimeSpan interval)
    {
        _timer.Interval = interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>A click on the notice: the terminal comes forward (inside the click's own handler, so Windows lets it), and the notice goes.</summary>
    private void Click()
    {
        if (_shown is not { } notice) return;
        var windows = _world.Snapshot().Windows
            .Select(w => new WindowFact(w.Handle, (int)Native.ProcessIdOf(new IntPtr(w.Handle)), w.Title, w.ZOrder))
            .ToList();
        if (TerminalChoice.Choose(notice.Chain, windows, notice.ProjectName) is { } chosen) _world.Outside.BringForward(chosen.Handle);
        _queue.Dismiss();
        Hide();
        Tick();
    }

    public void Dispose() => _timer.Stop();
}
