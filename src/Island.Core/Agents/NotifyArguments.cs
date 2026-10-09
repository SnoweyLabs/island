namespace Island.Core;

/// <summary>What Island.Notify was asked on its command line (WORK-ORDER-11 section 3).</summary>
/// <param name="Agent">The helper's name, lower case; "claude" when <c>--agent</c> is absent, so entries written before today keep working.</param>
/// <param name="Event">The event's name from <c>--event</c>, for a helper whose input does not name its own; null when absent.</param>
/// <param name="PipeName">The name of another pipe (what every test does); null for the real one.</param>
public sealed record NotifyArguments(string Agent, string? Event, string? PipeName)
{
    /// <summary>The helper that is meant when none is named.</summary>
    public const string DefaultAgent = "claude";

    public const int MaxAgentChars = 32;

    public const int MaxEventChars = 48;

    /// <summary>
    /// <c>--agent &lt;name&gt;</c>, <c>--event &lt;name&gt;</c> and at most one other argument, the pipe name; a pipe name never begins with a hyphen.
    /// Anything else (an option that is not known where a pipe name should be, a repeated option, a name that is not plain, an option without its value) gives null and
    /// Island.Notify does nothing: a name that was given but is not plain is never replaced by the real one. Arguments after the pipe name are ignored, as they always were.
    /// </summary>
    public static NotifyArguments? Parse(IReadOnlyList<string> args)
    {
        string? agent = null, eventName = null, pipe = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--agent":
                    if (agent is not null || i + 1 >= args.Count || !IsPlain(args[i + 1], MaxAgentChars)) return null;
                    agent = args[++i].ToLowerInvariant();
                    break;
                case "--event":
                    if (eventName is not null || i + 1 >= args.Count || !IsPlain(args[i + 1], MaxEventChars)) return null;
                    eventName = args[++i];
                    break;
                default:
                    if (pipe is not null) break; // anything after the pipe name is ignored, as it always was
                    if (arg.StartsWith('-') || !AgentPipe.IsValidName(arg)) return null; // the first other argument is the pipe's name: never one with a hyphen, never one that is not plain
                    pipe = arg;
                    break;
            }
        }

        return new NotifyArguments(agent ?? DefaultAgent, eventName, pipe);
    }

    // Letters, digits, hyphen and underscore, and nothing that could name a path, a switch or another machine.
    private static bool IsPlain(string text, int max) =>
        text.Length is > 0 && text.Length <= max && !text.StartsWith('-') && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
