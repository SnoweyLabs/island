using Island.Core;

namespace Island.Tests;

public class FolderMatchTests
{
    private const string DownloadsGuid = "::{AAAAAAAA-0000-0000-0000-000000000001}"; // made-up parsing name

    private static FolderMatch Match() => new(
    [
        ("Downloads", @"X:\Alpha\Downloads"),
        ("Downloads", DownloadsGuid),
        ("Documents", @"X:\Alpha\Documents"),
        ("Desktop", @"X:\Alpha\Desktop"),
        ("Secrets", @"X:\Alpha\Secrets"), // not a known folder of the island: ignored
    ]);

    [Fact]
    public void Exact_Path_Matches_Its_Known_Folder()
    {
        var match = Match();

        Assert.Equal("Downloads", match.Resolve(@"X:\Alpha\Downloads"));
        Assert.Equal("Documents", match.Resolve(@"X:\Alpha\Documents"));
        Assert.Equal("Desktop", match.Resolve(@"X:\Alpha\Desktop"));
    }

    [Fact]
    public void Subfolder_Is_Not_The_Known_Folder()
    {
        var match = Match();

        Assert.Null(match.Resolve(@"X:\Alpha\Downloads\Beta"));
        Assert.Null(match.Resolve(@"X:\Alpha\DownloadsOld"));
        Assert.Null(match.Resolve(@"X:\Alpha"));
    }

    [Fact]
    public void Case_And_Trailing_Slash_Do_Not_Matter()
    {
        var match = Match();

        Assert.Equal("Downloads", match.Resolve(@"x:\alpha\DOWNLOADS\"));
        Assert.Equal("Downloads", match.Resolve("X:/Alpha/Downloads/"));
        Assert.Equal("Desktop", match.Resolve(@"  X:\Alpha\Desktop  "));
    }

    [Fact]
    public void Unknown_Path_Has_No_Known_Folder()
    {
        var match = Match();

        Assert.Null(match.Resolve(@"X:\Gamma\Downloads"));
        Assert.Null(match.Resolve(@"X:\Alpha\Secrets"));
        Assert.Null(match.Resolve(null));
        Assert.Null(match.Resolve(""));
        Assert.Null(match.Resolve("   "));
    }

    [Fact]
    public void Shell_Parsing_Name_Maps_Only_When_It_Is_The_Known_Folder()
    {
        var match = Match();

        Assert.Equal("Downloads", match.Resolve(DownloadsGuid));
        Assert.Equal("Downloads", match.Resolve(DownloadsGuid.ToLowerInvariant()));
        Assert.Null(match.Resolve("::{AAAAAAAA-0000-0000-0000-000000000002}"));
    }

    [Fact]
    public void Empty_Lookup_Matches_Nothing()
    {
        Assert.Null(new FolderMatch([]).Resolve(@"X:\Alpha\Downloads"));
    }
}
