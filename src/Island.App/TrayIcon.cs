using System.Runtime.InteropServices;
using System.Windows.Threading;
using Island.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Island.App;

/// <summary>
/// The tray icon, drawn in code (no image file): a dark pill with a red ring. Menu: Show / hide,
/// Open settings file, Quit. Without it there would be no way to close the app. Messages shown from
/// it can be hidden by Windows "Do not disturb"; the log always has them.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int BalloonGapSeconds = 7;

    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _drawn;
    private readonly Queue<(string Title, string Text)> _pending = [];
    private readonly Dictionary<string, Action> _openers = [];
    private readonly DispatcherTimer _balloons = new() { Interval = TimeSpan.FromSeconds(BalloonGapSeconds) };

    public TrayIcon(Action showHide, Action openSettings, Action quit,
        Func<GlassKind>? currentGlass = null, Action<GlassKind>? setGlass = null, Func<bool>? blurOffered = null, Action? openScreen = null,
        Func<Mode>? currentMode = null, Action<Mode>? setMode = null,
        Func<IReadOnlyList<(string Id, string Name)>>? scenes = null, Action<string>? runScene = null)
    {
        _drawn = FromProgram() ?? Draw(); // the program's own icon (the island's), or the drawn ring when it cannot be read
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => showHide());
        if (openScreen is not null) menu.Items.Add("Settings", null, (_, _) => openScreen());
        if (scenes is not null && runScene is not null) menu.Items.Add(ScenesMenu(scenes, runScene));
        if (currentGlass is not null && setGlass is not null) menu.Items.Add(GlassMenu(currentGlass, setGlass, blurOffered ?? (() => false), _openers));
        if (currentMode is not null && setMode is not null) menu.Items.Add(ModeMenu(currentMode, setMode, _openers));
        menu.Items.Add("Open settings file", null, (_, _) => openSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _drawn,
            Text = "Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) showHide();
        };

        _balloons.Tick += (_, _) => ShowNext();
    }

    /// <summary>The "Glass" entry: Approved and Darker always, Blur only while the app says it is offered. Check marks are set each time it opens.</summary>
    private static Forms.ToolStripMenuItem GlassMenu(Func<GlassKind> current, Action<GlassKind> set, Func<bool> blurOffered, Dictionary<string, Action> openers)
    {
        var glass = new Forms.ToolStripMenuItem("Glass");
        void Fill()
        {
            glass.DropDownItems.Clear();
            foreach (var kind in new[] { GlassKind.Approved, GlassKind.Darker, GlassKind.Blur })
            {
                if (kind == GlassKind.Blur && !blurOffered()) continue;
                var entry = new Forms.ToolStripMenuItem(kind.ToString()) { Checked = current() == kind };
                var chosen = kind;
                entry.Click += (_, _) => set(chosen);
                glass.DropDownItems.Add(entry);
            }
        }

        glass.DropDownOpening += (_, _) => Fill();
        openers["Glass"] = Fill;
        glass.DropDownItems.Add(new Forms.ToolStripMenuItem("Approved")); // placeholder so the arrow shows before the first opening
        return glass;
    }

    /// <summary>The "Scenes" entry (WORK-ORDER-7 section 5): one entry per scene, read each time it opens; with none, one line that says so.</summary>
    private static Forms.ToolStripMenuItem ScenesMenu(Func<IReadOnlyList<(string Id, string Name)>> scenes, Action<string> run)
    {
        var menu = new Forms.ToolStripMenuItem("Scenes");
        menu.DropDownOpening += (_, _) =>
        {
            menu.DropDownItems.Clear();
            var all = scenes();
            if (all.Count == 0) menu.DropDownItems.Add(new Forms.ToolStripMenuItem("No scenes yet") { Enabled = false });
            foreach (var (id, name) in all)
            {
                var chosen = id;
                menu.DropDownItems.Add(new Forms.ToolStripMenuItem(name, null, (_, _) => run(chosen)));
            }
        };
        menu.DropDownItems.Add(new Forms.ToolStripMenuItem("No scenes yet")); // placeholder so the arrow shows before the first opening
        return menu;
    }

    /// <summary>The "Mode" entry (WORK-ORDER-7 section 1): the three modes, the one that is on ticked. Check marks are set each time it opens.</summary>
    private static Forms.ToolStripMenuItem ModeMenu(Func<Mode> current, Action<Mode> set, Dictionary<string, Action> openers)
    {
        var mode = new Forms.ToolStripMenuItem("Mode");
        void Fill()
        {
            mode.DropDownItems.Clear();
            foreach (var kind in Enum.GetValues<Mode>())
            {
                var entry = new Forms.ToolStripMenuItem(kind.ToString()) { Checked = current() == kind };
                var chosen = kind;
                entry.Click += (_, _) => set(chosen);
                mode.DropDownItems.Add(entry);
            }
        }

        mode.DropDownOpening += (_, _) => Fill();
        openers["Mode"] = Fill;
        mode.DropDownItems.Add(new Forms.ToolStripMenuItem("Focus")); // placeholder so the arrow shows before the first opening
        return mode;
    }

    /// <summary>How many messages were handed to the tray (for the self-test).</summary>
    public int Notified { get; private set; }

    public bool Visible => _icon.Visible;

    /// <summary>The tray's menu, for the self-test (which opens its entries without showing it).</summary>
    internal Forms.ContextMenuStrip? MenuForSelfTest => _icon.ContextMenuStrip;

    /// <summary>Fills an entry of the menu the way its opening does (the entry's text: Glass, Mode), without showing the menu.</summary>
    internal bool OpenEntryForSelfTest(string text)
    {
        if (!_openers.TryGetValue(text, out var open)) return false;
        open();
        return true;
    }
    public bool HasDrawnIcon => _icon.Icon is not null && _icon.Icon.Width > 0;

    /// <summary>Shows a refusal as a notification from the tray icon. Several in a row are shown one after the other.</summary>
    public void Notify(Refusal refusal)
    {
        Notified++;
        if (OutsideGate.Current.SelfTest) return; // a balloon can make a sound: under the self-test it is counted, never shown
        if (!_pending.Contains(("Island", refusal.Message))) _pending.Enqueue(("Island", refusal.Message)); // the same words already waiting are not told twice
        if (!_balloons.IsEnabled) ShowNext();
    }

    private void ShowNext()
    {
        if (_pending.Count == 0)
        {
            _balloons.Stop();
            return;
        }

        var (title, text) = _pending.Dequeue();
        _icon.ShowBalloonTip(BalloonGapSeconds * 1000, title, text, Forms.ToolTipIcon.Warning);
        _balloons.Start();
    }

    /// <summary>The icon of the running program (the one in its own file); null when it cannot be read.</summary>
    private static Drawing.Icon? FromProgram()
    {
        try
        {
            return Environment.ProcessPath is { } path ? Drawing.Icon.ExtractAssociatedIcon(path) : null;
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Drawing.Icon Draw()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var body = new Drawing.SolidBrush(Drawing.Color.FromArgb(235, 20, 22, 32));
            using var ring = new Drawing.Pen(Drawing.Color.FromArgb(255, 255, 64, 85), 3);
            g.FillEllipse(body, 3, 3, 26, 26);
            g.DrawEllipse(ring, 3.5f, 3.5f, 25, 25);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _balloons.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _drawn.Dispose();
    }
}
