using Island.Core;
using Island.Tests.SettingsEdit;

namespace Island.Tests.Packaged;

/// <summary>WORK-ORDER-8 section 3: every place where a package differs has both answers, exercised through a pretend "I am packaged".</summary>
public class PackagedChoiceTests
{
    private static readonly PretendPackage Packaged = new(true);

    [Fact]
    public void Unpackaged_Uses_The_Registry_Switch()
    {
        var registry = new MemoryStartupRegistry();
        var asked = false;
        var chosen = StartupChoice.For(PretendPackage.NotPackaged, () => new StartupSwitch(registry, @"C:\Island\Island.App.exe"), () =>
        {
            asked = true;
            return new MemoryStartupTask();
        });

        Assert.IsType<StartupSwitch>(chosen);
        Assert.False(asked); // the task is never even looked at
        Assert.True(chosen.TurnOn().Ok);
        Assert.Equal(1, registry.Writes);
    }

    [Fact]
    public void Packaged_Uses_The_Startup_Task()
    {
        var registry = new MemoryStartupRegistry();
        var task = new MemoryStartupTask();
        var registryAsked = false;
        var chosen = StartupChoice.For(Packaged, () =>
        {
            registryAsked = true;
            return new StartupSwitch(registry, @"C:\Island\Island.App.exe");
        }, () => task);

        Assert.IsType<PackagedStartupSwitch>(chosen);
        Assert.False(registryAsked);
        Assert.False(chosen.IsOn()); // the manifest declares the task switched off
        Assert.True(chosen.TurnOn().Ok);
        Assert.True(chosen.IsOn());
        Assert.Equal(1, task.Requests);
        Assert.Equal(0, registry.Writes);
        Assert.True(chosen.TurnOff().Ok);
        Assert.False(chosen.IsOn());
        Assert.Equal(1, task.Disables);
    }

    [Fact]
    public void A_User_No_Is_Never_Overridden()
    {
        var task = new MemoryStartupTask(StartupTaskState.DisabledByUser);
        var chosen = new PackagedStartupSwitch(task);

        var result = chosen.TurnOn();

        Assert.False(result.Ok);
        Assert.Equal("STARTUP_OFF_IN_WINDOWS", result.Refusal!.Code);
        Assert.Equal(0, task.Requests); // not even asked: the person's "no" in Windows' own settings stands
        Assert.False(chosen.IsOn());
        Assert.Equal(StartupRefusals.SwitchedOffInWindows.Message, chosen.Explanation); // the switch shows the state the call reports

        // A policy that keeps it off, or on, is not the person's to change either.
        Assert.False(new PackagedStartupSwitch(new MemoryStartupTask(StartupTaskState.DisabledByPolicy)).TurnOn().Ok);
        Assert.False(new PackagedStartupSwitch(new MemoryStartupTask(StartupTaskState.EnabledByPolicy)).TurnOff().Ok);

        // And through the settings session: the refusal reaches the person in words, the file goes back as it was.
        using var dir = new TempDir();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files, startup: new PackagedStartupSwitch(new MemoryStartupTask(StartupTaskState.DisabledByUser)));
        var edit = session.SetStartWithWindows(true);
        Assert.False(edit.Ok);
        Assert.Contains("switched off", edit.Refusal, StringComparison.Ordinal);
        Assert.False(session.Settings.StartWithWindows);
    }

    [Fact]
    public void Packaged_Startup_Launch_Shows_Nothing()
    {
        // The package's start-up task carries no --autostart: the activation kind says it was the task.
        Assert.True(AppLaunch.IsSilentStart([], new PretendPackage(true, StartedByStartupTask: true)));
        Assert.False(AppLaunch.IsSilentStart([], new PretendPackage(true)));
        Assert.False(AppLaunch.IsSilentStart([], PretendPackage.NotPackaged));
        // Unpackaged, only the argument counts; a "started by the task" that cannot be true unpackaged counts for nothing.
        Assert.True(AppLaunch.IsSilentStart([StartupSwitch.AutostartArgument], PretendPackage.NotPackaged));
        Assert.False(AppLaunch.IsSilentStart([], new PretendPackage(false, StartedByStartupTask: true)));
    }

    [Fact]
    public void Packaged_Hook_Names_The_Alias_Or_Is_Not_Offered()
    {
        Assert.Equal(HookCommandKind.CopiedProgram, HookCommand.For(PretendPackage.NotPackaged, aliasDeclared: true));
        Assert.Equal(HookCommandKind.Alias, HookCommand.For(Packaged, aliasDeclared: true));
        Assert.Equal(HookCommandKind.NotOffered, HookCommand.For(Packaged, aliasDeclared: false));

        // Packaged, the hook names the alias and nothing else; unpackaged, an absolute path and not the alias.
        var lines = string.Join("\n", HookInstaller.LinesToAdd(PackageNames.NotifyAlias, viaAlias: true));
        Assert.Contains("\"command\": \"Island.Notify.exe\"", lines, StringComparison.Ordinal);
        var packaged = HookInstaller.Connect("{}", PackageNames.NotifyAlias, viaAlias: true);
        Assert.Null(packaged.Reason);
        Assert.True(packaged.Changed);
        Assert.True(HookInstaller.IsConnected(packaged.Text));
        Assert.Equal(HookInstaller.NotifyPathInvalid, HookInstaller.Connect("{}", PackageNames.NotifyAlias).Reason);
        Assert.Equal(HookInstaller.NotifyPathInvalid, HookInstaller.Connect("{}", @"C:\x\Island.Notify.exe", viaAlias: true).Reason);
        Assert.Empty(HookInstaller.LinesToAdd(@"C:\x\Island.Notify.exe", viaAlias: true));
        Assert.Contains("not offered", HookCommand.NotOfferedText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Names_The_Manifest_Must_Carry_Are_Fixed_Here()
    {
        Assert.Equal("IslandStartup", PackageNames.StartupTaskId);
        Assert.Equal("Island.Notify.exe", PackageNames.NotifyAlias);
        Assert.Equal("Island.App.exe", PackageNames.AppExecutable);
    }
}
