using Island.Core;

namespace Island.Tests;

/// <summary>The command line the uninstaller runs first (WORK-ORDER-14 section 3): Disconnect for every connected helper, Start with Windows off, nothing else.</summary>
public class UninstallCleanupTests
{
    private const string Program = @"C:\Apps\Alpha\Island.App.exe";

    private sealed class FakeConnector(string helper, AgentConnection state, bool disconnects = true) : IAgentConnector
    {
        public List<string> Calls { get; } = [];

        public string Helper => helper;

        public string Location => "%USERPROFILE%\\." + helper + "\\settings.json";

        public string? NotOffered => null;

        public IReadOnlyList<string> LinesToAdd => [];

        public AgentConnection State()
        {
            Calls.Add("state");
            return state;
        }

        public ConnectorResult Connect()
        {
            Calls.Add("connect");
            return new ConnectorResult(true, null);
        }

        public ConnectorResult Update()
        {
            Calls.Add("update");
            return new ConnectorResult(true, null);
        }

        public ConnectorResult Disconnect()
        {
            Calls.Add("disconnect");
            return new ConnectorResult(disconnects, disconnects ? null : AgentRefusals.HooksFileNotWrittenForDisconnect);
        }
    }

    [Fact]
    public void It_Disconnects_Every_Connected_Helper_And_Nothing_Else()
    {
        var connected = new FakeConnector("claude", AgentConnection.Connected);
        var older = new FakeConnector("codex", AgentConnection.ConnectedOlder);
        var notConnected = new FakeConnector("third", AgentConnection.NotConnected);
        var unreadable = new FakeConnector("fourth", AgentConnection.Unreadable);
        var notFound = new FakeConnector("fifth", AgentConnection.NotFound);
        var registry = new MemoryStartupRegistry();

        var outcome = UninstallCleanup.Run([connected, older, notConnected, unreadable, notFound], new StartupSwitch(registry, Program), new OutsideGate(selfTest: false));

        Assert.Equal(["state", "disconnect"], connected.Calls);
        Assert.Equal(["state", "disconnect"], older.Calls);
        Assert.Equal(["state"], notConnected.Calls);
        Assert.Equal(["state"], unreadable.Calls); // a file that cannot be read is left alone, as the button leaves it
        Assert.Equal(["state"], notFound.Calls);
        Assert.Equal(["claude", "codex"], outcome.Disconnected);
        Assert.Empty(outcome.NotDisconnected);
        Assert.False(outcome.Refused);
        Assert.True(outcome.AllDone);
        Assert.Equal(0, registry.Writes); // nothing was written to the start-up list: the value was not there
    }

    [Fact]
    public void A_Helper_That_Could_Not_Be_Disconnected_Is_Said_And_The_Rest_Goes_On()
    {
        var stuck = new FakeConnector("claude", AgentConnection.Connected, disconnects: false);
        var codex = new FakeConnector("codex", AgentConnection.Connected);
        var registry = new MemoryStartupRegistry($"\"{Program}\" --autostart");

        var outcome = UninstallCleanup.Run([stuck, codex], new StartupSwitch(registry, Program), new OutsideGate(selfTest: false));

        Assert.Equal(["claude"], outcome.NotDisconnected);
        Assert.Equal(["codex"], outcome.Disconnected);
        Assert.True(outcome.StartupOff);
        Assert.Null(registry.Value);
        Assert.False(outcome.AllDone);
    }

    [Fact]
    public void It_Turns_Start_With_Windows_Off()
    {
        var registry = new MemoryStartupRegistry($"\"{Program}\" --autostart");

        var outcome = UninstallCleanup.Run([], new StartupSwitch(registry, Program), new OutsideGate(selfTest: false));

        Assert.True(outcome.StartupOff);
        Assert.Null(registry.Value);
        Assert.Equal(1, registry.Removes);
        Assert.Equal(0, registry.Writes);

        // As the switch does: a value that names another copy of Island is that copy's and is left alone.
        var other = new MemoryStartupRegistry("\"C:\\Other\\Island.App.exe\" --autostart");
        Assert.True(UninstallCleanup.Run([], new StartupSwitch(other, Program), new OutsideGate(selfTest: false)).StartupOff);
        Assert.Equal("\"C:\\Other\\Island.App.exe\" --autostart", other.Value);
        Assert.Equal(0, other.Removes);

        // A registry that will not let go is reported, not hidden.
        var locked = new MemoryStartupRegistry($"\"{Program}\" --autostart") { RefuseChanges = true };
        Assert.False(UninstallCleanup.Run([], new StartupSwitch(locked, Program), new OutsideGate(selfTest: false)).StartupOff);
    }

    [Fact]
    public void The_Gate_Refuses_It_Under_The_Self_Test()
    {
        var connected = new FakeConnector("claude", AgentConnection.Connected);
        var registry = new MemoryStartupRegistry($"\"{Program}\" --autostart");
        var gate = new OutsideGate(selfTest: true);

        var outcome = UninstallCleanup.Run([connected], new StartupSwitch(registry, Program), gate);

        Assert.True(outcome.Refused);
        Assert.False(outcome.AllDone);
        Assert.Empty(connected.Calls); // not even asked whether it is connected
        Assert.Equal($"\"{Program}\" --autostart", registry.Value);
        Assert.Equal(0, registry.Removes);
        Assert.Equal(1, gate.Refused(OutsideKind.EditAgentSettings));
        Assert.Equal(1, gate.Refused(OutsideKind.WriteStartupValue));
        Assert.Equal(0, gate.AllowedCount);
    }
}
