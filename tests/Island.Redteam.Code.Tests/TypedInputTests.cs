using System.Diagnostics;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: what a person types or chooses (an address for a site pick, a place for a hand-made pick, a page's or a scene's name, the search text) at its edges:
/// too long, odd Unicode, in bulk, fast. Invented names only.
/// </summary>
public class TypedInputTests
{
    private static readonly string[] Pieces =
    [
        "http", "https", "ftp", "://", "//", ":", "@", "/", "\\", "?", "#", ".", "..", "-", "_", "www", "example", "org", "com", "xn--", "\u00FC", "\uD800", "\uDC00", "\U0001F600", "\u200B", "\u202E", "\0", " ", "\t",
        "8080", "65536", "99999999999999999999", "[::1]", "127.0.0.1", "C:", "C:\\", "%USERPROFILE%", "CON", "NUL.txt", "a", "b", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    ];

    private static string Random(Random rng, int pieces)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < pieces; i++) sb.Append(Pieces[rng.Next(Pieces.Length)]);
        return sb.ToString();
    }

    [Fact]
    public void TypedSite_And_PickPath_Fuzzed_Never_Throw_And_What_They_Accept_Is_In_Their_Own_Limits()
    {
        var rng = new Random(31);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 60_000; i++)
        {
            var text = Random(rng, rng.Next(0, 14));
            SiteUnderstanding site = null!;
            Assert.Null(Record.Exception(() => site = TypedSite.Understand(text)));
            Assert.True(site.Ok ^ site.Refusal is not null);
            if (site.Host is { } host)
            {
                Assert.True(host.Length <= 195 && host.Contains('.') && host == host.ToLowerInvariant() && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'), host);
                Assert.Equal(host, TypedSite.Understand(host).Host); // what is shown is what is kept: understanding it again changes nothing
            }

            string normalized = "";
            string reason = "";
            var ok = false;
            Assert.Null(Record.Exception(() => ok = PickPath.TryNormalize(text, out normalized, out reason)));
            if (ok)
            {
                Assert.True(normalized.Length <= PickPath.MaxPathChars && normalized[1] == ':' && !normalized.Contains('/') && !normalized.EndsWith("\\\\"), normalized);
                Assert.True(PickPath.TryNormalize(normalized, out var again, out _) && again == normalized, "normalizing twice changes the path: " + normalized);
            }
            else if (text.Trim().Length >= 8)
            {
                Assert.DoesNotContain(text.Trim(), reason); // a refusal names the rule, never what was typed
            }

            Assert.Null(Record.Exception(() => PickPath.TryCanonical(text, out _, out _)));
            Assert.Null(Record.Exception(() => PickPath.Compress(text, "C:\\Users\\Invented")));
            Assert.Null(Record.Exception(() => PickPath.Expand(text, "C:\\Users\\Invented")));
            Assert.Null(Record.Exception(() => PickPath.LeafName(text)));
            Assert.Null(Record.Exception(() => SearchMatch.Fold(text)));
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), clock.Elapsed.ToString());
    }

    [Fact]
    public void Huge_Typed_Text_Is_Refused_Without_Being_Read()
    {
        var huge = new string('a', 50_000_000) + ".org";
        var clock = Stopwatch.StartNew();
        Assert.False(TypedSite.Understand(huge).Ok);
        Assert.False(PickPath.TryNormalize("C:\\" + huge, out _, out _));
        Assert.False(PickPath.TryCanonical(huge, out _, out _));
        Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");
    }

    [Theory]
    [InlineData("example.org", "example.org")]
    [InlineData("https://www.Example.org:8080/page?x#y", "example.org")]
    [InlineData("user@example.org", "example.org")]
    [InlineData(" example.org. ", "example.org")]
    [InlineData("//example.org/x", "example.org")]
    [InlineData("http://user:pw@example.org:80@other.example/", "other.example")]
    [InlineData("example.org:", "example.org")]
    [InlineData("b\u00FCcher.example", "xn--bcher-kva.example")]
    [InlineData("https://example.org/a?u=https://x.example", "example.org")]
    public void TypedSite_Understands(string typed, string host) => Assert.Equal(host, TypedSite.Understand(typed).Host);

    [Theory]
    [InlineData("example")]
    [InlineData("ftp://example.org")]
    [InlineData("127.0.0.1")]
    [InlineData("0x7f.1")]
    [InlineData("example.org:99999")]
    [InlineData("example.org:80x")]
    [InlineData("[::1]:80")]
    [InlineData("-a.example.org")]
    [InlineData("a_b.example.org")]
    [InlineData("a..example.org")]
    [InlineData("exa mple.org")]
    [InlineData("example.org\nx")]
    public void TypedSite_Refuses(string typed) => Assert.False(TypedSite.Understand(typed).Ok);

    [Fact]
    public void Defect_TypedSite_A_Bare_Address_With_A_Web_Address_In_Its_Query_Is_Refused_As_Not_A_Site()
    {
        // code-1-12 (LOW): HostOf takes the FIRST "://" anywhere in the text as the end of a scheme. For "example.org/go?u=https://other.example" the "scheme" is "example.org/go?u=https",
        // which is not http or https, so the whole address is refused with NOT_A_SITE ("That is not a website's name") although "example.org/page?x#y" and "https://example.org/go?u=..." both work.
        // Expected: example.org (a scheme is only what stands before the first "://" when it holds none of / ? # @ :). Repair: look for "://" only in the part before the first of those.
        var site = TypedSite.Understand("example.org/go?u=https://other.example");
        Assert.Equal("example.org", site.Host);
    }

    [Fact]
    public void Names_Of_Pages_And_Scenes_Are_Cut_And_Compared_The_Same_Way_Whatever_The_Spacing()
    {
        var store = PageStore.Default;
        var created = store.Create("My  \t Page\u00A0Two", "#112233");
        Assert.False(created.Refused, created.Refusal);
        Assert.Equal("My Page Two", created.Page!.Name);
        Assert.True(created.Store.Create("my page two", "#445566").Refused); // the same name in other capitals and spacing
        Assert.True(store.Create(new string('x', 5_000_000), "#112233").Refused);
        Assert.True(store.Create("   ", "#112233").Refused);
        Assert.True(store.Create("Fine", "#12345").Refused);
        Assert.True(store.Create("Fine", "#1234567").Refused);
        Assert.True(store.Create("Fine", "#12345\n").Refused);

        var scenes = SceneStore.Empty;
        Assert.False(scenes.Create("Work").Refused);
        Assert.True(scenes.Create(new string('x', 5_000_000)).Refused);
        Assert.True(scenes.Create("\u200B\u200B").Refused);
        Assert.True(scenes.Create("a\u202Eb").Refused);
    }

    [Fact]
    public void SearchMatch_A_Long_Text_Against_Many_Candidates_Stays_Quick()
    {
        var candidates = Enumerable.Range(0, 2000).Select(i => new SearchCandidate("Candidate Number " + i, "program:c" + i, true, false)).ToArray();
        var clock = Stopwatch.StartNew();
        foreach (var text in new[] { "c", "candidate", new string('c', 5000), new string('\u00E9', 5000), "number 1999", "\u0130\u0130\u0130" })
            Assert.Null(Record.Exception(() => SearchMatch.Rank(candidates, text)));
        Assert.True(clock.ElapsedMilliseconds < 5000, clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void HandAdd_Odd_Paths_And_Choosers_That_Answer_Strangely_Never_Throw_And_Add_Nothing_Wrong()
    {
        var context = new HandContext("C:\\Users\\Invented", [("Downloads", "C:\\Users\\Invented\\Downloads")]);
        foreach (var path in new[] { "", " ", "C:", "C:\\", "C:\\a\\..\\..\\b", "C:\\Users\\Invented\\..\\Other", "\\\\server\\share\\x", "C:\\CON", "C:\\a\\NUL.txt", "C:\\" + new string('x', 400), "C:\\a:b", "C:\\Users\\Invented", "c:/users/invented/downloads", "C:\\a\\b.exe\\" })
        {
            var folder = Record.Exception(() => HandPicks.Folder(path, PageIds.Apps, context));
            var file = Record.Exception(() => HandPicks.File(path, PageIds.Apps, context));
            var program = Record.Exception(() => HandPicks.BrowsedProgram(path, PageIds.Apps, context));
            Assert.Null(folder);
            Assert.Null(file);
            Assert.Null(program);
        }

        // a refused thing carries no pick and a reason that names no path
        var refused = HandPicks.Folder("\\\\server\\share\\x", PageIds.Apps, context);
        Assert.True(refused.Pick is null);
    }
}
