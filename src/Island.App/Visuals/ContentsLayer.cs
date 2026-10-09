using Page = Island.Core.Page;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>How one element of the contents looks at one moment of its entrance or exit.</summary>
internal readonly record struct Pose(double Opacity, double Rise, double Scale, double BlurRadius)
{
    public static Pose Shown { get; } = new(1, 0, 1, 0);
    public static Pose Hidden { get; } = new(0, LookConstants.ContentsRise, LookConstants.ContentsStartScale, Units.RadiusForFilterBlur(LookConstants.ContentsBlurStart));
}

/// <summary>One tile of the second row: a thing that is open and not on the island. <paramref name="Key"/> is the stable key of the thing; the item carries its name, letters, colour and icon.</summary>
internal sealed record RowTile(string Key, Item Item);

/// <summary>
/// The row inside the capsule: category chip, item circles, title and subtitle, controls.
/// Positions follow the reference's flex row. Built per category; element 0 is the chip, then one
/// per item, then the text block, then the controls.
/// </summary>
internal sealed class ContentsLayer : Canvas
{
    // Vertical centre of the row: the reference's content box starts inside the 1 px border.
    private const double RowCentre = LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth;
    private const double RowStart = 19;
    private const double TitleLineHeight = LookConstants.TitleFontSize * 1.45;
    private const double SubtitleLineHeight = LookConstants.SubtitleFontSize * 1.45;

    private static readonly FontFamily Font = new($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}");

    private readonly List<Reveal> _reveals = [];
    private readonly List<TileView> _tiles = [];
    private readonly List<Reveal> _tileReveals = []; // the entrance of each tile of the first row, in the tiles' order (the other elements have theirs in _reveals too)
    private readonly TranslateTransform _origin = new();
    private StripView? _strip;
    private int _visiblePicks;
    private double _stripPosition;

    // The second row ("Open now"), built when the + was pressed.
    private Canvas? _row2;
    private StripView? _row2Strip;
    private readonly StripScroll _row2Scroll = new(0, 0);
    private readonly List<TileView> _row2Tiles = [];
    private string? _hoverTitle;
    private string? _hoverSubtitle;
    private PageContents _contents = Pages.AllPlaceholders[0];
    private Rgb _colour = Rgb.FromHex(LookConstants.MediaColor);
    private TextBlock? _title;
    private TextBlock? _subtitle;
    private int _selected;
    private NowPlayingView? _nowPlaying;
    private Grid? _middleButton;
    private readonly List<Grid> _mediaButtons = [];
    private Grid? _closeButton;
    private bool _closeReady;
    private string? _closeNote;
    private string? _sceneTitle;
    private string? _sceneSubtitle;

    public ContentsLayer()
    {
        RenderTransform = _origin;
        IsHitTestVisible = true;
    }

    /// <summary>
    /// The raw pointer on a tile of the first row, for the drag (WORK-ORDER-5 §6): press (index and the pointer's place in the
    /// window), move, release, and the loss of the mouse. A click is decided from these by the drag handler, on the release.
    /// </summary>
    public event Action<int, Point>? TilePressed;

    public event Action<int, Point>? TileMoved;

    public event Action<int, Point>? TileReleased;

    public event Action<int>? TileMouseLost;

    /// <summary>What pointer positions are measured from: the window-sized overlay of the island's view.</summary>
    public UIElement? PointerReference { get; set; }

    /// <summary>How many tiles the first row has (for the self-test and the drag).</summary>
    public int TileCount => _tiles.Count;

    /// <summary>Raised when an arrow of the sliding row is clicked (true: the right one).</summary>
    public event Action<bool>? ArrowClicked;

    /// <summary>Raised when a tile of the second row is clicked (jump to it once), with its index.</summary>
    public event Action<int>? RowTileClicked;

    /// <summary>Raised when the small + of a tile of the second row is clicked (add it to the page being shown), with its index.</summary>
    public event Action<int>? RowAddClicked;

    /// <summary>Raised when the pointer moves onto a tile of the second row (its index) or off it (null).</summary>
    public event Action<int?>? RowTileHovered;

    /// <summary>Raised when something in the second row was used (an arrow): it counts as use.</summary>
    public event Action? RowUsed;

