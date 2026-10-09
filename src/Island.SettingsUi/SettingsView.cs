using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// The full-screen settings view in the island's glass (look C of reference/island-setup-previews.html): a small
/// island at the top with the steps, large centred text, glass key caps, round buttons at the bottom centre.
/// It only draws and listens. Everything it changes goes through the <see cref="SettingsSession"/> it is given,
/// which keeps the keys, the files and the island in step, so the view never touches a file or Windows itself.
/// The host window gives it the keyboard and decides when it closes: on <see cref="DoneRequested"/>, and on Esc
/// when <see cref="HandleEscape"/> returns false.
/// </summary>
public sealed class SettingsView : UserControl, ISectionHost
{
    /// <summary>The sections, in the order of the steps at the top: what each is called there, its heading and the sentence under it.</summary>
    private static readonly (SettingsSection Id, string Step, string Title, string Sub)[] FullSections =
    [
        (SettingsSection.Key, "Your key", "Your key", "One key brings the island. Change it here whenever you like."),
        (SettingsSection.Pages, "Your pages", "Your pages", "Each page has its own colour. Rename them, recolour them, or add your own."),
        (SettingsSection.OnTheIsland, "On the island", "What goes on the island", "Switch any pick off, or bring a page's starter list back. Add more later with the + button on the island."),
        (SettingsSection.Scenes, "Scenes", "Scenes", "A scene opens a few things together with one key."),
        (SettingsSection.Mode, "Mode", "Mode", "One mode is always on. It decides what may appear by itself while you are busy."),
        (SettingsSection.Glass, "Glass", "Glass", "How the island and this screen look."),
        (SettingsSection.CodingAgents, "Coding agents", "Coding agents", "Tell Island when your coding agent has finished, so it can say so."),
        (SettingsSection.General, "General", "General", "Everyday things: starting with Windows, and how long the island waits."),
    ];

    /// <summary>The steps of the first start (WORK-ORDER-7 section 6), built from the same pieces: the greeting, then the sections of the preview Dan chose.</summary>
    private static readonly (SettingsSection Id, string Step, string Title, string Sub)[] SetupSections =
    [
        .. FirstStart.Steps.Select(s => (SectionOf(s.Step), s.Name, s.Title, s.Sub)),
    ];

    private static SettingsSection SectionOf(SetupStep step) => step switch
    {
        SetupStep.Welcome => SettingsSection.Welcome,
        SetupStep.Key => SettingsSection.Key,
        SetupStep.Practice => SettingsSection.Practice,
        SetupStep.Pages => SettingsSection.Pages,
        SetupStep.OnTheIsland => SettingsSection.OnTheIsland,
        SetupStep.Addon => SettingsSection.Addon,
        _ => SettingsSection.Mode,
    };

    private readonly (SettingsSection Id, string Step, string Title, string Sub)[] _sections;
    private readonly string[] _stepNames;
    private readonly bool _setup;

    private int SectionIndex => Array.FindIndex(_sections, x => x.Id == _section);

    private readonly SettingsSession _session;
    private readonly Grid _root = new();
    private readonly Grid _layout = new() { Margin = new Thickness(24, 20, 24, 26) };
    private readonly StepIsland _island = new();
    private readonly ScrollViewer _scroll = new();
    private readonly Grid _scrollContent = new();
    private readonly StackPanel _page = new();
    private readonly StackPanel _foot = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
    private readonly ConfirmOverlay _overlay = new();
    private SettingsSection _section = SettingsSection.Key;
    private GlassKind _glass;
    private string? _capturing;
    private string? _notice;
    private string? _openPanel;

    /// <param name="setup">The first start's steps instead of the settings sections: the same pieces, saved the moment each is touched.</param>
    public SettingsView(SettingsSession session, bool setup = false)
    {
        _session = session;
        _setup = setup;
        _sections = setup ? SetupSections : FullSections;
        _stepNames = [.. _sections.Select(x => x.Step)];
        _section = _sections[0].Id;
        _glass = session.EffectiveGlass;
        Focusable = false;
        FocusVisualStyle = null;
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Parts.ScrollBarStyle());

        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.Focusable = false;
        _page.HorizontalAlignment = HorizontalAlignment.Stretch;
        _page.MaxWidth = Look.ContentMaxWidth;
        _page.VerticalAlignment = setup ? VerticalAlignment.Top : VerticalAlignment.Center; // the first start's heading stays where it is from step to step (Dan's P21, WORK-ORDER-13); the settings sections keep the approved centring
        _scrollContent.Margin = new Thickness(0, 18, 0, 18);
        _scrollContent.Children.Add(_page);
        _scroll.Content = _scrollContent;
        _scroll.SizeChanged += (_, _) => _scrollContent.MinHeight = Math.Max(0, _scroll.ActualHeight - 36); // centres a short page; a tall one scrolls

