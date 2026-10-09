using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.SettingsUi;

/// <summary>
/// The small island at the top of look C (<c>.c-top</c>): a glass pill with a label and one dot per step, and the
/// light running round its edge in the colour of the step. The dots are buttons, so the steps can be reached by
/// keyboard as well.
/// </summary>
internal sealed class StepIsland : Grid
{
    private readonly RimRing _rim = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = Look.Label(string.Empty, 13, FontWeights.SemiBold);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _freeze;

    public StepIsland()
    {
        Height = Look.TopHeight;
        HorizontalAlignment = HorizontalAlignment.Center;
        _label.TextWrapping = TextWrapping.NoWrap;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.Margin = new Thickness(0, 0, 12, 0);

        var gradient = new LinearGradientBrush(
            Color.FromArgb(56, 255, 255, 255), Color.FromArgb(13, 255, 255, 255), 90); // white .22 to .05
        var body = new Border
        {
            CornerRadius = new CornerRadius(Look.TopHeight / 2),
            Background = Look.Solid(20, 22, 32, 0.55),
            BorderBrush = Look.Solid(255, 255, 255, 0.35),
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                CornerRadius = new CornerRadius(Look.TopHeight / 2 - 1),
                Background = gradient,
                Padding = new Thickness(16, 0, 8, 0),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _label, _dots } },
            },
        };
        _rim.Margin = new Thickness(-2);
        _rim.IsHitTestVisible = false;
        Children.Add(body);
        Children.Add(_rim);

        Loaded += (_, _) => { if (!_freeze) CompositionTarget.Rendering += OnFrame; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Raised with the step number (from 0) when a dot is clicked or chosen with the keyboard.</summary>
    public event Action<int>? StepChosen;

    /// <summary>Holds the light still at the given moment, so a picture of the screen is the same every time. Stops the animation.</summary>
    public void FreezeAt(double seconds)
    {
        _freeze = true;
        CompositionTarget.Rendering -= OnFrame;
        _rim.Seconds = seconds;
    }

    public void Show(string label, IReadOnlyList<string> stepNames, int active, string accent)
    {
        _label.Text = label;
        _rim.Accent = Rgb.FromHex(accent);
        _dots.Children.Clear();
        for (var i = 0; i < stepNames.Count; i++)
        {
            var step = i;
            var dot = new Border
            {
                Width = 22,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = i <= active ? Look.BrushOf(accent) : Look.Solid(255, 255, 255, 0.5), // 0.2 gave 1.9:1 against the pill (Dan's P7, WORK-ORDER-13)
            };
            var button = Parts.Flat(dot, 5, () => StepChosen?.Invoke(step), $"step:{i}", $"Step {i + 1} of {stepNames.Count}: {stepNames[i]}{(i == active ? ", current" : string.Empty)}", "#00000000");
            button.Padding = new Thickness(2, 6, 2, 6);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0);
            _dots.Children.Add(button);
        }
    }

    private void OnFrame(object? sender, EventArgs e) => _rim.Seconds = _clock.Elapsed.TotalSeconds;

    /// <summary>The light on the edge (<c>.c-top:before</c>): a 2 px ring just outside the pill with a streak that fades in, turns white at its head and ends.</summary>
    private sealed class RimRing : FrameworkElement
    {
        private const int Pieces = 40;
        private const double Streak = 0.38;       // the visible part of the turn: 62% to 100% in the reference
        private const double Overlap = 0.0015;
        private double _seconds;

        public Rgb Accent { get; set; } = Rgb.FromHex(LookConstants.MediaColor);

        public double Seconds
        {
            get => _seconds;
            set
            {
                _seconds = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth < 8 || ActualHeight < 8) return;
            var perimeter = new RoundedPerimeter(1, 1, ActualWidth - 2, ActualHeight - 2, (ActualHeight - 2) / 2);
            var head = ArcClock.Head(_seconds);

            for (var i = 0; i < Pieces; i++)
            {
                var u = (i + 0.5) / Pieces;
                var (colour, alpha) = StreakAt(u);
                if (alpha <= 0.01) continue;

                var start = head - Streak + i * Streak / Pieces;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), (byte)colour.R, (byte)colour.G, (byte)colour.B)), 2)
                {
                    StartLineCap = PenLineCap.Flat,
                    EndLineCap = PenLineCap.Flat,
                };
                dc.DrawGeometry(null, pen, Walk(perimeter, start, Streak / Pieces + Overlap));
            }
        }

        // conic-gradient(transparent 0 62%, cat 82%, #fff 92%, transparent 100%), position u in 0..1 within the streak.
        private (Rgb Colour, double Alpha) StreakAt(double u)
        {
            var position = (1 - Streak) + u * Streak;
            if (position < 0.82) return (Accent, Math.Clamp((position - 0.62) / 0.20, 0, 1));
            if (position < 0.92) return (ColorMath.LerpSrgb(Accent, Rgb.White, (position - 0.82) / 0.10), 1);
            return (Rgb.White, Math.Clamp(1 - (position - 0.92) / 0.08, 0, 1));
        }

        private static StreamGeometry Walk(RoundedPerimeter perimeter, double start, double length)
        {
            var pieces = perimeter.Walk(start, length);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (pieces.Count == 0) return g;
                ctx.BeginFigure(new Point(pieces[0].From.X, pieces[0].From.Y), false, false);
                foreach (var piece in pieces)
                {
                    var to = new Point(piece.To.X, piece.To.Y);
                    if (piece.IsArc) ctx.ArcTo(to, new Size(piece.Radius, piece.Radius), 0, false, SweepDirection.Clockwise, true, false);
                    else ctx.LineTo(to, true, false);
                }
            }

            g.Freeze();
            return g;
        }
    }
}
