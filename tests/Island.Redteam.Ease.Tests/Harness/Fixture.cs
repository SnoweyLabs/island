using System.IO;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.SettingsUi;

namespace Island.Redteam.Ease.Tests.Harness;

/// <summary>A pretend registrar that accepts every key, as Windows would for a free one.</summary>
internal sealed class AcceptingRegistrar : IHotkeyRegistrar
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

/// <summary>A connector for a helper that does not exist (invented name), so the "Coding agents" section can be built.</summary>
internal sealed class PretendConnector(string helper, string name, AgentConnection state) : IAgentConnector
{
    public string Helper => helper;

    public string DisplayName => name;

    public string Location => @"%USERPROFILE%\.invented\settings.json";

    public string? NotOffered => null;

    public IReadOnlyList<string> LinesToAdd => ["alpha-hook --event done"];

    public AgentConnection State() => state;

    public ConnectorResult Connect() => new(true, null);

    public ConnectorResult Disconnect() => new(true, null);
}

/// <summary>A settings session over temporary files in a folder of its own, with invented programs only; deleted with <see cref="Dispose"/>.</summary>
internal sealed class Fixture : IDisposable
{
    private Fixture(string dir, SettingsSession session, SettingsFiles files)
    {
        Dir = dir;
        Session = session;
        Files = files;
    }

    public string Dir { get; }

    public SettingsFiles Files { get; }

    public SettingsSession Session { get; }

    /// <param name="withHandAdded">Adds a page of its own ("Alpha games") with an invented program and an invented site on it.</param>
    /// <param name="graphicsLight">Whether the graphics-card light counts as available.</param>
    public static Fixture Make(bool withHandAdded = true, bool graphicsLight = true, bool startup = true, bool agents = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "island-ease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"), Path.Combine(dir, "scenes.json"));
        var installed = StarterPicks.Programs.Select(p => new InstalledProgram(p.Name, p.ExeCandidates[0], null, "launch-" + p.Name)).ToList();

        var pages = PageStore.Default;
        var picks = new PickStore(StarterPicks.Build(installed));
        var settings = Settings.Defaults;
        if (withHandAdded)
        {
            pages = pages.Create("Alpha games", "#7CE04A").Store;
            var custom = pages.Pages[^1].Id;
            picks = picks.Add(Pick.ForProgram("Alpha", custom, "alpha.exe", null), out _).Add(Pick.ForSite("Example", "example.org", custom), out _);
        }

        var session = new SettingsSession(
            files,
            new SettingsLoad(settings, SettingsStatus.Loaded, null),
            new PageStoreLoad(pages, PageStoreStatus.Loaded, null),
            new PickStoreLoad(picks, PickStoreStatus.Loaded, null),
            new AcceptingRegistrar(),
            () => installed,
            () => false,
            startup ? new StartupSwitch(new MemoryStartupRegistry(), @"Q:\Invented\Island.exe") : null,
            () => [new RunningProgram("Alpha", "alpha.exe"), new RunningProgram("Beta", "beta.exe")],
            agents ? new PretendConnector("claude", "Claude Code", AgentConnection.NotConnected) : null,
            SceneStore.Load(files.ScenesPath!),
            null,
            agents ? [new PretendConnector("codex", "Codex", AgentConnection.ConnectedOlder)] : null)
        {
            GraphicsLightAvailable = () => graphicsLight,
        };
        return new Fixture(dir, session, files);
    }

    public SettingsView NewView(bool setup = false)
    {
        var view = new SettingsView(Session, setup);
        view.FreezeAnimations(1.0);
        return view;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { /* a temporary folder */ }
        catch (UnauthorizedAccessException) { /* a temporary folder */ }
    }
}