        Grid.SetRow(_island, 0);
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(_foot, 2);
        _layout.Children.Add(_island);
        _layout.Children.Add(_scroll);
        _layout.Children.Add(_foot);

        _root.Children.Add(_layout);
        _root.Children.Add(_overlay);
        Content = _root;
        ApplyGlass();

        _island.StepChosen += step => { if (step >= 0 && step < _sections.Length) Section = _sections[step].Id; };
        _overlay.Closed += FocusTheAsker;
        Loaded += (_, _) => _session.Changed += OnSessionChanged;
        Unloaded += (_, _) => _session.Changed -= OnSessionChanged;

        Rebuild(null);
    }

    /// <summary>Raised when the Done button is pressed. The host closes the screen (the ball shrinks away).</summary>
    public event EventHandler? DoneRequested;

    public SettingsSection Section
    {
        get => _section;
        set
        {
            if (!Enum.IsDefined(value) || Array.FindIndex(_sections, x => x.Id == value) < 0) return; // a step of the setup is not a section of the settings, and the other way round
            CancelCapture();
            _openPanel = null;
            _notice = null;
            _section = value;
            Rebuild("foot:next");
        }
    }

    /// <summary>The glass this screen is tinted for. Follows the session's glass choice by itself; the host sets it once when the screen opens.</summary>
    public GlassKind Glass
    {
        get => _glass;
        set
        {
            _glass = value;
            ApplyGlass();
        }
    }

    /// <summary>True while Esc would be used up by the screen itself: a key is being captured, a question is open, the Add panel of a page is open, or a text is being edited.</summary>
    public bool WantsEscape => _capturing is not null || _overlay.IsOpen || AddByHand.PageOf(_openPanel) is not null || EditHooks.Get(Keyboard.FocusedElement as DependencyObject) is not null;

    /// <summary>
    /// Esc, offered to the screen first. Returns true when the screen used it (it cancelled a key capture, a question
    /// a text edit or the Add panel) and false when the host should close the screen.
    /// </summary>
    public bool HandleEscape()
    {
        if (_overlay.Cancel()) return true;
        if (_capturing is not null)
        {
            CancelCapture();
            return true;
        }

        if (EditHooks.Get(Keyboard.FocusedElement as DependencyObject) is { } cancel)
        {
            cancel();
            return true;
        }

        if (AddByHand.PageOf(_openPanel) is { } pageId)
        {
            AddByHand.Close(this, pageId); // the "Add…" panel of a page (its fields do not use EditHooks, so Esc in them lands here)
            return true;
        }

        return false;
    }

    /// <summary>Puts the keyboard on the first thing that can use it. The host calls this once the screen is on show.</summary>
    /// <summary>For the self-test and the tests, which press no mouse button: presses the button of this key as a click would. False when there is none.</summary>
    public bool PressButton(string key)
    {
        if (FindByKey(key) is not System.Windows.Controls.Primitives.ButtonBase button) return false;
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        return true;
    }

    public void FocusFirst() => (FindByKey("key:main") ?? FindByKey("foot:next"))?.Focus();

    /// <summary>Stops the light on the small island's edge at a given moment, so that a picture of the screen is the same every time.</summary>
    public void FreezeAnimations(double seconds) => _island.FreezeAt(seconds);

    /// <summary>How many pieces of text on the screen contain <paramref name="part"/>. For checks, never for names.</summary>
    public int CountTextContaining(string part) => TextsIn(this).Count(t => t.Contains(part, StringComparison.Ordinal));

    // ---- Navigation ----------------------------------------------------------

    /// <summary>What the button under the page does: on to the next step, or on the last one the screen is done. The self-test presses it through here.</summary>
    public void PressContinue()
    {
        if (SectionIndex >= _sections.Length - 1) DoneRequested?.Invoke(this, EventArgs.Empty);
        else Section = _sections[SectionIndex + 1].Id;
    }

    /// <summary>Raised by "Run the setup again" in General: the host closes this screen and opens the first-start steps.</summary>
    public event EventHandler? SetupRequested;

    /// <summary>True when this view shows the first-start steps.</summary>
    public bool IsSetup => _setup;

    private SettingsSection? _toldSection;

    private FrameworkElement? NoticeLine()
    {
        FrameworkElement? Walk(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement element && FocusKey.Get(child) == "notice") return element;
                if (Walk(child) is { } found) return found;
            }

            return null;
        }

        return Walk(this);
    }

    private void Rebuild(string? focusKey)
    {
        var keep = focusKey ?? FocusKey.Get(Keyboard.FocusedElement as DependencyObject);
        var (_, _, title, sub) = _sections[SectionIndex];

        _page.Children.Clear();
        _page.Children.Add(Parts.Heading(title));
        _page.Children.Add(Parts.Subheading(sub));
        var body = BuildBody();
        body.Margin = new Thickness(0, 26, 0, 0);
        _page.Children.Add(body);

        _island.Show(_setup ? FirstStart.Heading : "Settings", _stepNames, SectionIndex, AccentColour);
        BuildFoot();
        if (!string.IsNullOrEmpty(_notice))
        {
            // A refusal or a warning is shown at the foot of the section: scrolled into view, so that it is not missed below the fold.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var line = NoticeLine();
                line?.BringIntoView();
                // The line is a new element at every build, so a screen reader that was told it is a live region has not seen this one change: its peer is made here and the change is raised (Dan's P17, WORK-ORDER-13).
                if (line is not null && System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged)
                    && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(line) is { } peer)
                    peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
            });
        }

        if (_toldSection != _section)
        {
            _toldSection = _section;
            var heading = _page.Children[0];
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                // The heading is a new element at every build: its peer is made here (FromElement answers null for an element nobody asked a peer for), and the event is raised only when a client listens.
                if (System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged)
                    && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(heading) is { } peer)
                    peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
            });
        }

        if (keep is not null)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => (FindByKey(keep) ?? FindByKey("foot:next"))?.Focus());
    }

    private FrameworkElement BuildBody() => _section switch
    {
        SettingsSection.Key => KeySection.Build(this),
        SettingsSection.Pages => PagesSection.Build(this),
        SettingsSection.OnTheIsland => PicksSection.Build(this),
        SettingsSection.Scenes => ScenesSection.Build(this),
        SettingsSection.Mode => ModeSection.Build(this),
        SettingsSection.Welcome => WelcomeSection.Build(this),
        SettingsSection.Practice => PracticeSection.Build(this),
        SettingsSection.Addon => AddonSection.Build(this),
        SettingsSection.Glass => GlassSection.Build(this),
        SettingsSection.CodingAgents => AgentsSection.Build(this),
        _ => GeneralSection.Build(this),
    };

    private void BuildFoot()
    {
        _foot.Children.Clear();
        if (SectionIndex > 0)
        {
            var back = Parts.Pill("Back", false, () => Section = _sections[SectionIndex - 1].Id, "foot:back");
            back.Margin = new Thickness(0, 0, 8, 0);
            _foot.Children.Add(back);
        }

        var last = SectionIndex == _sections.Length - 1;
        _foot.Children.Add(Parts.Pill(_setup ? FirstStart.ButtonText(SectionIndex) : last ? "Done" : "Continue", true, PressContinue, "foot:next"));
    }

    private void ApplyGlass() => _root.Background = Look.TintFor(_glass);

    private void OnSessionChanged(SettingsArea area)
    {
        if (area == SettingsArea.Glass) Glass = _session.EffectiveGlass;
    }

    // ---- ISectionHost (explicit: the sections' view of this screen is not public API) ----------

    SettingsSession ISectionHost.Session => _session;

    string ISectionHost.Accent => AccentColour;

    string? ISectionHost.Notice
    {
        get => _notice;
        set => _notice = value;
    }

    string? ISectionHost.OpenPanel
    {
        get => _openPanel;
        set => _openPanel = value;
    }

    string? ISectionHost.Capturing => _capturing;

    void ISectionHost.BeginCapture(string actionId)
    {
        _capturing = actionId;
        _notice = null;
        Rebuild(KeySection.FocusKeyOf(actionId));
    }

    void ISectionHost.CancelCapture() => CancelCapture();

    void ISectionHost.Refresh(string? focusKey) => Rebuild(focusKey);

    private string? _askedFrom;

    /// <summary>The screen is enabled again and the keyboard goes back to the control that opened the question (answering Yes rebuilds the section and puts it there too; answering No or Esc only collapses the question).</summary>
    private void FocusTheAsker()
    {
        _layout.IsEnabled = true;
        var from = _askedFrom;
        _askedFrom = null;
        if (from is not null) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FindByKey(from)?.Focus());
    }

    void ISectionHost.Ask(string text, string yesText, Action onYes, string noText)
    {
        _askedFrom = FocusKey.Get(Keyboard.FocusedElement as DependencyObject);
        _layout.IsEnabled = false;
        _overlay.Ask(text, yesText, onYes, noText);
    }

    void ISectionHost.RequestSetup() => SetupRequested?.Invoke(this, EventArgs.Empty);

    void ISectionHost.RequestPractice() => PracticeRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raised by "Start the practice" on the step Try it: the host hides the screen and practises on the real island, then brings the screen back on the next step.</summary>
    public event EventHandler? PracticeRequested;

    void ISectionHost.Report(SessionResult result) => Report(result);

    /// <summary>The first four pages' colours stand for the four steps, as the reference uses a page colour per step.</summary>
    private string AccentColour => _session.Pages.Pages[Math.Min(SectionIndex, _session.Pages.Pages.Count - 1)].Color;

    private void CancelCapture()
    {
        if (_capturing is not { } was) return;
        _capturing = null;
        _notice = null;
        Rebuild(KeySection.FocusKeyOf(was));
    }

    private void Report(SessionResult result)
    {
        if (result.Waiting) return;
        _notice = result.Refusal ?? result.Warning;
    }

    // ---- Keys -------------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || _overlay.IsOpen) return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && HandleEscape())
        {
            e.Handled = true;
            return;
        }

        if (_capturing is not null)
        {
            e.Handled = true; // while a key is being chosen nothing else may react to it
            if (e.IsRepeat) return; // the auto-repeat of the key that started the wait (Enter, held) is not a new key
            Capture(e);
        }
    }

    private void Capture(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

        var modifiers = Keyboard.Modifiers;
        var press = new KeyPress(
            KeyInterop.VirtualKeyFromKey(key),
            (modifiers.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Control : 0)
            | (modifiers.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0),
            modifiers.HasFlag(ModifierKeys.Windows));

        var target = _capturing!;
        var result = _session.PressKey(target, press);
        if (result.Waiting) return;

        Report(result);
        if (result.Refusal is null) _capturing = null; // taken, or the same key again; a refusal keeps listening
        Rebuild(KeySection.FocusKeyOf(target));
    }

    // ---- Finding things -----------------------------------------------------------

    /// <summary>
    /// For the self-test's snapshots: shows the "What goes on the island" section with the "Add…" panel of one page open, and types into its two fields what the picture needs
    /// (the narrowing text of the program list, the address of the website). Invented text only.
    /// </summary>
    public void ShowAddPanel(string pageId, string? narrowText = null, string? siteText = null)
    {
        Section = SettingsSection.OnTheIsland; // changing the section closes any panel: set it first
        _openPanel = AddByHand.PanelOf(pageId);
        Rebuild(null);
        UpdateLayout();
        if (narrowText is not null && FindByKey($"add:filter:{pageId}") is TextBox narrow) narrow.Text = narrowText;
        if (siteText is not null && FindByKey($"add:site:{pageId}") is TextBox site) site.Text = siteText;
        UpdateLayout();
    }

    private IInputElement? FindByKey(string key) => FindEnabled(key) ?? FindEnabled(PartnerOf(key));

    private static string? PartnerOf(string key) =>
        key.EndsWith("-less", StringComparison.Ordinal) ? key[..^5] + "-more" : key.EndsWith("-more", StringComparison.Ordinal) ? key[..^5] + "-less" : null;

    private IInputElement? FindEnabled(string? key)
    {
        IInputElement? Walk(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is IInputElement { Focusable: true, IsEnabled: true } input && FocusKey.Get(child) == key) return input; // a disabled control cannot take the keyboard: the caller falls back to Continue
                if (Walk(child) is { } found) return found;
            }

            return null;
        }

        return key is null ? null : Walk(this);
    }

    private static IEnumerable<string> TextsIn(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock { Text.Length: > 0 } block) yield return block.Text;
            foreach (var text in TextsIn(child)) yield return text;
        }
    }
}
