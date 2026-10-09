using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

/// <summary>Every number of the version-2 message and the book is pinned here: a change must be a decision.</summary>
public class SessionLimitsTests
{
    [Fact]
    public void The_Numbers_Of_The_Work_Order_Are_Pinned()
    {
        Assert.Equal(2, SessionLimits.WireVersion);
        Assert.Equal(64, SessionLimits.MaxSessionIdChars);
        Assert.Equal(260, SessionLimits.MaxFolderChars);
        Assert.Equal(AgentPipe.MaxChain, SessionLimits.MaxChain);
        Assert.Equal(16, SessionLimits.MaxChain);
        Assert.Equal(AgentPipe.MaxMessageBytes, SessionLimits.MaxMessageBytes);
        Assert.Equal(4096, SessionLimits.MaxMessageBytes);
        Assert.Equal(64, SessionLimits.MaxSessions);
    }

    [Fact]
    public void The_Numbers_Claude_Chose_Are_Pinned()
    {
        Assert.Equal(32, SessionLimits.MaxHelperChars);
        Assert.Equal(48, SessionLimits.MaxEventChars);
        Assert.Equal(64, SessionLimits.MaxKindChars);
        Assert.Equal("claude", HelperNames.ClaudeCode);
    }

    [Fact]
    public void The_Worst_Case_Of_The_Limits_Fits_In_A_Message()
    {
        // Every character may escape to six bytes; the chain's numbers are at most ten digits and a comma; the rest is punctuation.
        var chars = SessionLimits.MaxHelperChars + SessionLimits.MaxEventChars + SessionLimits.MaxKindChars + SessionLimits.MaxSessionIdChars + SessionLimits.MaxFolderChars;
        var worst = chars * 6 + SessionLimits.MaxChain * 11 + 120;

        Assert.True(worst < SessionLimits.MaxMessageBytes, $"{worst}");
    }
}
