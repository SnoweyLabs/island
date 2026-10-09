using Island.Core;

namespace Island.Attack6.Tests;

/// <summary>A pretend world for the close button whose answers can change between any two calls, and which can throw on chosen questions.</summary>
internal sealed class FactsPlay : ICloseFacts
{
    public HashSet<long> Existing { get; } = [];
    public HashSet<long> Island { get; } = [];
    public HashSet<long> Elevated { get; } = [];
    public HashSet<long> Disabled { get; } = [];
    public HashSet<string> Tabs { get; } = [];
    public bool Connected { get; set; }
    public string? ThrowOn { get; set; }
    public List<string> Asked { get; } = [];

    public FactsPlay With(params long[] windows)
    {
        foreach (var w in windows) Existing.Add(w);
        return this;
    }

    private T Ask<T>(string name, Func<T> answer)
    {
        Asked.Add(name);
        if (ThrowOn == name) throw new InvalidOperationException(name);
        return answer();
    }

    public bool WindowExists(long window) => Ask(nameof(WindowExists), () => Existing.Contains(window));

    public bool IsIslandWindow(long window) => Ask(nameof(IsIslandWindow), () => Island.Contains(window));

    public bool NeedsMoreRights(long window) => Ask(nameof(NeedsMoreRights), () => Elevated.Contains(window));

    public bool IsEnabled(long window) => Ask(nameof(IsEnabled), () => !Disabled.Contains(window));

    public bool AddonConnected => Ask(nameof(AddonConnected), () => Connected);

    public bool TabExists(string tabKey) => Ask(nameof(TabExists), () => Tabs.Contains(tabKey));
}

/// <summary>WO6 section 5: CloseButton, TabMessages close frames, ResultMessage "close".</summary>
public class CloseAttackTests
{
    private static readonly Pick Alpha = Make.Program("Alpha", PageIds.Apps, "alpha.exe");
    private static readonly Pick Beta = Make.Program("Beta", PageIds.Apps, "beta.exe");
    private static readonly Pick Site = Pick.ForSite("Site", "example.org", PageIds.Browser);
    private static readonly Pick Folder = Pick.ForFolder("Downloads", PageIds.Folders);

    private static ClickPlan Forward(long window) => new(ClickKind.BringForward, window);

    // ---- window picks ------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Dimmed_And_Silent_Until_A_Pick_Was_Clicked_And_After_A_New_Showing()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        button.PickClicked(Alpha, Forward(10), null);
        Assert.Equal(CloseState.Ready, button.State);
        button.NewShowing();
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_A_Press_Closes_The_Clicked_Window_Once_And_A_Second_Press_Does_Nothing()
    {
        var facts = new FactsPlay().With(10, 20);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        var first = button.Press();
        Assert.Equal(CloseAction.CloseWindow, first.Action);
        Assert.Equal(10, first.Window);
        Assert.Equal(CloseState.Dimmed, button.State);
        for (var i = 0; i < 5; i++) Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_Two_Clicks_Then_One_Press_Closes_Only_The_Window_Of_The_Last_Click()
    {
        var facts = new FactsPlay().With(10, 20);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        button.PickClicked(Beta, Forward(20), null);
        var outcome = button.Press();
        Assert.Equal(20, outcome.Window);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_A_Later_Click_That_Arms_Nothing_Disarms_The_Earlier_One()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        button.PickClicked(Folder, new ClickPlan(ClickKind.OpenFolder), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        button.PickClicked(Alpha, Forward(10), null);
        button.PickClicked(Beta, new ClickPlan(ClickKind.Start), null);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Theory]
    [InlineData(ClickKind.None)]
    [InlineData(ClickKind.Start)]
    [InlineData(ClickKind.OpenFolder)]
    [InlineData(ClickKind.OpenSite)]
    public void Holds_A_Click_That_Only_Starts_Or_Opens_Something_Does_Not_Arm_The_Button_Even_With_A_Window_Named(ClickKind kind)
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, new ClickPlan(kind, 10), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_A_Folder_Pick_Never_Arms_Whatever_The_Plan_Says()
    {
        var facts = new FactsPlay().With(10);
        facts.Connected = true;
        facts.Tabs.Add("t");
        var button = new CloseButton(facts);
        foreach (var kind in Enum.GetValues<ClickKind>())
        {
            button.PickClicked(Folder, new ClickPlan(kind, 10), "t");
            Assert.Equal(CloseState.Dimmed, button.State);
            Assert.Equal(CloseAction.Nothing, button.Press().Action);
        }
    }

    [Fact]
    public void Holds_A_Window_That_Vanishes_Between_The_Click_And_The_Press_Is_Not_Closed_And_The_Button_Dims()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        facts.Existing.Clear();
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Dimmed, button.State);
        facts.With(10); // it comes back (a recycled handle): the button must not have remembered it
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_The_Islands_Own_Window_Is_Never_Armed_Even_When_It_Becomes_One_After_The_Click()
    {
        var facts = new FactsPlay().With(10, 11);
        facts.Island.Add(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        button.PickClicked(Alpha, Forward(11), null);
        Assert.Equal(CloseState.Ready, button.State);
        facts.Island.Add(11);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Dimmed, button.State);
    }

    [Fact]
    public void Holds_A_Window_That_Becomes_Elevated_After_The_Click_Is_Refused_With_A_Reason_On_Every_Press_Until_It_Is_Not()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        facts.Elevated.Add(10);
        for (var i = 0; i < 3; i++)
        {
            var outcome = button.Press();
            Assert.Equal(CloseAction.Refused, outcome.Action);
            Assert.Contains("Alpha", outcome.Refusal!.Message);
            Assert.Equal(CloseState.NeedsAdmin, button.State);
            Assert.Equal(CloseButton.AdminLine, button.Line);
        }

        facts.Elevated.Clear();
        Assert.Equal(CloseAction.CloseWindow, button.Press().Action);
        Assert.Null(button.Line);
    }

