using Island.Core;

namespace Island.Tests;

public class FolderLocationTests
{
    [Fact]
    public void File_Address_Becomes_A_Windows_Path()
    {
        Assert.Equal(@"X:\Alpha\Downloads", FolderLocation.FromUrl("file:///X:/Alpha/Downloads"));
        Assert.Equal(@"X:\Alpha Beta\Downloads", FolderLocation.FromUrl("file:///X:/Alpha%20Beta/Downloads"));
    }

    [Fact]
    public void Network_Address_Becomes_A_Unc_Path()
    {
        Assert.Equal(@"\\alpha\beta\gamma", FolderLocation.FromUrl("file://alpha/beta/gamma"));
    }

    [Fact]
    public void Shell_Parsing_Name_Is_Kept_As_It_Is()
    {
        Assert.Equal("::{AAAAAAAA-0000-0000-0000-000000000001}", FolderLocation.FromUrl("file:///::{AAAAAAAA-0000-0000-0000-000000000001}"));
    }

    [Fact]
    public void Anything_Else_Has_No_Path()
    {
        Assert.Null(FolderLocation.FromUrl(null));
        Assert.Null(FolderLocation.FromUrl(""));
        Assert.Null(FolderLocation.FromUrl("https://example.org/Alpha"));
    }
}
