using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Island.App.Visuals;

/// <summary>
/// The island's own window content, as a screen reader meets it (WORK-ORDER-12 section 4, EASE: the island is drawn by custom elements that carry no name of their own, so a screen reader
/// was shown an unnamed window with nothing in it). The root says what the island shows now, in words: the page, the selected tile and its second line, how many tiles there are, or the notice or
/// the playing line. It changes nothing that is drawn. The words are made by the controller and are in memory only; nothing is written anywhere.
/// </summary>
internal sealed class IslandRoot : Canvas
{
    /// <summary>The name of the island when it is open and says nothing else yet.</summary>
    public const string Name = "Island";

    private string _description = Name;

    public string Description => _description;

    /// <summary>What the island shows now. A change is told to a screen reader as a change of the name (a notice is an alert: told at once).</summary>
    public void Describe(string text, bool alert)
    {
        text = string.IsNullOrWhiteSpace(text) ? Name : text;
        if (text == _description) return;
        var old = _description;
        _description = text;
        AutomationProperties.SetName(this, text);
        AutomationProperties.SetLiveSetting(this, alert ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, old, text);
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged); // the island never holds the keyboard focus, so a change of its words is told as a live region: at once for a notice, politely for the rest
        }
    }

    /// <summary>The island went away: what it said stands no longer, so the same words at the next summon are told again.</summary>
    public void Forget() => _description = Name;

    protected override AutomationPeer OnCreateAutomationPeer() => new IslandRootPeer(this);

    private sealed class IslandRootPeer(IslandRoot owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "IslandRoot";

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetNameCore() => owner.Description;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }
}
