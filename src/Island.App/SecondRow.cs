using Island.App.Visuals;
using Island.Core;
using Page = Island.Core.Page;

namespace Island.App;

/// <summary>
/// WORK-ORDER-5 §5: the second row that the + opens inside the island. It shows what is open right now and is not on the
/// island: a click on a tile jumps to that thing once and adds nothing; its small + adds it to the page being shown, and it
/// leaves the second row and appears in the first. Pointing at a tile names it in the text block. What the row shows is on the
/// screen only; nothing of it reaches a file. A pick that is added is stored like any other.
/// </summary>
internal sealed class SecondRow
{
    private readonly ContentsLayer _layer;
    private readonly IslandController _controller;
    private readonly PickBook _book;
    private readonly AppWorld _world;
    private readonly string? _ownExe;
    private IReadOnlyList<PlusEntry> _entries = [];

    private readonly Dictionary<string, IconImage?> _icons = [];
    private readonly HashSet<string> _iconRequested = [];
    private readonly IconMisses _misses = new();
    private readonly System.Windows.Threading.Dispatcher _ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;

    public SecondRow(ContentsLayer layer, IslandController controller, PickBook book, AppWorld world, string? ownExe)
    {
        _layer = layer;
        _controller = controller;
        _book = book;
        _world = world;
        _ownExe = ownExe;
        layer.RowTileClicked += Jump;
        layer.RowAddClicked += Add;
        layer.RowTileHovered += Hover;
        layer.RowUsed += _controller.Activity;
        controller.SecondRowOpened += () => Refresh(force: true);
    }

    private Page Page => _controller.Machine.ContentsPage;

    /// <summary>The keys of the things the row shows now, in order (for the self-test).</summary>
    public IReadOnlyList<string> Keys => [.. _entries.Select(e => e.Key)];

    /// <summary>Reads what is open again and draws the row. Called when the row opens, when the first row was drawn again while it is open, and when something opened or closed.</summary>
    public void Refresh(bool force = true)
    {
        var entries = PlusRow.Entries(_world.Snapshot(), _book.Store, _world.Catalog.Installed, _ownExe);
        var tiles = entries.Select(e => new RowTile(e.Key, new Item(e.Name, PlusRow.HoverSubtitle, PickItems.Mark(e.Name), PickItems.Hue(e.Key), Icon: IconOf(e)))).ToList();
        var same = !force && tiles.Count == _shown.Count && tiles.Zip(_shown).All(p => p.First.Key == p.Second.Key && p.First.Item.Title == p.Second.Item.Title && ReferenceEquals(p.First.Item.Icon, p.Second.Item.Icon));
        _entries = entries;
        if (same) return; // what is open did not change as far as the row can tell: it is not drawn again, nobody loses their place
        _shown = tiles;
        _layer.SetSecondRow(tiles);
    }

    private IReadOnlyList<RowTile> _shown = [];

    /// <summary>What is open changed while the row is showing: it is drawn again.</summary>
    public void RefreshIfOpen()
    {
        if (_controller.Machine.SecondRowOpen && _controller.Machine.Phase == IslandPhase.Open) Refresh(force: false);
    }

    private void Hover(int? index)
    {
        if (index is { } i && i >= 0 && i < _entries.Count) _layer.ShowHoverText(_entries[i].Name, PlusRow.HoverSubtitle);
        else _layer.ShowHoverText(null, null);
    }

    /// <summary>The tile of an open thing, with its icon when it is known (read in the background otherwise): the second row's and search's.</summary>
    public Item ItemOf(PlusEntry entry) => new(entry.Name, PlusRow.HoverSubtitle, PickItems.Mark(entry.Name), PickItems.Hue(entry.Key), Icon: IconOf(entry));

    /// <summary>A click on a tile of the second row: jumps to that thing once. Nothing is added; the row stays.</summary>
    public void Jump(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        JumpTo(_entries[index]);
        _controller.Activity();
    }

    /// <summary>Jumps to an open thing once: the window or the tab a click on its tile goes to.</summary>
    public void JumpTo(PlusEntry entry)
    {
        if (PlusRow.JumpTarget(entry) is { } target)
        {
            if (entry.Kind == PickKind.Site)
            {
                if (_world.Snapshot().Tabs.FirstOrDefault(t => t.LastActiveOrder == target) is { } tab) _world.TabControl.Activate(tab.Key);
            }
            else
            {
                _world.Outside.BringForward(target);
            }
        }
    }

    /// <summary>The small + of a tile: puts the thing on the page being shown.</summary>
    public void Add(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        if (_entries[index].ToPick(Page.Id) is { } pick) _book.Add(pick);
        _controller.Activity();
    }

    // The icon of an entry is read in the background; the row opens at once with letters and is drawn again when it arrives.
    private IconImage? IconOf(PlusEntry entry)
    {
        if (_icons.TryGetValue(entry.Key, out var known)) return known;
        if (!_iconRequested.Add(entry.Key) && !_misses.ShouldRetry(entry.Key)) return null;

        _ = Task.Run(() =>
        {
            IconImage? icon = null;
            try
            {
                icon = entry.Kind switch
                {
                    PickKind.Program => _world.Icons.ProgramIcon(entry.ExeName, entry.PackageFamily),
                    PickKind.Folder when entry.KnownFolder is not null => _world.Icons.FolderIcon(entry.KnownFolder),
                    _ => null,
                };
                if (icon is not null) RoundIcons.Of(icon);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // no icon: the tile keeps its letters
            }

            _ui.BeginInvoke(() =>
            {
                if (icon is null) { _misses.Missed(entry.Key); return; }

                _icons[entry.Key] = icon;
                RefreshIfOpen();
            });
        });
        return null;
    }
}
