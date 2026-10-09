namespace Island.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_Assembly_Loads()
    {
        var name = typeof(Island.Core.CoreMarker).Assembly.GetName().Name;
        Assert.Equal("Island.Core", name);
    }
}
