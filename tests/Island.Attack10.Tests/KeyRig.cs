using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>
/// The island's keyboard as the controller wires it, rebuilt over Island.Core only (the controller is WPF and cannot run here): the same
/// <see cref="IslandKeys.Decide"/>, the same context fields, the same switch as <c>IslandController.HandleKey</c>, the same legacy path, and a
/// row of picks that the Delete handler really removes from. Written from reading the controller; where the controller differs, this rig is wrong.
/// </summary>
internal sealed class KeyRig
{
    public const string AppsPage = "apps";

    public static readonly IReadOnlyList<Page> ThreePages =
    [
        new(AppsPage, "Apps", "#112233", "grid", null, false),
        new(PageIds.Media, "Media", "#223344", "play", null, true),
        new("other", "Other", "#334455", "star", null, false),
    ];

    private readonly Dictionary<string, List<string>> _picks = new()
    {
        [AppsPage] = ["p1", "p2", "p3", "p4"],
        [PageIds.Media] = ["m1", "m2"],
        ["other"] = [],
    };

    public KeyRig(bool keyboard = true, int settleMs = 1500)
    {
        M = new IslandMachine(60, ThreePages, Items);
        C = new Clock(M);
        if (keyboard) M.MainKey(C.Now);
        else M.ShowHideKey(C.Now);
        C.Run(settleMs);
    }

    public IslandMachine M { get; }

    public Clock C { get; }

    public bool Lifted { get; set; }

    public bool SettingsShowing { get; set; }

    public bool MediaCanControl { get; set; } = true;

    public int Row2Tiles { get; set; } = 3;

    public List<string> Removed { get; } = [];

    public List<int> Row2Activated { get; } = [];

    public List<int> Row2Added { get; } = [];

    public int PlayPauses { get; private set; }

    public int ItemsActivated { get; private set; }

    /// <summary>The pick the last successful first Delete asked about.</summary>
    public string? LastAsked { get; private set; }

    public List<string> PicksOf(string page) => _picks[page];

    public int Row2Count => M.SecondRowOpen ? Row2Tiles : 0;

    private bool _enterRowWhenBuilt;

    public IReadOnlyList<Item> Items(Page page) =>
        [.. _picks[page.Id].Select(id => new Item(id, "open", id[..2], 100, PickId: id)), new Item("Add", "", "+", 0, IsPlus: true)];

    public void RemovePickFromStore(string id)
    {
        foreach (var list in _picks.Values) list.Remove(id);
        M.ContentsChanged(C.Now);
    }

    public KeyContext Context()
    {
        var m = M;
        var items = m.ContentsItems;
        var inRow2 = m.SecondRowSelected >= 0;
        var row2Count = Row2Count;
        var selectedItem = m.SelectedItem >= 0 && m.SelectedItem < items.Count ? items[m.SelectedItem] : null;
        return new KeyContext
        {
            HasKeyboard = m.HasKeyboard,
            SearchOpen = m.SearchOpen,
            SettingsShowing = SettingsShowing,
            TileLifted = Lifted,
            IslandLeaving = m.Phase is IslandPhase.Hidden or IslandPhase.Closing,
            RowIsCurrentPage = m.Phase == IslandPhase.Open && m.ContentsVisible && m.ContentsPageId == m.PageId,
            PageChangeUnderWay = m.ContentsPageId != m.PageId,
            SecondRowOpen = m.SecondRowOpen,
            SelectionInSecondRow = inRow2,
            SecondRowHasItems = !m.SecondRowOpen || row2Count > 0,
            Selected = selectedItem is null ? SelectedTile.None : selectedItem.IsPlus ? SelectedTile.Plus : SelectedTile.Pick,
            AtFirst = inRow2 ? m.SecondRowSelected <= 0 : m.SelectedItem <= 0,
            AtLast = inRow2 ? m.SecondRowSelected >= row2Count - 1 : m.SelectedItem >= items.Count - 1,
            PageCount = m.PageCount,
            PlusClickActs = m.Phase == IslandPhase.Open && !m.ShowsPill && !m.SearchOpen,
            MediaCanPlayPause = m.ContentsPageId == PageIds.Media && MediaCanControl,
            DeletePending = m.PendingDeleteId is not null,
            SelectedIsPendingPick = m.PendingDeleteId is not null && selectedItem?.PickId == m.PendingDeleteId,
        };
    }

