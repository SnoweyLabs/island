using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

internal static class SessionFixtures
{
    public static SettingsFiles Files(TempDir dir) =>
        new(dir.File("settings.json"), dir.File("pages.json"), dir.File("picks.json"), dir.File("scenes.json"));

    /// <summary>A session on made-up data. Nothing is written until the first change.</summary>
    public static SettingsSession Open(
        SettingsFiles files,
        Settings? settings = null,
        PageStore? pages = null,
        PickStore? picks = null,
        IReadOnlyList<InstalledProgram>? installed = null,
        FakeRegistrar? registrar = null,
        bool blurAvailable = false,
        IAgentConnector? agents = null,
        SceneStoreLoad? scenes = null,
        IStartupSwitch? startup = null)
    {
        var known = installed ?? Fixtures.EverythingInstalled;
        return new SettingsSession(
            files,
            new SettingsLoad(settings ?? Settings.Defaults, SettingsStatus.Loaded, null),
            new PageStoreLoad(pages ?? PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(picks ?? new PickStore(StarterPicks.Build(known)), PickStoreStatus.Loaded, null),
            registrar ?? new FakeRegistrar(),
            () => known,
            () => blurAvailable,
            startup: startup,
            agents: agents,
            scenes: scenes);
    }
}
