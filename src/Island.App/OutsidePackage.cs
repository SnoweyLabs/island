using System.Runtime.InteropServices;
using System.Text;
using Island.Core;

namespace Island.App;

/// <summary>
/// The real answers to "am I running as a package?" and "did the package's start-up task start me?" (WORK-ORDER-8 section 3), and the package's start-up
/// task itself. Reading the package identity changes nothing; enabling or disabling the task asks <see cref="OutsideGate"/> first, which refuses under the
/// self-test. Nothing here is reached unpackaged except the first question, which answers no.
/// </summary>
internal sealed class OutsidePackage : IPackageFacts
{
    // APPMODEL_ERROR_NO_PACKAGE (15700): "The process has no package identity" (Microsoft Learn, GetCurrentPackageFullName and the system error codes).
    private const int NoPackage = 15700;
    private const int InsufficientBuffer = 122;

    private readonly Lazy<bool> _packaged = new(AskIdentity);
    private readonly Lazy<bool> _startedByTask;

    public OutsidePackage() => _startedByTask = new Lazy<bool>(() => IsPackaged && AskActivation());

    public bool IsPackaged => !OutsideGate.Current.SelfTest && _packaged.Value;

    /// <summary>
    /// Asked once, as early as it can be: the arguments of the activation are only returned the first time (the Windows App SDK page says so for its own
    /// variant of the call). The call is the documented one of Windows.ApplicationModel, for packaged desktop apps from Windows 10 1809.
    /// </summary>
    public bool StartedByStartupTask => _startedByTask.Value;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    private static bool AskIdentity()
    {
        try
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) == InsufficientBuffer; // the sample's own way: a null buffer first; a package answers "buffer too small"
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool AskActivation()
    {
        try
        {
            var args = global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
            return args?.Kind == global::Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Kept so that the one number is read next to the constant it answers.</summary>
    internal static int NoPackageCode => NoPackage;
}

/// <summary>The package's start-up task (the manifest declares it switched off). Used only when packaged.</summary>
internal sealed class OutsideStartupTask : IStartupTask
{
    public StartupTaskState State() => Map(Get()?.State);

    public StartupTaskState RequestEnable()
    {
        if (!OutsideGate.Current.Allow(OutsideKind.WriteStartupValue) || Get() is not { } task) return State();
        try
        {
            return Map(Task.Run(async () => await task.RequestEnableAsync()).GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            return State();
        }
    }

    public void Disable()
    {
        if (!OutsideGate.Current.Allow(OutsideKind.WriteStartupValue) || Get() is not { } task) return;
        try
        {
            task.Disable();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            // the state says what it is afterwards
        }
    }

    private static global::Windows.ApplicationModel.StartupTask? Get()
    {
        try
        {
            return Task.Run(async () => await global::Windows.ApplicationModel.StartupTask.GetAsync(PackageNames.StartupTaskId)).GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static StartupTaskState Map(global::Windows.ApplicationModel.StartupTaskState? state) => state switch
    {
        global::Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
        global::Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
        global::Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupTaskState.DisabledByPolicy,
        null => StartupTaskState.DisabledByPolicy, // no task to ask: "Platforms that don't support startup tasks also report DisabledByPolicy"
        _ => StartupTaskState.Disabled,
    };
}
