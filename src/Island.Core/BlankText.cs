using System.Globalization;
using System.Text;

namespace Island.Core;

/// <summary>
/// Whether text draws as something. Text that arrives from outside, or that a person types, can be made only of characters that draw as nothing (a
/// zero-width space, a lone accent, a braille blank, a Hangul filler): such text is treated like white space where a visible name or a first
/// character is wanted.
/// </summary>
public static class BlankText
{
    /// <summary>True when the character draws as nothing on its own: white space, a format character, a combining mark, or one of the blank letters and symbols.</summary>
    public static bool IsBlankLooking(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune)) return true;
        if (rune.Value is 0x2800 or 0x3164 or 0x1160 or 0x115F or 0xFFA0 or 0x180E or 0x034F) return true;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Control;
    }

    /// <summary>
    /// True when the text holds a character that could make it say something else or nothing at all, or that is not kept as it is typed: a control, private-use or unassigned character, a
    /// right-to-left override or other format character, or a lone surrogate (JSON cannot hold one: it is saved as U+FFFD and comes back as another character).
    /// Let through, because they draw: a zero-width joiner or non-joiner between two characters that draw (an emoji sequence, a Persian or Hindi word), and a character of the emoji planes
    /// (U+1F000 to U+1FFFF) that the runtime's table does not know yet. A real U+FFFD is a visible character and is kept as it is.
    /// </summary>
    public static bool HasHiddenCharacters(string text)
    {
        if (HasBrokenCharacter(text) || text.Contains('\uFFFD')) return true; // a name that already shows the replacement character is not kept either
        var runes = text.EnumerateRunes().ToArray();
        for (var i = 0; i < runes.Length; i++)
            if (IsHiddenAt(runes, i)) return true;
        return false;
    }

    /// <summary>The text without what is hidden (controls, right-to-left overrides, private-use and unassigned characters, a joiner that joins nothing), by the same rule: a joiner between two characters that draw stays.</summary>
    public static string WithoutHidden(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        var kept = new System.Text.StringBuilder();
        for (var i = 0; i < runes.Length; i++)
            if (!IsHiddenAt(runes, i) && runes[i].Value != 0xFFFD) kept.Append(runes[i].ToString());
        return kept.ToString();
    }

    private static bool IsHiddenAt(Rune[] runes, int i)
    {
        var rune = runes[i];
        var category = Rune.GetUnicodeCategory(rune);
        if (rune.Value is 0x200C or 0x200D)
        {
            if (i > 0 && i < runes.Length - 1 && Joinable(runes[i - 1]) && Joinable(runes[i + 1])) return false;
            if (i > 0 && i == runes.Length - 1 && Rune.GetUnicodeCategory(runes[i - 1]) == UnicodeCategory.NonSpacingMark && runes.Length > 1 && Joinable(runes[0])) return false; // a chillu: the joiner ends the word after a virama
            return true;
        }

        if (rune.Value is >= 0xE0020 and <= 0xE007F && IsInFlagTags(runes, i)) return false;
        if (category == UnicodeCategory.OtherNotAssigned && rune.Value is >= 0x1F000 and <= 0x1FFFF && (rune.Value & 0xFFFE) != 0xFFFE) return false; // an emoji newer than the runtime's table (not a noncharacter)
        return category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
    }

    private static readonly string[] FlagTags = ["gbeng", "gbsct", "gbwls"];

    /// <summary>True when the tag character at <paramref name="i"/> is part of the tags of a flag of England, Scotland or Wales: the black flag, the letters of the region, the cancel tag. Tags that spell anything else draw nothing and are hidden text.</summary>
    private static bool IsInFlagTags(Rune[] runes, int i)
    {
        static bool IsTag(Rune r) => r.Value is >= 0xE0020 and <= 0xE007F;
        var start = i;
        while (start > 0 && IsTag(runes[start - 1])) start--;
        if (start == 0 || runes[start - 1].Value != 0x1F3F4) return false;
        var end = i;
        while (end < runes.Length - 1 && IsTag(runes[end + 1])) end++;
        if (runes[end].Value != 0xE007F) return false;
        var letters = string.Concat(runes[start..end].Select(r => (char)(r.Value - 0xE0000)));
        return FlagTags.Contains(letters);
    }

    /// <summary>A character a joiner may sit next to: it is not white space or a control, so it is a letter, a sign, an emoji, a virama or a variation selector.</summary>
    private static bool Joinable(Rune rune) => !Rune.IsWhiteSpace(rune) && Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse);

    /// <summary>How many characters a name has for its limit: what a person sees as one (a flag, a thumb with its skin tone, a family), not the code points or units it is made of.</summary>
    public static int CountCharacters(string text) => new System.Globalization.StringInfo(text).LengthInTextElements;

    /// <summary>A name as it is compared: composed, and without the joiners that draw nothing between two letters (two names that look and read alike are one name).</summary>
    public static string SameName(string name) => string.Concat(name.Normalize(NormalizationForm.FormC).EnumerateRunes().Where(r => !IsInvisibleMark(r)).Select(r => r.ToString()));

    /// <summary>A character that adds nothing a person can see: the joiners, the combining grapheme joiner, the variation selectors (the picture selector of a heart, the Mongolian ones).</summary>
    private static bool IsInvisibleMark(Rune r) => r.Value is 0x200C or 0x200D or 0x034F or 0x2800 or 0x3164 or 0x1160 or 0x115F or 0xFFA0 or (>= 0x180B and <= 0x180F) or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>True when the text holds a lone surrogate: half of a character, which is saved as another one.</summary>
    public static bool HasBrokenCharacter(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(text[i])) return true;
        }

        return false;
    }

    /// <summary>True when at least one character of the text draws as something.</summary>
    public static bool HasVisible(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsBlankLooking(rune)) return true;
        }

        return false;
    }
}
