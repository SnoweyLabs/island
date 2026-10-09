namespace Island.Core;

/// <summary>The refusal of the close button (WORK-ORDER-6 section 5). A template: <see cref="ForName"/> puts the program's name in.</summary>
public static class CloseRefusals
{
    public static Refusal NeedsAdmin { get; } = new(
        "CLOSE_NEEDS_ADMIN",
        "{name} runs as administrator, so Island cannot close it.",
        "Windows does not let an ordinary program close one that has more rights.",
        "Close it from its own window.");

    public static Refusal ForName(string name) => NeedsAdmin.With("{name}", name);
}
