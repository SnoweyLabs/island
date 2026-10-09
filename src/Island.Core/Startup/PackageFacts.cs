namespace Island.Core;

/// <summary>
/// What the app knows about how it runs (WORK-ORDER-8 section 3). One question at start, "am I running with package identity?", asked in the way
/// Microsoft Learn documents (GetCurrentPackageFullName answers APPMODEL_ERROR_NO_PACKAGE when there is none); for every place where a package differs,
/// both answers are built. The real answers come from a file whose name begins "Outside"; tests and the self-test use <see cref="PretendPackage"/>.
/// </summary>
public interface IPackageFacts
{
    /// <summary>True when the process has package identity.</summary>
    bool IsPackaged { get; }

    /// <summary>True when the package's start-up task started this process (the activation kind StartupTask). Never true unpackaged.</summary>
    bool StartedByStartupTask { get; }
}

/// <summary>A package that is only a pretence, for tests and the self-test.</summary>
public sealed record PretendPackage(bool IsPackaged, bool StartedByStartupTask = false) : IPackageFacts
{
    public static PretendPackage NotPackaged { get; } = new(false);
}

/// <summary>The names the package manifest and the code must agree on; `ShipGuardTests` check the manifest against them.</summary>
public static class PackageNames
{
    /// <summary>The <c>TaskId</c> of the manifest's <c>desktop:StartupTask</c>.</summary>
    public const string StartupTaskId = "IslandStartup";

    /// <summary>The <c>Alias</c> of the manifest's execution alias: the stable command name that runs Island.Notify from inside the package.</summary>
    public const string NotifyAlias = "Island.Notify.exe";

    /// <summary>The program the package runs.</summary>
    public const string AppExecutable = "Island.App.exe";
}

public static class AppLaunch
{
    /// <summary>
    /// Whether the island stays silent at start (the tray icon and the key only): Windows' start-up list started it (<c>--autostart</c>), or, in a package,
    /// the package's start-up task did, which carries no argument (the documented way to learn it is the activation kind).
    /// </summary>
    public static bool IsSilentStart(IReadOnlyList<string> args, IPackageFacts package) =>
        StartupSwitch.IsAutostartLaunch(args) || package.IsPackaged && package.StartedByStartupTask;
}
