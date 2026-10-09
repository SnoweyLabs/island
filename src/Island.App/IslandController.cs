using System.Diagnostics;
using System.Windows.Media;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// Drives the drawing from <see cref="IslandMachine"/>, once per rendered frame. The frame callback can
/// fire more than once for the same frame, so frames are told apart by the frame's own rendering
/// time. While the island is hidden the controller is detached from the frame callback entirely.
/// The keybinds will call exactly these handlers.
/// </summary>
internal sealed class IslandController : IDisposable
{
    private readonly IslandView _view;
    private readonly IslandMachine _machine;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly RevealTimeline _reveal = new(0);
    private readonly ColourTransition _colour;
    private readonly double _centreX;
    private TimeSpan _lastRendering = TimeSpan.MinValue;
    private double _lastFrameMs = double.NaN;
    private string? _builtPageId;
    private bool _wasAtRest;
    private readonly KeyboardFocus? _keyboard;
    private MediaPage? _media;
    private IGlassLayer? _glassLayer;
    private bool _glassShown;
    private bool _holdingKeyboard;
    private readonly StripScroll _strip = new();
    private double _stripLastMs = double.NaN;
    private string? _stripPageId;
    private double _row2Opacity;
    private bool _row2WasOpen;
    private readonly RevealTimeline _pillReveal = new(1);
    private PillContent? _pillShown;
    private bool _wasPill;
    private double? _ringShare;
    private bool _rendering;
    private System.Windows.Threading.DispatcherTimer? _pillTimer;
    private readonly RevealTimeline _noticeReveal = new(1);
    private NoticeContent? _noticeShown;
    private bool _wasNotice;
    private readonly RevealTimeline _searchReveal = new(1);
    private SearchViewData? _searchData;
    private bool _wasSearch;

    /// <summary>Search in the island (WORK-ORDER-7 section 3); null before the app has made it.</summary>
    internal SearchSession? Search { get; set; }

    /// <summary>What search shows now; null while search is not open. The session sets it, the next drawn frame shows it.</summary>
    internal void SetSearchData(SearchViewData? data) => _searchData = data;

    public IslandController(IslandView view, IslandMachine machine, double windowWidth, KeyboardFocus? keyboard = null)
    {
        _view = view;
        _machine = machine;
        _keyboard = keyboard;
        _centreX = windowWidth / 2;
        _colour = new ColourTransition(Rgb.FromHex(machine.Page.Color));
        _view.SetShown(false);
        _view.Pill.PreviousClicked += () => PillButton(MediaCommand.Previous);
        _view.Pill.PlayPauseClicked += () => PillButton(MediaCommand.PlayPause);
        _view.Pill.NextClicked += () => PillButton(MediaCommand.Next);
        _view.Pill.TitleClicked += () =>
        {
            Activity();
            _media?.BringLineForward();
        };
        _view.Pill.SearchClicked += () => Input((m, now) => Search?.OpenFromPill(m, now)); // inside the click's own handler, so the keyboard can be asked for
        _view.Notice.Clicked += () => NoticeClicked?.Invoke();
        _view.Search.TileClicked += index => Input((m, now) => Search?.ActivateTile(index, m, now));
        _view.Search.FieldClicked += () => Input((m, now) => m.TakeKeyboard(now));
        _view.Contents.ArrowClicked += right =>
        {
            if (IsLifted?.Invoke() == true) return;
            if (right) _strip.ArrowRight();
            else _strip.ArrowLeft();
            Activity();
        };
    }

    /// <summary>What the notice says now; set by the app before it asks for the notice to show.</summary>
    internal NoticeContent? NoticeText { get; set; }

    /// <summary>The notice was clicked (the app brings the terminal forward).</summary>
    public event Action? NoticeClicked;

    /// <summary>
    /// The notice shows or goes (WORK-ORDER-7 section 4). The app has asked the table and the queue; the island only draws it, without the keyboard.
    /// From hidden, the screen is chosen first, as for any summon.
    /// </summary>
    public void SetNotice(bool show)
    {
        if (show && _machine.Phase == IslandPhase.Hidden) BeforeSummon?.Invoke();
        Input((m, now) =>
        {
            if (show && NoticeText is { } text) m.SetNoticeWidth(_view.Notice.TextWidth, now);
            m.SetNotice(show, now);
        });
    }

    /// <summary>The picture of the source the pill names (its pick's own icon), or null for its two letters.</summary>
    public Func<NowPlayingView, IconImage?>? PillIcon { get; set; }

    private void PillButton(MediaCommand command)
    {
        Activity();
        _media?.Send(command);
    }

    /// <summary>
    /// Something that counts started or stopped playing, or the table now allows or refuses the pill (WORK-ORDER-7 sections 1 and 2): the island
    /// is told, and the pill comes, stays or leaves through the springs. From hidden, the screen is chosen first, as for any summon.
    /// </summary>
    public void SetPill(bool wanted, bool allowed)
    {
        if (_machine.Phase == IslandPhase.Hidden && wanted && allowed) BeforeSummon?.Invoke();
        Input((m, now) => m.SetPill(wanted, allowed, now));
    }

    /// <summary>The sliding row of picks (WORK-ORDER-5 §4): where it is and where it is heading.</summary>
    internal StripScroll Strip => _strip;

    /// <summary>
    /// The mouse wheel turned over the island (WHEEL_DELTA units, positive away from the user): slides the picks, or the tiles of the
    /// second row when the pointer is over it, and counts as use.
    /// </summary>
    public void Wheel(int delta, bool overSecondRow = false)
    {
        if (IsLifted?.Invoke() == true) return; // while a tile is lifted the wheel does nothing
        _machine.CancelPendingDelete(); // the wheel is not the second Delete
        if (overSecondRow && _machine.SecondRowOpen) _view.Contents.SecondRowWheel(delta);
        else _strip.Wheel(delta);
        Activity();
    }

    /// <summary>True while a tile is lifted (WORK-ORDER-5 §6): the wheel, the arrows and the page keys do nothing, and the idle time does not run.</summary>
    public Func<bool>? IsLifted { get; set; }

    /// <summary>True from the press on a tile until the release: the first row is never drawn again under a pressed tile, and the page keys wait.</summary>
    public Func<bool>? IsPressed { get; set; }

    /// <summary>The first row may be drawn again now: while its contents are out (the capsule is changing) or at rest, and never under a pressed tile.</summary>
    private bool CanRedrawRow() => IsPressed?.Invoke() != true && (_machine.IsAtRest || !_machine.ContentsVisible);

    /// <summary>Raised when the second row has to be (re)drawn: it just opened, or the first row was drawn again while it is open.</summary>
    public event Action? SecondRowOpened;

    /// <summary>True while the pointer is over the island: with the second row open the idle time does not run (WORK-ORDER-5 §5).</summary>
    public Func<bool>? PointerIsOver { get; set; }

    /// <summary>A click on the + tile: the capsule grows into two rows, or the second row closes.</summary>
    public void TogglePlusRow() => Input((m, now) => m.ToggleSecondRow(now));

    /// <summary>The second row closes (a click on a pick, Esc).</summary>
    public void CloseSecondRow() => Input((m, now) => m.CloseSecondRow(now));

    public IslandMachine Machine => _machine;

