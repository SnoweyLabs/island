using System.Reflection;
using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-10 §1: every number of the colour rule is a named constant, pinned here, marked (Claude) in its comment.</summary>
public class RoundIconConstantsTests
{
    [Fact]
    public void Every_Number_Of_The_Rule_Is_Pinned()
    {
        Assert.Equal(128, RoundIconConstants.OpaqueAlphaLine);
        Assert.Equal(4, RoundIconConstants.GroupBitsPerChannel);
        Assert.Equal(70, RoundIconConstants.PlatePercent);
        Assert.Equal(90, RoundIconConstants.FlatPercent);
        Assert.Equal(230, RoundIconConstants.NearlyWhiteLine);
        Assert.Equal((40, 42, 50), (RoundIconConstants.DarkNeutralRed, RoundIconConstants.DarkNeutralGreen, RoundIconConstants.DarkNeutralBlue));
        Assert.Equal(new RoundIconColour(40, 42, 50), RoundIconConstants.DarkNeutral);
        Assert.Equal(60, RoundIconConstants.VisibleDifference);
        Assert.Equal(90, RoundIconConstants.VisiblePercent);
        Assert.Equal(0.66, RoundIconConstants.IconBoxFraction);
        Assert.Equal(8, RoundIconConstants.DiscStepPercent);
        Assert.Equal(12, RoundIconConstants.MaxDiscSteps);
        Assert.Equal(100, RoundIconConstants.DarkDiscMaxChannel);
        Assert.Equal(2048 * 2048, RoundIconConstants.MaxPixelsRead);
    }

    [Fact]
    public void No_Constant_Is_Added_Without_Being_Pinned_Above()
    {
        var fields = typeof(RoundIconConstants).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name).Order().ToArray();
        string[] pinned =
        [
            "ClosedVisibleDifference", "DarkDiscMaxChannel", "DarkNeutralBlue", "DarkNeutralGreen", "DarkNeutralRed", "DiscStepPercent", "FlatPercent", "GroupBitsPerChannel",
            "IconBoxFraction", "MaxDiscSteps", "MaxPixelsRead", "NearlyWhiteLine", "OpaqueAlphaLine", "PlatePercent", "VisibleDifference", "VisiblePercent",
        ];
        Assert.Equal(pinned, fields);
    }

    [Fact]
    public void Every_Chosen_Number_Says_It_Is_Claudes_In_Its_Comment()
    {
        var source = File.ReadAllText(RepoPaths.File("src", "Island.Core", "RoundIcon", "RoundIconConstants.cs"));
        foreach (var name in new[] { "GroupBitsPerChannel", "PlatePercent", "FlatPercent", "NearlyWhiteLine", "VisibleDifference", "VisiblePercent", "ClosedVisibleDifference", "IconBoxFraction", "DiscStepPercent", "MaxDiscSteps", "DarkDiscMaxChannel", "MaxPixelsRead", "OpaqueAlphaLine" })
        {
            var at = source.IndexOf(name + " =", StringComparison.Ordinal);
            Assert.True(at > 0, name);
            var comment = source[source.LastIndexOf("/// <summary>", at, StringComparison.Ordinal)..at];
            Assert.Contains("(Claude", comment);
        }
    }
}
