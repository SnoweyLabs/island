namespace Island.Core;

/// <summary>
/// A message the app shows when it will not do something. Each keeps its three
/// parts: what happened, why refusing is right, what to do now.
/// </summary>
public sealed record Refusal(string Code, string WhatHappened, string Why, string NextAction)
{
    public string Message => $"{WhatHappened} {Why} {NextAction}";

    public Refusal With(string placeholder, string value) => this with
    {
        WhatHappened = WhatHappened.Replace(placeholder, value),
        Why = Why.Replace(placeholder, value),
        NextAction = NextAction.Replace(placeholder, value),
    };
}

public static class Refusals
{
    public static Refusal HotkeyTaken { get; } = new(
        "HOTKEY_TAKEN",
        "{combo} is already used by another program, so {category} has no keybind now.",
        "Island left it alone, because taking it would break that program.",
        "Pick another combination in Settings, Key."); // no file to edit and no restart (Dan's P12, WORK-ORDER-13)

    public static Refusal SettingsUnreadable { get; } = new(
        "SETTINGS_UNREADABLE",
        "Island's settings could not be read.",
        "Island runs on its defaults and left the file exactly as it was, so nothing you wrote is lost.",
        "Close Island, delete settings.json in AppData\\Roaming\\Island, then start Island again."); // plain steps, no editing of a file (Dan's P12, WORK-ORDER-13)

    public static Refusal SettingsFromNewerVersion { get; } = new(
        "SETTINGS_FROM_NEWER_VERSION",
        "Your settings were written by a newer Island, so this version runs on its defaults.",
        "It left your file exactly as it was, because changing a file it does not fully understand could lose what you set up.",
        "Update Island, and your settings come back.");

    public static Refusal AlreadyRunning { get; } = new(
        "ALREADY_RUNNING",
        "Island is already running.",
        "A second copy would fight the first one for the keybinds, so the second one closed.",
        "Use the tray icon of the one that is running.");

    public static Refusal BlurUnavailable { get; } = new(
        "BLUR_UNAVAILABLE",
        "Blur glass is not available (transparency effects are off, or the layer could not be made).",
        "Island uses the Approved glass instead.",
        "Turn on Transparency effects in Windows Settings, Colours, then pick Blur again.");

    public static IReadOnlyList<Refusal> All { get; } = [HotkeyTaken, SettingsUnreadable, SettingsFromNewerVersion, AlreadyRunning, BlurUnavailable, StartupRefusals.PathTooLong, StartupRefusals.SwitchedOffInWindows, CloseRefusals.NeedsAdmin, AgentRefusals.HooksFileUnreadable, SceneRefusals.PartMissingTemplate, HandPickRefusals.PickTargetMissing, HandPickRefusals.NotASite, TerminalsPageRefusals.NoRoom];

    public static Refusal ForHotkeyTaken(HotkeyCombo combo, string pageName) =>
        HotkeyTaken.With("{combo}", combo.ToString()).With("{category}", pageName);
}
