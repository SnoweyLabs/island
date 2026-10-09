using System.IO;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>
/// WORK-ORDER-7 section 5, with nothing of the machine's touched: the app starts on a temporary data folder holding a scene of two invented things, a
/// site that is closed (a scene opens it) and a program that is not installed (a scene skips it and says so). The scene's key is pressed through the
/// handler a key reaches. The gate is asked once for the open and refuses it, the skipped one is reported once, the island comes in without the keyboard
/// and its text block reads the scene's name and "1 opened", a key held down runs the scene once, and the foreground window at the end is the one from
/// before. The tray's balloon is not shown under the self-test (it can make a sound): the app counts it instead.
/// </summary>
internal sealed class ScenesStage(SelfTestReport report, TimeSpan hangLimit, string tempRoot)
{
    private static readonly HotkeyCombo SceneKey = HotkeyCombo.Parse("Ctrl+Alt+Shift+F8");

    public async Task RunAsync()
    {
        var dir = Path.Combine(tempRoot, "scenes-stage");
        Directory.CreateDirectory(dir);
        var files = new AppFiles(dir);
        var site = Scenes.ThingFrom(Pick.ForSite("Probe site", "probe.invalid", PageIds.Apps))!;
        var ghost = Scenes.ThingFrom(Pick.ForProgram("Ghost", PageIds.Apps, "ghost-never-installed.exe", null))!;
        var created = SceneStore.Empty.Create("Evening");
        var store = created.Store;
        var made = created.Scene!;
        store = store.SetThings(made.Id, [site, ghost]).Store;
        report.Check("the scenes file of the temporary folder holds one scene of two things and is read back",
            store.Save(files.ScenesPath) && SceneStore.Load(files.ScenesPath).Store.ById(made.Id)?.Things.Count == 2, "scenes.json in the temporary folder");
        (Settings.Defaults with { Mode = Mode.Focus }).WithPickKey(KeybindEditor.SceneActionId(made.Id), SceneKey).Save(files.SettingsPath);

        var before = Native.GetForegroundWindow();
        using var host = AppHost.Start(files, quit: () => { }, summonOnStart: false);
        if (!report.Check("the app starts on the temporary folder with the scene", host is not null, "AppHost.Start")) return;
        var c = host!.Runtime.Controller;
        var m = c.Machine;

        var opened = OutsideGate.Current.Refused(OutsideKind.OpenAddress);
        var told = host.Tray.Notified;
        host.Press(SceneKey);
        var came = await Waiter.UntilAsync(() => m.Phase != IslandPhase.Hidden && host.Runtime.View.Contents.TitleText == "Evening", "the island in with the scene's name", hangLimit, report);
        report.Check("the scene's key asks the gate for one open, which is refused and counted", OutsideGate.Current.Refused(OutsideKind.OpenAddress) == opened + 1, $"refused {OutsideGate.Current.Refused(OutsideKind.OpenAddress) - opened}");
        report.Check("the island comes in without the keyboard", came && !m.HasKeyboard, $"keyboard {m.HasKeyboard}");
        report.Check("its text block reads the scene's name and \"1 opened\"", host.Runtime.View.Contents.TitleText == "Evening" && host.Runtime.View.Contents.SubtitleText == "1 opened",
            $"{host.Runtime.View.Contents.TitleText} / {host.Runtime.View.Contents.SubtitleText}");
        report.Check("one message names what was skipped (SCENE_PART_MISSING), once", host.Tray.Notified == told + 1 && host.LastRefusalCode == "SCENE_PART_MISSING", $"{host.Tray.Notified - told} message, {host.LastRefusalCode}");

        // A key held down repeats its message: the scene runs once.
        host.Press(SceneKey);
        host.Press(SceneKey);
        await Task.Delay(150);
        report.Check("a key held down runs the scene once, not again and again", OutsideGate.Current.Refused(OutsideKind.OpenAddress) == opened + 1 && host.Tray.Notified == told + 1, "the repeats were swallowed");

        report.Check("the foreground window at the end is the one from before, or foregroundGranted: false", Native.GetForegroundWindow() == before, "a scene took nothing");
    }
}
