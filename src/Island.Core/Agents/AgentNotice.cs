namespace Island.Core;

/// <summary>
/// One notice as the island holds it: what happened, the project's name (already cleaned and cut), the chain of
/// process ids Island.Notify was started from (nearest first) and a key for "the same session". Memory only.
/// </summary>
public sealed record AgentNotice(AgentSignal Signal, string ProjectName, string SessionKey, IReadOnlyList<int> Chain)
{
    /// <summary>The notice a message means, or null when the event is one that is ignored.</summary>
    public static AgentNotice? From(AgentMessage message)
    {
        if (AgentSignals.Classify(message.Event, message.Kind) is not { } signal) return null;
        var name = Core.ProjectName.From(message.Folder);
        // The hook sends no session id (the order names four things), so the program that runs the agent, the first
        // in the chain, tells sessions apart; with no chain, the folder does.
        var key = message.Chain.Count > 0 ? $"p{message.Chain[0]}" : $"f{AgentText.Clean(message.Folder)}";
        return new AgentNotice(signal, name.Length == 0 ? Core.ProjectName.Unknown : name, key, message.Chain);
    }

    /// <summary>
    /// The notice a message of either version means, by the one signal table (WORK-ORDER-11 section 3): only the events the table marks as raising it (a helper's
    /// "finished" and "needs you"), so for Claude Code exactly Stop and Notification of kind permission_prompt, as before. Null for every other event.
    /// </summary>
    public static AgentNotice? From(Island.Core.Agents.Sessions.SessionMessage message, Island.Core.Agents.Sessions.HelperSignalTable table)
    {
        if (table.Match(message.Helper, message.Event, message.Kind) is not { RaisesNotice: true } match) return null;
        var signal = match.Signal == Island.Core.Agents.Sessions.HelperSignal.NeedsYou ? AgentSignal.NeedsYourAnswer : AgentSignal.Finished;
        var name = Core.ProjectName.From(message.Folder);
        var key = message.Chain.Count > 0 ? $"p{message.Chain[0]}" : $"f{AgentText.Clean(message.Folder)}";
        return new AgentNotice(signal, name.Length == 0 ? Core.ProjectName.Unknown : name, key, message.Chain);
    }

    public string Line => AgentSignals.Text(Signal);
}