    /// <summary>
    /// The blurred glass under the capsule, or null for the other glasses. The layer follows the shape from the same spring
    /// values in the same frame callback as the rest and is shown only while the island is.
    /// </summary>
    public void SetGlassLayer(IGlassLayer? layer)
    {
        if (ReferenceEquals(layer, _glassLayer)) return;
        _glassLayer?.Hide();
        _glassShown = false;
        _glassLayer = layer;
    }

    /// <summary>Gives the Media page its "Now playing" line and its working buttons.</summary>
    public void SetMedia(MediaPage media)
    {
        _media = media;
        media.Changed += () =>
        {
            _view.Contents.SetNowPlaying(media.View);
            if (Attached && !_rendering) Draw(NowMs); // the pill (or the quiet capsule) at rest draws nothing by itself: a new report is the moment to look again
        };
        _view.Contents.ControlClicked += command =>
        {
            Activity();
            media.Send(command);
        };
        _view.Contents.TextClicked += () =>
        {
            Activity();
            media.BringLineForward();
        };
    }

    /// <summary>The media page's now-playing line is read again (a source changed). Call on the UI thread.</summary>
    public void MediaChanged() => _media?.Refresh(DateTimeOffset.UtcNow);

    /// <summary>Frames this controller has drawn. Does not advance while hidden.</summary>
    public long FramesDrawn { get; private set; }

    /// <summary>True while the controller listens to the frame callback.</summary>
    public bool Attached { get; private set; }

    public double NowMs => _clock.Elapsed.TotalMilliseconds;

    /// <summary>When set, the interval between drawn frames is appended here (milliseconds).</summary>
    public List<double>? FrameIntervals { get; set; }

    /// <summary>When set, the clock time of every drawn frame is appended here, parallel to <see cref="FrameIntervals"/>.</summary>
    public List<double>? FrameStamps { get; set; }

    /// <summary>Clock time of the first drawn frame at which the capsule was open and at rest after the latest summon; NaN until then.</summary>
    public double AtRestMs { get; private set; } = double.NaN;

    /// <summary>The rim position (fraction of the outline) the latest drawn frame used for the first arc's head.</summary>
    public double LastArcHead { get; private set; }

    /// <summary>
    /// Time from the latest summon to the capsule open and at rest, in the machine's own time (exact to one spring
    /// step, whatever the frame pattern); NaN until it happens.
    /// </summary>
    public double RestDurationMs => double.IsNaN(_machine.RestReachedMs) ? double.NaN : _machine.RestReachedMs - SummonedMs;

    /// <summary>Clock time of the latest summon from hidden or while closing.</summary>
    public double SummonedMs { get; private set; } = double.NaN;

    // ---- The handlers the keybinds call -----------------------------------

    /// <summary>The show/hide handler of the tray menu: a click, so the person asked.</summary>
    public void ShowHide() => Input((m, now) => m.ShowHideKey(now), ShowOrigin.Asked);

    /// <summary>The island's own appearance at start-up: nobody asked, so over a fullscreen program it stays away.</summary>
    public void ShowHideByItself() => Input((m, now) => m.ShowHideKey(now), ShowOrigin.ByItself);

    public void PageKey(string pageId)
    {
        if (IsPressed?.Invoke() == true) return;
        Input((m, now) => m.PageKey(pageId, now), ShowOrigin.Asked);
    }

    /// <summary>A scene ran: the text block names it and says how many things it opened.</summary>
    public void ShowSceneNote(string name, string opened)
    {
        _sceneNote = (name, opened);
        _view.Contents.SetSceneNote(name, opened);
    }

    private (string Name, string Opened)? _sceneNote;

    /// <summary>The idle time changed in the settings (counts from now on).</summary>
    public void SetIdleSeconds(double seconds) => _machine.SetIdleSeconds(seconds, NowMs);

    /// <summary>Tile centre in window coordinates (for hanging a panel under a tile).</summary>
    public double TileCentreX(int index) => _view.Contents.TileCentreX(index);

    /// <summary>The rows of the page being shown changed (pick added, removed or moved): the capsule is laid out again.</summary>
    public void ContentsChanged() => Input((m, now) => m.ContentsChanged(now));

    /// <summary>The list of pages changed.</summary>
    public void PagesChanged(IReadOnlyList<Page> pages) => Input((m, now) => m.SetPages(pages, now));

    /// <summary>Asked before Esc dismisses the island: true when a panel took the key (it closes first).</summary>
    public Func<bool>? ConsumeEscape { get; set; }

    /// <summary>Called on every drawn frame, on the UI thread.</summary>
    public Action? FrameHook { get; set; }

    public void MainKey() => Input((m, now) => m.MainKey(now), ShowOrigin.Asked);

    /// <summary>A key with no modifier that is not a repeat (what the older callers and the self-test send).</summary>
    public void HandleKey(int virtualKey) => HandleKey(new KeyInput(virtualKey));

    /// <summary>True while the settings screen shows; set by the app. None of the keys of WORK-ORDER-10 §2 act then.</summary>
    public Func<bool>? SettingsShowing { get; set; }

    /// <summary>The second press of Delete on the pick that the first one asked about: the app removes that pick (nothing that is open is closed).</summary>
    public event Action<string>? RemoveRequested;

    private bool _enterRowWhenBuilt;

    /// <summary>
    /// A key read by the island's window while it has the keyboard (WORK-ORDER-10 §2): <see cref="IslandKeys.Decide"/> says what it means from the key, the
    /// modifiers, whether it is a repeat and the state laid out below; a key it leaves alone goes the old way: the digits 1 to 9 (number row or number pad) switch
    /// page, Esc dismisses, search reads its own, anything else only counts as use.
    /// </summary>
    public void HandleKey(KeyInput key) => Input((m, now) =>
    {
        var decision = IslandKeys.Decide(key, KeyContextOf(m));
        if (decision.CancelsPendingDelete) m.CancelPendingDelete();
        if (!decision.Handled)
        {
            LegacyKey(key.VirtualKey, m, now);
            return;
        }

        switch (decision.Action)
        {
            case KeyAction.MoveLeft or KeyAction.MoveRight:
                var step = decision.Action == KeyAction.MoveLeft ? -1 : 1;
                if (Practice) Practiced?.Invoke(PracticeEvent.Moved);
                if (m.SecondRowSelected >= 0) m.MoveSecondRow(step, _view.Contents.SecondRowCount, now);
                else
                {
                    m.MoveSelection(step, now);
                    if (m.SelectedItem < StripLayout.PickCount(m.ContentsItems)) _strip.BringIntoView(m.SelectedItem); // the selected tile is brought into view
                }

                break;
            case KeyAction.Activate:
                OnItemClicked(m.SelectedItem); // exactly a click: the same one path
                break;
            case KeyAction.OpenSecondRowAndEnter:
                if (Practice) Practiced?.Invoke(PracticeEvent.EnteredSecondRow);
                if (!m.SecondRowOpen) m.ToggleSecondRow(now);
                _enterRowWhenBuilt = true; // the row is built on the next frame; the selection goes in when it has tiles
                m.OtherKey(now);
                break;
            case KeyAction.OpenSecondRow:
                if (!m.SecondRowOpen) m.ToggleSecondRow(now);
                SelectPlusTile(m, now);
                break;
            case KeyAction.LeaveSecondRow:
                m.LeaveSecondRow(now);
                break;
            case KeyAction.SecondRowJump:
                if (Practice) Practiced?.Invoke(PracticeEvent.Chose); // practising: nothing is jumped to
                else _view.Contents.ActivateSecondRowTile(m.SecondRowSelected);
                m.OtherKey(now);
                break;
            case KeyAction.SecondRowAdd:
                if (Practice) Practiced?.Invoke(PracticeEvent.AddedFromRow); // practising: nothing is added
                else _view.Contents.AddSecondRowTile(m.SecondRowSelected);
                m.OtherKey(now);
                break;
            case KeyAction.NextPage or KeyAction.PreviousPage:
                if (Practice) Practiced?.Invoke(PracticeEvent.PageChanged);
                m.PageKey(m.PageIdAt(IslandKeys.PageAfterTab(m.PageIndex, m.PageCount, decision.Action == KeyAction.PreviousPage)), now);
                break;
            case KeyAction.PlayPause:
                _media?.Send(MediaCommand.PlayPause);
                m.OtherKey(now);
                break;
            case KeyAction.AskRemove:
                if (!m.AskRemove(now)) m.OtherKey(now);
                else if (Practice) Practiced?.Invoke(PracticeEvent.AskedToRemove);
                break;
            case KeyAction.ConfirmRemove:
                if (m.ConfirmRemove(now) is { } id)
                {
                    if (Practice) Practiced?.Invoke(PracticeEvent.Removed); // practising: nothing is taken off the island
                    else RemoveRequested?.Invoke(id);
                }

                break;
            case KeyAction.CancelPendingDelete:
                m.OtherKey(now);
                break;
            default:
                m.OtherKey(now); // CountsAsUse, and anything a later version of the function adds
                break;
        }
    });

