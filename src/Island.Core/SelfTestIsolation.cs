namespace Island.Core;

/// <summary>
/// The names a copy of the app uses to find another copy of itself: the "only one copy" lock and the two events
/// ("again": a second copy started and left; "quit": stop.cmd). The self-test has names of its own, so that its
/// copies meet each other and never the copy Dan runs.
/// </summary>
public sealed record InstanceNames(string Running, string Again, string Quit)
{
    /// <summary>What a self-test process holds while it runs, so two self-tests never run at once.</summary>
    public const string SelfTestRun = @"Local\Island.Snowey.SelfTest.Run";

    public static InstanceNames For(bool selfTest) => selfTest
        ? new InstanceNames(@"Local\Island.Snowey.SelfTest.Running", @"Local\Island.Snowey.SelfTest.Again", @"Local\Island.Snowey.SelfTest.Quit")
        : new InstanceNames(@"Local\Island.Snowey.Running", @"Local\Island.Snowey.Again", @"Local\Island.Snowey.Quit");
}

/// <summary>
/// Under the self-test only: when Windows refuses the main key because another program holds it (usually the copy Dan is
/// trying), the self-test's temporary settings get a stand-in key before any key is registered, so the self-test never
/// fights for Dan's key and the check "the registered keys are exactly the ones in the settings" still holds.
/// </summary>
public static class StandInKey
{
    /// <summary>In this order: the first one Windows accepts is used.</summary>
    public static IReadOnlyList<HotkeyCombo> Candidates { get; } =
        [HotkeyCombo.Parse("Ctrl+Alt+Shift+F11"), HotkeyCombo.Parse("Ctrl+Alt+Shift+F10"), HotkeyCombo.Parse("Ctrl+Alt+Shift+F9")];

    /// <summary>The wanted key when Windows accepts it; otherwise the first stand-in not in <paramref name="inUse"/> that it accepts; null when none works.</summary>
    public static HotkeyCombo? Choose(HotkeyCombo wanted, Func<HotkeyCombo, bool> accepts, IReadOnlyCollection<HotkeyCombo>? inUse = null)
    {
        if (accepts(wanted)) return wanted;
        foreach (var candidate in Candidates)
        {
            if (inUse is not null && inUse.Contains(candidate)) continue;
            if (accepts(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Looks at the main key in the settings file and, when Windows refuses it, writes the stand-in into the file.
    /// Returns true only when the file was changed. A file that cannot be read is left exactly as it was.
    /// </summary>
    public static bool Prepare(string settingsPath, IReadOnlyList<Page> pages, Func<HotkeyCombo, bool> accepts)
    {
        var load = Settings.Load(settingsPath, pages);
        if (load.Status != SettingsStatus.Loaded) return false;

        var used = load.Settings.PageKeys.Where(k => k.Combo is not null).Select(k => k.Combo!.Value).ToList();
        var chosen = Choose(load.Settings.ShowHide, accepts, used);
        if (chosen is null || chosen == load.Settings.ShowHide) return false;
        return (load.Settings with { ShowHide = chosen.Value }).Save(settingsPath);
    }
}
