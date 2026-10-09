using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// Runs a body on one STA thread that lives as long as the test run (a view built and measured there is never shown) and rethrows what it threw. One thread for everything, because the settings view keeps
/// templates and brushes in static fields (<c>Parts.Pills</c>, <c>Look</c>) that belong to the thread that made them: bodies on several threads at once would meet each other's objects.
/// </summary>
internal static class Sta
{
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> Queue = [];

    private static readonly Thread Worker = Start();

    private static Thread Start()
    {
        var thread = new Thread(() =>
        {
            foreach (var job in Queue.GetConsumingEnumerable()) job();
        })
        {
            IsBackground = true,
            Name = "design tests: the one STA thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    public static T Run<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        _ = Worker; // started on first use
        Queue.Add(() =>
        {
            try { result = body(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        done.Wait();
        if (error is not null) throw new InvalidOperationException("The STA body failed: " + error.Message, error);
        return result;
    }

    public static void Run(Action body) => Run(() => { body(); return 0; });
}

/// <summary>How wide a text is drawn in the island's own type (the same family list, sizes and line settings as <c>ContentsLayer.Label</c>).</summary>
internal static class TextWidth
{
    public const string Primary = "Segoe UI Variable Text, Segoe UI";
    public const string FallbackOnly = "Segoe UI";

    /// <summary>The width of the text as one line, measured by a TextBlock set up as the island sets up its own (so that its own text engine decides).</summary>
    public static double Of(string text, double size, FontWeight weight, string family = Primary)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(family),
            FontSize = size,
            FontWeight = weight,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }

    /// <summary>Whether Segoe UI Variable Text is installed here (the first name of the island's font list).</summary>
    public static bool HasVariable() => Fonts.SystemFontFamilies.Any(f => f.Source.Equals("Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase));
}