    /// <summary>Raised when previous, play/pause or next is clicked on the Media page.</summary>
    public event Action<MediaCommand>? ControlClicked;

    /// <summary>Raised when the X of a page that is not Media is clicked (WORK-ORDER-6 section 5). Whether it may do anything is decided elsewhere.</summary>
    public event Action? CloseClicked;

    /// <summary>Raised when the text block is clicked while it shows what is playing.</summary>
    public event Action? TextClicked;

    public int ElementCount => _reveals.Count;

    /// <summary>Where the centre of a tile is, in window coordinates (to hang a panel under it).</summary>
    public double TileCentreX(int index)
    {
        var isPlus = index >= 0 && index < _contents.Items.Count && _contents.Items[index].IsPlus;
        return _origin.X + RowStart + LookConstants.ChipSize + LookConstants.WidthChipGap
               + StripLayout.TileLeft(index, isPlus, _stripPosition, _visiblePicks) + LookConstants.ItemSize / 2;
    }

    /// <summary>The first text line as drawn now (for the self-test).</summary>
    public string TitleText => _title?.Text ?? string.Empty;

    /// <summary>The second text line as drawn now (for the self-test).</summary>
    public string SubtitleText => _subtitle?.Text ?? string.Empty;

    /// <summary>"play" or "pause": the glyph of the middle button of the Media page (for the self-test); empty on other pages.</summary>
    public string MiddleGlyph => _middleButton is null ? string.Empty : _nowPlaying?.IsPaused == true ? "play" : "pause";

    public PageContents Contents => _contents;

    /// <summary>Left edge of the contents for a capsule centred on <paramref name="centreX"/>, in window coordinates.</summary>
    public static double LeftEdge(PageContents contents, double centreX) => centreX - CapsuleLayout.SizeFor(contents).Width / 2;

    /// <summary>Where the title text sits, relative to the capsule's top-left corner.</summary>
    public static Rect TitleRect(PageContents contents)
    {
        var n = StripLayout.ShownTiles(contents.Items);
        var x = RowStart + LookConstants.ChipSize + LookConstants.WidthChipGap + (n * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap) + LookConstants.WidthItemsToTextGap;
        return new Rect(x, RowCentre - (TitleLineHeight + SubtitleLineHeight) / 2, LookConstants.WidthTextBlock, TitleLineHeight);
    }

    /// <summary>
    /// The tile that is playing dances: the bars cover the tile at <paramref name="index"/> (a negative index: no tile is playing
    /// here), with these heights; every other tile has none.
    /// </summary>
    public void SetEqualizer(int index, IReadOnlyList<double>? heights)
    {
        for (var i = 0; i < _tiles.Count; i++) _tiles[i].SetEqualizer(i == index ? heights : null);
    }

    /// <summary>The tile at an index of the row as drawn now (for the self-test).</summary>
    internal TileView TileAt(int index) => _tiles[index];

    private bool _built;