    [Fact]
    public void Holds_A_Window_That_Is_Elevated_At_The_Click_Dims_With_The_Admin_Line_And_A_New_Showing_Removes_It()
    {
        var facts = new FactsPlay().With(10);
        facts.Elevated.Add(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        Assert.Equal(CloseState.NeedsAdmin, button.State);
        Assert.Equal("runs as administrator", button.Line);
        button.NewShowing();
        Assert.Null(button.Line);
        Assert.Equal(CloseState.Dimmed, button.State);
    }

    [Fact]
    public void Holds_An_Elevated_Window_That_Vanishes_Is_Forgotten_Not_Refused()
    {
        var facts = new FactsPlay().With(10);
        facts.Elevated.Add(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        facts.Existing.Clear();
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Null(button.Line);
    }

    [Fact]
    public void Holds_A_Disabled_Window_Is_Sent_Nothing_And_Is_Closed_Once_Its_Question_Is_Answered()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        facts.Disabled.Add(10);
        for (var i = 0; i < 5; i++) Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Ready, button.State);
        facts.Disabled.Clear();
        Assert.Equal(CloseAction.CloseWindow, button.Press().Action);
    }

    [Fact]
    public void Holds_A_Window_Disabled_At_The_Click_Is_Still_Armed_And_Sent_Nothing_Until_Enabled()
    {
        var facts = new FactsPlay().With(10);
        facts.Disabled.Add(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        facts.Disabled.Clear();
        Assert.Equal(CloseAction.CloseWindow, button.Press().Action);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1L)]
    [InlineData(0xFFFFL)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Holds_Odd_Window_Handles_Are_Only_Ever_Closed_When_The_Facts_Say_They_Exist(long window)
    {
        var none = new FactsPlay();
        var button = new CloseButton(none);
        button.PickClicked(Alpha, Forward(window), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    // ---- a click then a new summon then a press -----------------------------------------------------------------------

    [Fact]
    public void Holds_Click_Then_New_Showing_Then_Press_Closes_Nothing_Even_If_Everything_Is_Still_There()
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        for (var i = 0; i < 50; i++)
        {
            button.PickClicked(Alpha, Forward(10), null);
            button.NewShowing();
            Assert.Equal(CloseAction.Nothing, button.Press().Action);
        }
    }

    // ---- website picks --------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Site_Pick_Closes_The_Tab_With_The_Add_On_And_Nothing_Without_It()
    {
        var facts = new FactsPlay();
        facts.Tabs.Add("t1");
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), "t1"); // add-on not connected
        Assert.Equal(CloseState.Dimmed, button.State);

        facts.Connected = true;
        button.PickClicked(Site, Forward(0), "t1");
        Assert.Equal(CloseState.Ready, button.State);
        var outcome = button.Press();
        Assert.Equal(CloseAction.CloseTab, outcome.Action);
        Assert.Equal("t1", outcome.TabKey);
        Assert.Equal(0, outcome.Window);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Fact]
    public void Holds_A_Site_Pick_With_The_Add_On_Gone_Or_The_Tab_Gone_At_The_Press_Closes_Nothing()
    {
        var facts = new FactsPlay { Connected = true };
        facts.Tabs.Add("t1");
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), "t1");
        facts.Connected = false;
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
        Assert.Equal(CloseState.Dimmed, button.State);

        facts.Connected = true;
        button.PickClicked(Site, Forward(0), "t1");
        facts.Tabs.Clear();
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Theory]
    [InlineData(ClickKind.None)]
    [InlineData(ClickKind.Start)]
    [InlineData(ClickKind.OpenFolder)]
    [InlineData(ClickKind.OpenSite)]
    public void Holds_A_Site_Click_That_Opened_A_New_Tab_Does_Not_Arm_The_Button(ClickKind kind)
    {
        var facts = new FactsPlay { Connected = true };
        facts.Tabs.Add("t1");
        var button = new CloseButton(facts);
        button.PickClicked(Site, new ClickPlan(kind, 0), "t1");
        Assert.Equal(CloseState.Dimmed, button.State);
    }

    [Fact]
    public void Holds_A_Site_Click_With_No_Tab_Key_Does_Not_Arm_And_A_Program_Pick_Never_Closes_A_Tab()
    {
        var facts = new FactsPlay { Connected = true }.With(10);
        facts.Tabs.Add("t1");
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), null);
        Assert.Equal(CloseState.Dimmed, button.State);
        button.PickClicked(Alpha, Forward(10), "t1");
        var outcome = button.Press();
        Assert.Equal(CloseAction.CloseWindow, outcome.Action);
        Assert.Null(outcome.TabKey);
    }

    [Fact]
    public void Holds_A_Site_Click_Then_A_Program_Click_Closes_The_Window_And_Not_The_Tab()
    {
        var facts = new FactsPlay { Connected = true }.With(10);
        facts.Tabs.Add("t1");
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), "t1");
        button.PickClicked(Alpha, Forward(10), null);
        Assert.Equal(CloseAction.CloseWindow, button.Press().Action);
    }

    [Fact]
    public void Holds_An_Empty_Tab_Key_Is_Treated_Like_Any_Other_Key()
    {
        var facts = new FactsPlay { Connected = true };
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), "");
        Assert.Equal(CloseState.Dimmed, button.State); // no such tab
        facts.Tabs.Add("");
        button.PickClicked(Site, Forward(0), "");
        Assert.Equal(CloseAction.CloseTab, button.Press().Action);
    }

    // ---- facts that throw ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(ICloseFacts.WindowExists))]
    [InlineData(nameof(ICloseFacts.IsIslandWindow))]
    [InlineData(nameof(ICloseFacts.NeedsMoreRights))]
    public void Holds_A_Fact_That_Throws_At_The_Click_Leaves_The_Button_Unable_To_Close_Anything(string thrower)
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        facts.ThrowOn = thrower;
        Assert.Throws<InvalidOperationException>(() => button.PickClicked(Alpha, Forward(10), null));
        facts.ThrowOn = null;
        Assert.NotEqual(CloseState.Ready, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Theory]
    [InlineData(nameof(ICloseFacts.AddonConnected))]
    [InlineData(nameof(ICloseFacts.TabExists))]
    public void Holds_A_Fact_That_Throws_At_A_Site_Click_Leaves_The_Button_Unable_To_Close_Anything(string thrower)
    {
        var facts = new FactsPlay { Connected = true };
        facts.Tabs.Add("t");
        var button = new CloseButton(facts);
        facts.ThrowOn = thrower;
        Assert.Throws<InvalidOperationException>(() => button.PickClicked(Site, Forward(0), "t"));
        facts.ThrowOn = null;
        Assert.NotEqual(CloseState.Ready, button.State);
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    [Theory]
    [InlineData(nameof(ICloseFacts.WindowExists))]
    [InlineData(nameof(ICloseFacts.IsIslandWindow))]
    [InlineData(nameof(ICloseFacts.NeedsMoreRights))]
    [InlineData(nameof(ICloseFacts.IsEnabled))]
    public void Holds_A_Fact_That_Throws_At_The_Press_Never_Yields_A_Close_And_The_Next_Press_Asks_Everything_Again(string thrower)
    {
        var facts = new FactsPlay().With(10);
        var button = new CloseButton(facts);
        button.PickClicked(Alpha, Forward(10), null);
        facts.ThrowOn = thrower;
        Assert.Throws<InvalidOperationException>(() => button.Press());
        // What the code does today: the exception goes up to the caller and the button stays armed (State Ready). That is safe only because
        // Press asks every question again; prove it by making the window unclosable now.
        facts.ThrowOn = null;
        facts.Elevated.Add(10);
        Assert.Equal(CloseAction.Refused, button.Press().Action);
    }

    [Theory]
    [InlineData(nameof(ICloseFacts.AddonConnected))]
    [InlineData(nameof(ICloseFacts.TabExists))]
    public void Holds_A_Fact_That_Throws_At_A_Tab_Press_Never_Yields_A_Close(string thrower)
    {
        var facts = new FactsPlay { Connected = true };
        facts.Tabs.Add("t");
        var button = new CloseButton(facts);
        button.PickClicked(Site, Forward(0), "t");
        facts.ThrowOn = thrower;
        Assert.Throws<InvalidOperationException>(() => button.Press());
        facts.ThrowOn = null;
        facts.Tabs.Clear();
        Assert.Equal(CloseAction.Nothing, button.Press().Action);
    }

    // ---- everything at once --------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_Random_Storm_Of_Clicks_Showings_Presses_And_Changing_Facts_Never_Closes_Anything_It_Should_Not()
    {
        var closed = new List<long>();
        var refused = 0;
        foreach (var seed in Enumerable.Range(0, 8))
        {
            var rng = new Random(seed);
            var facts = new FactsPlay { Connected = true }.With(7, 8, 9);
            facts.Tabs.Add("t1");
            facts.Tabs.Add("t2");
            var button = new CloseButton(facts);
            long[] handles = [0, -1, 1, 7, 8, 9, 0xFFFF];
            string[] tabKeys = ["", "t1", "t2"];
            Pick[] picks = [Alpha, Beta, Site, Folder];
            long? armedWindow = null;
            string? armedTab = null;

            for (var step = 0; step < 6000; step++)
            {
                var roll = rng.Next(40);
                switch (roll switch { < 4 => roll, < 5 => 4, < 7 => 5, < 17 => 6, _ => 7 })
                {
                    case 0: Toggle(facts.Existing, handles[rng.Next(handles.Length)]); break;
                    case 1: Toggle(facts.Island, handles[rng.Next(handles.Length)]); break;
                    case 2: Toggle(facts.Elevated, handles[rng.Next(handles.Length)]); break;
                    case 3: Toggle(facts.Disabled, handles[rng.Next(handles.Length)]); break;
                    case 4:
                        facts.Connected = rng.Next(2) == 0;
                        var t = tabKeys[rng.Next(tabKeys.Length)];
                        if (!facts.Tabs.Add(t)) facts.Tabs.Remove(t);
                        break;
                    case 5:
                        button.NewShowing();
                        armedWindow = null;
                        armedTab = null;
                        break;
                    case 6:
                        var pick = picks[rng.Next(picks.Length)];
                        var plan = new ClickPlan(Enum.GetValues<ClickKind>()[rng.Next(5)], handles[rng.Next(handles.Length)]);
                        var tab = rng.Next(3) == 0 ? null : tabKeys[rng.Next(tabKeys.Length)];
                        button.PickClicked(pick, plan, tab);
                        armedWindow = null;
                        armedTab = null;
                        if (pick.Kind == PickKind.Folder) { }
                        else if (pick.Kind == PickKind.Site)
                        {
                            if (plan.Kind == ClickKind.BringForward && tab is not null && facts.Connected && facts.Tabs.Contains(tab)) armedTab = tab;
                        }
                        else if (plan.Kind == ClickKind.BringForward && plan.Target != 0 && !facts.Island.Contains(plan.Target) && facts.Existing.Contains(plan.Target))
                            armedWindow = plan.Target;
                        break;
                    default:
                        var outcome = button.Press();
                        if (armedTab is { } at)
                        {
                            if (facts.Connected && facts.Tabs.Contains(at))
                            {
                                Assert.Equal(CloseAction.CloseTab, outcome.Action);
                                Assert.Equal(at, outcome.TabKey);
                            }
                            else Assert.Equal(CloseAction.Nothing, outcome.Action);
                            armedTab = null;
                        }
                        else if (armedWindow is { } aw)
                        {
                            if (!facts.Existing.Contains(aw) || facts.Island.Contains(aw))
                            {
                                Assert.Equal(CloseAction.Nothing, outcome.Action);
                                armedWindow = null;
                            }
                            else if (facts.Elevated.Contains(aw)) { Assert.Equal(CloseAction.Refused, outcome.Action); refused++; }
                            else if (facts.Disabled.Contains(aw)) Assert.Equal(CloseAction.Nothing, outcome.Action);
                            else
                            {
                                Assert.Equal(CloseAction.CloseWindow, outcome.Action);
                                Assert.Equal(aw, outcome.Window);
                                closed.Add(aw);
                                armedWindow = null;
                            }
                        }
                        else Assert.Equal(CloseAction.Nothing, outcome.Action);
                        break;
                }

                if (armedWindow is null && armedTab is null) Assert.NotEqual(CloseState.Ready, button.State);
                if (button.State == CloseState.NeedsAdmin) Assert.Equal(CloseButton.AdminLine, button.Line);
                else Assert.Null(button.Line);
            }

        }

        Assert.True(closed.Count > 50, $"only {closed.Count} windows were ever closed: the storm is too tame");
        Assert.True(refused > 20);
    }

    private static void Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Add(value)) set.Remove(value);
    }

    // ---- the frames -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Holds_The_Close_Frame_Is_The_Protocol_Example_And_Reads_Back()
    {
        Assert.Equal("{\"type\":\"close\",\"id\":11}", TabMessages.CloseTab(11));
        var parsed = TabMessages.ParseIsland(TabMessages.CloseTab(11));
        Assert.True(parsed.Ok);
        Assert.Equal(new CloseTabMessage(11), parsed.Message);
        foreach (var id in new[] { 0, 1, int.MaxValue })
            Assert.Equal(new CloseTabMessage(id), TabMessages.ParseIsland(TabMessages.CloseTab(id)).Message);
    }

    [Theory]
    [InlineData("{\"type\":\"close\"}")]
    [InlineData("{\"type\":\"close\",\"id\":-1}")]
    [InlineData("{\"type\":\"close\",\"id\":2147483648}")]
    [InlineData("{\"type\":\"close\",\"id\":9223372036854775807}")]
    [InlineData("{\"type\":\"close\",\"id\":1e999}")]
    [InlineData("{\"type\":\"close\",\"id\":1.5}")]
    [InlineData("{\"type\":\"close\",\"id\":\"5\"}")]
    [InlineData("{\"type\":\"close\",\"id\":null}")]
    [InlineData("{\"type\":\"close\",\"id\":[5]}")]
    [InlineData("{\"type\":\"close\",\"id\":{\"id\":5}}")]
    [InlineData("{\"type\":\"close\",\"id\":true}")]
    [InlineData("{\"type\":\"Close\",\"id\":5}")]
    [InlineData("{\"type\":\"close \",\"id\":5}")]
    [InlineData("{\"type\":[\"close\"],\"id\":5}")]
    [InlineData("[{\"type\":\"close\",\"id\":5}]")]
    [InlineData("\"close\"")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"type\":\"close\",\"id\":5")]
    public void Holds_A_Bad_Close_Frame_Is_Rejected_With_A_Reason_And_Never_Throws(string frame)
    {
        var parsed = TabMessages.ParseIsland(frame);
        Assert.False(parsed.Ok);
        Assert.False(string.IsNullOrEmpty(parsed.Reject));
        Assert.True(parsed.Reject!.Length <= 40, parsed.Reject); // a fixed phrase, never the frame
    }

    [Theory]
    [InlineData("{\"type\":\"close\",\"id\":11,\"extra\":{\"deep\":[1,2,3]}}", 11)]
    [InlineData("{\"id\":11,\"type\":\"close\"}", 11)]
    [InlineData("{\"type\":\"close\",\"id\":0}", 0)]
    [InlineData("{\"type\":\"close\",\"id\":-0}", 0)]
    [InlineData("{\"type\":\"close\",\"id\":11.0}", 11)]
    [InlineData("{\"type\":\"close\",\"id\":1.1e1}", 11)]
    [InlineData("{\"type\":\"close\",\"id\":2147483647}", int.MaxValue)]
    public void Holds_Odd_But_Valid_Close_Frames_Read_As_The_Same_Integer(string frame, int id)
    {
        var parsed = TabMessages.ParseIsland(frame);
        // 11.0 and 1.1e1 are numerically the integer 11; the reader may accept or refuse them, but never read a different number.
        if (parsed.Ok) Assert.Equal(new CloseTabMessage(id), parsed.Message);
        else Assert.StartsWith("bad", parsed.Reject);
    }

    [Fact]
    public void Holds_A_Repeated_Id_Name_Never_Reads_A_Negative_Or_Out_Of_Range_Number()
    {
        foreach (var frame in new[] { "{\"type\":\"close\",\"id\":1,\"id\":-1}", "{\"type\":\"close\",\"id\":-1,\"id\":1}", "{\"type\":\"close\",\"type\":\"resync\",\"id\":1}", "{\"type\":\"resync\",\"type\":\"close\",\"id\":1}" })
        {
            var parsed = TabMessages.ParseIsland(frame);
            if (parsed.Message is CloseTabMessage c) Assert.True(c.Id >= 0);
        }
    }

    [Fact]
    public void Holds_A_Frame_Of_One_Megabyte_Is_Refused_As_Oversize_And_One_Just_Under_Is_Read()
    {
        var pad = new string('x', TabProtocol.MaxFrameBytes);
        var big = "{\"type\":\"close\",\"id\":5,\"pad\":\"" + pad + "\"}";
        var parsed = TabMessages.ParseIsland(big);
        Assert.False(parsed.Ok);
        Assert.Equal("oversize", parsed.Reject);

        var head = "{\"type\":\"close\",\"id\":5,\"pad\":\"";
        var tail = "\"}";
        var fits = head + new string('x', TabProtocol.MaxFrameBytes - head.Length - tail.Length) + tail;
        Assert.Equal(TabProtocol.MaxFrameBytes, System.Text.Encoding.UTF8.GetByteCount(fits));
        Assert.Equal(new CloseTabMessage(5), TabMessages.ParseIsland(fits).Message);
        Assert.Equal("oversize", TabMessages.ParseIsland(fits + " ").Reject);
    }

    [Fact]
    public void Holds_A_Frame_Whose_Characters_Are_Few_But_Bytes_Are_Many_Is_Refused_As_Oversize()
    {
        var pad = new string('\u20AC', TabProtocol.MaxFrameBytes / 3 + 10); // three bytes each
        var frame = "{\"type\":\"close\",\"id\":5,\"pad\":\"" + pad + "\"}";
        Assert.True(frame.Length < TabProtocol.MaxFrameBytes);
        Assert.Equal("oversize", TabMessages.ParseIsland(frame).Reject);
    }

    [Fact]
    public void Holds_Deep_Nesting_Lone_Surrogates_And_Control_Characters_Never_Throw()
    {
        foreach (var frame in new[]
        {
            "{\"type\":\"close\",\"id\":5,\"x\":" + new string('[', 50_000) + "}",
            "{\"type\":\"close\",\"id\":5,\"x\":\"\\ud800\"}",
            "{\"type\":\"close\",\"id\":5,\"x\":\"\\udc00\\ud800\"}",
            "{\"type\":\"close\",\"id\":5,\"x\":\"\u0001\"}",
            "{\"type\":\"close\",\"id\":5}\0",
            "{\"type\":\"close\",\"id\":5}{\"type\":\"close\",\"id\":6}",
            "{\"type\":\"cl\\u006fse\",\"id\":5}",
            "\uD800{\"type\":\"close\",\"id\":5}",
            "﻿{\"type\":\"close\",\"id\":5}",
        })
        {
            var parsed = TabMessages.ParseIsland(frame);
            Assert.True(parsed.Ok == (parsed.Message is not null));
        }

        Assert.Equal(new CloseTabMessage(5), TabMessages.ParseIsland("{\"type\":\"cl\\u006fse\",\"id\":5}").Message);
        Assert.Equal(new CloseTabMessage(5), TabMessages.ParseIsland("{\"type\":\"close\",\"id\":5,\"x\":\"\\ud800\"}").Message);
    }

    [Fact]
    public void Holds_Random_Garbage_And_Truncated_Frames_Never_Throw_In_Either_Direction()
    {
        var rng = new Random(7);
        string[] seeds = [TabMessages.CloseTab(11), "{\"type\":\"result\",\"cmd\":\"close\",\"id\":11,\"ok\":true}", TabMessages.Welcome(), TabMessages.Activate(1, 2), TabMessages.MediaCommandFrame(3, MediaCommand.Next)];
        for (var i = 0; i < 20_000; i++)
        {
            var s = seeds[rng.Next(seeds.Length)];
            var chars = s.ToCharArray();
            for (var k = 0; k < rng.Next(0, 4); k++) chars[rng.Next(chars.Length)] = (char)rng.Next(0, 0x300);
            var text = new string(chars, 0, rng.Next(0, chars.Length + 1));
            Assert.Equal(TabMessages.ParseIsland(text).Ok, TabMessages.ParseIsland(text).Message is not null);
            Assert.Equal(TabMessages.ParseAddon(text).Ok, TabMessages.ParseAddon(text).Message is not null);
        }
    }

    [Fact]
    public void Holds_The_Add_On_Answer_To_A_Close_Reads_The_Protocol_Example_And_Rejects_Variants()
    {
        var parsed = TabMessages.ParseAddon("{\"type\":\"result\",\"cmd\":\"close\",\"id\":11,\"ok\":true}");
        Assert.Equal(new ResultMessage("close", 11, true), parsed.Message);
        Assert.Equal(new ResultMessage("close", 0, false), TabMessages.ParseAddon("{\"type\":\"result\",\"cmd\":\"close\",\"id\":0,\"ok\":false,\"error\":\"x\"}").Message);
        foreach (var bad in new[]
        {
            "{\"type\":\"result\",\"cmd\":\"Close\",\"id\":11,\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":-1,\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":1.5,\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":\"11\",\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":11,\"ok\":1}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":11,\"ok\":\"true\"}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"id\":11}",
            "{\"type\":\"result\",\"cmd\":\"close\",\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"closeTab\",\"id\":11,\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":null,\"id\":11,\"ok\":true}",
            "{\"type\":\"result\",\"id\":11,\"ok\":true}",
            "{\"type\":\"result\",\"cmd\":\"" + new string('c', 65) + "\",\"id\":11,\"ok\":true}",
        })
            Assert.False(TabMessages.ParseAddon(bad).Ok, bad);
    }

    [Fact]
    public void Holds_An_Island_Frame_Fed_To_The_Add_Side_Reader_And_Back_Is_Rejected()
    {
        Assert.False(TabMessages.ParseAddon(TabMessages.CloseTab(1)).Ok);
        Assert.False(TabMessages.ParseIsland("{\"type\":\"result\",\"cmd\":\"close\",\"id\":11,\"ok\":true}").Ok);
    }

    [Fact]
    public void Defect_CloseTab_Builds_A_Frame_Its_Own_Reader_Rejects_For_A_Negative_Id()
    {
        // The id is an int in the signature but the protocol (and ParseIsland) only knows 0 and up; the builder happily writes -1, the
        // add-side reader would refuse it. Tab ids come from the add-on (never negative) so nothing produces this today (LATENT).
        // Fixed by refusing at the builder: a negative id is no tab, so no frame is written (the protocol knows 0 and up).
        Assert.Throws<ArgumentOutOfRangeException>(() => TabMessages.CloseTab(-1));
        Assert.True(TabMessages.ParseIsland(TabMessages.CloseTab(0)).Ok);
    }
}
