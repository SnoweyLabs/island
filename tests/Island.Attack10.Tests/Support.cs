using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack10.Tests;

/// <summary>A pretend Windows for keys: grants everything.</summary>
internal sealed class PretendRegistrar : IHotkeyRegistrar
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

/// <summary>A temporary folder under the system's temp path that is removed afterwards, read-only files included.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack10-");

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

/// <summary>A chooser that throws, as a real shell window can (the shell fails, the owner is gone).</summary>
internal sealed class ThrowingChooser : IPlaceChooser
{
    public string? ChooseFile(PlaceChoice what) => throw new InvalidOperationException("the shell window failed");

    public string? ChooseFolder() => throw new InvalidOperationException("the shell window failed");
}

internal static class Make
{
    public const string Profile = @"Q:\Invented\Profile";

    public static SettingsSession Session(TempFolder temp, IEnumerable<Pick>? picks = null, IReadOnlyList<InstalledProgram>? installed = null, bool picksUnreadable = false,
        IPlaceChooser? chooser = null, string? profile = Profile)
    {
        var files = temp.Files();
        var store = new PickStore(picks ?? []);
        if (!picksUnreadable) store.Save(files.PicksPath);
        Settings.Defaults.Save(files.SettingsPath);
        var session = new SettingsSession(
            files,
            new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(store, picksUnreadable ? PickStoreStatus.Unreadable : PickStoreStatus.Loaded, picksUnreadable ? "invented" : null),
            new PretendRegistrar(),
            () => installed ?? [],
            () => false,
            scenes: new SceneStoreLoad(SceneStore.Empty, SceneStoreStatus.Missing, null));
        session.Chooser = chooser;
        session.HandContextSource = () => new HandContext(profile, [("Downloads", Profile + @"\Downloads")]);
        return session;
    }

    public static IconImage Icon(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (r, g, b, a) = pixel(x, y);
            var i = (y * width + x) * 4;
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = a;
        }

        return new IconImage(width, height, bgra);
    }

    public static IconImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255) => Icon(width, height, (_, _) => (r, g, b, a));
}
