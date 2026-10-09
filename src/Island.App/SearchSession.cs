using Island.App.Visuals;
using Island.Core;

namespace Island.App;

/// <summary>One thing search can offer: a pick (from any page) or something that is open and is not a pick, with the tile that stands for it.</summary>
/// <param name="Open">The open thing, for a thing that is not a pick; null for a pick.</param>
internal sealed record SearchEntry(string Name, string Key, bool IsPick, bool IsClosed, Item Item, PlusEntry? Open = null);

/// <summary>
/// Search in the island (WORK-ORDER-7 section 3): joins the pure rules of Island.Core (<see cref="SearchMatch"/>, <see cref="SearchServices"/>,
/// <see cref="SearchKeys"/>) to the island. It reads keys the island's own window receives (no hook), works out the matches at each change of the
/// text, tells the machine how wide the capsule has to be and the view what to draw. What is typed lives in <see cref="SearchState"/> and the view only:
/// it is never passed to a logger, a file or a snapshot. Enter does what a click on the selected tile does.
/// </summary>
internal sealed class SearchSession
{
    private readonly IslandController _controller;
    private readonly Func<IReadOnlyList<SearchEntry>> _entries;
    private readonly Func<IReadOnlyList<string>> _played;
    private readonly Action<SearchEntry> _activate;
    private readonly Action<SearchServiceTile, string> _activateService;
    private SearchState _state = SearchState.Closed;
    private IReadOnlyList<SearchEntry> _matches = [];
    private IReadOnlyList<SearchServiceTile> _services = [];

    public SearchSession(IslandController controller, Func<IReadOnlyList<SearchEntry>> entries, Func<IReadOnlyList<string>> played, Action<SearchEntry> activate, Action<SearchServiceTile, string> activateService)
    {
        _controller = controller;
        _entries = entries;
        _played = played;
        _activate = activate;
        _activateService = activateService;
    }

    public bool IsOpen => _state.IsOpen;

    /// <summary>How many tiles there are now: the matches and the service's tile (for the self-test).</summary>
    public int TileCount => _state.TileCount;

    /// <summary>The selected tile's index (for the self-test).</summary>
    public int Selected => _state.Selected;

    /// <summary>Forgets everything (the island left, or a page's key ended search).</summary>
    public void Reset()
    {
        if (!_state.IsOpen && _matches.Count == 0) return;
        _state = SearchState.Closed;
        _matches = [];
        _services = [];
        _controller.SetSearchData(null);
    }

    /// <summary>A key of the island's own window by its virtual-key code. True when search took it; false leaves it to the island as it would be without search.</summary>
    public bool Key(int virtualKey, IslandMachine machine, double now)
    {
        var key = virtualKey switch
        {
            0x25 => SearchKey.Left,
            0x27 => SearchKey.Right,
            0x0D => SearchKey.Enter,
            0x08 => SearchKey.Backspace,
            0x1B => SearchKey.Escape,
            >= 0x30 and <= 0x39 => SearchKey.Typed(((char)virtualKey).ToString()),
            >= 0x60 and <= 0x69 => SearchKey.Typed(((char)(virtualKey - 0x60 + '0')).ToString()),
            _ => (SearchKey?)null,
        };
        return key is { } k && Apply(k, machine, now);
    }

    /// <summary>Text typed into the window. Opens search when the island has the keyboard and the character is not a digit.</summary>
    public bool Text(string text, IslandMachine machine, double now)
    {
        if (!_state.IsOpen && !(machine.HasKeyboard && machine.Phase == IslandPhase.Open && !machine.ShowsPill)) return false;
        return Apply(SearchKey.Typed(text), machine, now);
    }

    /// <summary>The pill's search button: the pill grows into the capsule already in search, and the keyboard is asked for inside the click's own handler.</summary>
    public void OpenFromPill(IslandMachine machine, double now)
    {
        _state = new SearchState(true, string.Empty, 0, 0);
        Recompute(machine, now, opening: true, fromPill: true);
    }

    /// <summary>A click on a tile (its index among all the tiles): does what a click on it does, then search is over.</summary>
    public void ActivateTile(int index, IslandMachine machine, double now)
    {
        if (!_state.IsOpen || index < 0 || index >= _state.TileCount) return;
        Do(index);
        Close(machine, now);
    }

    private bool Apply(SearchKey key, IslandMachine machine, double now)
    {
        var result = SearchKeys.Apply(_state, key);
        switch (result.Action)
        {
            case SearchKeyAction.None:
                return false;
            case SearchKeyAction.SwitchPage:
                if (!_state.IsOpen) return false; // search is closed: the island's own digit does it
                Close(machine, now);
                machine.DigitKey(result.Page, now);
                return true;
            case SearchKeyAction.Opened:
                _state = result.State;
                Recompute(machine, now, opening: true, fromPill: false);
                return true;
            case SearchKeyAction.TextChanged:
                _state = result.State;
                Recompute(machine, now, opening: false, fromPill: false);
                return true;
            case SearchKeyAction.SelectionChanged:
                _state = result.State;
                machine.OtherKey(now);
                Push(machine);
                return true;
            case SearchKeyAction.Activate:
                _state = result.State;
                Do(_state.Selected);
                Close(machine, now);
                return true;
            case SearchKeyAction.Leave:
                Close(machine, now);
                return true;
            default:
                return false;
        }
    }

    private void Do(int index)
    {
        if (index >= _matches.Count && index - _matches.Count < _services.Count) _activateService(_services[index - _matches.Count], _state.Text);
        else if (index >= 0 && index < _matches.Count) _activate(_matches[index]);
    }

    private void Close(IslandMachine machine, double now)
    {
        machine.CloseSearch(now);
        _state = SearchState.Closed;
        _matches = [];
        _services = [];
        _controller.SetSearchData(null);
    }

    /// <summary>The matches for the text now: picks, then what is open and is no pick; then the service's tile.</summary>
    private void Recompute(IslandMachine machine, double now, bool opening, bool fromPill)
    {
        var entries = _entries();
        var byKey = entries.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First());
        var ranked = SearchMatch.Rank(entries.Select(e => new SearchCandidate(e.Name, e.Key, e.IsPick, e.IsClosed)), _state.Text);
        _matches = [.. ranked.Select(c => byKey[c.Key])];
        _services = SearchServices.Tiles(_played(), _state.Text);
        _state = _state with { Selected = 0, TileCount = _matches.Count + _services.Count };

        var width = SearchLayout.Width(_state.Text.Length, _state.TileCount);
        if (opening) machine.OpenSearch(width, now, keyboard: fromPill);
        else machine.SetSearchWidth(width, now);
        machine.OtherKey(now);
        Push(machine);
    }

    private void Push(IslandMachine machine)
    {
        var tiles = _matches.Select(m => m.Item).ToList();
        foreach (var tile in _services) tiles.Add(new Item(tile.Name, "Enter to open", IconChoice.TwoLetterMark(SearchServices.DisplayName(tile.Service)), 200, IsClosed: false));
        var selected = Math.Clamp(_state.Selected, 0, Math.Max(0, tiles.Count - 1));
        var (title, subtitle) = tiles.Count > 0
            ? (tiles[selected].Title, "Enter to open")
            : _state.Text.Length > 0 ? ("Nothing found", string.Empty) : ("Type to search", string.Empty);
        _controller.SetSearchData(new SearchViewData(_state.Text, tiles, selected, NeedsClick: false, title, subtitle, _services.Count));
    }
}
