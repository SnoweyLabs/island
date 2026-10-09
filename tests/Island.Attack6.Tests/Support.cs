using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack6.Tests;

/// <summary>A pretend Windows for keys: it can grant, refuse with a code, or be told to fail for chosen combinations, and it counts what is held.</summary>
internal sealed class PretendRegistrar : IHotkeyRegistrar
{
    private readonly HashSet<HotkeyCombo> _held = [];

    public Func<HotkeyCombo, int>? ErrorFor { get; set; }

    public int Registered { get; private set; }

    /// <summary>How often the same combination was registered while it was already held (the real host answers true and holds it once).</summary>
    public int DoubleRegistrations { get; private set; }

    public int Released { get; private set; }

    public IReadOnlyCollection<HotkeyCombo> Held => _held;

    public bool TryRegister(HotkeyCombo combo, out int error)
    {
        error = ErrorFor?.Invoke(combo) ?? 0;
        if (error != 0) return false;
        Registered++;
        if (!_held.Add(combo)) DoubleRegistrations++; // as the real HotkeyHost: already held by this very app counts as granted
        return true;
    }

    public void Release(HotkeyCombo combo)
    {
        Released++;
        _held.Remove(combo);
    }
}

/// <summary>A temporary folder that is removed afterwards, read-only files and sub-folders included.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack6-");

    public string Root => _dir.FullName;

    public string File(string name) => Path.Combine(_dir.FullName, name);

    public SettingsFiles Files() => new(File("settings.json"), File("pages.json"), File("picks.json"));

    public void Dispose()
    {
        foreach (var f in _dir.GetFiles("*", SearchOption.AllDirectories)) f.Attributes = FileAttributes.Normal;
        _dir.Delete(recursive: true);
    }
}

internal static class Combos
{
    public static HotkeyCombo A => HotkeyCombo.Parse("Ctrl+Alt+A");
    public static HotkeyCombo B => HotkeyCombo.Parse("Ctrl+Alt+B");
    public static HotkeyCombo C => HotkeyCombo.Parse("Ctrl+Alt+C");

    public static KeyPress Press(HotkeyCombo c) => new(c.VirtualKey, c.Modifiers);
}

internal static class Make
{
    public static Pick Program(string name, string page, string exe = "alpha.exe") =>
        Pick.ForProgram(name, page, exe, null);

    /// <summary>A session over a temporary folder with the given picks already saved and a registrar that grants everything.</summary>
    public static SettingsSession Session(TempFolder temp, PretendRegistrar registrar, IEnumerable<Pick>? picks = null, Settings? settings = null, PageStore? pages = null)
    {
        var files = temp.Files();
        var store = new PickStore(picks ?? []);
        store.Save(files.PicksPath);
        (settings ?? Settings.Defaults).Save(files.SettingsPath);
        return new SettingsSession(
            files,
            new SettingsLoad(settings ?? Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(pages ?? PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(store, PickStoreStatus.Loaded, null),
            registrar,
            () => [],
            () => false);
    }
}
