using System.Globalization;
using Xunit.Abstractions;

namespace Island.Redteam.Design.Tests;

/// <summary>ROUND 3: <c>SearchView.TailThatFits</c> after its repair (design-2-3): the cut now never starts at half a UTF-16 pair, and still starts inside a character that is more than one code point.</summary>
public class Round3SearchTests(ITestOutputHelper output)
{
    private static string Tail() => Between(Src.Read("Island.App/Visuals/SearchView.cs"), "private static string TailThatFits", "private void Place");

    private static string Between(string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(i >= 0, "not found: " + from);
        var j = text.IndexOf(to, i, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }

    /// <summary>The repair of design-2-3 holds: the one check is on the low surrogate and it steps on by one unit (so the loop still ends).</summary>
    [Fact]
    public void The_Repair_Skips_A_Cut_At_A_Low_Surrogate_And_Steps_On()
    {
        var t = Tail();
        // Repaired again in WORK-ORDER-12 (design-3-4; this test held the low-surrogate skip before): the cut falls only where a character as a person sees it begins.
        Assert.Contains("ParseCombiningCharacters(text)", t);
        Assert.Contains(": \"…\";", t); // when nothing fits (a binary search since WORK-ORDER-12: `return low < starts.Length ? Candidate(low) : "…";`)
    }

    /// <summary>Every cut the loop can try (it tries them one by one from the whole text until the width fits), by the repaired rule, that begins inside a character a person sees as one: a combining mark, a skin tone, a variation selector, a joiner, the second half of a flag.</summary>
    [Fact]
    public void Record_Cuts_Of_An_Invented_Query_That_Still_Begin_Inside_A_Character_The_Person_Sees_As_One()
    {
        var text = "caf\u0065\u0301 alpha \U0001F44D\U0001F3FD beta \u2764\uFE0F gamma \U0001F1E9\U0001F1EA delta";
        var starts = StringInfo.ParseCombiningCharacters(text).ToHashSet();
        var insideGrapheme = new List<int>();
        for (var take = 1; take < text.Length; take++)
        {
            var index = text.Length - take;
            if (char.IsLowSurrogate(text[index])) continue; // what the repaired loop skips
            if (!starts.Contains(index)) insideGrapheme.Add(take);
        }

        output.WriteLine($"of {text.Length} cuts, {insideGrapheme.Count} start inside a character that is one to the eye (take = {string.Join(", ", insideGrapheme)})");
        Assert.NotEmpty(insideGrapheme);
    }

    /// <summary>
    /// FINDING design-3-4 (LOW). Expected: the same rule as design-2-3 at the level of what a person sees as one character: a tail that starts at a combining mark, a skin tone, a variation selector or the second half of a flag
    /// is skipped like one that starts at half of a pair. The repaired loop skips low surrogates only.
    /// </summary>
    [Fact]
    public void Defect_The_End_Of_A_Long_Search_Text_Does_Not_Start_Inside_A_Character_Made_Of_Several_Code_Points()
    {
        var t = Tail();
        Assert.True(t.Contains("StringInfo") || t.Contains("EnumerateRunes") && t.Contains("Grapheme") || t.Contains("GetTextElementEnumerator"), "TailThatFits does not cut at the start of a character as a person sees it");
    }
}