    /// <summary>One key-down, exactly as <c>IslandController.HandleKey(KeyInput)</c> treats it. Returns the decision.</summary>
    public KeyDecision Key(KeyInput key)
    {
        var m = M;
        var now = C.Now;
        var removalsBefore = Removed.Count;
        var decision = IslandKeys.Decide(key, Context());
        if (decision.CancelsPendingDelete) m.CancelPendingDelete();
        if (!decision.Handled)
        {
            Legacy(key.VirtualKey, now);
            return decision;
        }

        switch (decision.Action)
        {
            case KeyAction.MoveLeft or KeyAction.MoveRight:
                var step = decision.Action == KeyAction.MoveLeft ? -1 : 1;
                if (m.SecondRowSelected >= 0) m.MoveSecondRow(step, Row2Count, now);
                else m.MoveSelection(step, now);
                break;
            case KeyAction.Activate:
                Click(m.SelectedItem, now);
                break;
            case KeyAction.OpenSecondRowAndEnter:
                if (!m.SecondRowOpen) m.ToggleSecondRow(now);
                _enterRowWhenBuilt = true;
                m.OtherKey(now);
                break;
            case KeyAction.OpenSecondRow:
                if (!m.SecondRowOpen) m.ToggleSecondRow(now);
                SelectPlus(now);
                break;
            case KeyAction.LeaveSecondRow:
                m.LeaveSecondRow(now);
                break;
            case KeyAction.SecondRowJump:
                if (m.SecondRowSelected >= 0 && m.SecondRowSelected < Row2Count) Row2Activated.Add(m.SecondRowSelected);
                m.OtherKey(now);
                break;
            case KeyAction.SecondRowAdd:
                if (m.SecondRowSelected >= 0 && m.SecondRowSelected < Row2Count) Row2Added.Add(m.SecondRowSelected);
                m.OtherKey(now);
                break;
            case KeyAction.NextPage or KeyAction.PreviousPage:
                m.PageKey(m.PageIdAt(IslandKeys.PageAfterTab(m.PageIndex, m.PageCount, decision.Action == KeyAction.PreviousPage)), now);
                break;
            case KeyAction.PlayPause:
                PlayPauses++;
                m.OtherKey(now);
                break;
            case KeyAction.AskRemove:
                if (!m.AskRemove(now)) m.OtherKey(now);
                else LastAsked = m.PendingDeleteId;
                break;
            case KeyAction.ConfirmRemove:
                if (m.ConfirmRemove(now) is { } id)
                {
                    Removed.Add(id);
                    RemovePickFromStore(id);
                }

                break;
            case KeyAction.CancelPendingDelete:
                m.OtherKey(now);
                break;
            default:
                m.OtherKey(now);
                break;
        }

        // One press never removes more than one pick, and only a first-time Delete press removes at all.
        var removed = Removed.Count - removalsBefore;
        if (removed > 1 || removed == 1 && (key.VirtualKey != KeyCodes.Delete || key.IsRepeat)) throw new InvalidOperationException("a key removed more than it may");
        return decision;
    }

    /// <summary>Typed text, as <c>IslandController.HandleText</c> treats it (search is not modelled: a text that opens search only opens it).</summary>
    public void Text(string text)
    {
        var m = M;
        m.CancelPendingDelete();
        if (!m.SearchOpen && !IslandKeys.IsOpeningText(text)) m.OtherKey(C.Now);
        else if (Lifted) m.OtherKey(C.Now);
        else if (!m.SearchOpen) m.OpenSearch(400, C.Now, keyboard: false);
    }

    /// <summary>What the controller does each frame for the keyboard (<c>ApplyKeyboardState</c>).</summary>
    public void Frame()
    {
        var m = M;
        if (_enterRowWhenBuilt && m.SecondRowOpen)
        {
            _enterRowWhenBuilt = false;
            if (Row2Count == 0) SelectPlus(C.Now);
            else m.EnterSecondRow(Row2Count, C.Now);
        }

        if (!m.SecondRowOpen) _enterRowWhenBuilt = false;
    }

    private void Legacy(int vk, double now)
    {
        var m = M;
        if (!Lifted && m.SearchOpen && vk != KeyCodes.Escape) { m.OtherKey(now); return; } // search takes its own keys
        if (Lifted) m.OtherKey(now); // a lifted tile: Esc puts it back (ConsumeEscape), every other key only counts as use
        else if (vk == KeyCodes.Escape) m.EscapeKey(now);
        else if (vk is >= KeyCodes.Digit1 and <= KeyCodes.Digit9) m.DigitKey(vk - KeyCodes.Digit1 + 1, now);
        else if (vk is >= KeyCodes.Pad1 and <= KeyCodes.Pad9) m.DigitKey(vk - KeyCodes.Pad1 + 1, now);
        else m.OtherKey(now);
    }

    private void SelectPlus(double now)
    {
        var items = M.ContentsItems;
        if (items.Count > 0 && items[^1].IsPlus) M.ItemClick(items.Count - 1, now);
    }

    private void Click(int index, double now)
    {
        M.ItemClick(index, now);
        ItemsActivated++;
        var items = M.ContentsItems;
        if (index >= 0 && index < items.Count && items[index].IsPlus) M.ToggleSecondRow(now);
        else M.CloseSecondRow(now);
    }
}
