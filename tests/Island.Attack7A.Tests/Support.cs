using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack7A.Tests;

/// <summary>A pretend Windows for keys: grants everything and counts what is held.</summary>
internal sealed class PretendRegistrar : IHotkeyRegistrar
{
    private readonly HashSet<HotkeyCombo> _held = [];

    public IReadOnlyCollection<HotkeyCombo> Held => _held;

    public bool TryRegister(HotkeyCombo combo, out int error)
    {
        error = 0;
        _held.Add(combo);
        return true;
    }

    public void Release(HotkeyCombo combo) => _held.Remove(combo);
}

/// <summary>A temporary folder under the system's temp path that is removed afterwards, read-only files included.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack7a-");

    public string Root => _dir.FullName;

    public string File(string name) => Path.Combine(_dir.FullName, name);

    public SettingsFiles Files() => new(File("settings.json"), File("pages.json"), File("picks.json"), File("scenes.json"));

    public void Dispose()
    {
        foreach (var f in _dir.GetFiles("*", SearchOption.AllDirectories)) f.Attributes = FileAttributes.Normal;
        _dir.Delete(recursive: true);
    }
}

/// <summary>The machine with a frame clock, as the app drives it.</summary>
internal sealed class Clock(IslandMachine machine)
{
    public const double Frame = 1000.0 / 60;
    public double Now { get; private set; }

    public IslandMachine M { get; } = machine;

    public void RunTo(double ms)
    {
        while (Now + Frame <= ms)
        {
            Now += Frame;
            M.Tick(Now);
        }

        Now = ms;
        M.Tick(Now);
    }

    public void Run(double forMs) => RunTo(Now + forMs);
}

internal static class Make
{
    public static Pick Program(string name, string page = "apps", string exe = "alpha.exe") => Pick.ForProgram(name, page, exe, null);

    public static SettingsSession Session(TempFolder temp, PretendRegistrar registrar, IEnumerable<Pick>? picks = null, Settings? settings = null)
    {
        var files = temp.Files();
        var store = new PickStore(picks ?? []);
        store.Save(files.PicksPath);
        (settings ?? Settings.Defaults).Save(files.SettingsPath);
        return new SettingsSession(
            files,
            new SettingsLoad(settings ?? Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(store, PickStoreStatus.Loaded, null),
            registrar,
            () => [],
            () => false,
            scenes: new SceneStoreLoad(SceneStore.Empty, SceneStoreStatus.Missing, null));
    }
}
