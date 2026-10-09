using System.Diagnostics;
using System.Text;
using Island.Core;
using Xunit.Abstractions;
using static Island.Attack7B.Tests.Support;

namespace Island.Attack7B.Tests;

/// <summary>AgentWire, AgentNotice and ProjectName on hostile text, in memory only.</summary>
public class WireAttackTests(ITestOutputHelper output)
{
    private static string RandomText(Random r, int max)
    {
        const string pool = "abcXYZ019 /\\:.-_\"<>&'\t\r\n\0\u0085\u2028\u2029\u202E\u200B\u00e9\u4e2d\ud800\udc00\ud83d\ude00";
        var sb = new StringBuilder();
        var n = r.Next(0, max);
        for (var i = 0; i < n; i++)
        {
            var c = r.Next(10) == 0 ? (char)r.Next(0, 0x10000) : pool[r.Next(pool.Length)];
            sb.Append(c); // includes lone surrogates on purpose
        }

        return sb.ToString();
    }

    // ---- Encode / TryParse ----------------------------------------------------------------------------------

    [Fact]
    public void Holds_Two_Thousand_Random_Messages_Encode_Within_4096_And_Decode_To_Clean_Bounded_Fields()
    {
        var r = new Random(77);
        for (var i = 0; i < 2000; i++)
        {
            var chain = Enumerable.Range(0, r.Next(0, 40)).Select(_ => r.Next(-5, int.MaxValue)).ToList();
            var bytes = AgentWire.Encode(RandomText(r, 80), RandomText(r, 120), RandomText(r, 700), chain);
            Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes, "length " + bytes.Length);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.DoesNotContain((byte)'\n', bytes[..^1]);
            var m = AgentWire.TryParse(bytes.AsSpan(0, bytes.Length - 1));
            Assert.NotNull(m);
            Assert.True(m.Event.Length <= 32 && m.Kind.Length <= 64 && m.Folder.Length <= 260);
            Assert.True(m.Chain.Count <= 16 && m.Chain.All(p => p > 0));
            Assert.Equal(chain.Where(p => p > 0).Take(16), m.Chain);
            foreach (var s in new[] { m.Event, m.Kind, m.Folder })
            {
                Assert.DoesNotContain(s, ch => char.IsControl(ch) || ch is '\u2028' or '\u2029' or '\u202E');
                for (var k = 0; k < s.Length; k++)
                    if (char.IsSurrogate(s[k]))
                    {
                        Assert.True(char.IsHighSurrogate(s[k]) && k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]), "lone surrogate survived");
                        k++;
                    }
            }
        }
    }

    [Fact]
    public void Holds_The_Worst_Case_Of_Escaping_Still_Fits_In_4096()
    {
        // every char escaped to six bytes: quotes and angle brackets, supplementary characters (two escapes each)
        foreach (var ch in new[] { "\"", "<", "\ud83d\ude00", "\u4e2d", "\\" })
        {
            var folder = string.Concat(Enumerable.Repeat(ch, 400));
            var bytes = AgentWire.Encode(string.Concat(Enumerable.Repeat(ch, 40)), string.Concat(Enumerable.Repeat(ch, 80)), folder,
                Enumerable.Repeat(2_000_000_000, 30).ToList());
            output.WriteLine($"'{ch}': {bytes.Length} bytes");
            Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes);
            Assert.NotNull(AgentWire.TryParse(bytes.AsSpan(0, bytes.Length - 1)));
        }
    }

    [Fact]
    public void Holds_Null_And_Empty_Inputs_Encode_And_Decode()
    {
        var bytes = AgentWire.Encode(null, null, null, []);
        var m = AgentWire.TryParse(bytes.AsSpan(0, bytes.Length - 1));
        Assert.NotNull(m);
        Assert.Equal("", m.Event);
        Assert.Empty(m.Chain);
    }

    public static TheoryData<string> HostileMessages() =>
    [
        "", " ", "{}", "[]", "null", "0", "\"v\"", "{",
        "{\"v\":1}", "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\"}", // chain missing
        "{\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}", // version missing
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1],\"x\":1}", // too many
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1],\"c\":[1]}", // duplicate
        "{\"v\":1,\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"e\":\"Stop\",\"f\":\"x\",\"c\":[1]}", // e twice, k missing: seen is 5
        "{\"v\":1,\"e\":\"" + "S1234567890123456789012345678901234567890" + "\",\"e\":\"Stop\",\"f\":\"x\",\"c\":[1]}", // first e too long, second fine, k missing
        "{'v':1,'e':'Stop','k':'','f':'x','c':[1]}", // single quotes
        "{v:1,e:\"Stop\",k:\"\",f:\"x\",c:[1]}",
        "{\"v\":1;\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}", // wrong separator
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1],}", // trailing comma
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]} // c", // comment
        "{\"v\":1,\"e\":\"Stop\",\"k\":null,\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1,\"e\":5,\"k\":\"\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":[\"x\"],\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":{}}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":null}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[2147483648]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1e2]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1.0]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[-1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[0]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[true]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1,null]}",
        "{\"v\":1.0,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1.5,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":true,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"\\ud800\",\"c\":[1]}", // lone surrogate escape
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"\\udc00x\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[[1]]}",
        "\ufeff{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}", // BOM
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}x",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[1]}{\"v\":1}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"" + new string('a', 261) + "\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"" + new string('a', 65) + "\",\"f\":\"x\",\"c\":[1]}",
        "{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":" + new string('[', 50) + new string(']', 50) + "}",
    ];

    [Theory]
    [MemberData(nameof(HostileMessages))]
    public void Holds_Hostile_Decode_Gives_Null_And_Never_Throws(string text)
    {
        Assert.Null(AgentWire.TryParse(text));
        Assert.Null(AgentWire.TryParse(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Holds_Text_Over_The_Limit_Or_Bytes_That_Are_Not_Utf8_Give_Null()
    {
        Assert.Null(AgentWire.TryParse(new string(' ', 4097)));
        Assert.Null(AgentWire.TryParse(new byte[5000]));
        Assert.Null(AgentWire.TryParse([0xFF, 0xFE, 0x7B]));
        Assert.Null(AgentWire.TryParse(default(ReadOnlySpan<byte>)));
        Assert.Null(AgentWire.TryParse((string?)null));
    }

    [Fact]
    public void Holds_Order_Of_The_Properties_Does_Not_Matter_And_Unicode_Escapes_In_Names_Are_Equal_To_Plain_Names()
    {
        var m = AgentWire.TryParse("{\"c\":[3],\"f\":\"a/b\",\"\\u006b\":\"\",\"e\":\"Stop\",\"v\":1}");
        Assert.NotNull(m);
        Assert.Equal([3], m.Chain);
    }

    [Fact]
    public void Holds_Duplicate_And_Wrong_Pids_In_The_Chain_Within_Limits_Are_Carried_As_Given()
    {
        var m = AgentWire.TryParse("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"x\",\"c\":[7,7,7,1,2147483647]}");
        Assert.NotNull(m);
        Assert.Equal([7, 7, 7, 1, int.MaxValue], m.Chain);
        var n = AgentNotice.From(m);
        Assert.Equal("p7", n!.SessionKey);
    }

    // ---- AgentNotice ----------------------------------------------------------------------------------------

    [Fact]
    public void Holds_A_Decoded_But_Uncleaned_Folder_Becomes_A_Clean_Name_In_The_Notice()
    {
        // the decoder keeps what the sender wrote (control characters, bidi); the notice is where it is cleaned
        var m = AgentWire.TryParse("{\"v\":1,\"e\":\"Stop\",\"k\":\"\",\"f\":\"C:\\\\a\\\\is\\u0000\\u202Eland\\n\\u2028\",\"c\":[1]}");
        Assert.NotNull(m);
        var n = AgentNotice.From(m);
        Assert.NotNull(n);
        Assert.Equal("island", n.ProjectName);
    }

    [Theory]
    [InlineData("Stop", "", true)]
    [InlineData("Stop", "anything", true)]
    [InlineData("Notification", "permission_prompt", true)]
    [InlineData("Notification", "idle_prompt", false)]
    [InlineData("Notification", "", false)]
    [InlineData("Notification", "permission_prompt ", false)]
    [InlineData("stop", "", false)]
    [InlineData("STOP", "", false)]
    [InlineData("SubagentStop", "", false)]
    [InlineData("", "", false)]
    [InlineData("Stop\0", "", false)]
    public void Holds_Which_Events_Mean_Something(string ev, string kind, bool expected)
    {
        var n = AgentNotice.From(new AgentMessage(ev, kind, "x/island", [1]));
        Assert.Equal(expected, n is not null);
    }

    [Fact]
    public void Holds_Session_Key_Falls_Back_To_The_Cleaned_Folder_With_No_Chain()
    {
        var n = AgentNotice.From(new AgentMessage("Stop", "", "C:\\a\\is\u0000land", []));
        Assert.Equal("fC:\\a\\island", n!.SessionKey);
    }

    // ---- ProjectName ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\work\island", "island")]
    [InlineData(@"C:\work\island\", "island")]
    [InlineData(@"C:\work\island\\\\", "island")]
    [InlineData("C:/work/island/", "island")]
    [InlineData(@"C:/work\island/", "island")]
    [InlineData(@"\\server\share", "share")]
    [InlineData(@"\\server\share\", "share")]
    [InlineData(@"\\?\C:\a\b", "b")]
    [InlineData("island", "island")]
    [InlineData("  island  ", "island")]
    [InlineData(@"C:\a\ island \", "island")]
    [InlineData(@"C:\a\CON", "CON")]
    [InlineData(@"C:\a\..", "..")]
    [InlineData(@"C:\a\.", ".")]
    [InlineData(@"C:\a\...", "...")]
    [InlineData(@"C:\", "C:")]
    [InlineData("C:", "C:")]
    [InlineData(@"\\\", "")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData("      ", "")]
    [InlineData("\t\r\n", "")]
    [InlineData(@"C:\a\is" + "\n" + "land", "island")]
    [InlineData("C:\\a\\\u202Eisland", "island")]
    [InlineData("C:\\a\\\u202E\u2066", "a")]
    public void Holds_The_Name_Is_The_Last_Non_Empty_Part(string folder, string expected)
    {
        Assert.Equal(expected, ProjectName.From(folder));
    }

    [Fact]
    public void Holds_Null_Gives_Empty()
    {
        Assert.Equal("", ProjectName.From(null));
    }

    [Fact]
    public void Holds_A_One_Megabyte_Folder_Is_Cut_Quickly_To_Forty()
    {
        var folder = @"C:\a\" + new string('x', 1_000_000);
        var clock = Stopwatch.StartNew();
        var name = ProjectName.From(folder);
        output.WriteLine($"1 MB folder: {clock.ElapsedMilliseconds} ms");
        Assert.Equal(40, name.Length);
        Assert.True(clock.ElapsedMilliseconds < 500);
        var many = string.Concat(Enumerable.Repeat(@"a\", 500_000));
        Assert.Equal("a", ProjectName.From(many));
        Assert.Equal("", ProjectName.From(new string('\\', 1_000_000)));
    }

    [Fact]
    public void Holds_The_Cut_Never_Splits_A_Surrogate_Pair_And_Never_Ends_In_A_Blank()
    {
        for (var pad = 36; pad <= 41; pad++)
        {
            var name = ProjectName.From(@"C:\a\" + new string('x', pad) + "\ud83d\ude00\ud83d\ude00 tail");
            Assert.True(name.Length <= 40);
            if (name.Length > 0 && char.IsHighSurrogate(name[^1])) Assert.Fail("pair split at pad " + pad);
            Assert.Equal(name.TrimEnd(), name);
        }

        Assert.Equal(new string('a', 39), ProjectName.From(new string('a', 39) + " " + new string('b', 20)));
    }

    [Fact]
    public void Holds_Lone_Surrogates_In_The_Name_Are_Removed()
    {
        Assert.Equal("island", ProjectName.From("is\ud800land"));
        Assert.Equal("island", ProjectName.From("is\udc00land"));
        Assert.Equal("is\ud83d\ude00land", ProjectName.From("is\ud83d\ude00land"));
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200B\u200B\u200B")]
    [InlineData("\u2060")]
    [InlineData("\uFEFF")]
    [InlineData("\u00AD")]
    [InlineData("\u3164")]
    [InlineData("\u2800")]
    [InlineData("\u115F")]
    [InlineData("\u180E")]
    [InlineData("\u200D")]
    public void Defect_A_Folder_Named_Only_With_Invisible_Characters_Draws_A_Blank_Name_Instead_Of_The_Fallback(string invisible)
    {
        var notice = AgentNotice.From(new AgentMessage("Stop", "", @"C:\work\" + invisible, [1]));
        Assert.NotNull(notice);
        // The name must be something a person can see: the fallback "Agent" when nothing visible is left.
        Assert.Equal(ProjectName.Unknown, notice.ProjectName);
    }
}
