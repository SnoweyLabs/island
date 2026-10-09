using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.Redteam.Design.Tests;

/// <summary>Round 2: a settings view on the one STA thread (built, laid out, read, never shown), over the invented session of round 1, with a few things round 2 needs (a pick key, a chosen glass, a click on a control).</summary>
internal static class Round2World
{
    public const string AlphaId = "program:alpha";

    /// <summary>The custom page of <see cref="SettingsWorld.MadeUpSession"/> (its id is made when the world is made).</summary>
    public static string CustomPageId(SettingsSession session) => session.Pages.Pages[^1].Id;

    public static T WithView<T>(bool setup, Func<SettingsView, Grid, SettingsSession, T> body, double width = SettingsWorld.Width, double height = SettingsWorld.Height, Func<string, SettingsSession>? makeSession = null)
    {
        var dir = SettingsWorld.TempFolder();
        try
        {
            return Sta.Run(() =>
            {
                var session = makeSession is null ? SettingsWorld.MadeUpSession(dir) : makeSession(dir);
                var view = new SettingsView(session, setup);
                view.FreezeAnimations(1.0);
                var stage = SettingsWorld.Stage(view, width, height);
                return body(view, stage, session);
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { /* under this test's own bin folder */ }
        }
    }

    /// <summary>The control a section named with this key (the internal <c>FocusKey</c>), anywhere in the view.</summary>
    public static T? ByKey<T>(DependencyObject root, string key) where T : DependencyObject =>
        SettingsWorld.Descendants(root).OfType<T>().FirstOrDefault(c => SettingsWorld.KeyOf(c) == key);

    /// <summary>Presses a button the way a click does (raises its Click event); nothing is shown.</summary>
    public static void Press(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    /// <summary>The yes/no question's overlay, when the view has one open (its type is internal: found by name).</summary>
    public static Grid? OpenQuestion(SettingsView view) =>
        SettingsWorld.Descendants(view).OfType<Grid>().FirstOrDefault(g => g.GetType().Name == "ConfirmOverlay" && g.Visibility == Visibility.Visible);

    /// <summary>The lines a wrapped TextBlock holds and the share of the box the last one fills (read from the position of its last character after layout).</summary>
    public static (int Lines, double LastShare) LinesOf(TextBlock t)
    {
        var lineHeight = t.FontSize * 1.37;
        var lines = Math.Max(1, (int)Math.Round(t.ActualHeight / lineHeight));
        var last = t.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
        return (lines, last.Right / Math.Max(1, t.ActualWidth));
    }

    /// <summary>The same invented world as round 1, with a key on the hand-added program and a chosen glass.</summary>
    public static SettingsSession SessionWithKeyOnAlpha(string dir, GlassKind glass = GlassKind.Approved)
    {
        var made = SettingsWorld.MadeUpSession(dir);
        var custom = CustomPageId(made);
        var settings = made.Settings.WithPickKey(AlphaId, HotkeyCombo.Parse("Ctrl+Alt+F1")) with { Glass = glass };
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"));
        return new SettingsSession(
            files,
            new SettingsLoad(settings, SettingsStatus.Loaded, null),
            new PageStoreLoad(made.Pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(made.Picks, PickStoreStatus.Loaded, null),
            new KeyAccepter(),
            () => [],
            () => false);
    }

    private sealed class KeyAccepter : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }
}
