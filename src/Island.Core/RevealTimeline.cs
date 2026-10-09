namespace Island.Core;

/// <summary>How one element of the contents looks at one moment: opacity 0..1, rise-and-scale progress (may overshoot 1) and blur in CSS pixels.</summary>
public readonly record struct RevealValues(double Opacity, double Move, double Blur)
{
    public static RevealValues Shown { get; } = new(1, 1, 0);
    public static RevealValues Hidden { get; } = new(0, 0, LookConstants.ContentsBlurStart);
}

/// <summary>
/// The entrance and exit of every element of the contents, as pure functions of time.
/// In: each element starts 32 ms after the previous; opacity over 280 ms, rise and scale over
/// 440 ms with a slight overshoot, a 4 px blur clearing over 280 ms. Out: all together, 110 ms.
/// A change that arrives while another is running starts from where the elements are, so nothing jumps.
/// </summary>
public sealed class RevealTimeline
{
    private const double FadeMs = LookConstants.ContentsFadeInMs;
    private const double MoveMs = LookConstants.ContentsScaleMs;
    private const double OutMs = LookConstants.DismissFadeMs;

    private readonly struct Element(RevealValues from, double startMs, bool toVisible)
    {
        public RevealValues From { get; } = from;
        public double StartMs { get; } = startMs;
        public bool ToVisible { get; } = toVisible;
    }

    private Element[] _elements;

    /// <summary>When true (Windows shows no animations: Dan's P1) every element is where it is going, with no stagger, fade, rise or blur.</summary>
    public bool Instant { get; set; }

    public RevealTimeline(int elementCount) => _elements = Hidden(elementCount);

    public int Count => _elements.Length;

    /// <summary>True when the last change asked for the contents to be shown.</summary>
    public bool Visible { get; private set; }

    /// <summary>Starts again with that many elements, all hidden, with no transition (a new page's contents).</summary>
    public void Reset(int elementCount)
    {
        _elements = Hidden(elementCount);
        Visible = false;
    }

    /// <summary>
    /// The number of elements changed while the contents are shown (a tile came or went on the page that fills itself): the elements that stay keep how they look,
    /// a new one comes in at <paramref name="atMs"/>, one that is gone is dropped. Nothing is hidden again.
    /// </summary>
    public void Resize(int elementCount, double atMs)
    {
        if (elementCount == _elements.Length) return;
        var next = new Element[elementCount];
        for (var i = 0; i < next.Length; i++)
            next[i] = i < _elements.Length ? _elements[i] : new Element(RevealValues.Hidden, atMs, Visible);
        _elements = next;
    }

    /// <summary>Shows or hides everything, the change counted from <paramref name="atMs"/>.</summary>
    public void Set(bool visible, double atMs)
    {
        var next = new Element[_elements.Length];
        for (var i = 0; i < next.Length; i++)
        {
            var delay = visible ? i * LookConstants.ContentsStaggerMs : 0;
            next[i] = new Element(At(atMs, i), atMs + delay, visible);
        }

        _elements = next;
        Visible = visible;
    }

    public RevealValues At(double nowMs, int element)
    {
        var e = _elements[element];
        var elapsed = nowMs - e.StartMs;
        var to = e.ToVisible ? RevealValues.Shown : RevealValues.Hidden;
        if (Instant) return to;
        if (elapsed <= 0) return e.From;

        if (e.ToVisible)
        {
            var fade = Easing.Fade(elapsed / FadeMs);
            var move = Easing.Move(elapsed / MoveMs);
            var blur = Easing.Ease(elapsed / FadeMs);
            return new RevealValues(
                Lerp(e.From.Opacity, to.Opacity, fade),
                Lerp(e.From.Move, to.Move, move),
                Lerp(e.From.Blur, to.Blur, blur));
        }

        var ease = Easing.Ease(elapsed / OutMs);
        return new RevealValues(
            Lerp(e.From.Opacity, to.Opacity, ease),
            Lerp(e.From.Move, to.Move, ease),
            Lerp(e.From.Blur, to.Blur, ease));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static Element[] Hidden(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new Element(RevealValues.Hidden, 0, false))];
}
