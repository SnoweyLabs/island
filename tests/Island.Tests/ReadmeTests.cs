using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// The public README (WORK-ORDER-14 section 4): every link goes inside the repository, to snoweylabs.com or to GitHub, and every picture it shows exists.
/// In the public copy the README is at the top; in the workshop its source is ship/public/README.md, and the files the public copy puts at the top
/// (the licence, the third-party notices) are found where they come from.
/// </summary>
public class ReadmeTests
{
    // What ship/public/make-public.ps1 puts at the top of the public copy, and where each comes from.
    private static readonly Dictionary<string, string> TopFiles = new(StringComparer.Ordinal)
    {
        ["LICENSE"] = "ship/public/LICENSE",
        ["README.md"] = "ship/public/README.md",
        ["THIRD-PARTY-NOTICES.md"] = "ship/THIRD-PARTY-NOTICES.md",
    };

    private static string ReadmeText() => File.ReadAllText(Resolve("README.md") ?? throw new FileNotFoundException("README.md"));

    /// <summary>The file a path relative to the public copy's top names, here: itself, or the source of a file that is copied to the top; null when there is none.</summary>
    private static string? Resolve(string relative)
    {
        var direct = RepoPaths.File(relative.Split('/'));
        if (File.Exists(direct) || Directory.Exists(direct)) return direct;
        return TopFiles.TryGetValue(relative, out var source) && File.Exists(RepoPaths.File(source.Split('/'))) ? RepoPaths.File(source.Split('/')) : null;
    }

    // [text](target) and ![alt](target); a target ends at the first space or closing bracket.
    private static IEnumerable<(bool Picture, string Target)> Links(string text) =>
        Regex.Matches(text, @"(!?)\[[^\]]*\]\(([^)\s]+)[^)]*\)").Select(m => (m.Groups[1].Value == "!", m.Groups[2].Value));

    [Fact]
    public void Every_Link_In_The_Readme_Points_Inside_The_Repository_Or_To_Snoweylabs_Or_GitHub()
    {
        var links = Links(ReadmeText()).ToList();
        Assert.NotEmpty(links);
        foreach (var (_, target) in links)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            {
                Assert.Equal("https", uri.Scheme);
                Assert.True(uri.Host is "snoweylabs.com" or "github.com", $"a link leaves the repository, snoweylabs.com and GitHub: {uri.Host}");
                if (uri.Host == "github.com") Assert.StartsWith("/SnoweyLabs/", uri.AbsolutePath, StringComparison.Ordinal);
                continue;
            }

            var path = target.Split('#')[0];
            Assert.False(path.StartsWith('/') || path.Contains(".."), $"a link that is not inside the repository: {target}");
            Assert.True(Resolve(path) is not null, $"a link to a file that is not in the repository: {target}");
        }

        // The download link is the one that always gives the newest installer: the file's name carries no version.
        Assert.Contains("https://github.com/SnoweyLabs/island/releases/latest/download/Island-Setup.exe", links.Select(l => l.Target));
    }

    [Fact]
    public void Every_Picture_In_The_Readme_Exists()
    {
        var pictures = Links(ReadmeText()).Where(l => l.Picture).Select(l => l.Target).ToList();
        Assert.NotEmpty(pictures);
        foreach (var picture in pictures)
        {
            Assert.False(Uri.TryCreate(picture, UriKind.Absolute, out _), $"a picture from outside the repository: {picture}");
            Assert.True(Resolve(picture) is { } file && File.Exists(file), $"a picture that does not exist: {picture}");
        }
    }

    [Fact]
    public void The_Readme_Has_No_Placeholder_Left()
    {
        Assert.DoesNotMatch(new Regex("__[A-Z]+__"), ReadmeText());
    }
}
