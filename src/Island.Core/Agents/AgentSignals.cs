namespace Island.Core;

public enum AgentSignal
{
    Finished,
    NeedsYourAnswer,
}

/// <summary>
/// Which events mean something. The names are those of the Claude Code hooks page: the event Stop, and the event
/// Notification of the kind permission_prompt. Everything else is ignored, idle_prompt included (it would repeat
/// the first notice a minute later).
/// </summary>
public static class AgentSignals
{
    public static AgentSignal? Classify(string? hookEvent, string? notificationType) => hookEvent switch
    {
        "Stop" => AgentSignal.Finished,
        "Notification" when notificationType == "permission_prompt" => AgentSignal.NeedsYourAnswer,
        _ => null,
    };

    public static string Text(AgentSignal signal) => signal switch
    {
        AgentSignal.NeedsYourAnswer => "Agent needs your answer",
        _ => "Agent finished — waiting for you",
    };
}
