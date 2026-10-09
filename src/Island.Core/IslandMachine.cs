namespace Island.Core;

public enum IslandPhase
{
    Hidden,
    FlyingIn,
    Open,
    Closing,
}

/// <summary>
/// The island's behaviour with no window attached. Time is passed in as
/// milliseconds on any monotonic clock the caller likes; the machine never
/// reads the system clock. The machine owns the four springs (vertical
/// position, width, height, corner radius) so that tests can follow the shape
/// through every phase.
/// </summary>
public sealed class IslandMachine
{
    private enum Kind
    {
        Expand,
        ContentsIn,
        ShrinkToBall,
        FlyOut,
        SwapContents,
    }

    private readonly record struct Scheduled(double DueMs, Kind Kind);

    private readonly List<Scheduled> _queue = [];
    private double _idleMs;
    private double _clockMs;
    private double _idleDeadlineMs = double.PositiveInfinity;
    private double _capsuleTargetWidth;
    private bool _flyOutIssued;
    private bool _restStamped;

    private Spring _y = Spring.At(-LookConstants.SpawnHeightAboveEdge);
    private Spring _w = Spring.At(LookConstants.BallSize);
    private Spring _h = Spring.At(LookConstants.BallSize);
    private Spring _r = Spring.At(LookConstants.BallSize / 2);

    private IReadOnlyList<Page> _pages;
    private readonly Func<Page, IReadOnlyList<Item>> _itemsOf;
    private readonly Func<Page, int>? _preferredSelection;

    /// <param name="idleSeconds">Idle time before the island leaves.</param>
    /// <param name="pages">The pages, in order; the built-in five by default. The first is shown first.</param>
    /// <param name="itemsOf">The rows of a page; the hard-coded placeholders by default.</param>
    /// <param name="preferredSelection">Which item a page opens with selected; none (the first) by default. The Media page opens on the pick that is playing (WORK-ORDER-5 §7).</param>
    public IslandMachine(double idleSeconds = LookConstants.IdleSeconds, IReadOnlyList<Page>? pages = null, Func<Page, IReadOnlyList<Item>>? itemsOf = null, Func<Page, int>? preferredSelection = null)
    {
        _preferredSelection = preferredSelection;
        _idleMs = idleSeconds * 1000.0;
        _pages = pages ?? Pages.BuiltIn;
        _itemsOf = itemsOf ?? Pages.PlaceholderItems;
        PageId = ContentsPageId = _pages[0].Id;
        _capsuleTargetWidth = CapsuleLayout.SizeFor(Contents).Width;
    }

    // ---- Outputs ------------------------------------------------------

    public IslandPhase Phase { get; private set; } = IslandPhase.Hidden;

    /// <summary>
    /// False when Windows is set to show no animations (Dan's P1, WORK-ORDER-13): the island then comes and goes without spring or delay, every target reached at once and every timed step taken at the next
    /// tick. True until the app says otherwise.
    /// </summary>
    public bool Animations { get; set; } = true;

    /// <summary>
    /// While true the idle time does not close the island (the practice of the first start: the person reads a balloon and presses at their own pace). Esc and the main key close it as ever.
    /// </summary>
    public bool HoldOpen { get; set; }

    /// <summary>The page the island is in (and was last in, while hidden). Drives the colour.</summary>
    public string PageId { get; private set; }

    /// <summary>The page whose contents are laid out. Lags <see cref="PageId"/> by the swap delay when switching.</summary>
    public string ContentsPageId { get; private set; }

    public Page Page => PageById(PageId);

    public Page ContentsPage => PageById(ContentsPageId);

    /// <summary>The rows of <see cref="ContentsPage"/>.</summary>
    public IReadOnlyList<Item> ContentsItems => _itemsOf(ContentsPage);

    public PageContents Contents => new(ContentsPage, ContentsItems);

    private Page PageById(string id) => _pages.First(p => p.Id == id);

    private int _selectedItem;
    private long? _selectedKey;