    /// <summary>The old way for a key the new rules leave alone: search, Esc, the digits, then any other key only counts as use.</summary>
    private void LegacyKey(int virtualKey, IslandMachine m, double now)
    {
        const int escape = 0x1B, digit1 = 0x31, digit9 = 0x39, pad1 = 0x61, pad9 = 0x69;
        if (IsLifted?.Invoke() != true && Search?.Key(virtualKey, m, now) == true) return; // search took the key (open, or the key opens it)
        if (IsLifted?.Invoke() == true && virtualKey != escape) m.OtherKey(now); // the digits and the rest do nothing while a tile is lifted
        else if (virtualKey == escape && ConsumeEscape?.Invoke() == true) m.OtherKey(now);
        else if (virtualKey == escape) m.EscapeKey(now);
        else if (virtualKey is >= digit1 and <= digit9)
        {
            if (Practice) Practiced?.Invoke(PracticeEvent.PageChanged);
            m.DigitKey(virtualKey - digit1 + 1, now);
        }
        else if (virtualKey is >= pad1 and <= pad9)
        {
            if (Practice) Practiced?.Invoke(PracticeEvent.PageChanged);
            m.DigitKey(virtualKey - pad1 + 1, now);
        }
        else m.OtherKey(now);
    }

    private void SelectPlusTile(IslandMachine m, double now)
    {
        var items = m.ContentsItems;
        if (items.Count > 0 && items[^1].IsPlus) m.ItemClick(items.Count - 1, now); // as a click on the + tile selects it
    }

    /// <summary>What <see cref="IslandKeys"/> needs to know, read from the machine and the screen as they are now.</summary>
    private KeyContext KeyContextOf(IslandMachine m)
    {
        var items = m.ContentsItems;
        var inRow2 = m.SecondRowSelected >= 0;
        var row2Count = _view.Contents.SecondRowCount;
        var selectedItem = m.SelectedItem >= 0 && m.SelectedItem < items.Count ? items[m.SelectedItem] : null;
        return new KeyContext
        {
            HasKeyboard = m.HasKeyboard,
            SearchOpen = m.SearchOpen,
            SettingsShowing = SettingsShowing?.Invoke() == true,
            TileLifted = IsLifted?.Invoke() == true,
            IslandLeaving = m.Phase is IslandPhase.Hidden or IslandPhase.Closing,
            RowIsCurrentPage = m.Phase == IslandPhase.Open && m.ContentsVisible && m.ContentsPageId == m.PageId,
            PageChangeUnderWay = m.ContentsPageId != m.PageId,
            SecondRowOpen = m.SecondRowOpen,
            SelectionInSecondRow = inRow2,
            SecondRowHasItems = !m.SecondRowOpen || row2Count > 0, // not built yet while it is closed: Down opens it and looks again when it is built
            Selected = selectedItem is null ? SelectedTile.None : selectedItem.IsPlus ? SelectedTile.Plus : SelectedTile.Pick,
            AtFirst = inRow2 ? m.SecondRowSelected <= 0 : m.SelectedItem <= 0,
            AtLast = inRow2 ? m.SecondRowSelected >= row2Count - 1 : m.SelectedItem >= items.Count - 1,
            PageCount = m.PageCount,
            PlusClickActs = m.Phase == IslandPhase.Open && !m.ShowsPill && !m.SearchOpen && m.ContentsPageId != PageIds.Terminals, // no + tile and no second row on the Terminals page
            MediaCanPlayPause = m.ContentsPageId == PageIds.Media && _media?.View is { CanControl: true },
            DeletePending = m.PendingDeleteId is not null,
            SelectedIsPendingPick = m.PendingDeleteId is not null && selectedItem?.PickId == m.PendingDeleteId,
        };
    }

    /// <summary>
    /// Text typed into the island's own window (a character, a surrogate pair): opens search when the island has the keyboard, goes into the field
    /// when it is open. Anything else only counts as use. Never logged. Text made only of spaces or control characters (Space, Enter, Tab and Esc can arrive here as
    /// characters too) never opens search.
    /// </summary>
    public void HandleText(string text) => Input((m, now) =>
    {
        m.CancelPendingDelete(); // typed text is any other key
        if (!m.SearchOpen && !IslandKeys.IsOpeningText(text)) m.OtherKey(now);
        else if (IsLifted?.Invoke() == true || Search?.Text(text, m, now) != true) m.OtherKey(now);
    });

    /// <summary>Any click on the island.</summary>
    public void Activity() => Input((m, now) => m.Activity(now));

    /// <summary>
    /// The mouse moving over the capsule (Dan's P24, WORK-ORDER-13): the idle time starts again and nothing is drawn, because a move changes nothing that is drawn (a tile under the pointer reacts by its
    /// own mouse events). A frame is drawn only when the machine's state changed because of it (a deadline that had come due) and no frame callback is running to draw it.
    /// </summary>
    public void PointerMoved()
    {
        if (_machine.Phase == IslandPhase.Hidden) return;
        var before = MachineState();
        var now = NowMs;
        _machine.Activity(now);
        if (MachineState() == before || _rendering || !Attached) return;
        _disturbedMs = now;
        Draw(now);
    }

    private (IslandPhase, bool, bool, bool, bool, int, bool, string, bool) MachineState() =>
        (_machine.Phase, _machine.ContentsVisible, _machine.ShowsPill, _machine.ShowsNotice, _machine.SearchOpen, _machine.SelectedItem, _machine.SecondRowOpen, _machine.PageId, _machine.HasKeyboard);

    public void ItemClick(int index) => Input((m, now) => m.ItemClick(index, now));

    /// <summary>Raised after a click on a tile has moved the selection: the app carries out what the click means (start, open, bring forward).</summary>
    public event Action<int>? ItemActivated;

    /// <summary>A click on a tile, decided on the release of the button (the drag handler calls this when the pointer did not leave the drag distance).</summary>
    public void TileClicked(int index) => OnItemClicked(index);

