using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack5.Tests;

/// <summary>A machine and a clock of whole frames, so a test can say "run 300 ms".</summary>
internal sealed class Clock
{
    public const double Frame = 1000.0 / 60;

    public Clock(IslandMachine machine) => M = machine;

    public IslandMachine M { get; }

    public double Now { get; set; }

    public void Run(double ms)
    {
        var end = Now + ms;
        while (Now < end)
        {
            Now += Frame;
            M.Tick(Now);
        }
    }

    /// <summary>The Apps page, shown and at rest.</summary>
    public static Clock OpenOnApps(Func<Page, IReadOnlyList<Item>>? itemsOf = null, double idleSeconds = 30)
    {
        var c = new Clock(new IslandMachine(idleSeconds, Pages.BuiltIn, itemsOf));
        c.M.PageKey(PageIds.Apps, c.Now);
        c.Run(2000);
        return c;
    }
}

internal static class Make
{
    public static IReadOnlyList<Item> Items(int picks, bool plus = true)
    {
        var items = Enumerable.Range(0, picks).Select(i => new Item($"Pick {i}", "open", "Pk", i * 10, PickId: $"program:p{i}")).ToList();
        if (plus) items.Add(new Item("Add something", "0 open", "+", 0, IsPlus: true));
        return items;
    }

    public static NowPlayingView Session(string app, bool paused = false, string where = "Alpha") =>
        new("Track", null, where, where, paused ? PlaybackState.Paused : PlaybackState.Playing, paused, null, null, null,
            new MediaTarget(MediaTargetKind.Session, "s1"), true, app, null, false);

    public static NowPlayingView Tab(string host, bool paused = false, string where = "Beta") =>
        new("Clip", null, where, where, paused ? PlaybackState.Paused : PlaybackState.Playing, paused, null, null, null,
            new MediaTarget(MediaTargetKind.Tab, "t1"), true, null, host, false);
}

/// <summary>A pretend Windows that grants every key.</summary>
internal sealed class HoldingRegistrar : IHotkeyRegistrar
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

/// <summary>A temporary folder that is removed afterwards, read-only files included.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("island-attack5-");

    public string File(string name) => Path.Combine(_dir.FullName, name);

    public bool Exists(string name) => System.IO.File.Exists(File(name));

    public void Dispose()
    {
        foreach (var f in _dir.GetFiles()) f.Attributes = FileAttributes.Normal;
        _dir.Delete(recursive: true);
    }
}
