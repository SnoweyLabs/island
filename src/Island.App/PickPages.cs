using System.Collections.Concurrent;
using Island.Core;

namespace Island.App;

/// <summary>
/// Icons by pick, read off the drawing thread: the first time an icon is wanted the tile shows its letters and the
/// icon is fetched in the background; when it arrives <see cref="Arrived"/> is raised and the page is drawn again.
/// </summary>
internal sealed class IconCache(Func<Pick, IconImage?> load, bool synchronous = false)
{
    private readonly ConcurrentDictionary<string, IconImage?> _icons = new();
    private readonly ConcurrentDictionary<string, bool> _requested = new();
    private readonly IconMisses _misses = new();

    public event Action? Arrived;

    public IconImage? Get(Pick pick)
    {
        if (_icons.TryGetValue(pick.Id, out var icon)) return icon;
        if (!_requested.TryAdd(pick.Id, true) && !_misses.ShouldRetry(pick.Id)) return null;

        if (synchronous) Fetch(pick);
        else Task.Run(() => Fetch(pick));
        return _icons.GetValueOrDefault(pick.Id);
    }

    public static IconImage? Read(IIconSource source, Pick pick)
    {
        // Something the person added by hand has its own place: its icon is the icon of that place (WORK-ORDER-10 §3), read in memory and never kept anywhere.
        if (pick.Location is not null) return PickPath.Expand(pick.Location, PickPages.ProfileFolder) is { } place ? source.PlaceIcon(place) : null;
        return pick.Kind switch
        {
            PickKind.Program => source.ProgramIcon(pick.ExeName, pick.PackageFamily),
            PickKind.Folder when pick.KnownFolder is not null => source.FolderIcon(pick.KnownFolder),
            _ => null,
        };
    }

    private void Fetch(Pick pick)
    {
        IconImage? icon = null;
        try
        {
            icon = load(pick);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // An icon that cannot be read is simply not shown: the tile keeps its letters.
        }

        if (icon is not null) RoundIcons.Of(icon); // the disc, the picture on it and the grey copy for a closed pick are made here, off the drawing thread
        if (icon is not null) _icons[pick.Id] = icon; // a miss is not kept: it is asked again a while later
        else _misses.Missed(pick.Id);
        if (icon is not null) Arrived?.Invoke();
    }
}

/// <summary>
/// What the island's pages show and what a click on a tile does, from the picks and the readers: the items of each
/// page (cached until something changes), and for a click the plan of <see cref="PickStates.Plan"/> carried out
/// through the one outside door. Nothing is written to a file.
/// </summary>
internal sealed class PickPages
{
    private readonly Func<PickStore> _store;
    private readonly AppWorld _world;
    private readonly ClickCycler _cycler = new();
    private readonly IconCache _icons;
    private readonly Dictionary<string, IReadOnlyList<Item>> _cache = [];
    private readonly object _lock = new();
    private int _version;

    private readonly bool _plusTile;
    private readonly string? _ownExe;
    private readonly PickTargetCache _targets = new();

    /// <summary>The profile folder, read in memory: a place under it is written with %USERPROFILE% in the picks file and expanded only here.</summary>
    internal static string ProfileFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>What the logic needs besides what is open: the profile folder and the answers about hand-added places (asked off the drawing thread).</summary>
    private PickContext Context => new(ProfileFolder, _targets);

    /// <summary>A refusal a click ended in (a hand-added thing that is not where it was): the app shows it like any other.</summary>
    public event Action<Refusal>? Refused;

    /// <summary>
    /// Starts asking, off the drawing thread, whether each hand-added folder, file and program is still where it was (every few seconds; a changed answer draws the island
    /// again). Never while a row is laid out: the rows only read the answers already known. The probe reads the disk and nothing else.
    /// </summary>
    public void StartTargetProbe(IPickTargetProbe probe, TimeSpan? every = null)
    {
        var period = every ?? TimeSpan.FromSeconds(5);
        var timer = new Timer(_ =>
        {
            try
            {
                if (_targets.Refresh(probe, _store().Picks, ProfileFolder)) Invalidate();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A probe that fails leaves the answers as they were; it is asked again next time.
            }
        }, null, TimeSpan.Zero, period);
        _probeTimer = timer;
    }

    private Timer? _probeTimer;

    /// <summary>Asks once, now, on the calling thread (the self-test and the settings screen after a pick was added by hand). True when an answer changed.</summary>
    public bool ProbeNow(IPickTargetProbe probe) => _targets.Refresh(probe, _store().Picks, ProfileFolder);

