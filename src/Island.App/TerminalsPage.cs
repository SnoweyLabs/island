using System.IO;
using Island.Core;
using Island.Core.Terminals;

namespace Island.App;

/// <summary>
/// The Terminals page (WORK-ORDER-11 sections 1 and 2): the tiles of the terminal windows, the AI programs' windows and the helpers inside terminals,
/// worked out by <see cref="TerminalPage"/> from what the island already lists, and — only while the page is laid out — from the console windows and
/// the process list that <see cref="ITerminalProbe"/> reads (off the drawing thread, every <see cref="ProbePeriod"/>). Asking for <see cref="Items"/>
/// returns what is already known and never reads anything. Titles, process names and window handles stay in memory; nothing here writes or logs one.
/// </summary>
internal sealed class TerminalsPage : IDisposable
{
    /// <summary>How often the console windows and the process list are read while the page is up (Claude).</summary>
    public static readonly TimeSpan ProbePeriod = TimeSpan.FromSeconds(2);

    private readonly AppWorld _world;
    private readonly ITerminalProbe? _probe;
    private readonly TerminalPage _page;
    private readonly TerminalTables _tables;
    private readonly HelperSessions? _helpers;
    private readonly bool _readsWindows;
    private readonly bool _synchronousIcons;
    private readonly object _lock = new();
    private readonly Dictionary<string, IconImage?> _icons = [];
    private readonly HashSet<string> _requested = [];
    private readonly IconMisses _misses = new();
    private TerminalProbeFacts _facts = TerminalProbeFacts.Empty;
    private IReadOnlyList<Item> _items = [];
    private Timer? _timer;
    private bool _laidOut;
    private int _probes;

    /// <param name="probe">The reader of the console windows and the process list; null reads none (the self-test, and every stage but the one that hands in invented facts).</param>
    /// <param name="readsWindows">False: the page is handed an empty list of windows (the self-test never lets this page see the real desktop).</param>
    /// <param name="helpers">What the hooks reported (WORK-ORDER-11 section 3); none until then.</param>
    public TerminalsPage(AppWorld world, ITerminalProbe? probe, TerminalTables tables, bool readsWindows, HelperSessions? helpers = null, bool synchronousIcons = false)
    {
        _world = world;
        _probe = probe;
        _tables = tables;
        _page = new TerminalPage(tables);
        _readsWindows = readsWindows;
        _helpers = helpers;
        _synchronousIcons = synchronousIcons;
        world.Changed += OnWorldChanged;
        if (helpers is not null) helpers.Changed += OnWorldChanged;
        Recompute();
    }

    /// <summary>Raised (from whatever thread) when the tiles changed.</summary>
    public event Action? Changed;

    /// <summary>The tiles as they are known now, in page order. Never reads anything.</summary>
    public IReadOnlyList<Item> Items
    {
        get
        {
            lock (_lock) return _items;
        }
    }

    /// <summary>How many times the console windows and the process list were read (for the self-test and its guard of "only while laid out").</summary>
    public int ProbeCount => Volatile.Read(ref _probes);

    /// <summary>True while the page is laid out: the island is showing it. The reading of the process list runs only then.</summary>
    public bool LaidOut
    {
        get
        {
            lock (_lock) return _laidOut;
        }
    }