    private void OnItemClicked(int index)
    {
        ItemClick(index); // the + tile is selected too: its text block reads "Add something" while the second row is open
        if (Practice && !(_machine.ContentsItems is { } items && index >= 0 && index < items.Count && items[index].IsPlus))
        {
            Practiced?.Invoke(PracticeEvent.Chose); // practising: a tile is chosen and nothing is started
            return;
        }

        ItemActivated?.Invoke(index);
    }

    /// <summary>
    /// True while the person practises the keys on the real island (the tutorial of the first start, Dan's, WORK-ORDER-13): choosing a tile starts nothing, adding from the second row adds nothing, and the
    /// second Delete removes nothing; each is told through <see cref="Practiced"/> instead. Set by the app for the length of the practice only.
    /// </summary>
    public bool Practice { get; set; }

    /// <summary>What the person did while practising (Dan's tutorial). Raised on the UI thread.</summary>
    public event Action<PracticeEvent>? Practiced;

    private bool _itemsDirty;

    /// <summary>
    /// What the pages show may have changed (a window opened, an icon arrived). Call on the UI thread. The row is
    /// drawn again as soon as the capsule is at rest, so a tile never changes in the middle of the entrance.
    /// </summary>
    public void ItemsChanged()
    {
        _itemsDirty = true;
        if (Attached && CanRedrawRow()) RedrawItemsIfChanged();
        if (Attached && !_rendering) Draw(NowMs); // a quiet capsule looks again at once: a tile coming or going wakes it
    }

    private void RedrawItemsIfChanged()
    {
        _itemsDirty = false;
        var now = _machine.ContentsItems;
        var built = _view.Contents.Contents.Items;
        if (now.Count == built.Count && now.SequenceEqual(built)) return;
        if (_machine.ContentsPageId == PageIds.Terminals)
        {
            // The page that fills itself (WORK-ORDER-11): the selection follows its window, the capsule takes its new width through the springs, a changed tile is drawn
            // again in its own place and only a change of the windows builds the row again (without hiding the tiles that stay).
            _machine.SelfFillingItemsChanged(NowMs);
            if (_view.Contents.TryUpdateInPlace(now)) return;
            _resizeReveal = _machine.ContentsVisible;
            _builtPageId = null;
            return;
        }

        _machine.CancelPendingDelete(); // the row is laid out again: a first Delete does not carry over to the new layout
        _builtPageId = null; // SyncContents rebuilds the row from the current items on the next draw
    }

    private bool _resizeReveal;
    private bool _terminalsShown;

    /// <summary>Raised on the UI thread when the Terminals page starts or stops being laid out (the page reads the process list only while it is).</summary>
    public event Action<bool>? TerminalsShown;

    private void UpdateTerminalsShown()
    {
        var on = Attached && _machine.Phase is not (IslandPhase.Hidden or IslandPhase.Closing) && !_machine.ShowsPill && !_machine.SearchOpen && _machine.ContentsPageId == PageIds.Terminals;
        if (on == _terminalsShown) return;
        _terminalsShown = on;
        TerminalsShown?.Invoke(on);
    }

    /// <summary>
    /// Called before an input is handed to the machine while nothing of the island is visible (hidden): the island may have to move to
    /// another screen before the ball starts (WORK-ORDER-6 §1). Never called while any part is visible.
    /// </summary>
    public Action? BeforeSummon { get; set; }

    private Mode _mode = Mode.Focus; // the approved edge until the app says which mode is on
    private Mode _modeFrom = Mode.Focus;
    private double _modeChangedMs = double.NegativeInfinity;

    /// <summary>The mode whose mark the edge shows. The settings are what names it; a stage that draws the island by itself gets the approved edge.</summary>
    public Mode Mode => _mode;

    /// <summary>
    /// Changes the mode's mark on the edge. While the island is visible the change cross-fades over 350 ms; with <paramref name="instant"/>
    /// (the start of the app, or an island that is not on the screen) it is there at once.
    /// </summary>
    public void SetMode(Mode mode, bool instant = false)
    {
        if (mode == _mode && _modeChangedMs != double.NegativeInfinity) return;
        var now = NowMs;
        _modeFrom = instant || _machine.Phase == IslandPhase.Hidden ? mode : _mode;
        _mode = mode;
        _modeChangedMs = instant || _machine.Phase == IslandPhase.Hidden ? double.NegativeInfinity : now;
        if (Attached) Draw(now);
    }

    /// <summary>The look of the edge at a clock time, for the self-test.</summary>
    public ModeMark.Look LookAt(double nowMs) =>
        ModeMark.Blend(ModeMark.LookOf(_modeFrom, _animations ? nowMs / 1000.0 : 0, SmallPill), ModeMark.LookOf(_mode, _animations ? nowMs / 1000.0 : 0, SmallPill), _animations ? nowMs - _modeChangedMs : double.PositiveInfinity); // no animations: no breath, and a mode change is not faded

    /// <summary>The small pill: nothing breathes on it (the notice, which stays only a few seconds, does breathe in Vibe).</summary>
    private bool SmallPill => _machine.ShowsPill && !_machine.ShowsNotice;

    /// <summary>Raised when the island is summoned (from hidden, or while it is leaving): a new showing begins.</summary>
    public event Action? Summoned;

    /// <summary>Raised when the island has left and nothing of it is drawn any more.</summary>
    public event Action? Left;

    /// <summary>
    /// Asked once, at the moment the island is about to appear from hidden (a key, a click on the tray menu, the start-up): may it
    /// appear now, given what is in front (WORK-ORDER-6 §2)? False: it does not appear, with no sound and no message.
    /// </summary>
    public Func<ShowOrigin, bool>? MayAppear { get; set; }

    private void Input(Action<IslandMachine, double> apply, ShowOrigin? summons = null)
    {
        if (summons is { } origin && _machine.Phase == IslandPhase.Hidden)
        {
            if (MayAppear?.Invoke(origin) == false) return; // it stays away
            BeforeSummon?.Invoke();
        }

        var wasShown = _machine.Phase is IslandPhase.FlyingIn or IslandPhase.Open;
        var wasHidden = _machine.Phase == IslandPhase.Hidden;
        var now = NowMs;
        apply(_machine, now);
        var isShown = _machine.Phase is IslandPhase.FlyingIn or IslandPhase.Open;
        UpdateKeyboard(now);
        if (!wasShown && isShown)
        {
            SummonedMs = now;
            AtRestMs = double.NaN;
            _wasAtRest = false;
            _builtPageId = null; // the row is laid out again and the sliding row starts at the beginning, also for a summon during the island's way out
            _stripPageId = null;
            Summoned?.Invoke();
        }

        // The colour changes at the moment of the key; a summon from hidden arrives already lit.
        _colour.Retarget(Rgb.FromHex(_machine.Page.Color), now, snap: wasHidden);
        _disturbedMs = now;

        if (_machine.Phase != IslandPhase.Hidden) Attach();
        if (Attached) Draw(now);
    }

    // ---- Frames --------------------------------------------------------

    private void Attach()
    {
        if (Attached) return;
        Attached = true;
        _rendering = true;
        _budget.Reset();
        _lastFrameMs = double.NaN;
        _stripLastMs = double.NaN;
        _stripPageId = null;
        _row2Opacity = 0;
        _row2WasOpen = false;
        _view.SetShown(true);
        CompositionTarget.Rendering += OnRendering;
    }

