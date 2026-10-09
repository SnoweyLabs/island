using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.App;

/// <summary>
/// The settings screen as the app opens it: a ball appears in the centre of the screen that holds the mouse pointer and
/// expands, with the island's own spring, into a full-screen glass view; closing reverses it. The one place the app goes
/// full screen and takes the keyboard (and gives it back to the window it came from when it closes). The window exists
/// only while the screen is open. What is changed is saved and in force at once (the session does that); nothing here
/// needs a restart.
/// </summary>
internal sealed class SettingsScreen
{
    private const double OpenDelayMs = 150;
    private const double FadeMs = 220;
    private const double CloseFadeMs = 110;

    private readonly Func<SettingsSession> _newSession;
    private readonly Func<GlassKind> _glass;
    private readonly Func<PixelRect?>? _screenArea;
    private ScreenWindow? _window;

    /// <param name="newSession">A fresh session over the app's current settings, pages and picks, made every time the screen opens.</param>
    /// <param name="glass">The glass in force, to tint the screen.</param>
    /// <param name="screenArea">The full rectangle, in real pixels, of the screen that holds the pointer, chosen by the same code as the island's own place (WORK-ORDER-6 §1); null: ask Windows' own screen list.</param>
    public SettingsScreen(Func<SettingsSession> newSession, Func<GlassKind> glass, Func<PixelRect?>? screenArea = null)
    {
        _newSession = newSession;
        _glass = glass;
        _screenArea = screenArea;
    }

    public bool IsOpen => _window is not null;

    /// <summary>True from the first frame of the opening until the capsule-sized ball has gone.</summary>
    /// <summary>The screen's window, for the self-test (which raises an Esc on it).</summary>
    internal System.Windows.Window? WindowForSelfTest => _window;

    /// <summary>The width the ball or the screen is drawn at now (the spring's value), for the self-test.</summary>
    internal double? DrawnWidthForSelfTest => _window?.DrawnWidth;

    public SettingsScreenPhase Phase => _window?.Phase ?? SettingsScreenPhase.Closed;

    public SettingsSession? Session => _window?.Session;

    public SettingsView? View => _window?.View;

    /// <summary>Raised when the window has gone and the keyboard has been given back.</summary>
    public event Action? Closed;

    /// <summary>Raised when the setup was ended with "Done": its ball has flown to the top and the island is to take its place, open.</summary>
    public event Action? SetupFinished;

    /// <summary>Raised when "Start the practice" was pressed on the step Try it: the screen has stepped back (hidden), and the host practises on the real island, then calls <see cref="ResumeAfterPractice"/>.</summary>
    public event Action? PracticeRequested;

    /// <summary>True while the screen stands back for the practice (hidden, but not closed).</summary>
    public bool SteppedBack => _window?.SteppedBack == true;

    /// <summary>The practice is over: the screen comes back on the next step, with the keyboard.</summary>
    public void ResumeAfterPractice() => _window?.StepForward();

    /// <summary>Raised on the UI thread each time the session changes something (keys, pages, picks, glass).</summary>
    public event Action<SettingsArea, SettingsSession>? Changed;

    /// <summary>Opens the screen (from the tray menu). Called inside the click, so Windows lets the screen take the foreground.</summary>
    public void Open() => Open(setup: false);

    /// <summary>Opens the first-start steps (a first start, or "Run the setup again"): the same screen, the same ball, other pages.</summary>
    public void OpenSetup() => Open(setup: true);

    private bool _setupAgain;

    private void Open(bool setup)
    {
        if (_window is not null) return;
        var session = _newSession();
        session.Changed += area => Changed?.Invoke(area, session);
        var window = new ScreenWindow(session, _glass(), _screenArea?.Invoke(), setup);
        window.View.PracticeRequested += (_, _) =>
        {
            window.StepBack();
            PracticeRequested?.Invoke();
        };
        window.View.SetupRequested += (_, _) =>
        {
            _setupAgain = true; // the settings screen shrinks away, and the steps open in its place
            window.BeginClose();
        };
        window.Gone += () =>
        {
            _window = null;
            var again = _setupAgain;
            _setupAgain = false;
            Closed?.Invoke();
            if (window.ToIsland) SetupFinished?.Invoke();
            if (again) OpenSetup();
        };
        _window = window;
        window.Begin();
    }

    /// <summary>Starts the reverse motion (a no-op when closed or already closing).</summary>
    public void Close() => _window?.BeginClose();

    /// <summary>The motion state, for the self-test.</summary>
    public enum SettingsScreenPhase
    {
        Closed,
        Opening,
        Open,
        Closing,
    }

    // ---------------------------------------------------------------------------------------------

