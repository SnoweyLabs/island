namespace Island.Core;

/// <summary>
/// What a pick's key does (WORK-ORDER-6 section 4): the same thing a click on the pick does (jump to it, go to its next window, or open
/// it), done first, inside the key's own handler, and then the island comes in on the pick's page, without the keyboard, so the program
/// that was just brought forward keeps it. When the island stays away (what is in front says so) the jump still happened. A key whose pick
/// is gone does nothing at all.
/// </summary>
public sealed class PickKeyHandler(Func<string, Pick?> find, Action<Pick> jump, Action<string> comeIn)
{
    /// <summary>True when the pick was found and the jump was asked for.</summary>
    public bool Press(string pickId)
    {
        if (find(pickId) is not { } pick) return false;
        jump(pick);
        comeIn(pick.PageId);
        return true;
    }
}
