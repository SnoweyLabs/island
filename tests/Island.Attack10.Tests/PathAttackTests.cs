using System.Globalization;
using Island.Core;

namespace Island.Attack10.Tests;

/// <summary>PickPath attacked: odd and hostile spellings, the profile token, the same place under many spellings. Every path is invented, on the drive Q:.</summary>
public class PathAttackTests
{
    private const string Profile = @"Q:\Invented\Profile";

    public static IEnumerable<object[]> Refused =>
    [
        [@"\\?\Q:\Invented\x"], [@"\\.\Q:\Invented\x"], [@"\\?\UNC\server\share"], [@"\\server\share\x"], ["//server/share"], [@"\/server"], [@"/\server"],
        ["Q:foo"], ["Q:"], [@"Q:\a:b"], [@"Q:\a\b:stream:$DATA"], [@"Q:\CON"], [@"Q:\a\NUL.txt"], [@"Q:\a\con."], [@"Q:\a\COM1 "], [@"Q:\aux.tar.gz"], [@"Q:\PRN"], [@"Q:\LPT9\x"],
        [@"Q:\..\..\x"], [@"Q:\a\..\..\x"], ["http://example.org"], ["file:///Q:/x"], ["ftp://Q:/x"], [@"Q:\a<b"], [@"Q:\a>b"], [@"Q:\a|b"], [@"Q:\a?b"], [@"Q:\a*b"], ["Q:\\a\"b"],
        [@"1:\x"], [@"\Invented\x"], [@"Invented\x"], ["relative/x"], [" "], [""], ["Q:\\a\nb"], ["Q:\\a\0b"], ["Q:\\a\tb"], [@"Q:\..."], [@"Q:\a\ . "], [@"Q:\%"+"APPDATA%x:y"],
        [@"%APPDATA%\x"], [@"%USERPROFILE%x"], [@"~\x"], ["Q:\\" + new string('a', 300)], [new string('a', 5000)],
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public void Holds_Hostile_Real_Paths_Are_Refused_With_A_Reason_That_Names_No_Path(string path)
    {
        Assert.False(PickPath.TryNormalize(path, out var normalized, out var reason));
        Assert.Equal(string.Empty, normalized);
        Assert.NotEmpty(reason);
        Assert.DoesNotContain("Invented", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Q:\", reason, StringComparison.Ordinal);
        Assert.Null(PickPath.Compress(path, Profile));
        Assert.Null(PickPath.Key(path));
        Assert.Null(PickPath.RealKey(path));
        Assert.Null(PickPath.LeafName(path));
    }

    [Fact]
    public void Holds_A_Null_And_A_Megabyte_Path_Are_Refused_Without_Reading_Them_All()
    {
        Assert.False(PickPath.TryNormalize(null, out _, out _));
        var megabyte = "Q:\\" + new string('a', 1 << 20);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(PickPath.TryNormalize(megabyte, out _, out _));
        Assert.False(PickPath.TryCanonical(megabyte, out _, out _));
        Assert.Null(PickPath.Expand(megabyte, Profile));
        Assert.True(clock.ElapsedMilliseconds < 500);
        Assert.False(PickPath.IsStorableForm(megabyte, out _));
    }

    [Fact]
    public void Holds_One_Folder_Under_Many_Spellings_Is_One_Key()
    {
        var spellings = new[]
        {
            @"Q:\Invented\Alpha", @"q:\invented\alpha", @"Q:\INVENTED\ALPHA\", @"Q:/Invented/Alpha", @"Q:\Invented\.\Alpha", @"Q:\Invented\Beta\..\Alpha", @"Q:\\Invented\\Alpha",
            @"Q:\Invented\Alpha.", @"Q:\Invented\Alpha ", @"  Q:\Invented\Alpha  ", @"Q:\Invented\Alpha\.", @"Q:\Invented\Alpha\\", @"Q:\Invented/Alpha\",
        };
        var keys = spellings.Select(PickPath.Key).Distinct().ToList();
        Assert.Single(keys);
        Assert.Equal(@"Q:\INVENTED\ALPHA", keys[0]);
        Assert.All(spellings, s => Assert.Equal(@"Q:\INVENTED\ALPHA", PickPath.RealKey(s)));

        var picks = spellings.Select(s => HandPicks.Folder(s, "apps", new HandContext(null, []))).ToList();
        Assert.All(picks, p => Assert.True(p.Ok, p.Message));
        var first = picks[0].Pick!;
        Assert.All(picks.Skip(1), p => Assert.True(first.IsSameThing(p.Pick!)));
        Assert.All(picks, p => Assert.Equal("alpha", p.Pick!.Name, ignoreCase: true));
    }

    [Fact]
    public void Holds_Different_Places_Are_Different_Keys()
    {
        var keys = new[] { @"Q:\Invented\Alpha", @"Q:\Invented\Alph", @"Q:\Invented\Alpha2", @"R:\Invented\Alpha", @"Q:\Invented\Alpha\Sub", @"Q:\Alpha", @"Q:\" }.Select(PickPath.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Holds_Case_Folding_Does_Not_Depend_On_The_Current_Culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(@"Q:\INVENTED\TITLE", PickPath.Key(@"Q:\Invented\title"));
            Assert.Equal(@"Q:\INVENTED\TITLE", PickPath.Key(@"q:\INVENTED\TITLE"));
            Assert.Equal("example.org", SiteMatch.Normalize("EXAMPLE.ORG"));
            Assert.Equal("title.org", SiteMatch.Normalize("TITLE.ORG"));
            Assert.Equal("title.org", TypedSite.Understand("https://TITLE.org/").Host);
            Assert.Equal("%USERPROFILE%\\TITLE", PickPath.Key("%userprofile%\\title"));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Holds_The_Profile_Token_In_Any_Capitals_Is_Brought_To_One_Spelling()
    {
        foreach (var spelling in new[] { @"%USERPROFILE%\Docs", @"%userprofile%\Docs", @"%UserProfile%\Docs", @"%USERPROFILE%/Docs", @"%USERPROFILE%\Docs\", @"%USERPROFILE%\.\Docs", @"%USERPROFILE%\\Docs" })
        {
            Assert.True(PickPath.TryCanonical(spelling, out var canonical, out _), spelling);
            Assert.Equal(@"%USERPROFILE%\Docs", canonical);
            Assert.Equal(@"Q:\Invented\Profile\Docs", PickPath.Expand(spelling, Profile));
            Assert.Equal(@"%USERPROFILE%\DOCS", PickPath.Key(spelling));
        }

        Assert.True(PickPath.TryCanonical("%userprofile%", out var bare, out _));
        Assert.Equal("%USERPROFILE%", bare);
        Assert.Equal(Profile, PickPath.Expand("%USERPROFILE%", Profile));
    }

    [Fact]
    public void Holds_The_Profile_Token_Twice_Or_In_The_Middle_Is_Only_A_Name_Never_Expanded_Again()
    {
        Assert.False(PickPath.TryCanonical(@"%USERPROFILE%%USERPROFILE%", out _, out _));
        Assert.False(PickPath.TryCanonical(@"%USERPROFILE%x\y", out _, out _));
        Assert.False(PickPath.TryCanonical(@"%USERPROFILE%\..\x", out _, out _)); // may not climb out of the profile folder
        Assert.False(PickPath.TryCanonical(@"%USERPROFILE%\a\..\..\x", out _, out _));

        // a folder that really is called %USERPROFILE% inside the profile
        Assert.True(PickPath.TryCanonical(@"%USERPROFILE%\%USERPROFILE%\x", out var twice, out _));
        Assert.Equal(@"%USERPROFILE%\%USERPROFILE%\x", twice);
        Assert.Equal(@"Q:\Invented\Profile\%USERPROFILE%\x", PickPath.Expand(twice, Profile)); // one expansion, never recursive

        // a drive path with the token in the middle is a plain path with an odd folder name
        Assert.True(PickPath.TryCanonical(@"Q:\x\%USERPROFILE%\y", out var middle, out _));
        Assert.Equal(@"Q:\x\%USERPROFILE%\y", middle);
        Assert.Equal(middle, PickPath.Expand(middle, Profile));
    }

    [Theory]
    [InlineData(@"Q:\Invented\Profile", @"%USERPROFILE%")]
    [InlineData(@"Q:\Invented\Profile\", @"%USERPROFILE%")]
    [InlineData(@"q:\invented\PROFILE\Docs\x.txt", @"%USERPROFILE%\Docs\x.txt")]
    [InlineData(@"Q:\Invented\ProfileX\a", @"Q:\Invented\ProfileX\a")] // merely contains the profile folder's name
    [InlineData(@"Q:\Invented\Profile2", @"Q:\Invented\Profile2")]
    [InlineData(@"Q:\Invented", @"Q:\Invented")]
    [InlineData(@"R:\Invented\Profile\a", @"R:\Invented\Profile\a")] // another drive
    [InlineData(@"Q:\Other\Invented\Profile\a", @"Q:\Other\Invented\Profile\a")]
    public void Holds_Compress_Only_Takes_The_Real_Profile_Folder(string real, string expected)
    {
        Assert.Equal(expected, PickPath.Compress(real, Profile));
        Assert.Equal(PickPath.RealKey(real), PickPath.RealKey(PickPath.Expand(expected, Profile)));
    }

    [Fact]
    public void Holds_Compress_And_Expand_Round_Trip_For_Odd_Profile_Folders()
    {
        foreach (var profile in new[] { @"Q:\Invented\Profile", @"Q:\Invented\Profile\", @"q:/invented/profile", @"Q:\Invented\.\Profile", @"Q:\Invented\Other\..\Profile", @"Q:\Invented\Profile." })
        foreach (var real in new[] { @"Q:\Invented\Profile\a", @"Q:\Invented\Profile\a\b.txt", @"Q:\Invented\Profile", @"Q:\Elsewhere\a" })
        {
            Assert.True(PickPath.TryNormalize(real, out var normal, out _));
            var stored = PickPath.Compress(real, profile);
            Assert.NotNull(stored);
            Assert.Equal(PickPath.RealKey(normal), PickPath.RealKey(PickPath.Expand(stored, profile)));
        }

        // no usable profile folder: nothing is compressed, and a token cannot be expanded
        foreach (var bad in new string?[] { null, "", " ", @"Q:\", "Q:", "relative", @"\\server\share\x" })
        {
            Assert.Equal(@"Q:\Invented\Profile\a", PickPath.Compress(@"Q:\Invented\Profile\a", bad));
            Assert.Null(PickPath.Expand(@"%USERPROFILE%\a", bad));
        }
    }

    [Fact]
    public void Holds_Expand_Refuses_What_Would_Pass_The_Windows_Limit()
    {
        var longProfile = @"Q:\Invented\" + new string('p', 60);
        var stored = @"%USERPROFILE%\" + new string('d', 150) + @"\" + new string('e', 50);
        Assert.True(PickPath.TryCanonical(stored, out _, out _)); // fits the stored limit
        Assert.Null(PickPath.Expand(stored, longProfile)); // but not the real one
        Assert.NotNull(PickPath.Expand(stored, Profile));
        Assert.False(PickPath.IsStorableForm(stored + new string('f', 60), out _));
    }

    [Fact]
    public void Holds_LeafName_And_Spells_Out_Profile()
    {
        Assert.Equal("notes.txt", PickPath.LeafName(@"%USERPROFILE%\Docs\notes.txt"));
        Assert.Equal("Alpha", PickPath.LeafName(@"Q:\Invented\Alpha\"));
        Assert.Null(PickPath.LeafName(@"Q:\"));
        Assert.Null(PickPath.LeafName("%USERPROFILE%"));
        Assert.True(PickPath.SpellsOutProfile(@"Q:\Invented\Profile\Docs", Profile));
        Assert.True(PickPath.SpellsOutProfile(@"q:\INVENTED\profile", Profile));
        Assert.False(PickPath.SpellsOutProfile(@"Q:\Invented\ProfileX", Profile));
        Assert.False(PickPath.SpellsOutProfile(@"%USERPROFILE%\Docs", Profile));
        Assert.False(PickPath.SpellsOutProfile(@"Q:\Invented\Profile\Docs", null));
    }

    [Fact]
    public void Holds_Stored_Form_Is_Exactly_What_Is_Kept()
    {
        Assert.True(PickPath.IsStorableForm(@"Q:\Invented\Alpha", out _));
        Assert.True(PickPath.IsStorableForm(@"%USERPROFILE%\Docs", out _));
        Assert.True(PickPath.IsStorableForm("%USERPROFILE%", out _));
        Assert.False(PickPath.IsStorableForm(@"Q:\Invented\Alpha\", out _));
        Assert.False(PickPath.IsStorableForm(@"q:\Invented\Alpha", out _));
        Assert.False(PickPath.IsStorableForm(@"Q:/Invented/Alpha", out _));
        Assert.False(PickPath.IsStorableForm(@"%userprofile%\Docs", out _));
        Assert.False(PickPath.IsStorableForm(" Q:\\Invented", out _));
        Assert.Equal(272, PickPath.MaxStoredChars);
        Assert.Equal(PickPath.ProfileToken.Length + PickPath.MaxPathChars, PickPath.MaxStoredChars);
    }
}