    /// <param name="plusTile">The last tile of every page is a + (add from what is open).</param>
    public PickPages(Func<PickStore> store, AppWorld world, bool synchronousIcons = false, bool plusTile = true, string? ownExe = null)
    {
        _store = store;
        _world = world;
        _plusTile = plusTile;
        _ownExe = ownExe;
        _icons = new IconCache(pick => IconCache.Read(world.Icons, pick), synchronousIcons);
        _icons.Arrived += Invalidate;
        world.Changed += Invalidate;
    }

    /// <summary>Raised when what the pages show may have changed. Can come from any thread.</summary>
    public event Action? Changed;

    public AppWorld World => _world;

    public PickStore Store => _store();

    private TerminalsPage? _terminals;

    /// <summary>
    /// The Terminals page (WORK-ORDER-11): it fills itself and holds no pick, so its items come from this and never from the picks. Set once by the app; the page is
    /// drawn again when its tiles change.
    /// </summary>
    public TerminalsPage? Terminals
    {
        get => _terminals;
        set
        {
            _terminals = value;
            if (value is not null) value.Changed += () => Changed?.Invoke();
        }
    }

    public IReadOnlyList<Item> ItemsOf(Page page)
    {
        // The page that fills itself returns what is already known: no cache of the picks' kind, no reading, no + tile.
        if (page.Id == PageIds.Terminals) return _terminals?.Items ?? [];

        lock (_lock)
        {
            if (_cache.TryGetValue(page.Id, out var cached)) return cached;
        }

        var version = Volatile.Read(ref _version);
        var snapshot = _world.Snapshot();
        var rows = PickList.RowsFor(page.Id, _store(), snapshot, Context);
        var items = PickItems.For(rows, pick => pick.Kind == PickKind.Site ? TabIconOf(pick, snapshot) : _icons.Get(pick)).ToList();
        if (_plusTile)
        {
            var open = PlusRow.Entries(snapshot, _store(), _world.Catalog.Installed, _ownExe).Count;
            items.Add(new Item(PlusRow.AddTitle, PlusRow.AddSubtitle(open), "+", 0, IsPlus: true));
        }
        lock (_lock)
        {
            if (version == _version) _cache[page.Id] = items; // an Invalidate that ran while this was computed wins
        }

        return items;
    }

    private readonly Dictionary<string, IconImage?> _tabIcons = [];

    /// <summary>The icon of a site pick: the stored icon of its newest tab that the add-on sent, decoded once; null (letters) when there is none.</summary>
    private IconImage? TabIconOf(Pick pick, OpenSnapshot snapshot)
    {
        if (!snapshot.TabsConnected || pick.Host is null) return null;
        foreach (var tab in snapshot.Tabs.Where(t => SiteMatch.Matches(pick.Host, t.Host)).OrderByDescending(t => t.LastActiveOrder))
        {
            var png = _world.Tabs.IconPng(tab.Key);
            if (png is null) continue;
            var key = $"{tab.Key}:{tab.Host}:{png.Length}";
            lock (_lock)
            {
                if (_tabIcons.TryGetValue(key, out var cached)) return cached;
                var decoded = Decode(png);
                _tabIcons[key] = decoded;
                return decoded;
            }
        }

        return null;
    }

    private static IconImage? Decode(byte[] png)
    {
        try
        {
            using var stream = new System.IO.MemoryStream(png);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            return new IconImage(frame.PixelWidth, frame.PixelHeight, pixels);
        }
        catch (Exception e) when (e is NotSupportedException or System.IO.FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null; // a picture that cannot be read leaves the tile with its letters
        }
    }

    /// <summary>Forgets the cached items so the next look reads the readers again, and says so.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _version++;
            _cache.Clear();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// A click on a tile: starts a closed program or opens a closed folder or site, or brings the next window
    /// forward (newest first, cycling). Returns the plan that was carried out, or null for the + tile and anything unknown.
    /// Called inside the click handler, so the foreground request is made while Windows allows it.
    /// </summary>
    public ClickPlan? Click(Page page, int index)
    {
        var items = ItemsOf(page);
        if (index < 0 || index >= items.Count) return null;
        var item = items[index];
        // A tile of the Terminals page is a window, never a pick: a click only brings that window forward (through the one outside door, which may refuse).
        if (item.WindowKey is { } window) return ClickWindow(window);

        if (item.IsPlus || item.PickId is null) return null;
        if (_store().ById(item.PickId) is not { } pick) return null;
        var plan = JumpTo(pick, out var tabKey);
        Clicked?.Invoke(pick, plan, tabKey);
        return plan;
    }

