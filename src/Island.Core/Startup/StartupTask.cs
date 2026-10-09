namespace Island.Core;

/// <summary>
/// The states of a package's start-up task, with the meanings Microsoft Learn gives (StartupTaskState): "DisabledByUser: the task was disabled by the user.
/// It can only be re-enabled by the user."
/// </summary>
public enum StartupTaskState
{
    Disabled,
    DisabledByUser,
    Enabled,
    DisabledByPolicy,
    EnabledByPolicy,
}

/// <summary>
/// The package's start-up task as the switch needs it. The real one is an Outside file that asks the gate before it enables or disables anything and
/// is never reached unpackaged.
/// </summary>
public interface IStartupTask
{
    StartupTaskState State();

    /// <summary>Asks Windows to enable the task and says the state afterwards. A task the person disabled stays disabled: Windows does not override their choice.</summary>
    StartupTaskState RequestEnable();

    void Disable();
}

/// <summary>A start-up task for tests and the self-test: it lives in memory, counts its calls, and keeps the person's "no" as Windows does.</summary>
public sealed class MemoryStartupTask(StartupTaskState state = StartupTaskState.Disabled) : IStartupTask
{
    public int Requests { get; private set; }

    public int Disables { get; private set; }

    public StartupTaskState State() => state;

    public StartupTaskState RequestEnable()
    {
        Requests++;
        if (state == StartupTaskState.Disabled) state = StartupTaskState.Enabled; // DisabledByUser and DisabledByPolicy stay as they are
        return state;
    }

    public void Disable()
    {
        Disables++;
        if (state == StartupTaskState.Enabled) state = StartupTaskState.Disabled;
    }

    /// <summary>The person (or an administrator) changes it in Windows' own settings.</summary>
    public void SetByWindows(StartupTaskState next) => state = next;
}

/// <summary>"Start with Windows" in a package: the start-up task of the manifest, switched off there, asked for only when the person flips the switch.</summary>
public sealed class PackagedStartupSwitch(IStartupTask task) : IStartupSwitch
{
    public bool IsOn() => task.State() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>The state Windows reports, in words, when it is not simply on or off by the switch's own doing; null otherwise.</summary>
    public string? Explanation => task.State() switch
    {
        StartupTaskState.DisabledByUser => StartupRefusals.SwitchedOffInWindows.Message,
        StartupTaskState.DisabledByPolicy => "An administrator or a policy keeps Start with Windows switched off for Island.",
        StartupTaskState.EnabledByPolicy => "An administrator or a policy keeps Start with Windows switched on for Island.",
        _ => null,
    };

    public StartupResult TurnOn()
    {
        switch (task.State())
        {
            case StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy:
                return StartupResult.Done;
            case StartupTaskState.DisabledByUser:
                return new StartupResult(false, StartupRefusals.SwitchedOffInWindows); // never asked again over the person's own "no"
            case StartupTaskState.DisabledByPolicy:
                return StartupResult.Failed;
        }

        return task.RequestEnable() is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy ? StartupResult.Done : StartupResult.Failed;
    }

    public StartupResult TurnOff()
    {
        switch (task.State())
        {
            case StartupTaskState.Enabled:
                task.Disable();
                return task.State() == StartupTaskState.Enabled ? StartupResult.Failed : StartupResult.Done;
            case StartupTaskState.EnabledByPolicy:
                return StartupResult.Failed; // not the person's to switch
            default:
                return StartupResult.Done; // already off
        }
    }
}

public static class StartupChoice
{
    /// <summary>Unpackaged: the registry value. Packaged: the package's start-up task. Neither is built unless it is the one in use.</summary>
    public static IStartupSwitch For(IPackageFacts package, Func<IStartupSwitch> registrySwitch, Func<IStartupTask> task) =>
        package.IsPackaged ? new PackagedStartupSwitch(task()) : registrySwitch();
}
