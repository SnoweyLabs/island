using Island.Core;
using Island.SettingsUi;
using Island.Tests.Ui.Harness;

namespace Island.Tests.Ui;

/// <summary>
/// The "Coding agents" section of the settings screen (WORK-ORDER-7 section 4, WORK-ORDER-11 sections 3 and 5): what each row says, which buttons it has, and the question that comes before
/// anything is written. The view is built and laid out on a thread of its own and never shown; the connectors are invented and only count what they are asked.
/// </summary>
public class AgentsScreenTests
{
    private static SettingsView Build(Fixture fixture)
    {
        var view = fixture.NewView();
        view.Section = SettingsSection.CodingAgents;
        Tree.Layout(view, 1920, 1080);
        return view;
    }

    private static bool Has(SettingsView view, string key) => Tree.Of<System.Windows.Controls.Button>(view).Any(b => Tree.FocusKeyOf(b) == key);

    [Fact]
    public void Under_The_Gate_A_Row_Reads_Not_Connected_And_Offers_Connect()
    {
        Sta.Run(() =>
        {
            var claude = new RecordingConnector("claude", "Claude Code", AgentConnection.NotConnected);
            using var fixture = Fixture.Make(claude: claude, others: [new RecordingConnector("codex", "Codex", AgentConnection.NotConnected)]);
            var view = Build(fixture);

            Assert.True(view.CountTextContaining("Not connected.") >= 2); // both rows
            Assert.True(Has(view, "agents:claude"));
            Assert.Equal("Connect", Tree.AccessibleName(Tree.ButtonKeyed(view, "agents:claude")));
            Assert.False(Has(view, "agents:claude:update"));
        });
    }

    [Fact]
    public void A_Connection_That_Only_Tells_When_The_Helper_Has_Finished_Offers_Update_And_Disconnect()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make(claude: new RecordingConnector("claude", "Claude Code", AgentConnection.NotConnected), others: [new RecordingConnector("codex", "Codex", AgentConnection.ConnectedOlder)]);
            var view = Build(fixture);

            Assert.Equal(1, view.CountTextContaining("Connected, but only tells Island when Codex has finished. Update to see what it is doing."));
            Assert.Equal("Update", Tree.AccessibleName(Tree.ButtonKeyed(view, "agents:codex:update")));
            Assert.Equal("Disconnect", Tree.AccessibleName(Tree.ButtonKeyed(view, "agents:codex")));
        });
    }

    [Fact]
    public void A_Full_Connection_Says_What_Island_Shows_And_Offers_Only_Disconnect()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make(claude: new RecordingConnector("claude", "Claude Code", AgentConnection.Connected), others: [new RecordingConnector("codex", "Codex", AgentConnection.NotConnected)]);
            var view = Build(fixture);

            Assert.Equal(1, view.CountTextContaining("Connected. Island shows what Claude Code is doing in the Terminals page, and says so when Claude Code has finished or needs your answer."));
            Assert.Equal("Disconnect", Tree.AccessibleName(Tree.ButtonKeyed(view, "agents:claude")));
            Assert.False(Has(view, "agents:claude:update"));
        });
    }

    [Fact]
    public void A_Helper_That_Is_Not_On_This_Computer_Says_So_And_Has_No_Button()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make(claude: new RecordingConnector("claude", "Claude Code", AgentConnection.NotConnected), others: [new RecordingConnector("codex", "Codex", AgentConnection.NotFound)]);
            var view = Build(fixture);

            Assert.Equal(1, view.CountTextContaining("Not found on this computer."));
            Assert.False(Has(view, "agents:codex"));
            Assert.False(Has(view, "agents:codex:update"));
        });
    }

    [Fact]
    public void The_Two_Helpers_That_Cannot_Be_Connected_Say_Why_And_Have_No_Button_And_The_Hooks_Sentence_Is_There()
    {
        Sta.Run(() =>
        {
            using var fixture = Fixture.Make();
            var view = Build(fixture);

            foreach (var name in new[] { "Gemini", "Antigravity's terminal program" })
                Assert.Equal(1, view.CountTextContaining($"Not connected: {name} cannot start a hook without waiting for it, and Island never makes a helper\u00A0wait.")); // a no-break space before the last word (Dan's P22, WORK-ORDER-13)
            Assert.False(Has(view, "agents:gemini"));
            Assert.False(Has(view, "agents:antigravity"));
            Assert.Equal(1, view.CountTextContaining("You can see every hook with /hooks inside Claude Code."));
        });
    }

    [Fact]
    public void Connect_Shows_Every_Line_And_The_Place_Of_The_File_Before_Anything_Is_Written_And_A_Second_Button_Confirms()
    {
        Sta.Run(() =>
        {
            var claude = new RecordingConnector("claude", "Claude Code", AgentConnection.NotConnected);
            using var fixture = Fixture.Make(claude: claude, others: [new RecordingConnector("codex", "Codex", AgentConnection.NotFound)]);
            var view = Build(fixture);

            Tree.Click(view, "agents:claude");

            Assert.Equal(0, claude.Connects); // the question is open and nothing was written
            Assert.Equal(1, view.CountTextContaining("alpha-hook --event done"));
            Assert.Equal(1, view.CountTextContaining("alpha-hook --event needs-you"));
            Assert.Equal(1, view.CountTextContaining(@"%USERPROFILE%\.invented\settings.json")); // never expanded
            Assert.Equal(1, view.CountTextContaining("A copy of the file as it is now is saved beside it first"));
            Assert.Equal("Yes, connect Claude Code", Tree.AccessibleName(Tree.ButtonKeyed(view, "dialog:yes")));
            Assert.Equal("No, do not connect", Tree.AccessibleName(Tree.ButtonKeyed(view, "dialog:no")));

            Tree.Click(view, "dialog:no");
            Assert.Equal(0, claude.Connects);

            Tree.Click(view, "agents:claude");
            Tree.Click(view, "dialog:yes");
            Assert.Equal(1, claude.Connects);
        });
    }

    [Fact]
    public void Update_Says_What_To_Do_If_The_Helper_Complains_And_Disconnect_Says_It_Takes_Out_Only_Its_Own_Lines()
    {
        Sta.Run(() =>
        {
            var claude = new RecordingConnector("claude", "Claude Code", AgentConnection.ConnectedOlder);
            using var fixture = Fixture.Make(claude: claude, others: [new RecordingConnector("codex", "Codex", AgentConnection.NotFound)]);
            var view = Build(fixture);

            Tree.Click(view, "agents:claude:update");
            Assert.Equal(1, view.CountTextContaining("After Update, start Claude Code: if it complains about its settings, press Disconnect."));
            Assert.Equal(1, view.CountTextContaining("saved beside it first, under a second name"));
            Tree.Click(view, "dialog:no");

            Tree.Click(view, "agents:claude");
            Assert.Equal(1, view.CountTextContaining("Island will take its own lines out of Claude Code's file and nothing else."));
            Assert.Equal(0, claude.Disconnects);
            Tree.Click(view, "dialog:yes");
            Assert.Equal(1, claude.Disconnects);
        });
    }
}
