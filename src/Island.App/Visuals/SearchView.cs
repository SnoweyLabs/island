using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>What search shows: the typed text, the matches (and the service tile last) as tiles, which one is selected, and the text block.</summary>
internal sealed record SearchViewData(string Text, IReadOnlyList<Item> Tiles, int Selected, bool NeedsClick, string Title, string Subtitle, int ServiceTiles);

/// <summary>
/// The capsule laid out for search (WORK-ORDER-7 section 3, picture class <c>field</c> and the 9A picture): a text field 40 high (corner radius 20, white at
/// 14%, text 14, a thin caret), then the matches as round tiles, then the text block with the selected match's name and "Enter to open". Matches beyond
/// seven slide: the seven shown are the ones around the selection. A click anywhere asks for the keyboard when Windows has not given it.
/// </summary>
internal sealed class SearchView : Canvas
{
    private const double Centre = LookConstants.CapsuleHeight / 2 + LookConstants.BorderWidth;
    private readonly Canvas _host = new();
    private readonly TranslateTransform _top = new();
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _rise = new();
    private TextBlock? _typed;
    private SearchViewData? _shown;

    public SearchView()
    {
        IsHitTestVisible = true;
        _host.RenderTransformOrigin = new Point(0.5, 0.5);
        var group = new TransformGroup();
        group.Children.Add(_scale);
        group.Children.Add(_rise);
        _host.RenderTransform = group;
        RenderTransform = _top;
        Children.Add(_host);
        Visibility = Visibility.Collapsed;
    }

    /// <summary>A tile was clicked: its index among all the tiles.</summary>
    public event Action<int>? TileClicked;

    /// <summary>The field, or the empty part of the contents, was clicked: the island asks for the keyboard.</summary>
    public event Action? FieldClicked;

    public void SetTop(double top) => _top.Y = top;

    /// <summary>The text of the field as drawn, for the self-test ("tu" only).</summary>
    public string FieldText => _typed?.Text ?? string.Empty;

    /// <summary>The text block's two lines, for the self-test.</summary>
    public (string Title, string Subtitle) Lines => (_shown?.Title ?? string.Empty, _shown?.Subtitle ?? string.Empty);

    /// <summary>How many tiles are drawn now (at most seven), for the self-test.</summary>
    public int TilesDrawn { get; private set; }

    /// <summary>The index of the first tile drawn (the row slides to keep the selection in view), for the self-test.</summary>
    public int FirstTileDrawn { get; private set; }

