using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// Joins the close button's rules (<see cref="CloseButton"/>) to the island: a click on a pick arms it, a new showing dims it, the X on a
/// page that is not Media presses it, and what comes out is carried through the one outside file (a window) or the add-on (a tab).
/// The button is drawn dim until it is ready, and while it is dimmed for a reason ("runs as administrator") the second text line says so.
/// </summary>
internal sealed class CloseHandler
{
    private readonly CloseButton _button;
    private readonly ContentsLayer _contents;
    private readonly AppWorld _world;
    private readonly IslandController _controller;

    public CloseHandler(IslandController controller, ContentsLayer contents, AppWorld world, Func<long, bool> isIslandWindow)
    {
        _controller = controller;
        _contents = contents;
        _world = world;
        _button = new CloseButton(new WindowFacts(world, isIslandWindow));
        contents.CloseClicked += () => Press();
        controller.Summoned += () =>
        {
            _button.NewShowing();
            Show();
        };
    }

    /// <summary>Where a refusal is told (the kind only, never a program's name).</summary>
    public Action<string>? Log { get; set; }

    public CloseState State => _button.State;

    /// <summary>A pick was clicked on the island (not a key): the button is armed for the window that click brought forward.</summary>
    public void PickClicked(Pick pick, ClickPlan plan, string? tabKey)
    {
        _button.PickClicked(pick, plan, tabKey);
        Show();
    }

    /// <summary>The X was pressed (the self-test calls this too, as the handler the mouse reaches).</summary>
    public CloseOutcome Press()
    {
        _controller.Activity();
        // The X never closes anything on the Terminals page (WORK-ORDER-11): its tiles are windows that were never clicked as picks, and nothing here ends a terminal.
        if (_contents.Contents.Page.Id == PageIds.Terminals) return CloseOutcome.None;
        var outcome = _button.Press();
        switch (outcome.Action)
        {
            case CloseAction.CloseWindow:
                OutsideClose.Request(new IntPtr(outcome.Window));
                break;
            case CloseAction.CloseTab when outcome.TabKey is { } tab:
                _world.TabControl.Close(tab);
                break;
            case CloseAction.Refused when outcome.Refusal is { } refusal:
                Log?.Invoke("refusal " + refusal.Code);
                break;
        }

        Show();
        return outcome;
    }

    private void Show() => _contents.SetCloseState(_button.State == CloseState.Ready, _button.Line);
}
