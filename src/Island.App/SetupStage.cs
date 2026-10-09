using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 6, on a temporary data folder and through the code the screens call: each step of the setup is opened and drawn into
/// review/setup/; leaving early with Esc keeps what was chosen so far and does not open the island; "Done" ends with the island open and a settings file
/// in the temporary folder; a stand-in window that was in front is in front again, or foregroundGranted: false. The setup never starts by itself here
/// (FirstStart says so for the self-test), so every opening below is the stage's own call.
/// </summary>
internal sealed class SetupStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot, string folder)
{
    /// <summary>Keys are only drawn here: nothing is registered with Windows.</summary>
    private sealed class AcceptEverything : IHotkeyRegistrar
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

    public async Task RunAsync()
    {
        Snapshots();
        await WalkAsync();
    }

    private void Snapshots()
    {
        var dir = Path.Combine(tempRoot, "setup-snapshots");
        Directory.CreateDirectory(dir);
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"), Path.Combine(dir, "scenes.json"));
        Settings.Defaults.Save(files.SettingsPath);
        var installed = StarterPicks.Programs.Select(p => new InstalledProgram(p.Name, p.ExeCandidates[0], null, "launch-" + p.Name)).ToList();
        var session = new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), PageStore.Load(files.PagesPath),
            new PickStoreLoad(new PickStore(StarterPicks.Build(installed)), PickStoreStatus.Loaded, null), new AcceptEverything(), () => installed, () => false);
        var view = new SettingsView(session, setup: true);
        view.FreezeAnimations(1.0);

        var target = Path.Combine(folder, "setup");
        Directory.CreateDirectory(target);
        var written = 0;
        foreach (var (section, file) in new[] { (SettingsSection.Welcome, "welcome.png"), (SettingsSection.Key, "key.png"), (SettingsSection.Practice, "practice.png"), (SettingsSection.Pages, "pages.png"), (SettingsSection.OnTheIsland, "island.png"), (SettingsSection.Addon, "chrome.png"), (SettingsSection.Mode, "mode.png") })
        {
            view.Section = section;
            var stage = new Grid { Width = 1920, Height = 1080, Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x1F, 0x3A), Color.FromRgb(0x2A, 0x18, 0x40), 90) };
            stage.Children.Add(view);
            stage.Measure(new Size(1920, 1080));
            stage.Arrange(new Rect(0, 0, 1920, 1080));
            stage.UpdateLayout();
            Snapshot.Of(stage, 1920, 1080, 96).SavePng(Path.Combine(target, file));
            stage.Children.Clear();
            written++;
        }

        report.Check("each of the seven steps of the setup was drawn into review/setup", written == FirstStart.Steps.Count && File.Exists(Path.Combine(target, "chrome.png")) && File.Exists(Path.Combine(target, "mode.png")),
            "welcome, key, try it, pages, island, chrome, mode (a snapshot proves it draws, not that it looks right)");
        report.Check("the setup shows the defaults: the starter picks and the mode Vibe is the one the table starts from", session.AllPicks.Count > 0 && Settings.Defaults.Mode == Mode.Vibe, $"{session.AllPicks.Count} picks, mode {Settings.Defaults.Mode}");
    }

    private async Task WalkAsync()
    {
        var other = new Window { Title = "Island self-test: stand-in for another program", Width = 240, Height = 90, Left = 40, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual };
        other.Show();
        var before = new WindowInteropHelper(other).EnsureHandle();
        await Task.Delay(150);
        OutsideForeground.BringForward(before);
        var granted = false;

        try
        {
            var files = new AppFiles(Path.Combine(tempRoot, "setup-walk"));
            using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
            if (!report.Check("the app starts on the temporary folder", host is not null, "AppHost.Start")) return;
            var m = host!.Runtime.Controller.Machine;
            OutsideForeground.BringForward(before); // starting the app may have moved the foreground: the stand-in goes back in front first
            await Task.Delay(200);
            granted = Native.GetForegroundWindow() == before;
            report.Info["foregroundGrantedForSetup"] = granted;
            report.Check("a start under the self-test does not open the setup by itself", !host.Screen.IsOpen, "FirstStart says never under --selftest");

            // Esc half way: what was chosen is kept, the island does not open.
            host.OpenSetup();
            var open = await Waiter.UntilAsync(() => host.Screen.Phase == SettingsScreen.SettingsScreenPhase.Open, "the setup open", hangLimit, report);
            var view = host.Screen.View;
            report.Check("the setup opens as the first step with its own button", open && view is { IsSetup: true } && view.CountTextContaining("Welcome to Island") > 0, $"phase {host.Screen.Phase}");
            view?.PressContinue();
            view?.PressContinue(); // the pages step
            var made = host.Screen.Session!.CreatePage("Setup page", "#3FD0FF");
            report.Check("a page made in the setup is accepted", made.Ok, made.Refusal ?? "ok");
            host.Screen.Close(); // Esc
            var gone = await Waiter.UntilAsync(() => !host.Screen.IsOpen, "the setup closed by Esc", hangLimit, report);
            report.Check("leaving early keeps what was chosen so far and does not open the island",
                gone && PageStore.Load(files.PagesPath).Store.Pages.Any(p => p.Name == "Setup page") && m.Phase == IslandPhase.Hidden, $"island {m.Phase}");

            // The step Try it (Dan's tutorial): "Start the practice" steps the screen back and asks the host to practise on the real island; skipping the tutorial brings the screen back on the next step.
            host.OpenSetup();
            await Waiter.UntilAsync(() => host.Screen.Phase == SettingsScreen.SettingsScreenPhase.Open, "the setup open for the practice step", hangLimit, report);
            var practiceView = host.Screen.View;
            practiceView?.PressContinue();
            practiceView?.PressContinue(); // Welcome, Key, and now Try it
            var onTryIt = practiceView?.Section == SettingsSection.Practice;
            var requested = 0;
            host.Screen.PracticeRequested += () => requested++;
            var pressed = practiceView?.PressButton("practice:start") == true;
            await Task.Delay(200);
            report.Check("the step after the key is Try it, and Start the practice steps the screen back, asks for the practice and hands the keys to the island",
                onTryIt && pressed && requested == 1 && host.Screen.SteppedBack && host.Practice is not null && m.Phase == IslandPhase.Hidden, $"on the step {onTryIt}, pressed {pressed}, asked {requested}, stepped back {host.Screen.SteppedBack}");
            host.Practice?.Balloon.SkipTutorialButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(300);
            report.Check("skipping the tutorial brings the screen back on the next step, the pages, and the practice is gone",
                !host.Screen.SteppedBack && host.Screen.IsOpen && practiceView?.Section == SettingsSection.Pages && host.Practice is null, $"stepped back {host.Screen.SteppedBack}, section {practiceView?.Section}");
            host.Screen.Close();
            await Waiter.UntilAsync(() => !host.Screen.IsOpen, "the setup closed after the practice step", hangLimit, report);

            // Through every step to Done: the screen shrinks, its ball flies up, the island is open.
            host.OpenSetup();
            await Waiter.UntilAsync(() => host.Screen.Phase == SettingsScreen.SettingsScreenPhase.Open, "the setup open again", hangLimit, report);
            for (var i = 0; i < FirstStart.Steps.Count; i++) host.Screen.View?.PressContinue();
            var came = await Waiter.UntilAsync(() => m.Phase != IslandPhase.Hidden && !host.Screen.IsOpen, "the island open after Done", hangLimit, report);
            report.Check("Done ends with the island open and a settings file in the temporary folder", came && File.Exists(files.SettingsPath), $"island {m.Phase}");
            await Task.Delay(250);
            var back = Native.GetForegroundWindow() == before;
            report.Check("the foreground window at the end is the one from before, or foregroundGranted: false", back || !granted, granted ? (back ? "same" : $"different, island keyboard {m.HasKeyboard}, in front: {(Native.GetForegroundWindow() == IntPtr.Zero ? "nothing" : Native.ProcessIdOf(Native.GetForegroundWindow()) == Environment.ProcessId ? "own window" : "another program")}") : "foregroundGranted: false");
            if (!granted)
                report.NeedsHumanVerify.Add("Windows did not let the self-test bring its stand-in window forward (foregroundGranted: false), so the setup's hand-back of the keyboard was not checked: Settings, Run the setup again, Esc, and see that you are back where you were.");
        }
        finally
        {
            other.Close();
        }
    }
}
