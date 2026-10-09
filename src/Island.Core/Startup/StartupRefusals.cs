namespace Island.Core;

/// <summary>
/// Refusals of the start-with-Windows switch. Kept apart from <see cref="Refusals"/> (an existing file); the
/// main session folds this into <see cref="Refusals.All"/> when it fits the piece in.
/// </summary>
public static class StartupRefusals
{
    public static Refusal SwitchedOffInWindows { get; } = new(
        "STARTUP_OFF_IN_WINDOWS",
        "Windows has Start with Windows switched off for Island.",
        "You switched it off there (Settings, Apps, Startup, or Task Manager), and only you can switch it back on.",
        "Switch it on in Windows' own settings.");

    public static Refusal PathTooLong { get; } = new(
        "STARTUP_PATH_TOO_LONG",
        "Island sits in a folder whose path is too long for Windows' start-up list, so it was not added.",
        "Windows would cut the path and start nothing.",
        "Move the Island folder somewhere with a shorter path, then switch this on again.");
}
