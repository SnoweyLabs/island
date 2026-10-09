using System.Text;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Tests.Agents.Sessions;

public class WireTwoTests
{
    private const string Good = """{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":123456,"f":"Q:\\Invented\\Alpha","c":[100,50]}""";

    private static SessionMessage? Parse(string json) => SessionWire.TryParse(json);

    [Fact]
    public void Version_Two_Round_Trips()
    {
        var bytes = SessionWire.Encode("claude", "PermissionRequest", "Bash", "abc-123", 987_654_321_012, @"Q:\Invented\Alpha", [100, 50, 4]);

        var m = SessionWire.TryParse(bytes);

        Assert.NotNull(m);
        Assert.Equal("claude", m.Helper);
        Assert.Equal("PermissionRequest", m.Event);
        Assert.Equal("Bash", m.Kind);
        Assert.Equal("abc-123", m.SessionId);
        Assert.Equal(987_654_321_012, m.Time);
        Assert.Equal(@"Q:\Invented\Alpha", m.Folder);
        Assert.Equal([100, 50, 4], m.Chain);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes);
        Assert.Equal(m, SessionWire.TryParseAny(bytes) with { Chain = m.Chain }); // the same message through the combined parser
        Assert.Equal("""{"v":2,"a":"claude","e":"Stop","k":"","s":"","t":0,"f":"","c":[]}""" + "\n", Encoding.UTF8.GetString(SessionWire.Encode("claude", "Stop", "", "", -5, "", [])));
    }

    [Fact]
    public void Version_One_Is_Still_Taken_As_Claude_Code()
    {
        var one = AgentWire.Encode("Stop", "", @"Q:\Invented\Alpha", [100, 50]);

        var m = SessionWire.TryParseAny(one);

        Assert.NotNull(m);
        Assert.Equal(HelperNames.ClaudeCode, m.Helper);
        Assert.Equal("Stop", m.Event);
        Assert.Equal("", m.SessionId);
        Assert.Null(m.Time); // the island's own reading at arrival is used
        Assert.Equal([100, 50], m.Chain);
        Assert.Equal(@"Q:\Invented\Alpha", m.Folder);
        // The old parser is as it was: version 1 only, nothing of version 2.
        Assert.NotNull(AgentWire.TryParse(one));
        Assert.Null(AgentWire.TryParse(Good));
        Assert.Null(SessionWire.TryParse(one)); // the version-2 parser takes version 2 only
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("""{"v":1,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":0,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":3,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":"2","a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2.0,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[],"x":1}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[],"c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]} x""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}{}""")]
    [InlineData("""{"v":2,"a":null,"e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":1,"e":"Stop","k":"","s":"abc","t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":7,"t":1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":"1","f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":-1,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1.5,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":99999999999999999999,"f":"","c":[]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":"1"}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":["1"]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[0]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[-3]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[1.5]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[[1]]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":"","c":[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]}""")]
    [InlineData("""{"v":2,"a":"claude","e":"Stop","k":"","s":"abc","t":1,"f":{},"c":[]}""")]
    public void Any_Other_Shape_Is_Refused(string json)
    {
        Assert.Null(Parse(json));
        Assert.Null(SessionWire.TryParseAny(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("v")]
    [InlineData("a")]
    [InlineData("e")]
    [InlineData("k")]
    [InlineData("s")]
    [InlineData("t")]
    [InlineData("f")]
    [InlineData("c")]
    public void A_Message_Missing_Any_Property_Is_Refused(string missing)
    {
        var props = new Dictionary<string, string>
        {
            ["v"] = "2", ["a"] = "\"claude\"", ["e"] = "\"Stop\"", ["k"] = "\"\"", ["s"] = "\"abc\"", ["t"] = "1", ["f"] = "\"\"", ["c"] = "[]",
        };
        props.Remove(missing);
        var json = "{" + string.Join(",", props.Select(p => $"\"{p.Key}\":{p.Value}")) + "}";

        Assert.Null(Parse(json));
    }

    [Theory]
    [InlineData("a", 33)]
    [InlineData("e", 49)]
    [InlineData("k", 65)]
    [InlineData("s", 65)]
    [InlineData("f", 261)]
    public void A_Text_Over_Its_Limit_Is_Refused_And_At_The_Limit_Is_Taken(string field, int tooLong)
    {
        string With(int n)
        {
            var props = new Dictionary<string, string>
            {
                ["a"] = "claude", ["e"] = "Stop", ["k"] = "", ["s"] = "abc", ["f"] = "Q:",
            };
            props[field] = new string('x', n);
            return $$"""{"v":2,"a":"{{props["a"]}}","e":"{{props["e"]}}","k":"{{props["k"]}}","s":"{{props["s"]}}","t":1,"f":"{{props["f"]}}","c":[]}""";
        }

        Assert.NotNull(Parse(With(tooLong - 1)));
        Assert.Null(Parse(With(tooLong)));
    }

    [Fact]
    public void The_Encoder_Cuts_Every_Field_To_Its_Limit_And_Stays_In_The_Message_Size()
    {
        var huge = new string('\u00e9', 1_000_000); // a megabyte of two-byte characters, escaped to six bytes each on the wire
        var chain = Enumerable.Range(1, 1000).ToArray();

        var bytes = SessionWire.Encode(huge, huge, huge, huge, long.MaxValue, huge, chain);

        Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes, $"{bytes.Length} bytes");
        var m = SessionWire.TryParse(bytes);
        Assert.NotNull(m);
        Assert.Equal(SessionLimits.MaxHelperChars, m.Helper.Length);
        Assert.Equal(SessionLimits.MaxEventChars, m.Event.Length);
        Assert.Equal(SessionLimits.MaxKindChars, m.Kind.Length);
        Assert.Equal(SessionLimits.MaxSessionIdChars, m.SessionId.Length);
        Assert.Equal(SessionLimits.MaxFolderChars, m.Folder.Length);
        Assert.Equal(SessionLimits.MaxChain, m.Chain.Count);
        Assert.Equal(long.MaxValue, m.Time);
    }

    [Fact]
    public void The_Worst_Case_Of_Every_Field_Fits_Without_Losing_The_Folder()
    {
        var wide = new string('\u00e9', 500);

        var bytes = SessionWire.Encode(wide, wide, wide, wide, long.MaxValue, wide, Enumerable.Repeat(2_000_000_000, 40).ToArray());

        Assert.True(bytes.Length <= AgentPipe.MaxMessageBytes);
        Assert.Equal(SessionLimits.MaxFolderChars, SessionWire.TryParse(bytes)!.Folder.Length);
    }

    [Fact]
    public void A_Megabyte_Is_Refused_Without_Reading_It()
    {
        var big = new string(' ', 1_000_000) + Good;

        Assert.Null(Parse(big));
        Assert.Null(SessionWire.TryParse(Encoding.UTF8.GetBytes(big)));
        Assert.Null(SessionWire.TryParseAny(new byte[1_048_576]));
        Assert.Null(SessionWire.TryParse(Good + new string(' ', AgentPipe.MaxMessageBytes)));
    }

    [Fact]
    public void A_Session_Id_Made_Of_Path_Characters_Is_Plain_Text()
    {
        const string path = @"..\..\Windows\System32\config:evil/../x";

        var m = SessionWire.TryParse(SessionWire.Encode("claude", "Stop", "", path, 5, "", [1]));

        Assert.NotNull(m);
        Assert.Equal(path, m.SessionId); // kept as it came: it names nothing on disk, it is only compared
        var tracker = new SessionTracker(() => 100, HelperSignalTable.Default, ["claude.exe"]);
        Assert.Equal(ApplyOutcome.Applied, tracker.Apply(m).Outcome);
        Assert.Equal(path, tracker.Get(1)!.SessionId);
    }

    [Fact]
    public void A_Session_Id_Is_Cleaned_And_Cut_To_Sixty_Four_Characters()
    {
        var dirty = "  ab\u0000c\u202Ed\r\n" + new string('z', 100);

        var m = SessionWire.TryParse(SessionWire.Encode("claude", "Stop", "", dirty, 5, "", []));

        Assert.NotNull(m);
        Assert.Equal(64, m.SessionId.Length);
        Assert.StartsWith("abcdzzzz", m.SessionId);
        // A pair of surrogates is never cut in half.
        var pair = new string('x', 63) + "\U0001F600";
        Assert.Equal(63, SessionWire.TryParse(SessionWire.Encode("claude", "Stop", "", pair, 5, "", []))!.SessionId.Length);
    }

    [Fact]
    public void A_Control_Character_Escaped_In_Json_Is_Removed_On_The_Way_In()
    {
        var m = Parse("""{"v":2,"a":"cla\u0000ude","e":"St\nop","k":"","s":"a\u202Eb","t":1,"f":"","c":[]}""");

        Assert.NotNull(m);
        Assert.Equal("claude", m.Helper);
        Assert.Equal("Stop", m.Event);
        Assert.Equal("ab", m.SessionId);
    }

    [Fact]
    public void Random_Bytes_And_Mutations_Never_Throw()
    {
        var rng = new Random(20261007);
        var seed = Encoding.UTF8.GetBytes(Good);
        for (var i = 0; i < 2000; i++)
        {
            var bytes = i % 2 == 0 ? RandomBytes(rng, rng.Next(0, 300)) : (byte[])seed.Clone();
            if (i % 2 == 1) for (var n = rng.Next(1, 6); n > 0; n--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);

            var ex = Record.Exception(() =>
            {
                SessionWire.TryParse(bytes);
                SessionWire.TryParseAny(bytes);
            });

            Assert.Null(ex);
        }
    }

    [Fact]
    public void A_Version_Three_Message_Is_Refused_By_Both_Parsers()
    {
        var three = Good.Replace("\"v\":2", "\"v\":3");

        Assert.Null(SessionWire.TryParse(three));
        Assert.Null(SessionWire.TryParseAny(Encoding.UTF8.GetBytes(three)));
        Assert.Null(AgentWire.TryParse(three));
    }

    private static byte[] RandomBytes(Random rng, int n)
    {
        var b = new byte[n];
        rng.NextBytes(b);
        return b;
    }
}
