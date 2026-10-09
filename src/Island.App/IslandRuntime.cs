using System.IO;
using System.Windows.Input;
using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>
/// The island as the app runs it: the two fixed windows, the view drawn into them, the machine and
/// the controller that joins them. The windows stay on screen, empty, while the island is hidden.
/// </summary>
internal sealed class IslandRuntime : IDisposable
{
    private readonly PickPages? _pages;
    private readonly PickBook? _book;
    private readonly PickKeyHandler? _pickKeys;

    /// <param name="pages">Where the pages get their rows (the picks and what is open); null keeps the placeholder rows of the approved look.</param>
    private IReadOnlyList<Page> _pageList;

    /// <param name="pageList">The pages, in order (the five built-in ones by default); changed later with <see cref="SetPages"/>.</param>
    /// <param name="takeKeyboard">False only in a self-test stage that tests the keys and not the hand-over of the keyboard (KeyboardStage does that): the machine then keeps its keyboard flag whatever Windows says about the foreground.</param>
    public IslandRuntime(double idleSeconds = LookConstants.IdleSeconds, PickPages? pages = null, PickBook? book = null, IReadOnlyList<Page>? pageList = null, bool takeKeyboard = true)
    {
        _pageList = pageList ?? Pages.BuiltIn;
        Gate = new ShowGate(message => Log?.Invoke(message));
        _pages = pages;
        _book = book;
        Host = new IslandHost();
        View = new IslandView(Host.Shadow.Root, Host.Capsule.Root, Host.WidthDip, Host.HeightDip);
        Machine = new IslandMachine(idleSeconds, _pageList, pages is null ? null : pages.ItemsOf, PlayingIndex);
        Keyboard = new KeyboardFocus(Host.Capsule);
        Controller = new IslandController(View, Machine, Host.WidthDip, takeKeyboard ? Keyboard : null);
        Host.Capsule.RawKey += Controller.HandleKey; // a key with its modifiers and its repeat flag (WORK-ORDER-10 §2)
        Host.Capsule.RawText += Controller.HandleText;

        if (pages is not null)
        {
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            Controller.ItemActivated += index =>
            {
                if (Machine.ContentsItems is { } items && index >= 0 && index < items.Count && items[index].IsPlus) Controller.TogglePlusRow();
                else
                {
                    Controller.CloseSecondRow();
                    // A window's tile is resolved by the window it was drawn for, not by its place in the list as it is now (WORK-ORDER-11 attack, R-1).
                    var drawn = View.Contents.Contents.Items;
                    if (Machine.ContentsPageId == PageIds.Terminals && index >= 0 && index < drawn.Count && drawn[index].WindowKey is { } window) pages.ClickWindow(window);
                    else pages.Click(Machine.ContentsPage, index);
                }
            };
            pages.Changed += () => ui.BeginInvoke(Controller.ItemsChanged);
            Controller.TerminalsShown += on => pages.Terminals?.SetLaidOut(on);
            Close = new CloseHandler(Controller, View.Contents, pages.World, handle => handle == Host.Capsule.Handle.ToInt64() || handle == Host.Shadow.Handle.ToInt64());
            Close.Log = message => Log?.Invoke(message);
            pages.Clicked += Close.PickClicked;

            if (book is not null)
            {
                SecondRow = new SecondRow(View.Contents, Controller, book, pages.World, Path.GetFileName(Environment.ProcessPath));
                Search = new SearchSession(Controller, SearchEntries,
                    entry =>
                    {
                        if (entry.IsPick && pages.PickById(entry.Key) is { } pick) pages.ClickPick(pick);
                        else if (entry.Open is { } open) SecondRow.JumpTo(open);
                    },
                    (tile, text) => pages.World.Outside.OpenSearch(tile.Service, text));
                Controller.Search = Search;
                Drag = new DragHandler(Controller, View.Contents, View.Drag);
                Drag.Removed += book.Remove; // only the list changes: nothing that is open is closed
                Controller.RemoveRequested += book.Remove; // the second Delete of the keyboard: the same, and nothing is closed (EVALS C6)
                Controller.ConsumeEscape = Drag.Cancel;
                book.Changed += () =>
                {
                    pages.Invalidate();
                    ui.BeginInvoke(Controller.ContentsChanged);
                };
                pages.Changed += () => ui.BeginInvoke(SecondRow.RefreshIfOpen);
            }

            _pickKeys = new PickKeyHandler(id => pages.Store.ById(id), pick => pages.Jump(pick), Controller.PageKey);
            Media = new MediaPage(pages.World, () => pages.Store);
            Controller.SetMedia(Media);
            Media.Changed += RefreshPill; // something started or stopped playing, or what plays changed
            pages.World.Windows.Changed += () => ui.BeginInvoke(RefreshPill); // the foreground changed: the table is asked again
            Controller.PillIcon = view =>
            {
                var id = Media.PlayingPickId;
                var media = _pageList.FirstOrDefault(p => p.IsMedia);
                return id is null || media is null ? null : pages.ItemsOf(media).FirstOrDefault(i => i.PickId == id)?.Icon;
            };
            pages.World.Media.Changed += () => ui.BeginInvoke(Controller.MediaChanged);
            pages.World.Tabs.Changed += () => ui.BeginInvoke(Controller.MediaChanged);
        }

        // The mouse moving over the capsule, and any click on it, count as activity.
        Host.Capsule.Root.MouseMove += (_, _) => Controller.PointerMoved(); // draws nothing (Dan's P24)
        Host.Capsule.Root.MouseDown += OnMouse;
        // The wheel slides the picks of a long page. Windows hands it to this window only when "scroll inactive windows when
        // hovering over them" is on; the arrows work either way. No hook: only the window's own mouse events.
        Host.Capsule.Root.MouseWheel += (_, e) =>
        {
            var overSecondRow = e.GetPosition(Host.Capsule.Root).Y > LookConstants.TopGap + LookConstants.BorderWidth + ChoiceConstants.RowHeight;
            Controller.Wheel(e.Delta, overSecondRow);
            e.Handled = true;
        };
        Controller.PointerIsOver = () => Host.Capsule.Root.IsMouseOver;

        // Dan's P8 (WORK-ORDER-13): Focus and Vibe look alike in a still picture and the mode's name is nowhere on the island. Resting the pointer on the capsule says it, in a small tip that never takes the keyboard.
        var tip = new System.Windows.Controls.ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, IsHitTestVisible = false };
        Host.Capsule.Root.ToolTip = tip;
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(Host.Capsule.Root, 700);
        Host.Capsule.Root.ToolTipOpening += (_, _) => tip.Content = ModeTip.Of(Controller.Mode);
    }

    /// <summary>The item the Media page opens on: the pick that is playing (WORK-ORDER-5 §7); the first on every other page.</summary>
    private int PlayingIndex(Page page)
    {
        if (!page.IsMedia || Media?.PlayingPickId is not { } id || _pages is null) return -1;
        return _pages.ItemsOf(page).ToList().FindIndex(i => i.PickId == id); // -1 when the playing pick is not on the page
    }

    public MediaPage? Media { get; }

    /// <summary>The close button's rules joined to the island; null when the runtime has no pick pages (the placeholder runtimes of the self-test).</summary>
    public CloseHandler? Close { get; }

    /// <summary>
    /// A pick's key was pressed: the pick is jumped to, as a click on it would, and the island comes in on its page without taking the
    /// keyboard (it still stays away when what is in front says so). A pick that is gone does nothing. False when nothing was done.
    /// </summary>
    public bool PickKey(string pickId) => _pickKeys?.Press(pickId) == true;

    /// <summary>
    /// A scene's key (WORK-ORDER-7 section 5): the scene runs first, inside the key's own handler, and then the island comes in on the page it was on,
    /// without the keyboard, its text block reading the scene's name and "&lt;n&gt; opened". When what is in front says the island stays away, the
    /// scene still ran. Null when there is no way to run it (a runtime without picks).
    /// </summary>
    public ScenePlan? RunScene(Scene scene, IReadOnlyList<InstalledProgram> installed)
    {
        if (_pages is null) return null;
        var plan = _pages.RunScene(scene, installed);
        Controller.PageKey(Machine.PageId);
        if (Machine.Phase != IslandPhase.Hidden) Controller.ShowSceneNote(scene.Name, plan.OpenedText);
        return plan;
    }

    /// <summary>"Show the pill while something plays" (the settings): on by default.</summary>
    public bool ShowPill { get; set; } = true;

    /// <summary>
    /// Asks again whether the pill is wanted (something that counts plays and the setting is on) and whether the table lets it show (what is in
    /// front, the mode), and tells the island. Called when what plays changes, when the foreground changes, and when the mode or the setting does.
    /// </summary>
    public void RefreshPill()
    {
        var wanted = ShowPill && Media?.View is { IsPaused: false, CanControl: true };
        Controller.SetPill(wanted, Gate.Check(Appearer.Pill, ShowOrigin.ByItself));
    }

    /// <summary>The idle time changed in the settings: the island goes on with the new time at once.</summary>
    public void SetIdleSeconds(double seconds) => Controller.SetIdleSeconds(seconds);

    /// <summary>Dragging a pick off the island; null when the runtime has no pick book (the placeholder runtimes of the self-test).</summary>
    public DragHandler? Drag { get; }

    /// <summary>Search in the island; null when the runtime has no pick book.</summary>
    public SearchSession? Search { get; }

    /// <summary>Everything search can offer: the picks of every page, then what is open and is no pick (the same list the second row shows).</summary>
    private IReadOnlyList<SearchEntry> SearchEntries()
    {
        if (_pages is null || SecondRow is null) return [];
        var picks = _pageList.Where(p => p.Id != PageIds.Terminals).SelectMany(p => _pages.ItemsOf(p)).Where(i => !i.IsPlus && i.PickId is not null) // the Terminals page is never read or listed by search
            .Select(i => new SearchEntry(i.Title, i.PickId!, true, i.IsClosed, i)).ToList();
        var own = Path.GetFileName(Environment.ProcessPath);
        var open = PlusRow.Entries(_pages.World.Snapshot(), _book?.Store ?? PickStore.Empty, _pages.World.Catalog.Installed, own)
            .Select(e => new SearchEntry(e.Name, e.Key, false, false, SecondRow.ItemOf(e), e));
        return [.. picks, .. open];
    }

    /// <summary>The second row the + opens; null when the runtime has no pick book.</summary>
    public SecondRow? SecondRow { get; }

    public IslandHost Host { get; }
    public IslandView View { get; }

    /// <summary>How the island borrows the keyboard (a stage may pretend the grant).</summary>
    internal KeyboardFocus Keyboard { get; }
    public IslandMachine Machine { get; }
    public IslandController Controller { get; }

    private Island.Glass.GlassLayer? _blur;

    /// <summary>Chooses the screen at each summon and places the windows there; made when the windows are shown.</summary>
    public ScreenPlacer? Placer { get; private set; }

    /// <summary>Where the placer says something went wrong (the kind only).</summary>
    public Action<string>? PlacementLog { get; set; }

    /// <summary>Looks at what is in front before the island shows itself (WORK-ORDER-6 §2).</summary>
    public ShowGate Gate { get; }

    /// <summary>Where the gate says why the island stayed away (the kind only, never a program's name).</summary>
    public Action<string>? Log { get; set; }

    public void Show()
    {
        Host.Show();
        Placer ??= new ScreenPlacer(Host, message => PlacementLog?.Invoke(message));
        Controller.BeforeSummon = () =>
        {
            Placer.PlaceForSummon(visible: false);
            Controller.RefreshHz = ScreenRefresh.HzOf(Host.Capsule.Handle); // the screen it was placed on (WORK-ORDER-12 section 2)
        };
        Controller.RefreshHz = ScreenRefresh.HzOf(Host.Capsule.Handle);
        Controller.MayAppear = origin => Gate.MayAppear(Appearer.Island, origin);
        Visuals.TileView.PixelsPerDip = System.Windows.Media.VisualTreeHelper.GetDpi(Host.Capsule).PixelsPerDip;
        _blur ??= new Island.Glass.GlassLayer(Host.Capsule.Handle);
    }

    private Island.Glass.MovingLight? _light;

    /// <summary>
    /// Makes the graphics-card light (WORK-ORDER-12 section 2): two windows of its own and the compositor's tree. Only the app asks for it (never an island of a self-test stage that is not
    /// testing it). Never throws: when it cannot be made <see cref="MovingLightAvailable"/> is false and the island draws the light the lighter way.
    /// </summary>
    public void EnableMovingLight()
    {
        if (_light is not null) return;
        _light = new Island.Glass.MovingLight(Host.Capsule.Handle, Host.Shadow.Handle);
        Controller.SetMovingLight(_light);
    }

    public bool MovingLightAvailable => _light?.IsAvailable == true;

    /// <summary>Why the graphics-card light is not available, as a short code (never a message text); null when it is.</summary>
    public string? MovingLightUnavailableReason => _light is null ? "NOT_MADE" : _light.UnavailableReason;

    public Island.Glass.MovingLight? MovingLight => _light;

    /// <summary>True when the Blur glass can be had here (Windows transparency effects on and the layer made). Valid after <see cref="Show"/>.</summary>
    public bool BlurAvailable => _blur?.IsAvailable == true;

    /// <summary>Why blur is not available, as a short code (never a message text); null when it is.</summary>
    public string? BlurUnavailableReason => _blur?.IsAvailable == true ? null : _blur?.UnavailableReason ?? "NOT_SHOWN";

    /// <summary>The blur layer, for the self-test.</summary>
    public IGlassLayer? BlurLayer => _blur;

    /// <summary>Raised once, with the reason code, when Blur was chosen but cannot be had: the island uses the Approved glass instead.</summary>
    public event Action<string>? BlurFellBack;

    private bool _blurFallbackSaid;

    /// <summary>The list of pages changed (a page was made, renamed, recoloured or removed): the island goes on with the new list.</summary>
    public void SetPages(IReadOnlyList<Page> pages)
    {
        _pageList = pages;
        Controller.PagesChanged(pages);
    }

    /// <summary>Switches the glass of the capsule while the app runs. Blur draws the approved tint over the blurred backdrop.</summary>
    public void SetGlass(GlassKind kind)
    {
        if (kind == GlassKind.Blur && !BlurAvailable)
        {
            if (!_blurFallbackSaid)
            {
                _blurFallbackSaid = true;
                BlurFellBack?.Invoke(BlurUnavailableReason ?? "UNKNOWN");
            }

            kind = GlassKind.Approved; // the choice stays in the settings; this run uses the approved glass
        }

        Glass = kind;
        View.SetGlassAlpha(kind switch
        {
            GlassKind.Darker => LookConstants.GlassDarkerAlpha,
            GlassKind.Blur => LookConstants.GlassBlurTintAlpha,
            _ => LookConstants.GlassBaseAlpha,
        });
        Controller.SetGlassLayer(kind == GlassKind.Blur ? _blur : null);
    }

    public GlassKind Glass { get; private set; } = GlassKind.Approved;

    private void OnMouse(object sender, MouseEventArgs e) => Controller.Activity();

    public void Dispose()
    {
        Placer?.Dispose();
        _blur?.Dispose();
        _light?.Dispose();
        Controller.Dispose();
        Host.Close();
    }
}
