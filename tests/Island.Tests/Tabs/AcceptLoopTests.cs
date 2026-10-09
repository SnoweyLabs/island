using System.Text.RegularExpressions;

namespace Island.Tests.Tabs;

public class AcceptLoopTests
{
    [Fact]
    public void A_Failed_Accept_Pauses_Before_The_Next_One_So_A_Failing_Listener_Cannot_Spin()
    {
        var text = File.ReadAllText(RepoPaths.File("src", "Island.Bridge", "TabBridge.cs"));
        var catchAt = text.IndexOf("catch (SocketException)", StringComparison.Ordinal);
        Assert.True(catchAt > 0);
        var handler = text[catchAt..text.IndexOf("catch (Exception e) when", catchAt, StringComparison.Ordinal)];
        Assert.Matches(new Regex(@"await\s+Task\.Delay\("), handler);
    }
}
