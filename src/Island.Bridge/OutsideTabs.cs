using Island.Core;

namespace Island.Bridge;

/// <summary>
/// Commands to the add-on: switch to a tab, press play/pause, next or previous in its page. They act on the
/// outside world (Dan's browser), so each one asks <see cref="OutsideGate"/> first and sends nothing when
/// refused. Sending never waits for the browser.
/// </summary>
public sealed class OutsideTabs(TabBridge bridge) : ITabControl
{
    public bool Activate(string tabKey)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.TabCommand)) return false;
        return bridge.Model.Locate(tabKey) is { } at && bridge.TrySend(at.ConnectionId, TabMessages.Activate(at.TabId, at.WindowId));
    }

    public bool Close(string tabKey)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.TabCommand)) return false;
        return bridge.Model.Locate(tabKey) is { } at && bridge.TrySend(at.ConnectionId, TabMessages.CloseTab(at.TabId));
    }

    public bool Media(string tabKey, MediaCommand command)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.TabCommand)) return false;
        return bridge.Model.Locate(tabKey) is { } at && bridge.TrySend(at.ConnectionId, TabMessages.MediaCommandFrame(at.TabId, command));
    }
}