    private sealed class ScreenWindow : Window
    {
        private readonly Stopwatch _clock = new();
        private readonly Border _shape = new() { Background = Brushes.Transparent };
        private readonly Grid _stage;
        private readonly GlassKind _glass;
        private readonly IntPtr _previous = Native.GetForegroundWindow();
        private Spring _left, _top, _width, _height, _radius;
        private double _screenW, _screenH;
        private double _lastMs;
        private double _openedAtMs;
        private double _closeAtMs = double.NaN;
        private bool _expanded;
        private bool _focused;
        private bool _gone;

        private readonly PixelRect? _area;
        private readonly bool _setup;
        private bool _flying;

        public ScreenWindow(SettingsSession session, GlassKind glass, PixelRect? area = null, bool setup = false)
        {
            _area = area;
            _setup = setup;
            Session = session;
            _glass = glass;
            session.Chooser = new OutsidePlaceChooser(this); // Windows' windows for choosing open in front of this screen, never without it
            View = new SettingsView(session, setup);
            View.DoneRequested += (_, _) => BeginClose(toIsland: true);

            Title = "Island settings"; // the window's name for a screen reader
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = 100;
            Height = 100;

            _stage = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
            _shape.Background = Tint();
            _shape.BorderBrush = Brushes.Transparent;
            _shape.BorderThickness = new Thickness(1);
            _shape.HorizontalAlignment = HorizontalAlignment.Left;
            _shape.VerticalAlignment = VerticalAlignment.Top;
            var canvas = new Canvas { Background = Brushes.Transparent };
            canvas.Children.Add(_shape);
            Content = canvas;
            _shape.Child = _stage;
            _stage.Children.Add(View);

            PreviewKeyDown += OnKey;
            Activated += (_, _) =>
            {
                if (_focused) return;
                _focused = true;
                View.FocusFirst();
            };
        }

        public SettingsSession Session { get; }

        public SettingsView View { get; }

        /// <summary>True while the screen is hidden for the practice (Dan's tutorial): it is neither closed nor drawn.</summary>
        public bool SteppedBack { get; private set; }

        /// <summary>The screen steps back: hidden for the practice, the keyboard left to whatever the person uses next (the island).</summary>
        public void StepBack()
        {
            if (SteppedBack || _gone) return;
            SteppedBack = true;
            Hide();
        }

        /// <summary>The screen comes back where it was, on the step after Try it, and takes the keyboard.</summary>
        public void StepForward()
        {
            if (!SteppedBack || _gone) return;
            SteppedBack = false;
            Show();
            var handle = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(handle, Native.HwndTopmost, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize);
            OutsideForeground.BringForward(handle);
            View.PressContinue(); // the practice was a step of its own: the next one is shown
            View.FocusFirst();
        }

        public SettingsScreenPhase Phase { get; private set; } = SettingsScreenPhase.Opening;

        public event Action? Gone;

        public void Begin()
        {
            // The screen that holds the mouse pointer: chosen by the same code as the island's own place when it was given, else by Windows' own list.
            var b = _area is { } chosen && chosen.HasArea
                ? new System.Drawing.Rectangle(chosen.Left, chosen.Top, (int)chosen.Width, (int)chosen.Height)
                : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).Bounds;
            Show();
            var handle = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(handle, Native.HwndTopmost, b.X, b.Y, b.Width, b.Height, 0);
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _screenW = b.Width / scale;
            _screenH = b.Height / scale;
            _stage.Width = _screenW;
            _stage.Height = _screenH;
            View.Width = _screenW;
            View.Height = _screenH;
            View.Glass = _glass;

            var ball = LookConstants.BallSize;
            _left = Spring.At((_screenW - ball) / 2);
            _top = Spring.At((_screenH - ball) / 2);
            _width = Spring.At(ball);
            _height = Spring.At(ball);
            _radius = Spring.At(ball / 2);
            Apply();

            _clock.Start();
            OutsideForeground.BringForward(handle);
            CompositionTarget.Rendering += OnFrame;
        }

        /// <summary>True when the setup was ended with "Done": the ball flies to the top before the window goes, and the island takes its place.</summary>
        public bool ToIsland { get; private set; }

        /// <summary>The drawn width: the spring's value.</summary>
        public double DrawnWidth => _width.Value;

