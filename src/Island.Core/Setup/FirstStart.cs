namespace Island.Core;

/// <summary>The steps of the first start, in the order of the preview Dan chose (WORK-ORDER-7 section 6).</summary>
public enum SetupStep
{
    Welcome,
    Key,

    /// <summary>Try the keys on the real island (Dan, 8 Oct 2026): after the key is chosen, before the pages.</summary>
    Practice,
    Pages,
    OnTheIsland,

    /// <summary>The optional Chrome add-on, how to load it by hand (Dan, 2026-10-09, version 1.0.1): before the mode, skipped with Continue.</summary>
    Addon,
    Mode,
}

/// <param name="Name">What the small island's dots call it.</param>
/// <param name="Title">The heading of the step.</param>
/// <param name="Sub">The sentence under the heading.</param>
public sealed record SetupStepInfo(SetupStep Step, string Name, string Title, string Sub);

/// <summary>What closing the setup does to the island.</summary>
public enum SetupEnd
{
    /// <summary>Esc: the screen shrinks away. What was chosen so far is already saved; the rest stays as it was.</summary>
    JustClose,

    /// <summary>"Done": the screen shrinks to the ball, which flies to the top and becomes the island, left open.</summary>
    ShrinkFlyAndOpenIsland,
}

/// <summary>
/// The first start (WORK-ORDER-7 section 6): the steps and their words, when it runs by itself, and what ending it does. The steps are built from the
/// pieces the settings screen already has, so each edit is saved the moment it is made: leaving early keeps what was chosen, and running it again shows
/// what is set now and changes only what the person touches.
/// </summary>
public static class FirstStart
{
    public const string Heading = "Setting up";

    public static IReadOnlyList<SetupStepInfo> Steps { get; } =
    [
        new(SetupStep.Welcome, "Welcome", "Welcome to Island", "A small island at the top of your screen for the things you use all day. Set it up once; it takes about a minute."),
        new(SetupStep.Key, "Your key", "Choose your key", "One key brings the island. You can change it at any time."),
        new(SetupStep.Practice, "Try it", "Try it out", "Press the keys and see what the island does. Nothing is opened, added or removed while you practise, and you can skip any step."),
        new(SetupStep.Pages, "Your pages", "Your pages", "Each page has its own colour. Rename them, recolour them, or add your own."),
        new(SetupStep.OnTheIsland, "On the island", "What goes on the island", "A few things are picked for you. Tap to switch any of them off. You can add more later with the + button."),
        new(SetupStep.Addon, "Chrome", "Your browser tabs", "Optional. With the free Island add-on for Chrome, the island also sees your tabs and what plays in them. If you do not use Chrome, press Continue."),
        new(SetupStep.Mode, "Mode", "When should it show up?", "Pick how the island behaves while you play or work. You can switch at any time."),
    ];

    /// <summary>
    /// It runs by itself when the app starts and no settings file existed, and never when Windows started the app (<c>--autostart</c>) or the self-test
    /// did. The first test is made before the app writes its own settings file.
    /// </summary>
    public static bool ShouldRun(bool settingsFileExisted, bool startedByWindows, bool selfTest) => !settingsFileExisted && !startedByWindows && !selfTest;

    /// <summary>The words of the button under a step: "Start" on the first, "Done" on the last.</summary>
    public static string ButtonText(int stepIndex) => stepIndex == 0 ? "Start" : stepIndex == Steps.Count - 1 ? "Done" : "Continue";

    public static SetupEnd EndOf(bool done) => done ? SetupEnd.ShrinkFlyAndOpenIsland : SetupEnd.JustClose;
}
