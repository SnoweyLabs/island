namespace Island.Core;

/// <summary>
/// What the uninstaller asks of the program before it removes the program's files (WORK-ORDER-14 section 3, the command line <c>--uninstall-cleanup</c>):
/// what pressing "Disconnect" does, for every helper that is connected, and what switching "Start with Windows" off does — through the same connectors, the
/// same switch and the same gate as the buttons, nothing new — so that no helper's settings name a program that is gone, and Windows starts nothing at sign-in.
/// The person's own settings and picks are not touched. Under the self-test the gate refuses it, as it refuses the buttons.
/// </summary>
public static class UninstallCleanup
{
    public const string Argument = "--uninstall-cleanup";

    /// <param name="Disconnected">The helpers whose lines were taken out (by their island names).</param>
    /// <param name="NotDisconnected">The connected helpers whose lines could not be taken out.</param>
    /// <param name="StartupOff">Windows' start-up list no longer starts this program.</param>
    /// <param name="Refused">The gate refused: nothing was read, nothing was changed.</param>
    public sealed record Outcome(IReadOnlyList<string> Disconnected, IReadOnlyList<string> NotDisconnected, bool StartupOff, bool Refused)
    {
        public bool AllDone => !Refused && NotDisconnected.Count == 0 && StartupOff;
    }

    public static Outcome Run(IReadOnlyList<IAgentConnector> connectors, IStartupSwitch? startup, OutsideGate gate)
    {
        // The gate first, for both kinds of change: when it refuses, no connector and no switch is even asked.
        var agentsAllowed = gate.Allow(OutsideKind.EditAgentSettings);
        var startupAllowed = gate.Allow(OutsideKind.WriteStartupValue);
        if (!agentsAllowed || !startupAllowed) return new Outcome([], [], StartupOff: false, Refused: true);

        var done = new List<string>();
        var failed = new List<string>();
        foreach (var connector in connectors)
        {
            // Only a helper that is connected (today's lines or the older ones) is disconnected; one whose file cannot be read is left alone, as the button leaves it.
            if (connector.State() is not (AgentConnection.Connected or AgentConnection.ConnectedOlder)) continue;
            (connector.Disconnect().Done ? done : failed).Add(connector.Helper);
        }

        var startupOff = startup is null || startup.TurnOff().Ok;
        return new Outcome(done, failed, startupOff, Refused: false);
    }
}
