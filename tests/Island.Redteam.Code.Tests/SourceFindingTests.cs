using System.Text.RegularExpressions;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: defects of the app's windows and wiring that no test may run (nothing here starts the app or shows a window), shown by the app's own source text. Each test
/// states what a repair must contain, in the loosest words that still prove it; any of the usual repairs makes it pass.
/// </summary>
public class SourceFindingTests
{
    private static string Section(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "not found: " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return to < 0 ? text[from..] : text[from..to];
    }

    [Fact]
    public void Defect_Settings_Screen_Window_Closed_By_Alt_F4_Leaves_The_Screen_Open_For_Ever_And_The_Frame_Handler_On()
    {
        // code-1-11 (MEDIUM): SettingsScreen.ScreenWindow (WindowStyle None, no title bar) handles only Esc (PreviewKeyDown -> OnKey). Alt+F4 (or any other WM_CLOSE: the system closing windows)
        // closes a WPF window by itself. The only code that ends the screen is Finish(): it removes CompositionTarget.Rendering -= OnFrame, sets Phase Closed, gives the keyboard back and
        // raises Gone, and SettingsScreen.Open resets _window only in Gone. Nothing handles Closed, Closing or OnClosed. After Alt+F4: _window stays set, so Open() returns at its first line
        // ("if (_window is not null) return;"): the tray's Settings and "Run the setup again" never open again until Island is restarted; OnFrame keeps running on the closed window on every
        // frame, and the keyboard is not given back. The same for the first-start steps (the same window). UNVERIFIED by running (no window may be shown here); the source has no handler at all.
        // Expected: the window's own close is handled: ScreenWindow overrides OnClosed / handles Closed or Closing (to run what Finish() runs), or Alt+F4 is turned into BeginClose().
        var source = Repo.Text("src", "Island.App", "SettingsScreen.cs");
        var window = Section(source, "private sealed class ScreenWindow", "<<end>>");
        var handlesClose = Regex.IsMatch(window, @"OnClosed\s*\(|OnClosing\s*\(|\bClosed\s*\+=|\bClosing\s*\+=") || Regex.IsMatch(window, @"Key\.F4|Key\.System");
        Assert.True(handlesClose, "ScreenWindow does not handle being closed by anything but Finish()");
    }

    [Fact]
    public void Defect_A_Throw_In_AppHost_Start_Leaves_A_Window_Less_Process_Holding_The_Single_Instance_Lock()
    {
        // code-1-14 (MEDIUM, rare): App.OnStartup calls AppHost.Start inside no try. Start takes the single-instance mutex first (SingleInstance.TryAcquire) and then builds the world, the
        // windows, the tray icon and the pipe; anything that throws after that (a window that cannot be made, a tray icon, a reader) reaches the DispatcherUnhandledException handler,
        // which sets args.Handled = true and writes the line through "_host?.Files.Log(...)", where _host is still null: nothing is logged, the process does NOT exit (ShutdownMode is
        // OnExplicitShutdown) and no tray icon or window exists. The mutex stays held, so run.cmd says "already running" and stop.cmd's event has no subscriber yet (single.QuitRequested is
        // wired at the end of Start). Only Task Manager ends it. Expected: a failed start ends the process (Shutdown with a code) and says so in the log.
        var source = Repo.Text("src", "Island.App", "App.xaml.cs");
        var startup = Section(source, "protected override async void OnStartup", "protected override void OnExit");
        var guarded = Regex.IsMatch(startup, @"try\s*\{[^}]*AppHost\.Start", RegexOptions.Singleline)
                      || Regex.IsMatch(Section(startup, "DispatcherUnhandledException", "TaskScheduler"), @"Shutdown\s*\(");
        Assert.True(guarded, "a throw out of AppHost.Start is swallowed by the unhandled-exception handler and the process stays");
    }

    [Fact]
    public void Defect_IconCache_Remembers_A_Failed_Or_Timed_Out_Read_For_The_Rest_Of_The_Run()
    {
        // code-1-13 (MEDIUM): ProgramIcons.ProgramIcon does not remember a miss ("a miss is not cached: the catalog may not have been read yet") and OnOwnThread gives up after 8 s on its one
        // STA thread; IconCache.Fetch (picks) and TerminalsPage.Fetch (terminals) store whatever came back, a null included, in a dictionary whose entry Get() trusts for ever, and
        // _requested stops a second ask. So a program whose icon was asked for before InstalledProgramCatalog had finished its first read (the island's first draw is at start-up: the
        // pick rows are made in IslandRuntime's constructor, a few hundred ms after the catalog's thread began), or while the one icon thread was held by a hand-made place on an offline
        // network drive (ReadTarget on a UNC path can block for the share's time-out, every other ask times out at 8 s), shows its two letters until Island is restarted.
        // The race with the catalog is UNVERIFIED on this laptop (nothing may be run); the null that is kept for ever is in the source. Expected: a null is not kept (or is asked again later).
        var picks = Repo.Text("src", "Island.App", "PickPages.cs");
        var fetch = Section(picks, "private void Fetch(Pick pick)", "/// <summary>\n/// What the island's pages show");
        var keepsNull = Regex.IsMatch(fetch, @"_icons\[pick\.Id\]\s*=\s*icon\s*;") && !Regex.IsMatch(fetch, @"if\s*\(\s*icon\s+is\s+not\s+null\s*\)\s*_icons\[pick\.Id\]|_requested\.TryRemove|icon\s+is\s+null[^;]*_requested");
        Assert.False(keepsNull, "IconCache.Fetch stores a null icon for the pick's id and never asks again");
    }

    [Fact]
    public void The_Light_Windows_Are_Kept_Directly_Above_The_Islands_Windows_And_Never_Inserted_After_Themselves()
    {
        // code-1-18 (repaired in WORK-ORDER-12 section 4; the statement that was here said the windows were placed only in Show). Now: every beat of Follow asks Windows whether the rim is directly above the
        // island's window (and the glow above the shadow window) and puts it back when a click raised the island's window; and PlaceAbove leaves the stacking order alone when the window directly
        // above the anchor is the one being placed. The self-test's LightStage checks the order after a re-show and after a raise (it can run only with the screen lock).
        var light = Repo.Text("src", "Island.Glass", "MovingLight.cs");
        Assert.Contains("KeepOrder();", Section(light, "private void FollowCore(in LightFrame frame)", "private void KeepOrder()"));
        Assert.Contains("PlaceAbove(_rimWindow, _anchor, r)", Section(light, "private void ShowCore(double nowMs)", "public void Hide()"));
        var window = Repo.Text("src", "Island.Glass", "GlassWindow.cs");
        Assert.Contains("above == window ? SWP_NOZORDER : 0", window);
        Assert.Contains("catch (COMException e)", Section(light, "public void Follow(in LightFrame frame)", "private void FollowCore"));
    }
}
