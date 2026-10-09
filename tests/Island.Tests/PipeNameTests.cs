using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-13, Dan's P26: every Windows user has a pipe of their own.</summary>
public class PipeNameTests
{
    [Fact]
    public void Two_Users_Have_Two_Pipes_And_One_User_Has_The_Same_Pipe_Every_Time()
    {
        var one = AgentPipe.ForUser("LAPTOP", "Alpha");
        var other = AgentPipe.ForUser("LAPTOP", "Beta");
        Assert.NotEqual(one, other);
        Assert.Equal(one, AgentPipe.ForUser("laptop", "ALPHA")); // names of accounts are not case sensitive
        Assert.StartsWith("island.agents.", one); // the fixed name, then the user's part
        Assert.Equal(AgentPipe.ForThisUser(), AgentPipe.ForUser(Environment.UserDomainName, Environment.UserName));
        Assert.True(one.Length < 100, "a pipe name is short");
    }

    [Fact]
    public void The_App_And_The_Helper_Both_Ask_For_The_Pipe_Of_The_User_They_Run_As()
    {
        Assert.Contains("AgentPipe.ForThisUser()", File.ReadAllText(RepoPaths.File("src", "Island.App", "AppHost.cs")));
        Assert.Contains("AgentPipe.ForThisUser()", File.ReadAllText(RepoPaths.File("src", "Island.Notify", "Program.cs")));
    }
}