    private void FollowGlass(in ShapeFrame frame, double stretchX, double stretchY)
    {
        if (_glassLayer is not { } layer) return;
        // The same stretch the drawn shape has during the fly-in, about its centre.
        var w = frame.Width * stretchX;
        var h = frame.Height * stretchY;
        var centre = frame.Centre;
        layer.Follow(centre.X - w / 2, centre.Y - h / 2, w, h, frame.Radius);
        if (_glassShown) return;
        layer.Show();
        _glassShown = true;
    }

    private void Detach()
    {
        _glassLayer?.Hide();
        _glassShown = false;
        if (!Attached) return;
        Attached = false;
        if (_rendering) CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
        StopPillTimer();
        StopQuietBeat();
        TurnGraphicsLightOff();
        _pillShown = null;
        _noticeShown = null;
        _wasPill = false;
        _wasNotice = false;
        _wasSearch = false;
        _view.SetShown(false);
        _describedKey = default; // the next visit is told again (the page may have been renamed, a tile may have changed)
        _view.ForgetWords();
        _builtPageId = null;
        _view.Contents.SetSceneNote(null, null);
        _sceneNote = null;
        UpdateTerminalsShown();
        Left?.Invoke();
    }

    private readonly Island.Core.Speed.FrameBudget _budget = new();

    private bool _animations = true;

    /// <summary>
    /// Whether Windows is set to show animations (Dan's P1, WORK-ORDER-13). The app sets it to read the system's choice; null (a test, the self-test) means yes. With no animations the island comes and goes
    /// without spring or delay, its tiles are there without stagger or fade, the edge's light stands still (the compositor's light is not used) and Vibe does not breathe.
    /// </summary>
    public Func<bool>? AnimationsProbe { get; set; }

    /// <summary>
    /// How often the screen the island is on refreshes, in hertz; 0 when not known (every frame callback is then drawn, as before). The island never draws more frames than the
    /// screen refreshes (WORK-ORDER-12 section 2): the frame callback also comes when changes to the visual tree force an update, and a window drawn without the graphics card
    /// is not paced by the screen, so a cheap frame was followed at once by the next.
    /// </summary>
    public double RefreshHz { get; set; }