    /// <summary>The selected tile of the first row. Every change of it forgets a Delete that was waiting for its second press.</summary>
    public int SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (_selectedItem != value) PendingDeleteId = null;
            _selectedItem = value;
            // On the page that fills itself the selection is a window, not a place: it is remembered by the tile's identity (WORK-ORDER-11 section 1).
            var items = ContentsPageId == PageIds.Terminals ? ContentsItems : null;
            _selectedKey = items is not null && value >= 0 && value < items.Count ? items[value].WindowKey : null;
        }
    }

    /// <summary>
    /// WORK-ORDER-10 §2: the selected tile of the second row ("Open now"), or -1 while the selection is in the first row. The row's own tiles belong to the app,
    /// so the app says how many there are; the machine only keeps which one is selected, and forgets it when the row closes.
    /// </summary>
    public int SecondRowSelected { get; private set; } = -1;

    /// <summary>
    /// The pick that a first Delete asked about: a second Delete removes it only if it is still the selected one. The pick itself, not its place in the row.
    /// Forgotten when the selection moves, the row is laid out again, a page changes, the island leaves or loses the keyboard.
    /// </summary>
    public string? PendingDeleteId { get; private set; }

    /// <summary>How many pages there are, which one the island is in, and the id of any of them (Tab goes round them).</summary>
    public int PageCount => _pages.Count;

    public int PageIndex => Math.Max(0, _pages.ToList().FindIndex(p => p.Id == PageId));

    public string PageIdAt(int index) => _pages[Math.Clamp(index, 0, _pages.Count - 1)].Id;

    private double _pillWidth = PillLayout.WidestWidth;
    private double _noticeWidth = NoticeLayout.WidestWidth;
    private double _searchWidth = SearchLayout.Width(0, 0);

    /// <summary>
    /// True while the capsule is laid out for search (WORK-ORDER-7 section 3): a field, the matches and a text block instead of the page's row. It
    /// ends when search closes, when the island leaves or turns into the pill, and when a page's key is pressed.
    /// </summary>
    public bool SearchOpen { get; private set; }

    /// <summary>The width the capsule has for search, or is heading for.</summary>
    public double SearchWidth => _searchWidth;

    /// <summary>
    /// True while the shape the island has, or is heading for, is the small pill and not the capsule (WORK-ORDER-7 section 2). One window,
    /// one shape at a time: the pill grows into the capsule on the main key and shrinks back to the pill instead of flying out.
    /// </summary>
    public bool ShowsPill { get; private set; }

    /// <summary>
    /// True while the small shape is the notice "Your agent is done" and not the pill (it implies <see cref="ShowsPill"/>: the notice is a small shape
    /// too, and takes the pill's place, never the capsule's).
    /// </summary>
    public bool ShowsNotice { get; private set; }

    /// <summary>The width the notice has, or is heading for (its text decides).</summary>
    public double NoticeWidth => _noticeWidth;

    /// <summary>Something that counts is playing (the app says so).</summary>
    public bool PillWanted { get; private set; }

    /// <summary>The table of WORK-ORDER-7 section 1 lets the pill show now (the app asks it; the machine only remembers the answer).</summary>
    public bool PillAllowed { get; private set; }

    /// <summary>The width the pill has, or is heading for (its title decides, at most 150 wide in it).</summary>
    public double PillWidth => _pillWidth;

    /// <summary>True while the capsule is two rows high: the + was pressed and the second row ("Open now") shows (WORK-ORDER-5 §5).</summary>
    public bool SecondRowOpen { get; private set; }

    /// <summary>Machine time at which <see cref="SecondRowOpen"/> last changed.</summary>
    public double SecondRowChangedAtMs { get; private set; }

    /// <summary>How many times the second row has closed, and how many times the page was changed while the island was showing: their order tells which came first.</summary>
    public int RowClosedCount { get; private set; }

    public int PageChangeCount { get; private set; }

    /// <summary>The order in which the last row-close and the last page change happened (1, 2, 3 ...); 0 when it has not happened.</summary>
    public int RowClosedOrder { get; private set; }

    public int PageChangedOrder { get; private set; }

    private int _order;

    /// <summary>True from the moment the contents start coming in until they start going out.</summary>
    public bool ContentsVisible { get; private set; }

    /// <summary>Machine time at which <see cref="ContentsVisible"/> last changed.</summary>
    public double ContentsChangedAtMs { get; private set; }

    /// <summary>Machine time at which the idle clock will dismiss the island; infinity when nothing is shown.</summary>
    public double IdleDeadlineMs => _idleDeadlineMs;

    public bool NeedsFrames => Phase != IslandPhase.Hidden;

    /// <summary>
    /// True only after a summon by the main key, and until the island is dismissed or loses the keyboard.
    /// While it is true the plain digit keys and Esc act; the window layer reads it to know when the
    /// keyboard has to be handed back.
    /// </summary>
    public bool HasKeyboard { get; private set; }

    /// <summary>How many digit keys reach a page: one per page, at most nine.</summary>
    public int DigitKeyCount => Math.Min(9, _pages.Count);

    /// <summary>
    /// Machine time at which the capsule first was open, contents shown and every spring at rest after the
    /// latest summon, stamped at the spring's own step so that it does not depend on when frames arrived;
    /// NaN until it happens.
    /// </summary>
    public double RestReachedMs { get; private set; } = double.NaN;

    public Spring Y => _y;
    public Spring Width => _w;
    public Spring Height => _h;
    public Spring Radius => _r;

    /// <summary>The width the capsule is heading for (or was last sent to).</summary>
    public double CapsuleTargetWidth => _capsuleTargetWidth;

    private static double MinSize => LookConstants.BallSize * LookConstants.MinSizeFraction;

    public double DrawnWidth => Math.Max(MinSize, _w.Drawn);
    public double DrawnHeight => Math.Max(MinSize, _h.Drawn);
    public double DrawnRadius => Math.Clamp(_r.Drawn, 0, Math.Min(DrawnWidth, DrawnHeight) / 2);

    /// <summary>Vertical position of the shape's top edge to draw now.</summary>
    public double DrawnY => _y.Drawn;

    /// <summary>How far the shape is from ball width towards capsule width, 0..1.</summary>
    public double Progress =>
        Math.Clamp((DrawnWidth - LookConstants.BallSize)
                   / Math.Max(1, _capsuleTargetWidth - LookConstants.BallSize), 0, 1);

    private bool Stretching => Progress < LookConstants.StretchZoneFraction;

    public double StretchY => Stretching
        ? 1 + Math.Min(LookConstants.StretchMax, Math.Abs(_y.DrawnVelocity) / LookConstants.StretchSpeedDivisor)
        : 1;

    public double StretchX => 1 / Math.Sqrt(StretchY);

    /// <summary>True while a summon is wanted: FlyingIn or Open. Closing and Hidden count as not shown.</summary>
    private bool Shown => Phase is IslandPhase.FlyingIn or IslandPhase.Open;

    // ---- Inputs -------------------------------------------------------

    public void ShowHideKey(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return; // a time that is not a number would wedge the timers
        AdvanceTo(nowMs);
        if (Shown && ShowsPill) GrowToCapsule(nowMs, keyboard: false); // the tray menu while the pill is up: the pill becomes the capsule
        else if (Shown) Dismiss(nowMs);
        else Summon(nowMs, resetSelection: false);
    }

    /// <summary>
    /// The small pill (WORK-ORDER-7 section 2). <paramref name="wanted"/>: something that counts is playing. <paramref name="allowed"/>: the table of
    /// section 1 lets the pill show now (asked again by the app at every foreground change and every change of mode). From hidden, wanted and
    /// allowed bring the pill in by itself, without the keyboard. While it is up: not allowed makes it leave through the springs; not wanted lets
    /// it leave after the idle time; wanted and allowed keep it for as long as that holds. A capsule that would leave shrinks to the pill instead
    /// of flying out while both hold. A time that is not a number changes nothing.
    /// </summary>
    public void SetPill(bool wanted, bool allowed, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        PillWanted = wanted;
        PillAllowed = allowed;
        switch (Phase)
        {
            case IslandPhase.Hidden when wanted && allowed:
            case IslandPhase.Closing when wanted && allowed && !ShowsNotice:
                ShowPill(nowMs);
                break;
            case IslandPhase.FlyingIn or IslandPhase.Open when ShowsPill && !ShowsNotice:
                if (!allowed) Dismiss(nowMs);
                else Poke(nowMs); // not wanted: counts from now; wanted: no end
                break;
        }
    }

    /// <summary>
    /// Search opens in the capsule: the contents go out and the capsule comes back laid out for search, at this width. From the pill (its search
    /// button) the pill grows into the capsule already in search, with the keyboard asked for by the same click (<paramref name="keyboard"/>).
    /// Does nothing while nothing is shown, or while the capsule is still flying in.
    /// </summary>
    public void OpenSearch(double width, double nowMs, bool keyboard = false)
    {
        if (!double.IsFinite(nowMs) || !double.IsFinite(width)) return;
        AdvanceTo(nowMs);
        if (!Shown) return;
        if (ShowsPill)
        {
            _searchWidth = BoundSearch(width);
            SearchOpen = true;
            GrowToCapsule(nowMs, keyboard);
            return;
        }

        if (Phase != IslandPhase.Open) return;
        if (SearchOpen)
        {
            SetSearchWidth(width, nowMs); // already open: the width is a change of width
            return;
        }

        _searchWidth = BoundSearch(width);
        SearchOpen = true;
        Poke(nowMs);
        CloseRowAt(nowMs);
        SwitchWhileOpen(nowMs);
    }

    /// <summary>The width search asks of the capsule (the text and the matches decide); the capsule goes there through the width spring.</summary>
    public void SetSearchWidth(double width, double nowMs)
    {
        if (!double.IsFinite(width) || !double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        _searchWidth = BoundSearch(width);
        if (!SearchOpen || ShowsPill || Phase != IslandPhase.Open) return;
        _capsuleTargetWidth = _searchWidth;
        _w = _w.WithTarget(_searchWidth);
    }

    /// <summary>Search never asks for more than its layout can give (the pill and the notice are bounded by theirs).</summary>
    private static double BoundSearch(double width) =>
        Math.Clamp(width, SearchLayout.Width(0, 0), SearchLayout.Width(int.MaxValue, ChoiceConstants.MaxVisibleTiles));

    /// <summary>Search closes and the page shows again, through the same swap as a page change.</summary>
    public void CloseSearch(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!SearchOpen) return;
        SearchOpen = false;
        if (Shown && !ShowsPill && Phase == IslandPhase.Open)
        {
            Poke(nowMs);
            SwitchWhileOpen(nowMs);
        }
    }

    /// <summary>Gives the island the keyboard again (the person clicked into the search field after Windows had not granted it). Only while search is open.</summary>
    public void TakeKeyboard(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!Shown || ShowsPill || !SearchOpen) return;
        HasKeyboard = true;
        Poke(nowMs);
    }

    /// <summary>
    /// The notice (WORK-ORDER-7 section 4). <paramref name="show"/> is the app's answer to "may it show now": it knows the table, the fallback and the
    /// queue. From hidden it comes in by itself, without the keyboard; over the pill it takes the pill's place; over the capsule it does nothing (a notice never
    /// replaces the capsule: the app keeps it waiting). When it goes: back to the pill if the pill is wanted and allowed, otherwise out through the springs.
    /// While it is up nothing ends it but the app: the idle time does not run.
    /// </summary>
    public void SetNotice(bool show, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (show)
        {
            if (Phase is IslandPhase.Hidden or IslandPhase.Closing) ShowNotice(nowMs);
            else if (ShowsPill && !ShowsNotice) SwapSmall(nowMs, notice: true);
            return;
        }

        if (!ShowsNotice) return;
        if (Phase is IslandPhase.FlyingIn or IslandPhase.Open && PillWanted && PillAllowed) SwapSmall(nowMs, notice: false);
        else if (Phase is IslandPhase.FlyingIn or IslandPhase.Open) Dismiss(nowMs);
        ShowsNotice = false;
    }

    /// <summary>The width its text asks of the notice (90 to 230 of text); the notice goes there through the width spring.</summary>
    public void SetNoticeWidth(double textWidth, double nowMs)
    {
        if (!double.IsFinite(textWidth) || !double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        _noticeWidth = NoticeLayout.Width(textWidth);
        if (!ShowsNotice || Phase != IslandPhase.Open) return;
        _capsuleTargetWidth = _noticeWidth;
        _w = _w.WithTarget(_noticeWidth);
    }

    /// <summary>The width its title asks of the pill (at most 150): the pill goes there through the width spring. Ignored for a number that is not one.</summary>
    public void SetPillWidth(double titleWidth, double nowMs)
    {
        var width = titleWidth;
        if (!double.IsFinite(width) || !double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        _pillWidth = PillLayout.Width(width);
        if (!ShowsPill || ShowsNotice || Phase != IslandPhase.Open) return;
        _capsuleTargetWidth = _pillWidth;
        _w = _w.WithTarget(_pillWidth);
    }

    /// <summary>
    /// The main key, pressed by the person: hidden or leaving → summon and take the keyboard; shown →
    /// dismiss. Every other way of showing the island (start-up, the tray menu) goes through
    /// <see cref="ShowHideKey"/> and never takes the keyboard.
    /// </summary>
    public void MainKey(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (Shown && ShowsPill)
        {
            GrowToCapsule(nowMs, keyboard: true); // the pill grows into the capsule and the island takes the keyboard
            return;
        }

        if (Shown)
        {
            Dismiss(nowMs);
            return;
        }

        Summon(nowMs, resetSelection: false);
        HasKeyboard = true;
    }

    /// <summary>
    /// Left or Right (WORK-ORDER-10 §2): the selection moves one tile along the first row and stops at either end (it does not wrap). It works from the moment the island
    /// has the keyboard, while it is still opening; never while search or the pill is on, and never without the keyboard.
    /// </summary>
    public void MoveSelection(int delta, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown || ShowsPill || SearchOpen || Phase is IslandPhase.Closing) return;
        Poke(nowMs);
        var count = ContentsItems.Count;
        if (count == 0) return;
        SelectedItem = Math.Clamp(SelectedItem + Math.Sign(delta), 0, count - 1);
    }

    /// <summary>Down into the second row (it is open, with <paramref name="count"/> tiles): the selection goes to its first tile. False when it cannot.</summary>
    public bool EnterSecondRow(int count, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return false;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown || !SecondRowOpen || count <= 0 || Phase != IslandPhase.Open) return false;
        Poke(nowMs);
        PendingDeleteId = null;
        SecondRowSelected = 0;
        return true;
    }

    /// <summary>Up out of the second row: back to the first row, and the second row closes.</summary>
    public void LeaveSecondRow(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown) return;
        Poke(nowMs);
        CloseRowAt(nowMs);
    }

    /// <summary>Left or Right in the second row, which has <paramref name="count"/> tiles; stops at the ends.</summary>
    public void MoveSecondRow(int delta, int count, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown || SecondRowSelected < 0) return;
        Poke(nowMs); // every key resets the idle clock, also when the row emptied under the selection
        if (count <= 0) return;
        SecondRowSelected = Math.Clamp(SecondRowSelected + Math.Sign(delta), 0, count - 1);
    }

    /// <summary>
    /// A first Delete on a pick (the selected tile of the first row, laid out and showing): the island remembers which pick it was. False for the + tile, the second row,
    /// or when nothing is laid out.
    /// </summary>
    public bool AskRemove(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return false;
        AdvanceTo(nowMs);
        if (!HasKeyboard || Phase != IslandPhase.Open || !ContentsVisible || ShowsPill || SearchOpen || SecondRowSelected >= 0) return false;
        Poke(nowMs);
        var items = ContentsItems;
        if (SelectedItem < 0 || SelectedItem >= items.Count || items[SelectedItem] is not { IsPlus: false, PickId: { } id }) return false;
        PendingDeleteId = id;
        return true;
    }

    /// <summary>A second Delete, pressed anew: the id of the pick to remove if the selected tile is still the pick that was asked about; null otherwise. Clears the ask either way.</summary>
    public string? ConfirmRemove(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return null;
        AdvanceTo(nowMs);
        var pending = PendingDeleteId;
        PendingDeleteId = null;
        if (pending is null || !HasKeyboard || Phase != IslandPhase.Open || SecondRowSelected >= 0) return null;
        var items = ContentsItems;
        if (SelectedItem < 0 || SelectedItem >= items.Count || items[SelectedItem].PickId != pending) return null;
        Poke(nowMs);
        return pending;
    }

    /// <summary>Forgets a Delete that was waiting (any other key, a click, the wheel).</summary>
    public void CancelPendingDelete() => PendingDeleteId = null;

    /// <summary>A plain digit key while the island has the keyboard: 1 is the first page. Any other time it does nothing.</summary>
    public void DigitKey(int digit, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown) return;
        if (digit >= 1 && digit <= DigitKeyCount) PageKey(_pages[digit - 1].Id, nowMs);
        else Poke(nowMs);
    }

    /// <summary>Esc while the island has the keyboard closes the second row if it is open, otherwise dismisses the island. Any other time it does nothing.</summary>
    public void EscapeKey(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (!HasKeyboard || !Shown) return;
        if (!SecondRowOpen)
        {
            Dismiss(nowMs);
            return;
        }

        Poke(nowMs); // the person pressed a key: the idle clock starts again
        CloseSecondRow(nowMs);
    }

    /// <summary>
    /// A click on the + tile: the capsule grows downward into two rows, or, if they are already two, closes the second.
    /// Only while the capsule is open. The height changes through the height spring like every other size.
    /// </summary>
    public void ToggleSecondRow(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (Phase != IslandPhase.Open || ShowsPill || SearchOpen || ContentsPageId == PageIds.Terminals || PageId == PageIds.Terminals) return; // the page that fills itself has no second row, also not while the swap onto it is under way
        Poke(nowMs);
        if (SecondRowOpen) CloseSecondRow(nowMs);
        else
        {
            SecondRowOpen = true;
            SecondRowChangedAtMs = nowMs;
            _h = _h.WithTarget(ChoiceConstants.TwoRowHeight);
        }
    }

    /// <summary>Closes the second row (a click on a pick, a page change, Esc, the island leaving). Does nothing when it is not open.</summary>
    public void CloseSecondRow(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        CloseRowAt(nowMs);
    }

    private void CloseRowAt(double atMs)
    {
        SecondRowSelected = -1;
        if (!SecondRowOpen) return;
        SecondRowOpen = false;
        SecondRowChangedAtMs = atMs;
        RowClosedCount++;
        RowClosedOrder = ++_order;
        if (Phase == IslandPhase.Open) _h = _h.WithTarget(CapsuleLayout.SizeFor(Contents).Height);
        else if (Math.Abs(_h.Target - ChoiceConstants.TwoRowHeight) < 0.01) _h = _h.WithTarget(LookConstants.CapsuleHeight); // leaving: it does not keep the second row's height
    }

    /// <summary>Any other key pressed while the island has the keyboard: only the idle clock reacts.</summary>
    public void OtherKey(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (HasKeyboard && Shown) Poke(nowMs);
    }

    /// <summary>The person clicked into another window: the island keeps showing until its idle time but no longer has the keyboard.</summary>
    public void FocusLost(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        HasKeyboard = false;
        PendingDeleteId = null;
    }

    /// <summary>A page's keybind. An id that is not a page is ignored.</summary>
    public void PageKey(string id, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return; // a time that is not a number would wedge the timers
        AdvanceTo(nowMs);
        if (_pages.All(p => p.Id != id)) return;
        if (!Shown)
        {
            PageId = id;
            Summon(nowMs, resetSelection: true);
            return;
        }

        var wasSearching = SearchOpen;
        SearchOpen = false; // a page's key ends search (the swap below lays the page out)
        if (ShowsPill)
        {
            PageId = id; // a page's key while the pill is up: the pill grows into the capsule on that page, without the keyboard
            GrowToCapsule(nowMs, keyboard: false);
            return;
        }

        Poke(nowMs);
        if (id == PageId)
        {
            // The same page: nothing changes, unless search was open: its width is given back through the same swap.
            if (wasSearching && Phase == IslandPhase.Open) SwitchWhileOpen(nowMs);
            return;
        }

        // The second row closes first, then the page changes (WORK-ORDER-5 §5).
        CloseRowAt(nowMs);
        PageId = id;
        PageChangeCount++;
        PageChangedOrder = ++_order;
        if (Phase == IslandPhase.FlyingIn) Summon(nowMs, resetSelection: true);
        else SwitchWhileOpen(nowMs);
    }

    /// <summary>
    /// The list of pages changed (Dan made, renamed, recoloured or removed one in the settings). The machine goes on
    /// with the pages it has; one that no longer exists is replaced by the first page. An open capsule is laid out again.
    /// </summary>
    public void SetPages(IReadOnlyList<Page> pages, double nowMs)
    {
        if (pages.Count == 0 || !double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        _pages = pages;
        if (_pages.All(p => p.Id != PageId)) PageId = _pages[0].Id;
        if (_pages.All(p => p.Id != ContentsPageId)) ContentsPageId = PageId;
        if (ShowsPill) return; // the pill has no pages; the capsule that comes later is laid out from the new list
        if (Phase == IslandPhase.Open)
        {
            Poke(nowMs);
            CloseRowAt(nowMs);
            SwitchWhileOpen(nowMs);
        }
        else
        {
            _capsuleTargetWidth = CapsuleLayout.SizeFor(Contents).Width;
        }
    }

    /// <summary>
    /// The rows of the page being shown changed (a pick was added, removed or moved): the open capsule is laid out
    /// again, with the contents going out and coming back in, so the shape changes only through the springs.
    /// Counts as use.
    /// </summary>
    public void ContentsChanged(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (Phase != IslandPhase.Open || ShowsPill) return; // a summon in progress lays the page out when it expands
        Poke(nowMs);
        ClampSelection(); // a pick that was removed may have been the selected one
        SwitchWhileOpen(nowMs);
    }

    /// <summary>
    /// The tiles of the page that fills itself changed (WORK-ORDER-11 section 1: a window came, went, or was renamed). The selected tile stays selected wherever it
    /// now stands; when its own window is gone the selection goes to the tile now in its place, or the last. A change of the number of tiles lays the open
    /// capsule out to its new width through the springs it already has: no page-swap movement, no jump of the selection to the first tile, and it does not
    /// count as use (the island still goes away after its idle time).
    /// </summary>
    public void SelfFillingItemsChanged(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        if (ContentsPageId != PageIds.Terminals) return;
        var items = ContentsItems;
        var at = _selectedKey is { } key ? items.ToList().FindIndex(i => i.WindowKey == key) : -1;
        SelectedItem = at >= 0 ? at : Math.Clamp(_selectedItem, 0, Math.Max(0, items.Count - 1));
        if (Phase != IslandPhase.Open || ShowsPill || SearchOpen || !ContentsVisible) return; // a summon or a swap in progress lays the page out when it expands
        var size = CapsuleLayout.SizeFor(Contents);
        _capsuleTargetWidth = size.Width;
        _w = _w.WithTarget(size.Width);
    }

    /// <summary>
    /// The idle time changed in the settings: it counts from now on. A shown island starts counting again with the new time (the person
    /// is at the settings, so it is in use). A number that cannot be a time changes nothing.
    /// </summary>
    public void SetIdleSeconds(double seconds, double nowMs)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || !double.IsFinite(nowMs)) return;
        AdvanceTo(nowMs);
        _idleMs = seconds * 1000.0;
        if (Shown) Poke(nowMs);
    }

    /// <summary>Any keybind, click on the island or mouse move over the capsule.</summary>
    public void Activity(double nowMs)
    {
        if (!double.IsFinite(nowMs)) return; // a time that is not a number would wedge the timers
        AdvanceTo(nowMs);
        if (Shown) Poke(nowMs);
    }

    public void ItemClick(int index, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return; // a time that is not a number would wedge the timers
        AdvanceTo(nowMs);
        if (!Shown || ShowsPill) return;
        Poke(nowMs);
        PendingDeleteId = null; // a click is any other key
        var count = ContentsItems.Count;
        if (Phase == IslandPhase.Open && ContentsVisible && index >= 0 && index < count)
            SelectedItem = index;
    }

    /// <summary>
    /// One rendered frame. The springs advance by the elapsed time in fixed steps, and every input
    /// and timed event takes effect at its own moment, so the path never depends on when frames
    /// arrive. Calling it again with the same time changes nothing.
    /// </summary>
    public void Tick(double nowMs)
    {
        if (double.IsFinite(nowMs)) AdvanceTo(nowMs);
    }

    /// <summary>True when the capsule is open, its contents are shown and every spring has settled.</summary>
    public bool IsAtRest =>
        Phase == IslandPhase.Open && ContentsVisible
        && Settled(_y) && Settled(_w) && Settled(_h) && Settled(_r);

    private static bool Settled(Spring s) =>
        Math.Abs(s.Value - s.Target) < RestPosition && Math.Abs(s.Velocity) < RestVelocity;

    private const double MaxCatchUpMs = 5000;
    private const double RestPosition = 0.05;
    private const double RestVelocity = 1;

    // ---- Transitions --------------------------------------------------

    private void Summon(double nowMs, bool resetSelection)
    {
        PendingDeleteId = null;
        ShowsPill = false;
        ShowsNotice = false;
        SearchOpen = false;
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        CloseRowAt(nowMs);

        // A different page's contents (a switch cut short by a dismiss, say) never keep the old selection.
        var contentsChange = ContentsPageId != PageId;
        ContentsPageId = PageId;
        if (resetSelection || contentsChange) SelectedItem = PreferredSelection();
        else if (Preferred() is var preferred and >= 0) SelectedItem = preferred; // the Media page opens on the pick that is playing, whichever key summoned it
        ClampSelection();

        if (Phase == IslandPhase.Hidden)
        {
            // Only a fully hidden island is teleported; a ball still in sight is reversed in place.
            _y = _y.Snap(-LookConstants.SpawnHeightAboveEdge);
            _w = _w.Snap(LookConstants.BallSize);
            _h = _h.Snap(LookConstants.BallSize);
            _r = _r.Snap(LookConstants.BallSize / 2);
            _clockMs = nowMs;
        }

        _flyOutIssued = false;
        _restStamped = false;
        RestReachedMs = double.NaN;
        _y = _y.WithTarget(LookConstants.TopGap);
        Phase = IslandPhase.FlyingIn;
        Poke(nowMs);
        Schedule(nowMs + LookConstants.ExpandDelayMs, Kind.Expand);
    }

    /// <summary>What the page prefers to open on (the playing pick on Media), or -1 for no opinion.</summary>
    private int Preferred() => _preferredSelection?.Invoke(ContentsPage) ?? -1;

    private int PreferredSelection() => Math.Max(0, Preferred());

    /// <summary>The selected item is always one the page has (a page can shrink while the island is hidden).</summary>
    private void ClampSelection() => SelectedItem = Math.Clamp(SelectedItem, 0, Math.Max(0, ContentsItems.Count - 1));

    /// <summary>The notice comes in by itself, without the keyboard: the ball flies in as always and opens into the notice.</summary>
    private void ShowNotice(double nowMs)
    {
        ShowPill(nowMs);
        ShowsNotice = true;
        Poke(nowMs);
    }

    /// <summary>The small shape turns into the notice or back into the pill through the springs, the contents going out and coming in.</summary>
    private void SwapSmall(double nowMs, bool notice)
    {
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        ShowsNotice = notice;
        Poke(nowMs);
        Schedule(nowMs, Kind.Expand);
    }

    /// <summary>The pill comes in by itself, without the keyboard: the ball flies in as always and opens into the pill instead of the capsule.</summary>
    private void ShowPill(double nowMs)
    {
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        CloseRowAt(nowMs);
        ShowsPill = true;
        ShowsNotice = false;
        HasKeyboard = false;
        if (Phase == IslandPhase.Hidden)
        {
            _y = _y.Snap(-LookConstants.SpawnHeightAboveEdge);
            _w = _w.Snap(LookConstants.BallSize);
            _h = _h.Snap(LookConstants.BallSize);
            _r = _r.Snap(LookConstants.BallSize / 2);
            _clockMs = nowMs;
        }

        _flyOutIssued = false;
        _restStamped = false;
        RestReachedMs = double.NaN;
        _y = _y.WithTarget(LookConstants.TopGap);
        Phase = IslandPhase.FlyingIn;
        Poke(nowMs);
        Schedule(nowMs + LookConstants.ExpandDelayMs, Kind.Expand);
    }

    /// <summary>The pill becomes the capsule through the springs; with <paramref name="keyboard"/> the island takes the keyboard (the main key).</summary>
    private void GrowToCapsule(double nowMs, bool keyboard)
    {
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        ShowsPill = false;
        ShowsNotice = false;
        ContentsPageId = PageId;
        SelectedItem = PreferredSelection();
        ClampSelection();
        HasKeyboard = keyboard;
        Poke(nowMs);
        Schedule(nowMs, Kind.Expand);
    }

    /// <summary>The capsule would leave while the pill is wanted and allowed: it shrinks to the pill instead, and the keyboard goes back at once.</summary>
    private void ShrinkToPill(double nowMs)
    {
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        HasKeyboard = false;
        CloseRowAt(nowMs);
        SearchOpen = false;
        ShowsPill = true;
        Poke(nowMs);
        Schedule(nowMs, Kind.Expand);
    }

    private void Expand(double atMs)
    {
        if (ShowsPill)
        {
            var width = ShowsNotice ? _noticeWidth : _pillWidth;
            _capsuleTargetWidth = width;
            _w = _w.WithTarget(width);
            _h = _h.WithTarget(ShowsNotice ? NoticeLayout.Height : PillLayout.Height);
            _r = _r.WithTarget(ShowsNotice ? NoticeLayout.Radius : PillLayout.Radius);
            Phase = IslandPhase.Open;
            Schedule(atMs + LookConstants.ContentsDelayMs, Kind.ContentsIn);
            return;
        }

        ClampSelection();
        var size = CapsuleLayout.SizeFor(Contents);
        if (SearchOpen) size = size with { Width = _searchWidth }; // search lays the capsule out by its own width
        _capsuleTargetWidth = size.Width;
        _w = _w.WithTarget(size.Width);
        _h = _h.WithTarget(SecondRowOpen ? ChoiceConstants.TwoRowHeight : size.Height);
        _r = _r.WithTarget(size.Radius);
        Phase = IslandPhase.Open;
        Schedule(atMs + LookConstants.ContentsDelayMs, Kind.ContentsIn);
    }

    private void Dismiss(double nowMs)
    {
        PendingDeleteId = null;
        if (Phase is IslandPhase.Hidden or IslandPhase.Closing) return;
        if (!ShowsPill && PillWanted && PillAllowed)
        {
            ShrinkToPill(nowMs);
            return;
        }

        _queue.Clear();
        SetContentsVisible(false, nowMs);
        HasKeyboard = false;
        CloseRowAt(nowMs);
        SearchOpen = false;
        ShowsNotice = false;
        Phase = IslandPhase.Closing;
        _flyOutIssued = false;
        Schedule(nowMs + LookConstants.DismissShrinkAtMs, Kind.ShrinkToBall);
        Schedule(nowMs + LookConstants.DismissFlyOutAtMs, Kind.FlyOut);
    }

    private void SwitchWhileOpen(double nowMs)
    {
        PendingDeleteId = null;
        _queue.Clear();
        SetContentsVisible(false, nowMs);
        Schedule(nowMs + LookConstants.SwitchSwapDelayMs, Kind.SwapContents);
    }

    private void Fire(Scheduled e)
    {
        switch (e.Kind)
        {
            case Kind.Expand:
                Expand(e.DueMs);
                break;
            case Kind.ContentsIn:
                SetContentsVisible(true, e.DueMs);
                break;
            case Kind.SwapContents:
                ContentsPageId = PageId;
                // While the second row is open the + tile stays the selected one: the text block reads "Add something".
                SelectedItem = SecondRowOpen && ContentsItems.Count > 0 && ContentsItems[^1].IsPlus ? ContentsItems.Count - 1 : PreferredSelection();
                Expand(e.DueMs);
                break;
            case Kind.ShrinkToBall:
                var ball = CapsuleLayout.Ball;
                _w = _w.WithTarget(ball.Width);
                _h = _h.WithTarget(ball.Height);
                _r = _r.WithTarget(ball.Radius);
                break;
            case Kind.FlyOut:
                _y = _y.WithTarget(-LookConstants.SpawnHeightAboveEdge);
                _flyOutIssued = true;
                break;
        }
    }

    // ---- Helpers ------------------------------------------------------

    private void Schedule(double dueMs, Kind kind) => _queue.Add(new Scheduled(dueMs, kind));

    private void Poke(double nowMs) => _idleDeadlineMs = ShowsPill && (ShowsNotice || PillWanted && PillAllowed) ? double.PositiveInfinity : nowMs + _idleMs;

    private void SetContentsVisible(bool visible, double atMs)
    {
        if (ContentsVisible == visible) return;
        ContentsVisible = visible;
        ContentsChangedAtMs = atMs;
    }

    private bool IsSettledOutOfSight() =>
        Math.Abs(_y.Value - (-LookConstants.SpawnHeightAboveEdge)) < LookConstants.HiddenSettlePosition
        && Math.Abs(_y.Velocity) < LookConstants.HiddenSettleVelocity;

    /// <summary>
    /// Moves the machine to <paramref name="nowMs"/>: scheduled events and the idle deadline run in
    /// time order, and the springs are advanced to each one's moment first, so a target changes at
    /// exactly the time it was due whatever the frame pattern.
    /// </summary>
    private void AdvanceTo(double nowMs)
    {
        while (true)
        {
            var next = -1;
            for (var i = 0; i < _queue.Count; i++)
                if ((!Animations || _queue[i].DueMs <= nowMs) && (next < 0 || _queue[i].DueMs < _queue[next].DueMs))
                    next = i;

            var idleDue = Shown && !HoldOpen && _idleDeadlineMs <= nowMs;
            if (next < 0 && !idleDue) break;

            if (idleDue && (next < 0 || _idleDeadlineMs < _queue[next].DueMs))
            {
                StepSprings(_idleDeadlineMs);
                Dismiss(_idleDeadlineMs);
                continue;
            }

            var e = _queue[next];
            _queue.RemoveAt(next);
            if (!Animations && e.DueMs > nowMs) e = e with { DueMs = nowMs }; // no animations: a timed step is taken now
            StepSprings(e.DueMs);
            Fire(e);
        }

        StepSprings(nowMs);
        if (Phase == IslandPhase.Closing && _flyOutIssued && IsSettledOutOfSight())
        {
            Phase = IslandPhase.Hidden;
            ShowsPill = false;
            ShowsNotice = false;
            _idleDeadlineMs = double.PositiveInfinity;
        }
    }

    private void StepSprings(double toMs)
    {
        if (!Animations && Phase != IslandPhase.Hidden)
        {
            // no animations: every spring is at its target at once (also when no time has passed: a target was just set)
            _y = _y.Snap(_y.Target);
            _w = _w.Snap(_w.Target);
            _h = _h.Snap(_h.Target);
            _r = _r.Snap(_r.Target);
            StampRest(toMs);
            _clockMs = Math.Max(_clockMs, toMs);
            return;
        }

        var dt = toMs - _clockMs;
        if (dt <= 0) return;
        if (Phase == IslandPhase.Hidden)
        {
            _clockMs = toMs;
            return;
        }

        // One spring step at a time, so the moment of rest can be stamped exactly.
        // A stall (a laptop asleep with the island open, say) is caught up, but never step by step for
        // longer than the springs need: after a few seconds they have long since settled.
        var remaining = Math.Min(dt, MaxCatchUpMs) / 1000.0;
        var at = _clockMs;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, Spring.StepSeconds);
            _y = _y.Frame(chunk);
            _w = _w.Frame(chunk);
            _h = _h.Frame(chunk);
            _r = _r.Frame(chunk);
            remaining -= chunk;
            at += chunk * 1000.0;
            StampRest(at);
        }

        _clockMs = toMs;
    }

    private void StampRest(double atMs)
    {
        if (IsAtRest)
        {
            if (_restStamped) return;
            _restStamped = true;
            if (double.IsNaN(RestReachedMs)) RestReachedMs = atMs;
        }
        else
        {
            _restStamped = false;
        }
    }
}
