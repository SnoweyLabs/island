using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Island.App.Visuals;

/// <summary>The glyphs of the reference (ICON in island-previews.html), drawn on a 16 by 16 grid.</summary>
internal static class Icons
{
    private sealed record Shapes(string[] Filled, string[] Stroked, double StrokeWidth, bool Round);

    private static readonly Dictionary<string, Shapes> Table = new()
    {
        ["play"] = new(["M5 3.2v9.6l8-4.8z"], [], 0, false),
        ["pause"] = new(["M4.5 3.5H7v9H4.5zM9 3.5h2.5v9H9z"], [], 0, false),
        ["next"] = new(["M3 3.5v9L9.5 8zM10.5 3.5h2v9h-2z"], [], 0, false),
        ["prev"] = new(["M13 3.5v9L6.5 8zM3.5 3.5h2v9h-2z"], [], 0, false),
        ["search"] = new([], ["M7 2.6a4.4 4.4 0 1 0 0 8.8a4.4 4.4 0 1 0 0-8.8z", "M10.3 10.3 13.4 13.4"], 1.6, true),
        ["close"] = new([], ["M4 4l8 8M12 4l-8 8"], 1.6, true),
        ["folder"] = new(["M2 5a1.2 1.2 0 0 1 1.2-1.2h3l1.3 1.5h5.3A1.2 1.2 0 0 1 14 6.5v5.3a1.2 1.2 0 0 1-1.2 1.2H3.2A1.2 1.2 0 0 1 2 11.8z"], [], 0, false),
        ["grid"] = new(["M3 3h4v4H3zM9 3h4v4H9zM3 9h4v4H3zM9 9h4v4H9z"], [], 0, false),
        ["term"] = new([], ["M3 4.5 6.5 8 3 11.5M8.5 11.5H13"], 1.7, true),
        // The Terminals page (WORK-ORDER-11 section 1): a small window outline with a prompt mark in it.
        ["terminal"] = new([], ["M2 4.4a1.4 1.4 0 0 1 1.4-1.4h9.2A1.4 1.4 0 0 1 14 4.4v7.2a1.4 1.4 0 0 1-1.4 1.4H3.4A1.4 1.4 0 0 1 2 11.6z", "M4.7 6.3 6.7 8 4.7 9.7", "M8.2 9.9h3"], 1.6, true), // 1.2 until WORK-ORDER-13 (Dan's P22): the thinnest glyph of the family, 1.35 px at 100% scaling
        ["dot"] = new(["M8 4.2a3.8 3.8 0 1 0 0 7.6a3.8 3.8 0 1 0 0-7.6z"], [], 0, false),
        ["globe"] = new([], ["M2.5 8h11", "M13.5 8a5.5 5.5 0 1 1-11 0 5.5 5.5 0 1 1 11 0z", "M10.4 8a2.4 5.5 0 1 1-4.8 0 2.4 5.5 0 1 1 4.8 0z"], 1.3, false),
    };

    public static FrameworkElement Create(string name, double size, Brush brush)
    {
        var shapes = Table.TryGetValue(name, out var found) ? found : Table["dot"]; // a page made by Dan has the plain dot; an unknown glyph never crashes the island
        var grid = new Canvas { Width = 16, Height = 16 };

        foreach (var data in shapes.Filled)
            grid.Children.Add(new Path { Data = Geometry.Parse(data), Fill = brush });

        foreach (var data in shapes.Stroked)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = brush,
                StrokeThickness = shapes.StrokeWidth,
                StrokeLineJoin = shapes.Round ? PenLineJoin.Round : PenLineJoin.Miter,
            };
            if (shapes.Round)
            {
                path.StrokeStartLineCap = PenLineCap.Round;
                path.StrokeEndLineCap = PenLineCap.Round;
            }

            grid.Children.Add(path);
        }

        return new Viewbox { Width = size, Height = size, Child = grid, IsHitTestVisible = false };
    }
}
