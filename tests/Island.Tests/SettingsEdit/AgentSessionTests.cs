using Island.Core;

namespace Island.Tests.SettingsEdit;

/// <summary>WORK-ORDER-7 section 4: the settings session only forwards to the connector, and does nothing without one (the self-test has none).</summary>
public class AgentSessionTests
{
    private sealed class FakeConnector(AgentConnection state) : IAgentConnector
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string Location => @"%USERPROFILE%\.claude\settings.json";
        public string? NotOffered => null;
        public IReadOnlyList<string> LinesToAdd => ["line one", "line two"];

        public AgentConnection State()
        {
            Reads++;
            return state;
        }

        public ConnectorResult Connect()
        {
            Writes++;
            return new ConnectorResult(true, null);
        }

        public ConnectorResult Disconnect()
        {
            Writes++;
            return new ConnectorResult(false, AgentRefusals.HooksFileUnreadable);
        }
    }

    [Fact]
    public void Without_A_Connector_Nothing_Is_Available_And_Nothing_Is_Written()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir));
        Assert.False(session.AgentsAvailable);
        Assert.Equal(AgentConnection.NotConnected, session.AgentState());
        Assert.False(session.ConnectAgent().Ok);
    }

    [Fact]
    public void Opening_The_Section_Only_Reads_And_Only_Connect_Writes()
    {
        using var dir = new TempDir();
        var connector = new FakeConnector(AgentConnection.NotConnected);
        var session = SessionFixtures.Open(SessionFixtures.Files(dir), agents: connector);

        Assert.True(session.AgentsAvailable);
        Assert.Equal(["line one", "line two"], session.AgentLines);
        Assert.Equal(AgentConnection.NotConnected, session.AgentState());
        Assert.Equal((1, 0), (connector.Reads, connector.Writes));

        Assert.True(session.ConnectAgent().Ok);
        Assert.Equal((1, 1), (connector.Reads, connector.Writes));
    }

    [Fact]
    public void A_Refusal_Reaches_The_Person_In_Plain_Words()
    {
        using var dir = new TempDir();
        var session = SessionFixtures.Open(SessionFixtures.Files(dir), agents: new FakeConnector(AgentConnection.Unreadable));
        var result = session.DisconnectAgent();
        Assert.False(result.Ok);
        Assert.Equal(AgentRefusals.HooksFileUnreadable.Message, result.Refusal);
    }
}
