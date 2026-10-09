namespace Island.Core;

/// <summary>What the close button knows about the thing it would close, read at the moment it is asked.</summary>
public interface ICloseFacts
{
    bool WindowExists(long window);

    /// <summary>One of the island's own windows (never closed by the button).</summary>
    bool IsIslandWindow(long window);

    /// <summary>The window's program runs with more rights than the island (or that cannot be told): Windows would drop a close request silently.</summary>
    bool NeedsMoreRights(long window);

    /// <summary>The window accepts input; a window that shows its own question is disabled until the question is answered.</summary>
    bool IsEnabled(long window);

    bool AddonConnected { get; }

    bool TabExists(string tabKey);
}

public enum CloseState
{
    /// <summary>Nothing to close yet, or nothing that may be closed: drawn dim and does nothing.</summary>
    Dimmed,

    Ready,

    /// <summary>The window's program runs as administrator: dimmed, and the second text line says why.</summary>
    NeedsAdmin,
}

public enum CloseAction
{
    Nothing,
    CloseWindow,
    CloseTab,
    Refused,
}

/// <param name="Window">The window to ask to close (<see cref="CloseAction.CloseWindow"/>).</param>
/// <param name="TabKey">The tab to close through the add-on (<see cref="CloseAction.CloseTab"/>).</param>
/// <param name="Refusal">Why nothing was done, in plain words (<see cref="CloseAction.Refused"/>).</param>
public sealed record CloseOutcome(CloseAction Action, long Window = 0, string? TabKey = null, Refusal? Refusal = null)
{
    public static CloseOutcome None { get; } = new(CloseAction.Nothing);
}

/// <summary>
/// The close button of every page that is not Media (WORK-ORDER-6 section 5). A small X on a capsule looks like "close this capsule",
/// so a person who presses it to send the island away must not lose a window they were not looking at: it is dimmed and does nothing
/// until a pick was CLICKED during this showing of the island, and then it acts on the one window that click brought forward.
/// Summoning the island again dims it again; a jump made by a pick's key does not arm it. It never ends a process: the request is "close
/// this window", as a click on its X, and the program may ask its own question. Pure: it decides, the app carries out.
/// </summary>
public sealed class CloseButton(ICloseFacts facts)
{
    public const string AdminLine = "runs as administrator";

    private CloseTarget? _target;

    public CloseState State { get; private set; } = CloseState.Dimmed;

    /// <summary>The second text line that replaces the pick's own while the button is dimmed for a reason the person should read; null otherwise.</summary>
    public string? Line => State == CloseState.NeedsAdmin ? AdminLine : null;

    /// <summary>The island is summoned: the button is dimmed again.</summary>
    public void NewShowing()
    {
        _target = null;
        State = CloseState.Dimmed;
    }

    /// <summary>
    /// A pick was clicked (a click, not a key). <paramref name="plan"/> is what the click did; <paramref name="tabKey"/> is the tab it
    /// went to, for a website pick with the add-on. A folder pick, a click that only started a program, or one that found nothing
    /// to close leave the button dimmed.
    /// </summary>
    public void PickClicked(Pick pick, ClickPlan plan, string? tabKey)
    {
        _target = null;
        State = CloseState.Dimmed;
        if (pick.Kind == PickKind.Folder) return; // closing an Explorer window closes every tab in it: not what the button promises

        if (pick.Kind == PickKind.Site)
        {
            if (plan.Kind == ClickKind.BringForward && tabKey is not null && facts.AddonConnected && facts.TabExists(tabKey))
            {
                _target = new CloseTarget(0, tabKey, pick.Name);
                State = CloseState.Ready;
            }

            return;
        }

        var window = plan.Target;
        if (plan.Kind != ClickKind.BringForward || window == 0 || facts.IsIslandWindow(window) || !facts.WindowExists(window)) return;
        _target = new CloseTarget(window, null, pick.Name);
        State = facts.NeedsMoreRights(window) ? CloseState.NeedsAdmin : CloseState.Ready;
    }

    /// <summary>The button was pressed. Checks the facts again (the window may be gone, or showing its own question) and says what to do.</summary>
    public CloseOutcome Press()
    {
        if (_target is not { } target || State == CloseState.Dimmed) return CloseOutcome.None;

        if (target.TabKey is { } tab)
        {
            if (!facts.AddonConnected || !facts.TabExists(tab)) return Disarm();
            Disarm();
            return new CloseOutcome(CloseAction.CloseTab, TabKey: tab);
        }

        if (!facts.WindowExists(target.Window) || facts.IsIslandWindow(target.Window)) return Disarm();
        if (facts.NeedsMoreRights(target.Window))
        {
            State = CloseState.NeedsAdmin;
            return new CloseOutcome(CloseAction.Refused, Refusal: CloseRefusals.ForName(target.Name));
        }

        if (!facts.IsEnabled(target.Window)) return CloseOutcome.None; // its own question is open: nothing is sent, and the button stays armed

        Disarm();
        return new CloseOutcome(CloseAction.CloseWindow, Window: target.Window);
    }

    private CloseOutcome Disarm()
    {
        _target = null;
        State = CloseState.Dimmed;
        return CloseOutcome.None;
    }

    private sealed record CloseTarget(long Window, string? TabKey, string Name);
}
