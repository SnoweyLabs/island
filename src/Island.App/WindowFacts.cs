using Island.Core;

namespace Island.App;

/// <summary>What the close button asks the machine, read only: does the window exist, is it enabled, does its program run with more rights, which tabs are known.</summary>
internal sealed class WindowFacts(AppWorld world, Func<long, bool> isIslandWindow) : ICloseFacts
{
    private bool? _ownElevated;

    public bool WindowExists(long window) => Native.IsWindow(new IntPtr(window));

    public bool IsIslandWindow(long window) => isIslandWindow(window);

    public bool IsEnabled(long window) => Native.IsWindowEnabled(new IntPtr(window));

    /// <summary>
    /// True when Windows would drop a close request: the window's program runs elevated and the island does not, or it cannot be told
    /// (the process cannot be opened for a limited query, which a program with more rights than ours is not allowed).
    /// </summary>
    public bool NeedsMoreRights(long window)
    {
        var pid = Native.ProcessIdOf(new IntPtr(window));
        if (pid == Environment.ProcessId) return false;
        if (Native.IsElevated(pid) is not { } theirs) return true;
        _ownElevated ??= Native.IsElevated((uint)Environment.ProcessId) ?? false;
        return theirs && !_ownElevated.Value;
    }

    public bool AddonConnected => world.Tabs.Connected;

    public bool TabExists(string tabKey) => world.Tabs.Tabs.Any(t => t.Key == tabKey);
}
