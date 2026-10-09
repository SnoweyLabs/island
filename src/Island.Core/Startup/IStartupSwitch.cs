namespace Island.Core;

/// <summary>"Start with Windows" as the settings screen uses it: the registry value unpackaged, the package's start-up task packaged.</summary>
public interface IStartupSwitch
{
    bool IsOn();

    StartupResult TurnOn();

    StartupResult TurnOff();

    /// <summary>What the switch has to say about the state Windows reports (the person switched it off in Windows' own settings, say); null when nothing.</summary>
    string? Explanation { get; }
}