    /// <summary>A click on the tile of a window: that window, not whatever is at the tile's place in the list now (a window to its left may have closed since it was drawn).</summary>
    public ClickPlan ClickWindow(long window)
    {
        _world.Outside.BringForward(window);
        return new ClickPlan(ClickKind.BringForward, window);
    }

    /// <summary>A click on a pick that is not a tile of the page being shown (search's Enter on a match): the same as a click on its tile, including what the close button learns from it.</summary>
    public ClickPlan ClickPick(Pick pick)
    {
        var plan = JumpTo(pick, out var tabKey);
        Clicked?.Invoke(pick, plan, tabKey);
        return plan;
    }

    /// <summary>Raised after a CLICK on a pick (never after a key): the pick, what was done, and the tab the click went to (a website pick with the add-on).</summary>
    public event Action<Pick, ClickPlan, string?>? Clicked;

    /// <summary>
    /// What a click on the pick does, asked for by the pick itself (its own key, WORK-ORDER-6 section 4): start it, open it, or bring its
    /// next window forward. Called inside the key's handler, so the foreground request is made while Windows allows it.
    /// </summary>
    public ClickPlan Jump(Pick pick) => JumpTo(pick, out _);

    private ClickPlan JumpTo(Pick pick, out string? tabKey)
    {
        var snapshot = _world.Snapshot();
        var status = PickStates.For(pick, snapshot, Context);
        var plan = PickStates.Plan(pick, status, _cycler);
        tabKey = Carry(pick, plan, snapshot);
        LastJump = plan;
        return plan;
    }

    /// <summary>
    /// Runs a scene (WORK-ORDER-7 section 5): each thing that is closed is opened, each that is open is brought forward, in the list's order, and
    /// nothing is closed or placed. Every step is carried out the way a click on the pick is, through the outside actions. A thing that is no longer
    /// installed is skipped and the plan says so.
    /// </summary>
    public ScenePlan RunScene(Scene scene, IReadOnlyList<InstalledProgram> installed)
    {
        var snapshot = _world.Snapshot();
        var plan = ScenePlans.For(scene, snapshot, ScenePlans.InstalledIn(installed), Context);
        foreach (var step in plan.Steps)
        {
            if (step.Click is { } click) Carry(step.Thing, click, snapshot);
        }

        return plan;
    }

    /// <summary>The pick with this id, or null (a search result names a pick by its id).</summary>
    public Pick? PickById(string id) => _store().ById(id);

    /// <summary>The plan of the latest pick key (for the self-test).</summary>
    public ClickPlan? LastJump { get; private set; }

    /// <summary>Does what the plan says; returns the key of the tab it went to (a website pick that was brought forward), else null.</summary>
    private string? Carry(Pick pick, ClickPlan plan, OpenSnapshot snapshot)
    {
        string? tabKey = null;
        switch (plan.Kind)
        {
            case ClickKind.BringForward when pick.Kind == PickKind.Site:
                if (snapshot.Tabs.FirstOrDefault(t => t.LastActiveOrder == plan.Target) is { } tab && _world.TabControl.Activate(tab.Key)) tabKey = tab.Key;
                break;
            case ClickKind.BringForward:
                _world.Outside.BringForward(plan.Target);
                break;
            case ClickKind.Start:
                _world.Outside.StartProgram(pick);
                break;
            case ClickKind.OpenFolder when pick.KnownFolder is not null:
                _world.Outside.OpenFolder(pick.KnownFolder);
                break;
            case ClickKind.OpenSite when pick.Host is not null:
                _world.Outside.OpenSite(pick.Host);
                break;
            case ClickKind.OpenFolderAt when PickPath.Expand(pick.Location, ProfileFolder) is { } folder:
                _world.Outside.OpenFolderAt(folder);
                break;
            case ClickKind.OpenFile when PickPath.Expand(pick.Location, ProfileFolder) is { } file:
                _world.Outside.OpenFile(file);
                break;
            case ClickKind.TargetMissing:
                Refused?.Invoke(HandPickRefusals.ForMissing(pick)); // nothing is removed by itself
                break;
        }

        return tabKey;
    }
}
