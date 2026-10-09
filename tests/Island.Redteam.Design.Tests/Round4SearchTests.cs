using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>
/// ROUND 4: <c>SearchView.TailThatFits</c> after its second repair (design-3-4: the cut falls only where a character as a person sees it begins). The function is private to Island.App, which this project does not
/// reference: it is replayed here with the same probe (a TextBlock set up as <c>ContentsLayer.Label(14, Normal, White, 20)</c> sets it up: the island's font list, no wrap) and checked line for line against the source.
/// Nothing is shown; a TextBlock is measured on the one STA thread.
/// </summary>
public class Round4SearchTests(ITestOutputHelper output)
{
    private static string Tail(string text, double width)
    {
        if (text.Length == 0) return string.Empty;
        var probe = new TextBlock
        {
            FontFamily = new FontFamily($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}"),
            FontSize = 14,
            FontWeight = FontWeights.Normal,
            LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        var starts = StringInfo.ParseCombiningCharacters(text);
        string Candidate(int i) => (starts[i] > 0 ? "…" : string.Empty) + text[starts[i]..];
        bool Fits(string candidate)
        {
            probe.Text = candidate;
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width <= width;
        }

        int low = 0, high = starts.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Fits(Candidate(mid))) high = mid;
            else low = mid + 1;
        }

        return low < starts.Length ? Candidate(low) : "…";
    }

    private static double WidthOf(string text) => Sta.Run(() => TextWidth.Of(text, 14, FontWeights.Normal));

    /// <summary>The field's text room at the widest it gets (the field is 240 wide at most; the room is the field less 34).</summary>
    private static readonly double Room = SearchLayout.FieldWidth(1000) - 34;

    [Fact]
    public void The_Replay_Is_The_Sources_Loop()
    {
        var src = Src.Read("Island.App/Visuals/SearchView.cs");
        Assert.Contains("var starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);", src);
        // Repaired in WORK-ORDER-12 (design-4-2): the loop is a binary search over the cuts; the replay above is the same search.
        Assert.Contains("string Candidate(int i) => (starts[i] > 0 ? \"…\" : string.Empty) + text[starts[i]..];", src);
        Assert.Contains("return probe.DesiredSize.Width <= width;", src);
        Assert.Contains("return low < starts.Length ? Candidate(low) : \"…\";", src);
        Assert.Contains("_typed.Text = TailThatFits(data.Text, fieldWidth - 34);", src);
        Assert.Contains("public const int MaxFieldChars = 256;", Src.Read("Island.Core/Search/SearchKeys.cs"));
    }

    [Fact]
    public void An_Empty_Text_Is_An_Empty_Field_And_A_Text_Is_Never_An_Empty_Field()
    {
        Sta.Run(() =>
        {
            Assert.Equal(string.Empty, Tail(string.Empty, Room));
            foreach (var text in new[] { "a", "alpha beta", new string('W', 256), new string('中', 256), "e" + new string('́', 250), string.Concat(Enumerable.Repeat("\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466", 3)) })
            {
                var tail = Tail(text, Room);
                Assert.NotEqual(string.Empty, tail);
                Assert.True(tail == "…" || TextWidth.Of(tail, 14, FontWeights.Normal) <= Room + 1e-6, $"{text.Length} units: the tail is wider than the room");
            }
        });
    }

    [Fact]
    public void One_Element_Wider_Than_The_Field_Gives_The_Ellipsis_Alone_Never_An_Empty_Field()
    {
        Sta.Run(() =>
        {
            // twenty people joined by zero width joiners: one extended grapheme cluster for the text engine, and wide where a font does not ligate it
            var chain = string.Join("‍", Enumerable.Repeat("\U0001F468", 20));
            Assert.Single(StringInfo.ParseCombiningCharacters(chain));
            var width = TextWidth.Of(chain, 14, FontWeights.Normal);
            var tail = Tail(chain, Room);
            output.WriteLine($"one cluster of {chain.Length} units is {width:0.0} wide in a room of {Room:0}: the field shows \"{(tail == chain ? "the cluster" : tail)}\"");
            if (width > Room) Assert.Equal("…", tail); else Assert.Equal(chain, tail);
        });
    }

    /// <summary>A short text wider than the room because its letters are wide: the tail still ends with the last letter typed (the caret is after it) and starts after an ellipsis.</summary>
    [Fact]
    public void A_Tail_Always_Ends_With_The_Last_Character_Typed()
    {
        Sta.Run(() =>
        {
            foreach (var text in new[] { new string('W', 40) + "x", "café " + new string('m', 60) + "❤️", new string('中', 30) + "\U0001F44D\U0001F3FD" })
            {
                var tail = Tail(text, Room);
                if (tail == "…") continue;
                Assert.EndsWith(tail.TrimStart('…'), text);
            }
        });
    }

    /// <summary>How many times the loop asks the text engine to lay a text out, and how long one draw of the field takes, for a typed text of a given length (ordinary: a word or two; the most the field takes: 256 units).</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(256)]
    public void Record_The_Cost_Of_The_Loop_For_A_Typed_Text_Of_This_Length(int units)
    {
        var text = string.Concat(Enumerable.Repeat("alpha beta gamma ", 20))[..units];
        var (ms, measures) = Sta.Run(() =>
        {
            Tail(text, Room); // warm up
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 5; i++) Tail(text, Room);
            return (sw.Elapsed.TotalMilliseconds / 5, MeasuresOf(text));
        });
        output.WriteLine($"{units} units: {measures} layouts of the text, {ms:0.0} ms for one draw of the field (room {Room:0})");
        Assert.True(ms < 1000);
    }

    private static int MeasuresOf(string text)
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var probe = new TextBlock { FontFamily = new FontFamily($"{LookConstants.FontPrimary}, {LookConstants.FontFallback}"), FontSize = 14, TextWrapping = TextWrapping.NoWrap };
        var n = 0;
        int low = 0, high = starts.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            probe.Text = (starts[mid] > 0 ? "…" : string.Empty) + text[starts[mid]..];
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            n++;
            if (probe.DesiredSize.Width <= Room) high = mid;
            else low = mid + 1;
        }

        return n;
    }

    /// <summary>
    /// FINDING design-4-2 (LOW). Expected: finding the tail takes a handful of layouts, because the width of "the text from here on" only falls as the cut moves on (a binary search over the cuts: nine layouts for 256 units).
    /// The loop lays the whole text out and then one cut after another: about 230 layouts of up to 256 units each for a text of the most the field takes, about 0.1 s on the UI thread for every key typed beyond 120 characters
    /// (the loop is the old one in a new form: the repair of design-3-4 did not change its cost). Measured: 6 ms at 60 units, 27 ms at 120, 85 ms at 256 (more with wider letters); a text of an ordinary length is under a millisecond.
    /// </summary>
    [Fact]
    public void Defect_The_Tail_Of_A_Long_Typed_Text_Is_Found_In_A_Handful_Of_Layouts()
    {
        var text = string.Concat(Enumerable.Repeat("alpha beta gamma ", 20))[..256];
        var n = Sta.Run(() => MeasuresOf(text));
        Assert.True(n <= 16, $"{n} layouts of the text for one draw of a 256-unit query (a binary search over the cuts needs 9)");
    }
}
