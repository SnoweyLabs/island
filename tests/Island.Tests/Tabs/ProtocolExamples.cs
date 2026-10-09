using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>The labelled examples of extension/PROTOCOL.md (fenced blocks whose info string starts "json example").</summary>
internal static class ProtocolExamples
{
    public static IReadOnlyDictionary<string, string> All { get; } = Read();

    /// <summary>The frames the island sends; every other good example is a frame from the add-on.</summary>
    public static readonly string[] FromIsland = ["welcome", "activate", "media-command", "close", "resync", "pong"];

    public static string Get(string label) => All[label];

    private static Dictionary<string, string> Read()
    {
        var text = File.ReadAllText(RepoPaths.File("extension", "PROTOCOL.md")).Replace("\r\n", "\n");
        return Regex.Matches(text, @"^```json example (\S+)\n(.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }
}
