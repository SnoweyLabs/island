using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.App.Visuals;

/// <summary>
/// The island drawn into an off-screen tree on a flat background and rendered with
/// RenderTargetBitmap. This renders the island's own elements, never the screen.
/// </summary>
internal sealed class OffscreenScene
{
    private readonly Grid _root = new();
    private readonly IslandView _view;
    private readonly Canvas _back = new();
    private readonly Canvas _front = new();

    public OffscreenScene(Rgb? background, double glassBaseAlpha = LookConstants.GlassBaseAlpha, bool includeBack = true)
    {
        Width = WindowMetrics.Width;
        Height = WindowMetrics.Height;
        _root.Width = Width;
        _root.Height = Height;
        _root.Background = background is { } c ? Paint.Brush(c) : null;
        foreach (var canvas in new[] { _back, _front })
        {
            canvas.Width = Width;
            canvas.Height = Height;
        }

        if (includeBack) _root.Children.Add(_back);
        _root.Children.Add(_front);
        _view = new IslandView(_back, _front, Width, Height, glassBaseAlpha);
    }

    public double Width { get; }
    public double Height { get; }

    public ContentsLayer Contents => _view.Contents;

    /// <summary>The drag overlay of the view (for a snapshot of a lifted tile and the drop zone).</summary>
    public DragView Drag => _view.Drag;

    /// <summary>Switches the glass of the view being drawn (the dark base opacity).</summary>
    public void SetGlassAlpha(double alpha) => _view.SetGlassAlpha(alpha);

    /// <summary>The mode's mark on the edge of the view being drawn (WORK-ORDER-7 section 1); the approved look when never called.</summary>
    public void SetModeLook(ModeMark.Look look) => _view.SetModeLook(look);

    public bool RimVisible
    {
        get => _view.RimVisible;
        set => _view.RimVisible = value;
    }

    /// <summary>The shape at rest, centred, at the top gap. A null category draws the ball.</summary>
    public static ShapeFrame RestFrame(PageContents? contents, double arcHead, bool twoRows = false)
    {
        var size = contents is null ? CapsuleLayout.Ball : CapsuleLayout.SizeFor(contents);
        if (twoRows) size = size with { Height = ChoiceConstants.TwoRowHeight };
        var colour = Rgb.FromHex((contents ?? Pages.AllPlaceholders[0]).Page.Color);
        return new ShapeFrame(WindowMetrics.Width / 2, LookConstants.TopGap, size.Width, size.Height, size.Radius, colour, arcHead);
    }

    public Snapshot Render(ShapeFrame frame, PageContents? contents, double scale, bool textVisible = true, NowPlayingView? nowPlaying = null, double? stripPosition = null, IReadOnlyList<RowTile>? secondRow = null)
    {
        if (contents is not null)
        {
            Contents.Build(contents, frame.CentreX, 0, frame.Category);
            if (nowPlaying is not null) Contents.SetNowPlaying(nowPlaying);
            Contents.SetTextVisible(textVisible);
            if (secondRow is not null)
            {
                Contents.SetSecondRow(secondRow);
                Contents.SetSecondRowOpacity(1);
            }

            if (stripPosition is { } position)
            {
                var max = Math.Max(0, StripLayout.PickCount(contents.Items) - StripLayout.VisiblePicks(StripLayout.PickCount(contents.Items)));
                Contents.SetStripPosition(position, position > 0.02, position < max - 0.02);
            }

            Contents.Visibility = Visibility.Visible;
        }
        else
        {
            Contents.Visibility = Visibility.Collapsed;
        }

        _view.Apply(frame);
        return Capture(scale);
    }

    /// <summary>
    /// The small pill at rest, centred at the top gap (WORK-ORDER-7 section 2). <paramref name="litShare"/> is the part of the ring still to play;
    /// null draws the approved moving light in its place. <paramref name="arcHead"/> is where that light is.
    /// </summary>
    public Snapshot RenderPill(PillContent content, double? litShare, ModeMark.Look look, double scale, double arcHead = 0.3)
    {
        Contents.Visibility = Visibility.Collapsed;
        _view.Pill.Show(content, WindowMetrics.Width / 2);
        _view.Pill.SetPose(Pose.Shown);
        _view.SetPill(true, litShare);
        _view.SetModeLook(look);
        var width = PillLayout.Width(_view.Pill.TitleWidth);
        var frame = new ShapeFrame(WindowMetrics.Width / 2, LookConstants.TopGap, width, PillLayout.Height, PillLayout.Radius, Rgb.FromHex(LookConstants.MediaColor), arcHead);
        _view.Apply(frame);
        return Capture(scale);
    }

    /// <summary>The capsule laid out for search at rest (WORK-ORDER-7 section 3), in the colour of the page that was showing.</summary>
    public Snapshot RenderSearch(SearchViewData data, Rgb colour, double scale, double arcHead = 0.3)
    {
        Contents.Visibility = Visibility.Collapsed;
        _view.Search.Show(data, WindowMetrics.Width / 2, colour);
        _view.Search.SetPose(Pose.Shown);
        _view.SetSearch(true);
        _view.SetModeLook(ModeMark.Look.Approved);
        var width = SearchLayout.Width(data.Text.Length, data.Tiles.Count);
        var frame = new ShapeFrame(WindowMetrics.Width / 2, LookConstants.TopGap, width, LookConstants.CapsuleHeight, LookConstants.CapsuleCornerRadius, colour, arcHead);
        _view.Apply(frame);
        return Capture(scale);
    }

    /// <summary>The notice at rest, centred at the top gap (WORK-ORDER-7 section 4), with the mode's mark.</summary>
    public Snapshot RenderNotice(NoticeContent content, ModeMark.Look look, double scale)
    {
        Contents.Visibility = Visibility.Collapsed;
        _view.Notice.Show(content, WindowMetrics.Width / 2);
        _view.Notice.SetPose(Pose.Shown);
        _view.SetPill(true, null, notice: true);
        _view.SetModeLook(look);
        var width = NoticeLayout.Width(_view.Notice.TextWidth);
        var frame = new ShapeFrame(WindowMetrics.Width / 2, LookConstants.TopGap, width, NoticeLayout.Height, NoticeLayout.Radius, Rgb.FromHex(LookConstants.VibeColor), 0.3);
        _view.Apply(frame);
        return Capture(scale);
    }

    /// <summary>The notice view, for the self-test.</summary>
    public NoticeView Notice => _view.Notice;

    /// <summary>The search view, for the self-test.</summary>
    public SearchView Search => _view.Search;

    /// <summary>The pill view, for the self-test.</summary>
    public PillView Pill => _view.Pill;

    /// <summary>A snapshot of the scene as it stands, without laying the contents out again (after the drag overlay or the text was changed by hand).</summary>
    public Snapshot Capture(double scale)
    {
        _root.Measure(new Size(Width, Height));
        _root.Arrange(new Rect(0, 0, Width, Height));
        _root.UpdateLayout();
        return Snapshot.Of(_root, (int)Math.Round(Width * scale), (int)Math.Round(Height * scale), 96 * scale);
    }
}
