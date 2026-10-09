namespace Island.Core;

/// <summary>What a flip of the switch did. Ok with nothing to do (already so) counts as Ok.</summary>
/// <param name="Ok">The registry holds what was asked.</param>
/// <param name="Refusal">Set when Island itself refused; null with Ok false means the registry did not take the change.</param>
public sealed record StartupResult(bool Ok, Refusal? Refusal = null)
{
    public static StartupResult Done { get; } = new(true);

    public static StartupResult Failed { get; } = new(false);
}

/// <summary>
/// "Start with Windows", the logic only. It changes the registry in exactly two moments, the person turning it
/// on and the person turning it off: never at launch and never to repair a missing value. What it shows is what
/// the registry holds when asked, never what a settings file remembers.
/// </summary>
/// <param name="registry">The one value. The real one is an Outside file; tests use <see cref="MemoryStartupRegistry"/>.</param>
/// <param name="programPath">The full path of the running program (dist\Island\Island.App.exe).</param>
public sealed class StartupSwitch(IStartupRegistry registry, string programPath) : IStartupSwitch
{
    public string? Explanation => null;

    public const string AutostartArgument = "--autostart";

    /// <summary>The command line this switch writes: the path in double quotes, a space, --autostart. Null when the path cannot be written safely.</summary>
    public string? CommandLine => IsWritablePath(programPath) ? $"\"{programPath}\" {AutostartArgument}" : null;

    /// <summary>On only when the value is there and names this program. A value that names another program is "off".</summary>
    public bool IsOn()
    {
        var wanted = CommandLine;
        return wanted is not null && string.Equals(registry.Read(), wanted, StringComparison.OrdinalIgnoreCase);
    }

    public StartupResult TurnOn()
    {
        var line = CommandLine;
        if (line is null) return StartupResult.Failed;
        if (line.Length > StartupKey.MaxCommandLineLength) return new StartupResult(false, StartupRefusals.PathTooLong);
        if (IsOn()) return StartupResult.Done;
        return registry.Write(line) ? StartupResult.Done : StartupResult.Failed;
    }

    /// <summary>
    /// Removes the value only when it is this program's. A value naming another copy of Island is that copy's
    /// and is left alone (ponytail: a value left by a moved folder can be cleared by turning on, then off).
    /// </summary>
    public StartupResult TurnOff()
    {
        if (!IsOn()) return StartupResult.Done;
        return registry.Remove() ? StartupResult.Done : StartupResult.Failed;
    }

    /// <summary>True when the program was started by Windows' start-up list (the argument is among the arguments).</summary>
    public static bool IsAutostartLaunch(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, AutostartArgument, StringComparison.Ordinal));

    /// <summary>
    /// Whether the island shows itself when the program starts: yes, so that the person sees it is alive, except when Windows's
    /// start-up list started it (<c>--autostart</c>): then it comes up silently, the tray icon and the key only.
    /// </summary>
    public static bool SummonsAtStart(IReadOnlyList<string> args) => !IsAutostartLaunch(args);

    // A quote would end the quoted path early; a trailing backslash would escape the closing quote when Windows
    // splits the line; a NUL or newline would cut the stored text; a relative path would resolve against nothing.
    private static bool IsWritablePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && !path.Any(c => c == '"' || char.IsControl(c))
        && path[^1] is not ('\\' or '/');
}
