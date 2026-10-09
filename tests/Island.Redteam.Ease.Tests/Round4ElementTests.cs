using System.Globalization;
using Island.Core;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: what the runtime itself counts as one thing to look at. Held for the repair of design-3-4 (the search text is cut only where a text element starts) and the premise of ease-4-2 (the same count
/// is the one a limit on a name would use, and it is exactly the number of things a person sees).
/// </summary>
public class Round4ElementTests
{
    public static IEnumerable<object[]> Things()
    {
        yield return ["\U0001F1F7\U0001F1F4", 1, 2]; // a flag
        yield return ["\U0001F44D\U0001F3FD", 1, 2]; // a thumb with a skin tone
        yield return ["\U0001F468‍\U0001F469‍\U0001F467", 1, 5]; // a family joined by joiners
        yield return ["é", 1, 2]; // a letter with a combining accent
        yield return ["❤️", 1, 2]; // a heart with the picture selector
        yield return ["\U0001F600", 1, 1]; // a plain emoji
    }

    [Theory]
    [MemberData(nameof(Things))]
    public void One_Thing_To_Look_At_Is_One_Text_Element_And_Several_Code_Points(string thing, int elements, int runes)
    {
        Assert.Equal(elements, new StringInfo(thing).LengthInTextElements);
        Assert.Equal(elements, BlankText.CountCharacters(thing)); // repaired in WORK-ORDER-12 (ease-4-2): the limit counts what a person sees as one; this held the number of code points before
        Assert.Equal([0], StringInfo.ParseCombiningCharacters(thing));
    }

    /// <summary>Held (design-3-4): in a text of many things the starts the search field may cut at are the starts of things, so no cut falls inside a flag, a skin tone, a family or an accent.</summary>
    [Fact]
    public void The_Starts_A_Text_May_Be_Cut_At_Are_The_Starts_Of_Things()
    {
        var text = "ab\U0001F1F7\U0001F1F4é\U0001F44D\U0001F3FD\U0001F468‍\U0001F469‍\U0001F467z";
        var starts = StringInfo.ParseCombiningCharacters(text);
        Assert.Equal(7, starts.Length);
        foreach (var start in starts) Assert.False(start > 0 && (char.IsLowSurrogate(text[start]) || text[start] == '́' || text[start] == '‍'));
    }
}
