using System.IO;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>
/// WORK-ORDER-6 section 3: the switch for starting with Windows, flipped through the code the settings screen calls, over the REAL
/// outside file but under the self-test's gate: the gate refuses the write and the delete and counts them, so nothing is ever
/// written to the registry and the switch reads "off" afterwards as before. The count goes into selftest.json. The logic of the
/// switch itself is proven by StartupSwitchTests against an in-memory registry.
/// </summary>
internal sealed class StartupStage(SelfTestReport report, string tempRoot)
{
    private sealed class AnyKey : IHotkeyRegistrar
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

    public void Run()
    {
        var dir = Path.Combine(tempRoot, "startup-stage");
        Directory.CreateDirectory(dir);
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"));
        Settings.Defaults.Save(files.SettingsPath);
        var program = Environment.ProcessPath ?? Path.Combine(dir, "Island.App.exe");
        var session = new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), new AnyKey(), () => [], () => false,
            new StartupSwitch(new OutsideStartupRegistry(), program));

        var before = OutsideGate.Current.Refused(OutsideKind.WriteStartupValue);
        var on = session.SetStartWithWindows(true);
        var off = session.SetStartWithWindows(false);
        var refused = OutsideGate.Current.Refused(OutsideKind.WriteStartupValue) - before;

        report.Info["startupRefusals"] = refused;
        report.Check("flipping the switch for starting with Windows through the screen's own code is refused by the gate and counted, and writes nothing",
            !on.Ok && refused >= 1 && (off.Ok || refused == 2) && !File.ReadAllText(files.SettingsPath).Contains("\"startWithWindows\": true", StringComparison.Ordinal),
            $"{refused} refusal(s) counted (switching off with nothing of ours in the list has nothing to delete); the settings file still says off");
        report.Check("the settings file remembers only the yes or no of the switch, and it is off by default",
            Settings.Load(files.SettingsPath).Settings.StartWithWindows == false && Settings.Defaults.StartWithWindows == false, "off");
    }
}
