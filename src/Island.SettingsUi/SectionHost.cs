using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>The sections of the settings screen, in the order of the steps at the top.</summary>
public enum SettingsSection
{
    Key,
    Pages,
    OnTheIsland,
    Scenes,
    Mode,
    Glass,
    CodingAgents,
    General,

    /// <summary>Only in the setup (the first start): the orb and the greeting.</summary>
    Welcome,

    /// <summary>Only in the setup: the optional Chrome add-on and how to load it by hand (Dan, version 1.0.1).</summary>
    Addon,

    /// <summary>Only in the setup: try the keys on the real island (Dan's tutorial, WORK-ORDER-13).</summary>
    Practice,
}

/// <summary>What a section needs from the screen it sits in.</summary>
internal interface ISectionHost
{
    SettingsSession Session { get; }

    /// <summary>The colour of the current step: the section's own accent (the first four pages' colours, as the reference uses a page colour per step).</summary>
    string Accent { get; }

    /// <summary>The one warning line of the section; null clears it. Shown in the warn colour.</summary>
    string? Notice { get; set; }

    /// <summary>The page whose colour panel is open in the Pages section, or null. Kept by the screen so that it survives a rebuild.</summary>
    string? OpenPanel { get; set; }

    /// <summary>The action id (main key or page id) that is waiting for a key press, or null.</summary>
    string? Capturing { get; }

    void BeginCapture(string actionId);

    void CancelCapture();

    /// <summary>Builds the section again from the session; the keyboard goes back to the element named <paramref name="focusKey"/> (or stays where it was).</summary>
    void Refresh(string? focusKey = null);

    /// <summary>Shows the yes/no question; <paramref name="onYes"/> runs only on a yes.</summary>
    void Ask(string text, string yesText, Action onYes, string noText = "No, keep it");

    /// <summary>"Start the practice" (the step Try it of the setup): the screen steps back and the real island is practised on.</summary>
    void RequestPractice();

    /// <summary>"Run the setup again" (General): the screen closes and the first-start steps open, showing what is set now.</summary>
    void RequestSetup();

    /// <summary>Records the result of an edit: a refusal or a warning becomes the notice, a clean result clears it.</summary>
    void Report(SessionResult result);
}