    public void Show(SearchViewData data, double centreX, Rgb colour)
    {
        if (data == _shown && _host.Children.Count > 0) return;
        _shown = data;
        _host.Children.Clear();

        var width = SearchLayout.Width(data.Text.Length, data.Tiles.Count);
        var x = centreX - width / 2 + LookConstants.WidthLeadingInset;
        var fieldWidth = SearchLayout.FieldWidth(data.Text.Length);

        // The field: white at 14%, the typed text (its end, when it does not all fit) and a thin caret after it.
        var field = new Border
        {
            Width = fieldWidth,
            Height = SearchLayout.FieldHeight,
            CornerRadius = new CornerRadius(SearchLayout.FieldRadius),
            Background = Paint.Brush(Rgb.White, 0.14),
            Cursor = System.Windows.Input.Cursors.IBeam,
        };
        field.MouseLeftButtonDown += (_, e) =>
        {
            FieldClicked?.Invoke();
            e.Handled = true;
        };
        Place(field, x, Centre - SearchLayout.FieldHeight / 2);

        _typed = ContentsLayer.Label(14, FontWeights.Normal, Brushes.White, 20);
        _typed.Text = TailThatFits(data.Text, fieldWidth - 34);
        _typed.IsHitTestVisible = false;
        Place(_typed, x + 14, Centre - 10); // the reference's 14 (it was 16 until WORK-ORDER-13: Dan's P22)
        _typed.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var caret = new System.Windows.Shapes.Rectangle { Width = 1.5, Height = 18, Fill = Brushes.White, IsHitTestVisible = false, RadiusX = 0.75, RadiusY = 0.75 };
        Place(caret, x + 14 + Math.Min(_typed.DesiredSize.Width + 1, fieldWidth - 20), Centre - 9);
        x += fieldWidth + LookConstants.WidthChipGap;

        // The tiles: the seven around the selection.
        var shown = SearchLayout.ShownTiles(data.Tiles.Count);
        var first = data.Tiles.Count <= shown ? 0 : Math.Clamp(data.Selected - shown + 1, 0, data.Tiles.Count - shown);
        FirstTileDrawn = first;
        TilesDrawn = shown;
        for (var i = 0; i < shown; i++)
        {
            var index = first + i;
            var tile = new TileView(data.Tiles[index], index) { IsHitTestVisible = true };
            tile.SetColour(colour);
            tile.SetSelected(index == data.Selected, colour);
            tile.Clicked += i2 => TileClicked?.Invoke(i2);
            Place(tile, x + i * LookConstants.WidthItemPitch, Centre - LookConstants.ItemSize / 2);
            if (index >= data.Tiles.Count - data.ServiceTiles)
            {
                // The service's tile carries a small arrow: it opens that service's own results page.
                var arrow = ContentsLayer.Label(11, FontWeights.Bold, Brushes.White, 12);
                arrow.Text = "↗";
                arrow.IsHitTestVisible = false;
                Place(arrow, x + i * LookConstants.WidthItemPitch + LookConstants.ItemSize - 12, Centre + LookConstants.ItemSize / 2 - 15);
            }
        }

        x += (shown > 0 ? shown * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap : 0) + LookConstants.WidthItemsToTextGap;

        // The text block: the selected match's name, and under it "Enter to open".
        var text = new StackPanel { Width = LookConstants.WidthTextBlock, IsHitTestVisible = false };
        var title = ContentsLayer.Label(LookConstants.TitleFontSize, FontWeights.SemiBold, Brushes.White, LookConstants.TitleFontSize * 1.45);
        title.Text = data.Title;
        var subtitle = ContentsLayer.Label(LookConstants.SubtitleFontSize, FontWeights.Normal, Paint.Brush(Rgb.White, LookConstants.SubtitleAlpha), LookConstants.SubtitleFontSize * 1.45);
        subtitle.Text = data.Subtitle;
        text.Children.Add(title);
        text.Children.Add(subtitle);
        Place(text, x, Centre - (LookConstants.TitleFontSize + LookConstants.SubtitleFontSize) * 1.45 / 2);

        // Anywhere else in the contents: ask for the keyboard (the text block says "Click here to type" while it is not held).
        var backdrop = new Border { Width = width, Height = LookConstants.CapsuleHeight, Background = Brushes.Transparent };
        backdrop.MouseLeftButtonDown += (_, e) =>
        {
            FieldClicked?.Invoke();
            e.Handled = true;
        };
        SetLeft(backdrop, centreX - width / 2);
        SetTop(backdrop, 0);
        _host.Children.Insert(0, backdrop);
    }

    public void SetPose(Pose pose)
    {
        _host.Opacity = Math.Clamp(pose.Opacity, 0, 1);
        _scale.ScaleX = _scale.ScaleY = pose.Scale;
        _rise.Y = pose.Rise;
        _host.Effect = pose.BlurRadius > 0.05 ? Units.Blur(pose.BlurRadius) : null;
    }

    /// <summary>The click handlers, as the mouse reaches them (for the self-test, which presses no mouse button).</summary>
    internal void RaiseTileForSelfTest(int index) => TileClicked?.Invoke(index);

    internal void RaiseFieldForSelfTest() => FieldClicked?.Invoke();

    /// <summary>The end of the text that fits in the field: when it is longer, it starts with an ellipsis.</summary>
    private static string TailThatFits(string text, double width)
    {
        if (text.Length == 0) return string.Empty;
        var probe = ContentsLayer.Label(14, FontWeights.Normal, Brushes.White, 20);
        // The cut falls only where a character as a person sees it begins (an emoji with its skin tone, a letter with its accent, a flag): never inside one.
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        string Candidate(int i) => (starts[i] > 0 ? "…" : string.Empty) + text[starts[i]..];
        bool Fits(string candidate)
        {
            probe.Text = candidate;
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width <= width;
        }

        // The longer the tail the wider it is: the earliest cut that fits is found by halving, not by trying every cut.
        int low = 0, high = starts.Length; // the answer is in [low, high]; high means none fits
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Fits(Candidate(mid))) high = mid;
            else low = mid + 1;
        }

        return low < starts.Length ? Candidate(low) : "…";
    }

    private void Place(UIElement element, double x, double y)
    {
        SetLeft(element, x);
        SetTop(element, y);
        _host.Children.Add(element);
    }
}
