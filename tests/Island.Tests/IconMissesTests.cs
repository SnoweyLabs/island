using Island.Core;

namespace Island.Tests;

public class IconMissesTests
{
    private long _now;

    private IconMisses Make() => new(() => _now);

    [Fact]
    public void A_Key_That_Never_Missed_Is_Not_Asked_Again()
    {
        Assert.False(Make().ShouldRetry("alpha"));
    }

    [Fact]
    public void A_Miss_Is_Asked_Again_Once_After_The_Wait_And_Not_Before()
    {
        var misses = Make();
        misses.Missed("alpha");
        Assert.False(misses.ShouldRetry("alpha"));

        _now = IconMisses.RetryAfterMs;
        Assert.True(misses.ShouldRetry("alpha"));
        Assert.False(misses.ShouldRetry("alpha")); // the read is under way
    }

    [Fact]
    public void A_Miss_Is_Asked_Again_Only_A_Few_Times()
    {
        var misses = Make();
        var retries = 0;
        misses.Missed("alpha");
        for (var i = 0; i < 10; i++)
        {
            _now += IconMisses.RetryAfterMs;
            if (!misses.ShouldRetry("alpha")) continue;
            retries++;
            misses.Missed("alpha");
        }

        Assert.Equal(IconMisses.MaxTries - 1, retries);
    }
}