    public void Build(PageContents contents, double centreX, int selected, Rgb colour)
    {
        // A pick that opened or closed since the row was drawn last on this page cross-fades from how it looked (WORK-ORDER-5 §2).
        var before = _built && _contents.Page.Id == contents.Page.Id
            ? _contents.Items.Where(i => i.PickId is not null).GroupBy(i => i.PickId!).ToDictionary(g => g.Key, g => g.First().IsClosed)
            : [];
        _built = true;
        Children.Clear();
        _reveals.Clear();
        _tiles.Clear();
        _tileReveals.Clear();
        _contents = contents;
        if (!contents.Page.IsMedia) _nowPlaying = null;
        _colour = colour;
        _selected = Math.Clamp(selected, 0, Math.Max(0, contents.Items.Count - 1));
        _origin.X = LeftEdge(contents, centreX);

        var x = RowStart;
        Add(MakeChip(contents.Page), x, RowCentre - LookConstants.ChipSize / 2, LookConstants.ChipSize, LookConstants.ChipSize);
        x += LookConstants.ChipSize + LookConstants.WidthChipGap;

        // The picks lie in a sliding row cut off at its left and right ends only; the + tile stays in the slot after them.
        var picks = StripLayout.PickCount(contents.Items);
        _visiblePicks = StripLayout.VisiblePicks(picks);
        _strip = new StripView(_visiblePicks, RowCentre, LookConstants.CapsuleHeight);
        _strip.ArrowClicked += right => ArrowClicked?.Invoke(right);
        SetLeft(_strip, x - StripView.Slack);
        SetTop(_strip, 0);
        Children.Add(_strip);

        var plusDrawn = false;
        for (var i = 0; i < contents.Items.Count; i++)
        {
            var item = contents.Items[i];
            var tile = NewRowTile(item, i, item.PickId is { } id && before.TryGetValue(id, out var wasClosed) ? wasClosed : null);
            _tiles.Add(tile);
            var top = RowCentre - LookConstants.ItemSize / 2;
            if (item.IsPlus && plusDrawn) continue; // the tile exists (indexes stay) but is not drawn: a page has one + tile
            if (item.IsPlus)
            {
                plusDrawn = true;
                // Never through the entrance blur (Dan, 1.0.1): its thin white lines looked thicker while drawn through the effect and thinner the frame it was taken off, about half a second in.
                Add(tile, x + _visiblePicks * LookConstants.WidthItemPitch, top, LookConstants.ItemSize, LookConstants.ItemSize, blurs: false);
            }
            else
            {
                var reveal = new Reveal(tile, LookConstants.ItemSize, LookConstants.ItemSize);
                _strip.AddTile(reveal.Host, i, top);
                _reveals.Add(reveal);
                _tileReveals.Add(reveal);
                reveal.Apply(Pose.Shown);
            }
        }

        var shown = _visiblePicks + StripLayout.PlusCount(contents.Items);
        x += (shown > 0 ? shown * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap : -LookConstants.WidthItemTrailingGap) + LookConstants.WidthItemsToTextGap;
        var text = MakeText();
        Add(text, x, RowCentre - (TitleLineHeight + SubtitleLineHeight) / 2, LookConstants.WidthTextBlock, TitleLineHeight + SubtitleLineHeight);
        x += LookConstants.WidthTextBlock + LookConstants.WidthTextToControlsGap;

        var controls = MakeControls(contents.Page);
        Add(controls, x, RowCentre - LookConstants.ControlSize / 2, controls.Width, LookConstants.ControlSize);

        _stripPosition = 0;
        _row2 = null;
        _row2Strip = null;
        _row2Tiles.Clear();
        _hoverTitle = _hoverSubtitle = null;

        ApplySelection();
        ApplyColour();
        ApplyNowPlaying();
    }

    private TileView NewRowTile(Item item, int index, bool? fadeFromClosed)
    {
        var tile = new TileView(item, index, fadeFromClosed: fadeFromClosed);
        tile.RawPointer = true;
        tile.PointerReference = PointerReference;
        tile.Pressed += (i, at) => TilePressed?.Invoke(i, at);
        tile.Moved += (i, at) => TileMoved?.Invoke(i, at);
        tile.Released += (i, at) => TileReleased?.Invoke(i, at);
        tile.MouseLost += i => TileMouseLost?.Invoke(i);
        return tile;
    }