        public void BeginClose(bool toIsland = false)
        {
            if (_gone || Phase == SettingsScreenPhase.Closing) return;
            ToIsland = toIsland && _setup && FirstStart.EndOf(done: true) == SetupEnd.ShrinkFlyAndOpenIsland;
            Phase = SettingsScreenPhase.Closing;
            _closeAtMs = _clock.Elapsed.TotalMilliseconds;
            var ball = LookConstants.BallSize;
            _left = _left.WithTarget((_screenW - ball) / 2);
            _top = _top.WithTarget((_screenH - ball) / 2);
            _width = _width.WithTarget(ball);
            _height = _height.WithTarget(ball);
            _radius = _radius.WithTarget(ball / 2);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            // A key that comes from a popup of the screen (the list of Move to another page) is the popup's own: Esc closes only the list and must not close the whole screen.
            if (e.OriginalSource is DependencyObject source && PresentationSource.FromDependencyObject(source) is { } presentation && !ReferenceEquals(presentation.RootVisual, this)) return;
            if (!View.HandleEscape()) BeginClose();
            e.Handled = true;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var now = _clock.Elapsed.TotalMilliseconds;
            var dt = Math.Min(Math.Max(0, now - _lastMs), 500) / 1000.0;
            _lastMs = now;

            var animations = WindowsAnimations.On; // Dan's P1: with Windows' animations off the screen opens and closes at once
            if (!_expanded && Phase == SettingsScreenPhase.Opening && (now - _openedAtMs >= OpenDelayMs || !animations))
            {
                _expanded = true;
                _left = _left.WithTarget(0); // full screen
                _top = _top.WithTarget(0);
                _width = _width.WithTarget(_screenW);
                _height = _height.WithTarget(_screenH);
                _radius = _radius.WithTarget(0);
            }

            _left = _left.Frame(dt);
            _top = _top.Frame(dt);
            _width = _width.Frame(dt);
            _height = _height.Frame(dt);
            _radius = _radius.Frame(dt);
            if (!animations)
            {
                _left = _left.Snap(_left.Target);
                _top = _top.Snap(_top.Target);
                _width = _width.Snap(_width.Target);
                _height = _height.Snap(_height.Target);
                _radius = _radius.Snap(_radius.Target);
            }

            Apply();

            var progress = Math.Clamp((_width.Drawn - LookConstants.BallSize) / Math.Max(1, _screenW - LookConstants.BallSize), 0, 1);
            if (Phase == SettingsScreenPhase.Closing)
            {
                _stage.Opacity = animations ? Math.Max(0, 1 - (now - _closeAtMs) / CloseFadeMs) : 0;
                if (Settled(_width) && Settled(_height) && (now - _closeAtMs > CloseFadeMs || !animations))
                {
                    if (ToIsland && !_flying)
                    {
                        _flying = true; // the ball goes up to where the island's own ball starts
                        _top = _top.WithTarget(LookConstants.TopGap);
                    }
                    else if (!_flying || Settled(_top)) Finish();
                }
            }
            else
            {
                _stage.Opacity = Math.Clamp((progress - 0.78) / 0.2, 0, 1);
                if (_expanded && progress > 0.995 && Settled(_width) && Settled(_height) && Phase == SettingsScreenPhase.Opening) Phase = SettingsScreenPhase.Open;
            }
        }

        private static bool Settled(Spring s) => Math.Abs(s.Value - s.Target) < 0.5 && Math.Abs(s.Velocity) < 5;

        private void Apply()
        {
            var w = Math.Max(LookConstants.BallSize * LookConstants.MinSizeFraction, _width.Drawn);
            var h = Math.Max(LookConstants.BallSize * LookConstants.MinSizeFraction, _height.Drawn);
            var r = Math.Clamp(_radius.Drawn, 0, Math.Min(w, h) / 2);
            Canvas.SetLeft(_shape, _left.Drawn);
            Canvas.SetTop(_shape, _top.Drawn);
            _shape.Width = w;
            _shape.Height = h;
            _shape.CornerRadius = new CornerRadius(r);
            _shape.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
            // The view keeps its full size and is centred in the shape, so it never re-lays itself out while the shape grows.
            _stage.Margin = new Thickness((w - _screenW) / 2, (h - _screenH) / 2, 0, 0);
        }

        private Brush Tint()
        {
            var alpha = _glass == GlassKind.Darker ? LookConstants.GlassDarkerAlpha : LookConstants.GlassBaseAlpha;
            var brush = new SolidColorBrush(Paint(Rgb.FromHex(LookConstants.GlassBaseColor), alpha));
            brush.Freeze();
            return brush;
        }

        private static Color Paint(Rgb c, double alpha) =>
            Color.FromArgb((byte)Math.Round(alpha * 255), (byte)c.R, (byte)c.G, (byte)c.B);

        /// <summary>The window closed by something other than <see cref="Finish"/> (Alt+F4, the system): the screen must still end, or Open() returns at its first line for ever.</summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_gone) return;
            _gone = true;
            CompositionTarget.Rendering -= OnFrame;
            Phase = SettingsScreenPhase.Closed;
            Gone?.Invoke();
        }

        private void Finish()
        {
            if (_gone) return;
            _gone = true;
            CompositionTarget.Rendering -= OnFrame;
            Phase = SettingsScreenPhase.Closed;
            // The keyboard goes back to where it came from, unless another window was chosen in the meantime
            // (this window is still the foreground one until it closes).
            var hadKeyboard = Native.GetForegroundWindow() == new WindowInteropHelper(this).Handle;
            Close();
            if (hadKeyboard && _previous != IntPtr.Zero) OutsideForeground.BringForward(_previous);
            Gone?.Invoke();
        }
    }
}