    private void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastRendering) return;
        _lastRendering = time;

        var now = NowMs;
        // At rest, with only the edge moving, the fixed-rate light draws at most 40 times a second; with the graphics-card light and only a playing tile or a working ring moving
        // the island draws at half the screen's rate (Dan's P25); whatever else moves keeps the screen's full rate.
        var interval = RefreshHz > 0 ? 1000.0 / RefreshHz : 0;
        if (interval > 0 && _edgeOnlyMoves) interval = Math.Max(interval, LightClock.FixedRateIntervalMs);
        else if (interval > 0 && _tilesOnlyMove) interval *= 2;
        if (!_budget.Admit(now, interval)) return;
        if (FrameIntervals is { } list && !double.IsNaN(_lastFrameMs))
        {
            list.Add(now - _lastFrameMs);
            FrameStamps?.Add(now - SummonedMs);
        }
        _lastFrameMs = now;

        if (!Step(now)) return;
        FramesDrawn++;
    }

    /// <summary>What every frame does, and what the quiet beat does in its place: the machine's clock, the keyboard, the playing line, the drag, then the drawing. False when the island is gone.</summary>
    private bool Step(double now)
    {
        var animations = AnimationsProbe?.Invoke() ?? true;
        _machine.Animations = animations;
        _reveal.Instant = !animations;
        _animations = animations;
        _machine.Tick(now);
        UpdateKeyboard(now);
        if (_machine.Phase != IslandPhase.Hidden) _media?.Poll(DateTimeOffset.UtcNow);
        FrameHook?.Invoke();
        if (_machine.Phase == IslandPhase.Hidden)
        {
            Detach();
            return false;
        }

        Draw(now);
        return true;
    }

    /// <summary>
    /// Keeps the window's keyboard in step with the machine: takes it when the machine got it from the
    /// main key (inside the keybind handler), gives it back when the machine let go by Esc, the main
    /// key or the idle time, and tells the machine when the person clicked into another window.
    /// </summary>
    private void UpdateKeyboard(double now)
    {
        if (_keyboard is null) return;

        if (_machine.HasKeyboard && !_holdingKeyboard)
        {
            _holdingKeyboard = true;
            if (!_keyboard.Take()) _machine.FocusLost(now); // Windows did not allow it: the island has no keyboard
        }
        else if (!_machine.HasKeyboard && _holdingKeyboard)
        {
            _holdingKeyboard = false;
            _keyboard.Give();
        }

        if (_machine.HasKeyboard && !_keyboard.IslandIsForeground) _machine.FocusLost(now);
    }

    private void Draw(double now)
    {
        _machine.Tick(now);
        UpdateKeyboard(now);
        UpdateTerminalsShown();
        if (_machine.Phase == IslandPhase.Hidden)
        {
            _describedKey = default; // the next visit is told again: what the words were at the last one may not be what is there now
            return;
        }

        var pill = _machine.ShowsPill;
        var notice = _machine.ShowsNotice;
        if (notice) DrawNotice(now);
        else if (pill) DrawPill(now);
        if (!pill || notice) _ringShare = null;
        _wasPill = pill && !notice;
        _wasNotice = notice;
        var search = _machine.SearchOpen && !pill;
        if (!_machine.SearchOpen) Search?.Reset(); // the island left, turned into the pill, or a page's key ended search
        if (search) DrawSearch(now);
        _wasSearch = search;
        SyncContents();
        var stripSeconds = double.IsNaN(_stripLastMs) ? 0 : Math.Max(0, (now - _stripLastMs) / 1000.0);
        _stripLastMs = now;
        _strip.Tick(stripSeconds);
        _view.Contents.SetStripPosition(_strip.Position, _strip.HiddenLeft, _strip.HiddenRight);
        DrawSecondRow(now, stripSeconds);
        DrawPlayingTile(now);
        var colour = notice ? Rgb.FromHex(LookConstants.VibeColor) : pill ? Rgb.FromHex(LookConstants.MediaColor) : _colour.At(now);

        _view.Contents.SetColour(colour);
        if (_view.Contents.HasTurningArcs) _view.Contents.SetRingClock(now / 1000.0); // a working ring turns on the clock the light turns on
        _view.Contents.SetSelected(_machine.SelectedItem);
        ApplyKeyboardState(now);
        for (var i = 0; i < _reveal.Count; i++)
            _view.Contents.SetPose(i, ToPose(_reveal.At(now, i)));

        LastArcHead = _animations ? _lightClock.Head(now, Light, RefreshHz, _edgeOnlyMoves) : 0; // no animations: the light stands still
        var frame = new ShapeFrame(_centreX, _machine.DrawnY, _machine.DrawnWidth, _machine.DrawnHeight,
            _machine.DrawnRadius, colour, LastArcHead);
        _view.SetPill(pill, _ringShare, notice);
        _view.SetSearch(search);
        var look = LookAt(now);
        _view.SetModeLook(look);
        _view.Apply(frame, _machine.StretchX, _machine.StretchY);
        FollowGlass(frame, _machine.StretchX, _machine.StretchY);
        UpdateMovingLight(now, frame, colour, pill, notice, look);
        DescribeForScreenReader(pill, notice, search);

        if (!_wasAtRest && _machine.IsAtRest)
        {
            _wasAtRest = true;
            AtRestMs = now;
        }
        else if (!_machine.IsAtRest)
        {
            _wasAtRest = false;
        }

        UpdateFrameSource(now);
    }

    /// <summary>The notice: the disc, the project's name and what happened. It stands still, so it needs no frame callback at rest.</summary>
    private void DrawNotice(double now)
    {
        if (!_wasNotice) _noticeReveal.Reset(1);
        if (NoticeText is { } text && text != _noticeShown)
        {
            _noticeShown = text;
            _view.Notice.Show(text, _centreX);
            _machine.SetNoticeWidth(_view.Notice.TextWidth, now);
        }

        if (_machine.ContentsVisible != _noticeReveal.Visible) _noticeReveal.Set(_machine.ContentsVisible, _machine.ContentsChangedAtMs);
        _view.Notice.SetPose(ToPose(_noticeReveal.At(now, 0)));
    }

    /// <summary>Search: the field, the matches and the text block. "Click here to type" while Windows has not given the island the keyboard.</summary>
    private void DrawSearch(double now)
    {
        if (!_wasSearch) _searchReveal.Reset(1);
        if (_searchData is { } data)
        {
            var shown = _machine.HasKeyboard ? data : data with { NeedsClick = true, Title = "Click here to type", Subtitle = string.Empty };
            _view.Search.Show(shown, _centreX, _colour.At(now));
        }

        if (_machine.ContentsVisible != _searchReveal.Visible) _searchReveal.Set(_machine.ContentsVisible, _machine.ContentsChangedAtMs);
        _view.Search.SetPose(ToPose(_searchReveal.At(now, 0)));
    }

    /// <summary>
    /// The pill: the title, the tile and the buttons of what is playing; the ring's lit share, or none (then the approved moving light is drawn
    /// in its place); and the width its title asks for.
    /// </summary>
    private void DrawPill(double now)
    {
        if (!_wasPill) _pillReveal.Reset(1);
        var view = _media?.View;
        if (view is not null)
        {
            var content = new PillContent(view.Title, IconChoice.TwoLetterMark(view.Where), PillIcon?.Invoke(view), view.IsPaused, view.CanControl);
            if (content != _pillShown)
            {
                _pillShown = content;
                _view.Pill.Show(content, _centreX);
                _machine.SetPillWidth(_view.Pill.TitleWidth, now);
            }
        }

        if (_machine.ContentsVisible != _pillReveal.Visible) _pillReveal.Set(_machine.ContentsVisible, _machine.ContentsChangedAtMs);
        _view.Pill.SetPose(ToPose(_pillReveal.At(now, 0)));
        _ringShare = view?.Report is { } report ? LitShare.Of(report, DateTimeOffset.UtcNow) : null;
    }

    /// <summary>
    /// The pill may stay up for hours, so while it is at rest with its ring drawn nothing runs at frame rate: the frame callback is let go of and a
    /// timer wakes the island when the lit end will have moved by a pixel; a new report, a button, a change of mode or a summon wakes it at once.
    /// A pill with the moving light, a pill that is moving, and the capsule keep the frame callback.
    /// </summary>
    private void UpdateFrameSource(double now)
    {
        if (!Attached) return;
        _edgeOnlyMoves = Light == LightKind.FixedRate && EverythingButTheEdgeIsStill(now);
        _tilesOnlyMove = _gpuLightOn && !_edgeOnlyMoves && OnlyTilesMove(now);
        if (CapsuleIsQuiet(now))
        {
            LeaveFrameCallback();
            StopPillTimer();
            ArmQuietBeat();
            return;
        }

        StopQuietBeat();
        var quiet = _machine.ShowsPill && _machine.Phase == IslandPhase.Open && _machine.IsAtRest && (_ringShare is not null || _machine.ShowsNotice)
                    && now - _modeChangedMs >= ModeMark.CrossFadeMs && IsPressed?.Invoke() != true;
        if (!quiet)
        {
            StopPillTimer();
            if (!_rendering)
            {
                _rendering = true;
                _lastFrameMs = double.NaN;
                _stripLastMs = double.NaN;
                CompositionTarget.Rendering += OnRendering;
            }

            return;
        }

        LeaveFrameCallback();
        if (_machine.ShowsNotice) StopPillTimer();
        else ArmPillTimer();
    }

    private void LeaveFrameCallback()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>How long after something last moved (a reveal, a colour change, the row rebuilt) the capsule counts as still: the longest reveal ends well before it.</summary>
    private const double QuietTailMs = 2000;

    /// <summary>How often a quiet capsule looks at the world: the idle time, the keyboard, the playing line, the windows and the sessions are seen within this.</summary>
    private const double QuietBeatMs = 250;

    private double _disturbedMs = double.NegativeInfinity;
    private bool _equalizerMoving;
    private System.Windows.Threading.DispatcherTimer? _quietBeat;

    /// <summary>
    /// WORK-ORDER-11 section 4: in Do not disturb nothing on the open capsule moves by itself (no light, no breathing glow), so at rest the frame callback is let go of
    /// and a slow beat keeps the island alive. Anything that moves (a playing tile, a working ring, a reveal, a scroll, a pressed or lifted tile, a mode change) keeps
    /// or brings back the callback at once; any input draws directly and so does.
    /// </summary>
    private bool CapsuleIsQuiet(double now) => (_mode == Mode.DND || _gpuLightOn || !_animations) && EverythingButTheEdgeIsStill(now);

    /// <summary>
    /// The capsule is open and at rest, nothing is pressed or lifted, no mode is cross-fading and nothing but the edge (the moving light, Vibe's breathing) moves: no spring, no scroll, no
    /// reveal, no page change, no playing tile, no working ring, no pill, notice or search. The same list that lets the Do not disturb capsule leave the frame callback; it also tells the
    /// half-rate light when it may halve the frame rate (WORK-ORDER-12 section 2).
    /// </summary>
    private bool EverythingButTheEdgeIsStill(double now)
    {
        if (now - _modeChangedMs < ModeMark.CrossFadeMs) return Disturbed(now);
        if (_machine.Phase != IslandPhase.Open || !_machine.IsAtRest || _machine.ShowsPill || _machine.ShowsNotice || _machine.SearchOpen) return Disturbed(now);
        if (_view.Contents.HasTurningArcs || _equalizerMoving || _strip.Moving || _row2Opacity is not (0 or 1)) return Disturbed(now);
        if (IsPressed?.Invoke() == true || IsLifted?.Invoke() == true || _itemsDirty) return Disturbed(now);
        var since = Math.Max(_disturbedMs, Math.Max(_machine.ContentsChangedAtMs, _machine.SecondRowChangedAtMs));
        return now - since >= QuietTailMs;
    }

    /// <summary>
    /// Dan's P25: the capsule is open and at rest and nothing moves but a playing tile's bars or a working ring (and the compositor's light, which costs the app nothing): the island is
    /// drawn at half the screen's rate. The same list as <see cref="EverythingButTheEdgeIsStill"/> except for the bars and the ring.
    /// </summary>
    private bool OnlyTilesMove(double now)
    {
        if (now - _modeChangedMs < ModeMark.CrossFadeMs) return false;
        if (_machine.Phase != IslandPhase.Open || !_machine.IsAtRest || _machine.ShowsPill || _machine.ShowsNotice || _machine.SearchOpen) return false;
        if (_strip.Moving || _row2Opacity is not (0 or 1)) return false;
        if (IsPressed?.Invoke() == true || IsLifted?.Invoke() == true || _itemsDirty) return false;
        var since = Math.Max(_disturbedMs, Math.Max(_machine.ContentsChangedAtMs, _machine.SecondRowChangedAtMs));
        return now - since >= QuietTailMs && (_view.Contents.HasTurningArcs || _equalizerMoving);
    }

    private IMovingLight? _movingLight;
    private bool _gpuLightOn;

    /// <summary>The graphics-card light, when there is one (WORK-ORDER-12 section 2): made by the runtime, which owns its windows.</summary>
    public void SetMovingLight(IMovingLight? light)
    {
        if (ReferenceEquals(light, _movingLight)) return;
        TurnGraphicsLightOff();
        _movingLight = light;
    }

    /// <summary>True while the compositor draws the moving light (and the old drawing of the glow and the rim is not drawn at all).</summary>
    public bool GraphicsLightActive => _gpuLightOn;

    /// <summary>
    /// The graphics-card light goes on where the kind asks for it, it can be had, and the old drawing is not what has to be seen: not on the small pill, not on the notice, not while a tile is lifted
    /// (the lifted tile must stay above the light), and not in Do not disturb (a dashed rim and no light). Everywhere else the compositor draws it for the whole time the island is on the
    /// screen and the app only moves its shape and changes its look, as it does for the glass.
    /// </summary>
    private void UpdateMovingLight(double now, in ShapeFrame frame, Rgb colour, bool pill, bool notice, ModeMark.Look look)
    {
        var wanted = _animations && Light == LightKind.GraphicsCard && _movingLight is { IsAvailable: true } && !pill && !notice && IsLifted?.Invoke() != true
                     && look.DashedRim <= 0 && look.MovingLight > 0;
        if (!wanted)
        {
            TurnGraphicsLightOff();
            return;
        }

        var light = _movingLight!;
        if (!_gpuLightOn)
        {
            light.Show(now);
            _view.SetGpuLight(true);
            _gpuLightOn = true;
        }

        // The shape as the glass follows it: stretched about its own centre during the fly-in.
        var w = frame.Width * _machine.StretchX;
        var h = frame.Height * _machine.StretchY;
        var centre = frame.Centre;
        var breathing = _mode == Mode.Vibe && now - _modeChangedMs >= ModeMark.CrossFadeMs;
        // stretched about its own centre, the corners are ellipses (Dan's P30): the radius is stretched with the shape
        light.Follow(new LightFrame(centre.X - w / 2, centre.Y - h / 2, w, h, frame.Radius, colour, look, breathing, now, frame.Radius * _machine.StretchX, frame.Radius * _machine.StretchY));
    }

    private (string?, string?, int, int, bool, bool, bool, string?, string?, string?, bool, Item?, Item?, int, int, bool, SearchViewData?, string?, (string, string)?) _describedKey;

    /// <summary>
    /// What the island shows, in words, for a screen reader (WORK-ORDER-12 section 4, EASE): the notice (told at once), the playing line of the pill, search, or the page and the selected tile with
    /// its second line and its place among the tiles. Only when something of that changed; nothing that is drawn changes, and the words are in memory only.
    /// </summary>
    private void DescribeForScreenReader(bool pill, bool notice, bool search)
    {
        var items = _machine.ContentsItems;
        var selected = _machine.SelectedItem;
        var playing = pill ? _media?.View?.Title : null;
        var paused = pill && _media?.View is { IsPaused: true };
        var rowTile = _machine.SecondRowOpen && _machine.SecondRowSelected >= 0 && _machine.SecondRowSelected < _view.Contents.SecondRowCount ? _view.Contents.SecondRowTile(_machine.SecondRowSelected).Item : null;
        var key = (_machine.ContentsPageId, _machine.ContentsPage.Name, selected, items.Count, notice, pill, search, notice ? NoticeText?.Line : playing, NoticeText?.Project, notice ? NoticeText?.Line : null, paused, selected >= 0 && selected < items.Count ? items[selected] : null, rowTile, _machine.SecondRowSelected, _view.Contents.SecondRowCount, _machine.SecondRowOpen, search ? _searchData : null, _machine.PendingDeleteId, _sceneNote);
        if (key == _describedKey) return;
        _describedKey = key;
        if (notice && NoticeText is { } n)
        {
            _view.Describe($"{n.Project}: {n.Line}", alert: true);
            return;
        }

        if (pill)
        {
            _view.Describe(playing is null ? "Island, now playing" : $"{(paused ? "Paused" : "Now playing")}: {playing}", alert: false);
            return;
        }

        if (search)
        {
            // What is typed, how many things were found and the one the keyboard is on: told again at every change.
            var data = _searchData;
            var found = data is null ? 0 : data.Tiles.Count;
            var on = data is not null && data.Selected >= 0 && data.Selected < found ? $", {data.Tiles[data.Selected].Title} selected, Enter opens it" : string.Empty;
            var typed = string.IsNullOrEmpty(data?.Text) ? "nothing typed yet" : $"typed {data!.Text}";
            _view.Describe(found == 0 && !string.IsNullOrEmpty(data?.Text) ? $"Island, search: {typed}. Nothing found" : $"Island, search: {typed}. {found} found{on}", alert: false);
            return;
        }

        var page = _machine.ContentsPage.Name;
        if (rowTile is not null)
        {
            // The keyboard is in the second row (what is open now): Enter adds the tile it is on.
            _view.Describe($"Island, {page}, open now: {rowTile.Title}, Enter goes to it, Shift+Enter adds it, {_machine.SecondRowSelected + 1} of {_view.Contents.SecondRowCount}", alert: false);
            return;
        }

        if (selected < 0 || selected >= items.Count) _view.Describe($"Island, {page}, {items.Count} items", alert: false);
        else if (items[selected].IsPlus) _view.Describe($"Island, {page}: add something", alert: false);
        else _view.Describe($"Island, {page}: {items[selected].Title}, {items[selected].Subtitle}, {selected + 1} of {items.Count}{(_machine.PendingDeleteId is not null && items[selected].PickId == _machine.PendingDeleteId ? $". {DeleteAgainNote}" : string.Empty)}{(_sceneNote is { } scene ? $". Scene {scene.Name}, {scene.Opened}" : string.Empty)}", alert: false);
    }

    private void TurnGraphicsLightOff()
    {
        if (!_gpuLightOn) return;
        _gpuLightOn = false;
        _movingLight?.Hide();
        _view.SetGpuLight(false);
    }

    private bool _edgeOnlyMoves;
    private bool _tilesOnlyMove;
    private readonly Island.Core.LightClock _lightClock = new();

    /// <summary>
    /// How the moving light is drawn (WORK-ORDER-12 section 2). An island made by a self-test stage draws it "as before" unless the stage asks otherwise; the app sets it from the settings.
    /// </summary>
    public LightKind Light { get; set; } = LightKind.AsBefore;

    private bool Disturbed(double now)
    {
        _disturbedMs = now;
        return false;
    }

    private void ArmQuietBeat()
    {
        if (_quietBeat is not null) return;
        _quietBeat = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(QuietBeatMs) };
        _quietBeat.Tick += (_, _) =>
        {
            if (!Attached) { StopQuietBeat(); return; }
            Step(NowMs);
        };
        _quietBeat.Start();
    }

    private void StopQuietBeat()
    {
        _quietBeat?.Stop();
        _quietBeat = null;
    }

    /// <summary>True while the quiet beat stands in for the frame callback (for the self-test).</summary>
    public bool IsQuiet => _quietBeat is not null;

    private void ArmPillTimer()
    {
        StopPillTimer();
        if (_media?.View is not { Report: { } report } || !report.IsPlaying || _ringShare is null) return; // paused: the ring stands still
        var speed = Math.Abs(report.Speed);
        if (report.LengthSeconds is not > 0 || speed <= 0) return;
        var outline = (2 * (_machine.DrawnWidth - 2 * PillLayout.Radius) + 2 * Math.PI * PillLayout.Radius) * TileView.PixelsPerDip;
        var seconds = Math.Clamp(report.LengthSeconds.Value / (speed * Math.Max(1, outline)), 0.25, 10); // one pixel of the outline
        _pillTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(seconds) };
        _pillTimer.Tick += (_, _) =>
        {
            StopPillTimer();
            if (!Attached) return;
            // Only the ring moves: the rim layer alone is drawn again (a whole frame is hundreds of milliseconds of software blur). What plays
            // changes through the media events, which draw everything; nothing is read from the players here.
            _ringShare = _media?.View?.Report is { } report ? LitShare.Of(report, DateTimeOffset.UtcNow) : null;
            _view.SetPill(true, _ringShare);
            if (_ringShare is null) Draw(NowMs); // the ring went (an item that has no length): the moving light needs the frame callback
            else ArmPillTimer();
        };
        _pillTimer.Start();
    }

    private void StopPillTimer()
    {
        _pillTimer?.Stop();
        _pillTimer = null;
    }

    /// <summary>What the pill shows now (for the self-test): its ring's lit share, null while the moving light stands in for it.</summary>
    public double? RingShare => _ringShare;

    /// <summary>True while the frame callback runs (the pill at rest lets go of it).</summary>
    public bool RunsAtFrameRate => _rendering;

    /// <summary>
    /// The tile that is playing dances (WORK-ORDER-5 §7): on the Media page, when what is playing belongs to one of its picks, three
    /// bars cover that tile, moving on a fixed loop that depends only on this clock; paused, they stand still. What is playing
    /// belongs to no pick on the page, or nothing has played: no tile dances.
    /// </summary>
    private void DrawPlayingTile(double now)
    {
        var index = -1;
        double[]? bars = null;
        if (_machine.ContentsPage.IsMedia && _media is { View: { } view } && _media.PlayingPickId is { } id)
        {
            index = _machine.ContentsItems.ToList().FindIndex(i => i.PickId == id);
            if (index >= 0) bars = Equalizer.Heights(now / 1000.0, playing: !view.IsPaused);
        }

        _equalizerMoving = bars is not null && _media?.View is { IsPaused: false };
        _view.Contents.SetEqualizer(index, bars);
    }

    /// <summary>
    /// The second row: asked to be drawn when it opens; its contents fade in a moment after the capsule starts growing and out
    /// quickly when it closes; its tiles slide with the clock; and while the pointer is over the island the idle time does not run.
    /// </summary>
    private void DrawSecondRow(double now, double seconds)
    {
        var open = _machine.SecondRowOpen;
        if (open && !_row2WasOpen) SecondRowOpened?.Invoke();
        _row2WasOpen = open;

        var visible = open && _machine.ContentsVisible && now - _machine.SecondRowChangedAtMs >= LookConstants.ContentsDelayMs;
        var step = seconds * 1000.0 / (visible ? ChoiceConstants.RowFadeInMs : LookConstants.DismissFadeMs);
        _row2Opacity = visible ? Math.Min(1, _row2Opacity + step) : Math.Max(0, _row2Opacity - step);
        _view.Contents.SetSecondRowOpacity(_row2Opacity);
        if (open) _view.Contents.TickSecondRow(seconds);

        if (open && _machine.Phase == IslandPhase.Open && PointerIsOver?.Invoke() == true) _machine.Activity(now);
        if (_machine.Phase == IslandPhase.Open && IsLifted?.Invoke() == true) _machine.Activity(now); // the idle time does not run while a tile is lifted
    }

    private (int Selected, int Count) _row2Applied = (-1, -1);

    /// <summary>
    /// The parts of WORK-ORDER-10 §2 that need the screen: the selection goes into the second row once it is built and has tiles (Down was pressed before it
    /// existed), the second row's selected tile is drawn selected (and drawn again when the row was built again), and a pending Delete shows its note.
    /// </summary>
    private void ApplyKeyboardState(double now)
    {
        if (_enterRowWhenBuilt && _machine.SecondRowOpen && _view.Contents.SecondRowIsBuilt)
        {
            _enterRowWhenBuilt = false;
            if (_view.Contents.SecondRowCount == 0) SelectPlusTile(_machine, now); // nothing open: the selection stays on the + tile
            else _machine.EnterSecondRow(_view.Contents.SecondRowCount, now);
        }

        if (!_machine.SecondRowOpen) _enterRowWhenBuilt = false;
        var applied = (_machine.SecondRowSelected, _view.Contents.SecondRowCount);
        if (applied != _row2Applied)
        {
            _row2Applied = applied;
            _view.Contents.SetSecondRowSelected(_machine.SecondRowSelected);
        }

        _view.Contents.SetRemoveNote(_machine.PendingDeleteId is not null ? DeleteAgainNote : null);
    }

    /// <summary>What the text block says under a pick's name after a first Delete.</summary>
    public const string DeleteAgainNote = "Delete again to remove";

    /// <summary>Rebuilds the row when the page whose contents are laid out changes, and starts the entrance or exit.</summary>
    private void SyncContents()
    {
        if (_itemsDirty && CanRedrawRow()) RedrawItemsIfChanged(); // never under a pressed tile

        if (_builtPageId != _machine.ContentsPageId)
        {
            var contents = _machine.Contents;
            _view.Contents.Build(contents, _centreX, _machine.SelectedItem, _colour.At(NowMs));
            _view.Contents.SetNowPlaying(_media?.View);
            if (_resizeReveal && _stripPageId == _machine.ContentsPageId) _reveal.Resize(_view.Contents.ElementCount, NowMs); // the tiles that stay stay
            else _reveal.Reset(_view.Contents.ElementCount);
            _resizeReveal = false;
            _disturbedMs = NowMs;
            _builtPageId = _machine.ContentsPageId;

            if (_machine.SecondRowOpen) SecondRowOpened?.Invoke(); // the row was drawn again: the second row is built again with it

            // A page that was not showing starts at the beginning, with the selected pick brought into view; the same page drawn again keeps its place.
            var picks = StripLayout.PickCount(contents.Items);
            _strip.SetMaxVisible(StripLayout.VisibleLimit); // fewer on a screen too narrow for seven
            _strip.SetCount(picks);
            if (_stripPageId != _machine.ContentsPageId)
            {
                _strip.Reset(_machine.SelectedItem < picks ? _machine.SelectedItem : -1);
                _stripPageId = _machine.ContentsPageId;
            }
        }

        if (_machine.ContentsVisible != _reveal.Visible)
            _reveal.Set(_machine.ContentsVisible, _machine.ContentsChangedAtMs);
    }

    private static Pose ToPose(RevealValues v) => new(
        v.Opacity,
        LookConstants.ContentsRise * (1 - v.Move),
        LookConstants.ContentsStartScale + (1 - LookConstants.ContentsStartScale) * v.Move,
        Units.RadiusForFilterBlur(v.Blur));

    public void Dispose() => Detach();
}
