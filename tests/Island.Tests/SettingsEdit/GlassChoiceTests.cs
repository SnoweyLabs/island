using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

public class GlassChoiceTests
{
    [Fact]
    public void Lists_The_Three_Glasses_And_Offers_Blur_Only_When_It_Is_Available()
    {
        Assert.Equal([GlassKind.Approved, GlassKind.Darker, GlassKind.Blur], GlassChoice.All.Select(o => o.Kind));
        Assert.Equal([GlassKind.Approved, GlassKind.Darker, GlassKind.Blur], GlassChoice.Offered(true).Select(o => o.Kind));
        Assert.Equal([GlassKind.Approved, GlassKind.Darker], GlassChoice.Offered(false).Select(o => o.Kind));
        Assert.All(GlassChoice.All, o => Assert.False(string.IsNullOrWhiteSpace(o.Description)));
    }

    [Fact]
    public void Blur_That_Is_Saved_But_Not_Available_Draws_As_Approved_And_Stays_Saved()
    {
        Assert.False(GlassChoice.IsOffered(GlassKind.Blur, blurAvailable: false));
        Assert.True(GlassChoice.IsOffered(GlassKind.Darker, blurAvailable: false));
        Assert.Equal(GlassKind.Approved, GlassChoice.Effective(GlassKind.Blur, false));
        Assert.Equal(GlassKind.Blur, GlassChoice.Effective(GlassKind.Blur, true));
        Assert.Equal(GlassKind.Darker, GlassChoice.Effective(GlassKind.Darker, false));
    }
}