    /// <summary>
    /// The page that fills itself changed (WORK-ORDER-11 section 1): when the same windows stand in the same places, only the tiles whose item changed (a
    /// title, a line, a ring, dots, an icon that arrived) are made again, each in its own place, and nothing else is touched. False when the windows or their
    /// number changed: the row has to be built again.
    /// </summary>
    public bool TryUpdateInPlace(IReadOnlyList<Item> items)
    {
        if (!_built || _contents.Page.Id != PageIds.Terminals || items.Count != _tiles.Count || items.Count != _tileReveals.Count) return false;
        for (var i = 0; i < items.Count; i++)
            if (items[i].WindowKey is null || items[i].WindowKey != _contents.Items[i].WindowKey) return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] == _contents.Items[i]) continue;
            var tile = NewRowTile(items[i], i, null);
            tile.SetColour(_colour);
            var host = _tileReveals[i].Host;
            host.Children.Clear();
            host.Children.Add(tile);
            _tiles[i] = tile;
        }

        _contents = new PageContents(_contents.Page, items);
        ApplySelection();
        ApplyColour();
        return true;
    }

    /// <summary>
    /// The Media page's text block says what is playing (title, and where it plays under it), and its middle button
    /// shows play when that is paused. Null shows the selected pick as on every other page.
    /// </summary>
    public void SetNowPlaying(NowPlayingView? view)
    {
        _nowPlaying = _contents.Page.IsMedia ? view : null;
        ApplySelection();
        ApplyNowPlaying();
    }

    private void ApplyNowPlaying()
    {
        if (_middleButton is not null)
        {
            var glyph = _nowPlaying?.IsPaused == true ? "play" : "pause";
            _middleButton.Children.Clear();
            _middleButton.Children.Add(Icons.Create(glyph, LookConstants.ControlGlyphSize, Brushes.White));
        }

        var dim = _nowPlaying is { CanControl: false };
        foreach (var b in _mediaButtons) b.Opacity = dim ? LookConstants.DimmedControlOpacity : 1;
    }

    /// <summary>
    /// The X is drawn at full strength only when it is ready, and dim otherwise (it does nothing then). A note replaces the second text line
    /// while it is given ("runs as administrator").
    /// </summary>
    public void SetCloseState(bool ready, string? note)
    {
        _closeReady = ready;
        _closeNote = note;
        ApplyClose();
        ApplySelection();
    }

    /// <summary>Whether the X is drawn at full strength now (for the self-test).</summary>
    public bool CloseDrawnReady => _closeButton is { Opacity: >= 1 };

    /// <summary>True when the page has an X (every page but Media).</summary>
    public bool HasCloseButton => _closeButton is not null;

    /// <summary>The click handler of the X, as the mouse reaches it (for the self-test, which presses no mouse button).</summary>
    internal void RaiseCloseForSelfTest() => CloseClicked?.Invoke();

    private void ApplyClose()
    {
        if (_closeButton is not null) _closeButton.Opacity = _closeReady && _contents.Page.Id != PageIds.Terminals ? 1 : LookConstants.DimmedControlOpacity; // the X never does anything on the Terminals page
    }

    /// <summary>
    /// After a scene ran (WORK-ORDER-7 section 5) the text block reads the scene's name and "&lt;n&gt; opened" until the selection moves or the island
    /// leaves. A page being built again does not take it away: the island may still be on its way in.
    /// </summary>
    public void SetSceneNote(string? title, string? subtitle)
    {
        _sceneTitle = title;
        _sceneSubtitle = subtitle;
        ApplySelection();
    }

    public void SetSelected(int index)
    {
        if (index == _selected || index < 0 || index >= _tiles.Count) return;
        _selected = index;
        _sceneTitle = _sceneSubtitle = null;
        ApplySelection();
    }

    public void SetColour(Rgb colour)
    {
        if (colour == _colour) return; // drawn on every frame: a brush is made again for every tile only when the colour moved
        _colour = colour;
        ApplyColour();
    }

    /// <summary>True when a tile of the first row has a working arc to turn (the controller sets the arcs' clock on a frame only then).</summary>
    public bool HasTurningArcs => _tiles.Any(t => t.HasTurningArc);

    /// <summary>Turns the working arcs to the island's clock.</summary>
    public void SetRingClock(double seconds)
    {
        foreach (var tile in _tiles) tile.SetRingClock(seconds);
    }

    public void SetTop(double top) => _origin.Y = top;

    public void SetPose(int element, Pose pose)
    {
        if (element >= 0 && element < _reveals.Count) _reveals[element].Apply(pose);
    }

    public void SetAllPoses(Pose pose)
    {
        foreach (var r in _reveals) r.Apply(pose);
    }

    /// <summary>For the contrast measurement: the text can be switched off to see the glass behind it.</summary>
    public void SetTextVisible(bool visible)
    {
        if (_title is not null) _title.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (_subtitle is not null) _subtitle.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>Slides the picks: <paramref name="position"/> is how many tiles they have moved from the first pick at the left. Called on every drawn frame.</summary>
    public void SetStripPosition(double position, bool hiddenLeft, bool hiddenRight)
    {
        _stripPosition = position;
        _strip?.SetPosition(position, hiddenLeft, hiddenRight);
    }

    /// <summary>The click handler of an arrow, as the mouse reaches it (for the self-test, which presses no mouse button).</summary>
    internal void RaiseArrowForSelfTest(bool right) => _strip?.RaiseArrowForSelfTest(right);

    /// <summary>Whether the left or right arrow is drawn now (for the self-test).</summary>
    public (bool Left, bool Right) ArrowsShown => _strip?.ArrowsShown ?? (false, false);

    /// <summary>Which picks lie inside the sliding row as drawn now, first and last index (for the self-test).</summary>
    public (int First, int Last) VisibleRange
    {
        get
        {
            var first = (int)Math.Ceiling(_stripPosition - 0.0001);
            return (first, first + _visiblePicks - 1);
        }
    }

    // ---- The second row ------------------------------------------------------------------------

    private const double SecondRowTop = LookConstants.BorderWidth + ChoiceConstants.RowHeight;
    private const double SecondRowCentre = SecondRowTop + ChoiceConstants.RowHeight / 2 - 1;

    /// <summary>
    /// Builds the second row under the first (WORK-ORDER-5 §5): the line between the rows, the words "Open now", then one tile
    /// for each thing that is open and not on the island, each with a small + at its upper right. Nothing else open: the row
    /// reads "Nothing else is open". More tiles than fit slide, as in the first row. Starts invisible: the controller
    /// fades it in with <see cref="SetSecondRowOpacity"/>.
    /// </summary>
    public void SetSecondRow(IReadOnlyList<RowTile> tiles)
    {
        var again = _row2 is not null; // drawn again while it is open: it keeps its place and stays visible
        var opacity = _row2?.Opacity ?? 0;
        if (_row2 is not null) Children.Remove(_row2);
        _hoverTitle = _hoverSubtitle = null;
        ApplySelection();
        _row2Tiles.Clear();
        var width = CapsuleLayout.SizeFor(_contents).Width;
        var row = new Canvas { Width = width, Height = ChoiceConstants.RowHeight + 2 * LookConstants.BorderWidth + 1, Opacity = 0, IsHitTestVisible = false };

        var line = new Rectangle { Width = width - 2 * (RowStart + 1), Height = 1, Fill = Paint.Brush(Rgb.White, ChoiceConstants.RowLineAlpha), IsHitTestVisible = false };
        SetLeft(line, RowStart + 1);
        SetTop(line, SecondRowTop);
        row.Children.Add(line);

        var label = Label(ChoiceConstants.RowLabelSize, FontWeights.Normal, Paint.Brush(Rgb.White, ChoiceConstants.RowLabelAlpha), ChoiceConstants.RowLabelSize * 1.45);
        label.Text = tiles.Count == 0 ? PlusRow.NothingElse : PlusRow.Label;
        label.TextTrimming = TextTrimming.None;
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(label, RowStart);
        SetTop(label, SecondRowCentre - label.DesiredSize.Height / 2);
        row.Children.Add(label);

        if (tiles.Count > 0)
        {
            var startX = RowStart + label.DesiredSize.Width + LookConstants.WidthChipGap;
            var fit = Math.Max(1, StripLayout.TilesThatFit(width - 20 - startX));
            _row2Scroll.SetMaxVisible(fit);
            _row2Scroll.SetCount(tiles.Count);
            if (!again) _row2Scroll.Reset(-1);
            var strip = new StripView(_row2Scroll.Visible, SecondRowCentre, row.Height);
            strip.ArrowClicked += right =>
            {
                if (right) _row2Scroll.ArrowRight();
                else _row2Scroll.ArrowLeft();
                RowUsed?.Invoke();
            };
            SetLeft(strip, startX - StripView.Slack);
            SetTop(strip, 0);
            row.Children.Add(strip);
            _row2Strip = strip;

            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = new TileView(tiles[i].Item, i, addBadge: true) { PointerReference = PointerReference };
                tile.SetSelected(false, _colour);
                tile.Clicked += index => RowTileClicked?.Invoke(index);
                tile.AddClicked += index => RowAddClicked?.Invoke(index);
                var at = i;
                tile.MouseEnter += (_, _) =>
                {
                    tile.SetSelected(true, _colour);
                    RowTileHovered?.Invoke(at);
                };
                tile.MouseLeave += (_, _) =>
                {
                    tile.SetSelected(false, _colour);
                    RowTileHovered?.Invoke(null);
                };
                _row2Tiles.Add(tile);
                strip.AddTile(tile, i, SecondRowCentre - LookConstants.ItemSize / 2);
            }

            strip.SetPosition(0, false, _row2Scroll.HiddenRight);
        }
        else
        {
            _row2Strip = null;
        }

        _row2 = row;
        Children.Add(row);
        ApplyColour();
        if (again) SetSecondRowOpacity(opacity);
    }

    /// <summary>How visible the second row is, 0 to 1. At 0 it takes no clicks.</summary>
    public void SetSecondRowOpacity(double opacity)
    {
        if (_row2 is null) return;
        _row2.Opacity = Math.Clamp(opacity, 0, 1);
        _row2.IsHitTestVisible = opacity > 0.05;
        _row2.Visibility = opacity > 0.001 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Called on every drawn frame while the second row is open: slides its tiles.</summary>
    public void TickSecondRow(double elapsedSeconds)
    {
        if (_row2Strip is null) return;
        _row2Scroll.Tick(elapsedSeconds);
        _row2Strip.SetPosition(_row2Scroll.Position, _row2Scroll.HiddenLeft, _row2Scroll.HiddenRight);
    }

    /// <summary>The wheel turned over the second row: slides its tiles.</summary>
    public void SecondRowWheel(int delta) => _row2Scroll.Wheel(delta);

    // ---- The second row by keyboard (WORK-ORDER-10 §2) -----------------------------------------

    /// <summary>How many tiles the second row has now (0 while it is not built or has nothing).</summary>
    public int SecondRowCount => _row2Tiles.Count;

    /// <summary>True once the second row has been built (with tiles or with its words "Nothing else is open").</summary>
    public bool SecondRowIsBuilt => _row2 is not null;

    /// <summary>
    /// The selected tile of the second row (-1: none). The same look as the pointer on it: the tile is selected and the text block names it; the row slides to show it.
    /// </summary>
    public void SetSecondRowSelected(int index)
    {
        for (var i = 0; i < _row2Tiles.Count; i++) _row2Tiles[i].SetSelected(i == index, _colour);
        if (index >= 0 && index < _row2Tiles.Count) _row2Scroll.BringIntoView(index);
        RowTileHovered?.Invoke(index >= 0 && index < _row2Tiles.Count ? index : null);
    }

    /// <summary>Enter on a tile of the second row: what a click on the tile does (jump to it, add nothing).</summary>
    public void ActivateSecondRowTile(int index)
    {
        if (index >= 0 && index < _row2Tiles.Count) RowTileClicked?.Invoke(index);
    }

    /// <summary>Shift+Enter on a tile of the second row: what a click on its small + does.</summary>
    public void AddSecondRowTile(int index)
    {
        if (index >= 0 && index < _row2Tiles.Count) RowAddClicked?.Invoke(index);
    }

    private string? _removeNote;

    /// <summary>A first Delete asked about the selected pick: the text block reads its name and, under it, this note ("Delete again to remove"); null puts the normal text back.</summary>
    public void SetRemoveNote(string? note)
    {
        if (_removeNote == note) return;
        _removeNote = note;
        ApplySelection();
    }

    /// <summary>The second row's tiles as drawn now, and which are inside its row (for the self-test).</summary>
    internal (int Tiles, (int First, int Last) Range, (bool Left, bool Right) Arrows) SecondRowState
    {
        get
        {
            var first = (int)Math.Ceiling(_row2Scroll.Position - 0.0001);
            return (_row2Tiles.Count, (first, first + _row2Scroll.Visible - 1), _row2Strip?.ArrowsShown ?? (false, false));
        }
    }

    /// <summary>The text of the second row's label (for the self-test).</summary>
    internal bool SecondRowIsShown => _row2 is { Visibility: Visibility.Visible };

    internal TileView SecondRowTile(int index) => _row2Tiles[index];

    /// <summary>While the pointer is on a tile of the second row the text block names it; null puts the normal text back.</summary>
    public void ShowHoverText(string? title, string? subtitle)
    {
        _hoverTitle = title;
        _hoverSubtitle = subtitle;
        ApplySelection();
    }

    private void Add(FrameworkElement content, double x, double y, double w, double h, bool blurs = true)
    {
        var reveal = new Reveal(content, w, h, blurs);
        SetLeft(reveal.Host, x);
        SetTop(reveal.Host, y);
        Children.Add(reveal.Host);
        _reveals.Add(reveal);
        reveal.Apply(Pose.Shown);
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _tiles.Count; i++) _tiles[i].SetSelected(i == _selected, _colour);
        if (_sceneTitle is not null)
        {
            if (_title is not null) _title.Text = _sceneTitle;
            if (_subtitle is not null) _subtitle.Text = _sceneSubtitle ?? string.Empty;
            return;
        }

        if (_removeNote is not null && _selected < _contents.Items.Count && _contents.Items[_selected] is { IsPlus: false } picked)
        {
            if (_title is not null) _title.Text = picked.Title;
            if (_subtitle is not null) _subtitle.Text = _removeNote;
            return;
        }

        if (_hoverTitle is not null)
        {
            if (_title is not null) _title.Text = _hoverTitle;
            if (_subtitle is not null) _subtitle.Text = _hoverSubtitle ?? string.Empty;
            return;
        }

        if (_nowPlaying is { } playing)
        {
            if (_title is not null) _title.Text = playing.Title;
            if (_subtitle is not null) _subtitle.Text = PlayingTile.FittedSecondLine(playing, LookConstants.WidthTextBlock + 3, SubtitleWidth); // the name gives way, never "paused" (Dan's P18)
            return;
        }

        var item = _selected < _contents.Items.Count ? _contents.Items[_selected] : null;
        if (item is { IsPlus: true })
        {
            // Only the + tile on the page: nothing is picked here yet.
            var empty = _contents.Items.Count == 1;
            if (_title is not null) _title.Text = empty ? PickItems.NothingHere : item.Title;
            if (_subtitle is not null) _subtitle.Text = empty ? "press + to add something" : item.Subtitle;
            return;
        }

        if (_title is not null) _title.Text = item?.Title ?? (_contents.Items.Count == 0 ? (_contents.Page.Id == PageIds.Terminals ? PickItems.NoTerminal : PickItems.NothingHere) : _contents.Page.Name);
        if (_subtitle is not null) _subtitle.Text = _closeNote ?? item?.Subtitle ?? string.Empty;
    }

    private void ApplyColour()
    {
        foreach (var tile in _tiles) tile.SetColour(_colour);
        foreach (var tile in _row2Tiles) tile.SetColour(_colour);
        if (_chipFill is not null) _chipFill.Fill = Paint.Brush(_colour, LookConstants.ChipAlpha);
        _chipGlyph.Color = Paint.Of(ChipGlyph.For(_colour)); // white, or a deep dark on a light page colour (Dan's P22)
    }

    private Ellipse? _chipFill;
    private readonly SolidColorBrush _chipGlyph = new(Colors.White);

    private FrameworkElement MakeChip(Page page)
    {
        var grid = new Grid { Width = LookConstants.ChipSize, Height = LookConstants.ChipSize };
        _chipFill = new Ellipse { Fill = Paint.Brush(_colour, LookConstants.ChipAlpha) };
        grid.Children.Add(_chipFill);
        grid.Children.Add(new Path { Data = TopCrescent(LookConstants.ChipSize), Fill = Paint.Brush(Rgb.White, 0.55) });
        _chipGlyph.Color = Paint.Of(ChipGlyph.For(_colour));
        grid.Children.Add(Icons.Create(page.Glyph, LookConstants.ChipGlyphSize, _chipGlyph));
        return grid;
    }

    private FrameworkElement MakeText()
    {
        var panel = new StackPanel { Width = LookConstants.WidthTextBlock, Background = Brushes.Transparent };
        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (_nowPlaying is null) return;
            TextClicked?.Invoke();
            e.Handled = true;
        };
        _title = Label(LookConstants.TitleFontSize, FontWeights.SemiBold, Brushes.White, TitleLineHeight);
        _subtitle = Label(LookConstants.SubtitleFontSize, FontWeights.Normal, Paint.Brush(Rgb.White, LookConstants.SubtitleAlpha), SubtitleLineHeight);
        _subtitle.Margin = new Thickness(0, 0, -4, 0); // the empty page's hint is 131.03 wide in a slot of 130 (Dan's P22, WORK-ORDER-13): four dp more are lent to the right, nothing else moves
        panel.Children.Add(_title);
        panel.Children.Add(_subtitle);
        return panel;
    }

    private FrameworkElement MakeControls(Page page)
    {
        _mediaButtons.Clear();
        _middleButton = null;
        _closeButton = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = LookConstants.ControlSize };
        var glyphs = page.IsMedia ? new[] { "prev", "pause", "next" } : ["close"];
        MediaCommand[] commands = [MediaCommand.Previous, MediaCommand.PlayPause, MediaCommand.Next];
        for (var i = 0; i < glyphs.Length; i++)
        {
            var button = new Grid
            {
                Width = LookConstants.ControlSize,
                Height = LookConstants.ControlSize,
                Background = Brushes.Transparent,
                Margin = new Thickness(i == 0 ? 0 : LookConstants.ControlGap, 0, 0, 0),
            };
            button.Children.Add(Icons.Create(glyphs[i], LookConstants.ControlGlyphSize, Brushes.White));
            if (page.IsMedia)
            {
                var command = commands[i];
                button.Cursor = Cursors.Hand;
                button.MouseLeftButtonDown += (_, e) =>
                {
                    ControlClicked?.Invoke(command);
                    e.Handled = true;
                };
                _mediaButtons.Add(button);
                if (i == 1) _middleButton = button;
            }

            else
            {
                button.Cursor = Cursors.Hand;
                button.MouseLeftButtonDown += (_, e) =>
                {
                    CloseClicked?.Invoke();
                    e.Handled = true;
                };
                _closeButton = button;
            }

            row.Children.Add(button);
        }

        ApplyClose();
        row.Width = glyphs.Length * LookConstants.ControlSize + (glyphs.Length - 1) * LookConstants.ControlGap;
        return row;
    }

    private static TextBlock? _subtitleProbe;

    /// <summary>How wide a text is as the subtitle draws it, in one line.</summary>
    private static double SubtitleWidth(string text)
    {
        _subtitleProbe ??= Label(LookConstants.SubtitleFontSize, FontWeights.Normal, Brushes.White, SubtitleLineHeight);
        _subtitleProbe.TextWrapping = TextWrapping.NoWrap;
        _subtitleProbe.Text = text;
        _subtitleProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _subtitleProbe.DesiredSize.Width;
    }

    internal static TextBlock Label(double size, FontWeight weight, Brush brush, double lineHeight) => new()
    {
        FontFamily = Font,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        LineHeight = lineHeight,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        IsHitTestVisible = false,
        Effect = TextShadow(),
    };

    internal static DropShadowEffect TextShadow() => new()
    {
        Color = Colors.Black,
        Opacity = LookConstants.TextShadowAlpha,
        ShadowDepth = LookConstants.TextShadowOffsetY,
        Direction = 270,
        BlurRadius = LookConstants.TextShadowBlur / 2 * Units.WpfRadiusPerSigma,
        RenderingBias = RenderingBias.Quality,
    };

    /// <summary>A 1 px crescent along the top of a circle: the reference's inset 0 1px 0 highlight.</summary>
    internal static Geometry TopCrescent(double diameter)
    {
        var circle = new EllipseGeometry(new Rect(0, 0, diameter, diameter));
        var shifted = new EllipseGeometry(new Rect(0, 1, diameter, diameter));
        return new CombinedGeometry(GeometryCombineMode.Exclude, circle, shifted);
    }

    /// <summary>Wraps one element so its entrance and exit pose can be set.</summary>
    private sealed class Reveal
    {
        private readonly ScaleTransform _scale = new();
        private readonly TranslateTransform _rise = new();

        private readonly bool _blurs;

        public Reveal(FrameworkElement content, double width, double height, bool blurs = true)
        {
            _blurs = blurs;
            Host = new Grid { Width = width, Height = height, Background = null };
            Host.Children.Add(content);
            var group = new TransformGroup();
            group.Children.Add(_scale);
            group.Children.Add(_rise);
            Host.RenderTransform = group;
            Host.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        public Grid Host { get; }

        public void Apply(Pose p)
        {
            Host.Opacity = Math.Clamp(p.Opacity, 0, 1);
            _scale.ScaleX = _scale.ScaleY = p.Scale;
            _rise.Y = p.Rise;
            Host.Effect = _blurs && p.BlurRadius > 0.05 ? Units.Blur(p.BlurRadius) : null;
        }
    }
}
