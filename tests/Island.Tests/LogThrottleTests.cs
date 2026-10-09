using Island.Core;

namespace Island.Tests;

public class LogThrottleTests
{
    [Fact]
    public void The_Same_Line_Again_Within_Five_Seconds_Is_Not_Written_And_After_Them_It_Is()
    {
        var throttle = new LogThrottle();
        Assert.True(throttle.ShouldWrite("unhandled error: Alpha", 0));
        Assert.False(throttle.ShouldWrite("unhandled error: Alpha", 6));
        Assert.False(throttle.ShouldWrite("unhandled error: Alpha", LogThrottle.SameLineWithinMs - 1));
        Assert.True(throttle.ShouldWrite("unhandled error: Alpha", LogThrottle.SameLineWithinMs));
    }

    [Fact]
    public void A_Different_Line_Is_Always_Written()
    {
        var throttle = new LogThrottle();
        Assert.True(throttle.ShouldWrite("one", 0));
        Assert.True(throttle.ShouldWrite("two", 1));
        Assert.True(throttle.ShouldWrite("three", 2));
        Assert.False(throttle.ShouldWrite("one", 3)); // the last few lines are remembered, not only the one before
        Assert.True(throttle.ShouldWrite("one", 3 + LogThrottle.SameLineWithinMs));
    }
}
