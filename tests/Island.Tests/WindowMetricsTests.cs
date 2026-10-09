using Island.Core;

namespace Island.Tests;

public class WindowMetricsTests
{
    [Fact]
    public void Peak_Matches_The_Authors_Simulation_Of_The_Reference_Spring()
    {
        // The work order's simulation: a 502-wide capsule peaks near 526, the 76 height near 78.
        Assert.InRange(WindowMetrics.PeakCapsuleWidth, 520, 530);
        Assert.InRange(WindowMetrics.PeakCapsuleHeight, 76.5, 80);
    }

    [Fact]
    public void Window_Holds_The_Widest_Capsule_At_Peak_With_Shadow_On_Both_Sides()
    {
        Assert.True(WindowMetrics.Width >= WindowMetrics.PeakCapsuleWidth + 2 * WindowMetrics.ShadowReach);
        Assert.True(WindowMetrics.Height >=
                    LookConstants.TopGap + WindowMetrics.PeakCapsuleHeight
                    + WindowMetrics.ShadowReach + LookConstants.ShadowOffsetY);
    }

    [Fact]
    public void Shadow_Sigma_Is_Half_The_Css_Blur()
    {
        Assert.Equal(25, WindowMetrics.ShadowSigma);
    }
}