    /// <summary>The page came up (true) or went away (false: another page, the island left). Reads at once when it comes up, then every <see cref="ProbePeriod"/>.</summary>
    public void SetLaidOut(bool on)
    {
        lock (_lock)
        {
            if (_laidOut == on) return;
            _laidOut = on;
            if (!on)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }

            if (_probe is not null) _timer = new Timer(_ => ProbeOnce(), null, TimeSpan.Zero, ProbePeriod);
        }
    }

    /// <summary>Reads the console windows and the process list once, on the calling thread, and works the tiles out again (the timer calls this; the self-test too).</summary>
    public void ProbeOnce()
    {
        if (_probe is null) return;
        TerminalProbeFacts facts;
        try
        {
            facts = _probe.Read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return; // a reading that failed leaves what was known
        }

        Interlocked.Increment(ref _probes);
        lock (_lock) _facts = facts;
        if (_helpers is not null)
        {
            // The helpers found by name are told to the sessions, which settle the process each hangs on and end the ones whose process is gone (WORK-ORDER-11 section 3).
            // This runs on a timer thread, where an exception ends the app: a reading that cannot be settled leaves what was known.
            try
            {
                var page = PageWindows.From(WindowFacts(), _tables);
                _helpers.ApplyReading(facts.Processes, HelperFinder.FindByName(facts.Processes, page, _tables), [.. page.AiWindows.Select(w => w.OwnerProcessId).Distinct()]);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
            }
        }

        Recompute();
    }

    /// <summary>The window's handle of the tile at an index, or null (the + tile does not exist here, and every tile has one).</summary>
    public long? WindowAt(int index)
    {
        var items = Items;
        return index >= 0 && index < items.Count ? items[index].WindowKey : null;
    }

    public void Dispose()
    {
        _world.Changed -= OnWorldChanged;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _laidOut = false;
        }
    }

    private void OnWorldChanged() => Recompute();

    private void Recompute()
    {
        IReadOnlyList<Item> next;
        lock (_lock)
        {
            // Windows first seen are put in order while the island is away too: the list of windows is already kept, so this costs nothing but this call.
            // The console windows and the process list are the last ones read, and are only read while the page is up.
            var reading = new TerminalReading(WindowFacts(), _facts.ConsoleWindows, _facts.Processes, _helpers?.Facts() ?? []);
            next = [.. _page.Read(reading).Select(ItemOf)];
            if (next.SequenceEqual(_items)) return;
            _items = next;
        }

        Changed?.Invoke();
    }

    /// <summary>The windows the island already lists, as plain facts; none under the self-test (the page never sees the real desktop).</summary>
    private List<TermWindowFact> WindowFacts() =>
        _readsWindows
            ? [.. _world.Windows.Windows.Select(w => new TermWindowFact(w.Handle, w.OwnerProcessId, w.ExeName, w.PackageFamily, w.ClassName ?? string.Empty, w.Title, w.ZOrder))]
            : [];

    private Item ItemOf(TerminalTile tile)
    {
        var face = tile.Face;
        IconImage? icon = face.Kind == FaceKind.ProgramIcon ? IconOf(face.ExeName, face.PackageFamily) : null;
        var hue = PickItems.Hue(face.ExeName ?? face.PackageFamily ?? face.Letters);
        return new Item(tile.FirstLine, tile.SecondLine, face.Letters, hue, PickId: null, Icon: icon, IsClosed: false, Count: tile.Dots, IsPlus: false,
            WindowKey: tile.WindowHandle, Ring: tile.Ring, Disc: face.Kind == FaceKind.HelperDisc ? face.Disc : null);
    }

    /// <summary>The program's icon: read once, off the drawing thread (the tile shows its letters until it arrives); the page is worked out again when it does.</summary>
    private IconImage? IconOf(string? exe, string? package)
    {
        var key = $"{exe}|{package}";
        if (exe is null && package is null) return null;
        if (_icons.TryGetValue(key, out var known)) return known;
        if (!_requested.Add(key) && !_misses.ShouldRetry(key)) return null;
        if (_synchronousIcons)
        {
            Fetch(key, exe, package, recompute: false);
            return _icons.GetValueOrDefault(key);
        }

        Task.Run(() => Fetch(key, exe, package, recompute: true));
        return null;
    }

    private void Fetch(string key, string? exe, string? package, bool recompute)
    {
        IconImage? icon = null;
        try
        {
            icon = _world.Icons.ProgramIcon(exe, package);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // An icon that cannot be read is not shown: the tile keeps its letters.
        }

        if (icon is not null) RoundIcons.Of(icon); // made here, off the drawing thread, as the picks' icons are
        if (icon is null) { _misses.Missed(key); return; }

        lock (_lock) _icons[key] = icon;
        if (recompute) Recompute();
    }
}
