namespace Island.Core;

/// <summary>
/// The name drawn on the notice: the last part of the folder the agent works in. It arrives from outside, so it is
/// cleaned (control and text-direction characters removed) and cut to 40 characters. Kept in memory only.
/// </summary>
public static class ProjectName
{
    public const int MaxChars = 40;

    /// <summary>Shown when the folder gave no name at all.</summary>
    public const string Unknown = "Agent";

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>The last non-empty part of the folder, either separator; "" when there is none.</summary>
    public static string From(string? folder)
    {
        // The last part is taken first and cleaned after: a last part that is only invisible characters is no name (it must not fall back to its parent's).
        var parts = AgentText.Clean(folder, dropFormat: false).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "";
        var name = AgentText.Head(AgentText.Clean(parts[^1]), MaxChars).TrimEnd();
        return BlankText.HasVisible(name) ? name : ""; // a name that draws as nothing is no name
    }
}
