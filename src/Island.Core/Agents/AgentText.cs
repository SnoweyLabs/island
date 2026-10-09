using System.Globalization;
using System.Text;

namespace Island.Core;

/// <summary>Cleaning of text that arrives from outside before it is carried or drawn. Memory only.</summary>
internal static class AgentText
{
    /// <summary>
    /// Removes control characters, the characters that change the direction of text, line and paragraph
    /// separators, and lone surrogates (which cannot be encoded). Pairs of surrogates are kept.
    /// </summary>
    public static string Clean(string? text, bool dropFormat = true)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c) || char.IsControl(c) || IsBidi(c) || c is (char)0x2028 or (char)0x2029 || dropFormat && char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                // dropped
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>The first <paramref name="max"/> characters, never ending inside a surrogate pair.</summary>
    public static string Head(string text, int max)
    {
        if (text.Length <= max) return text;
        var n = max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..n];
    }

    /// <summary>The last <paramref name="max"/> characters, never starting inside a surrogate pair.</summary>
    public static string Tail(string text, int max)
    {
        if (text.Length <= max) return text;
        var start = text.Length - max;
        if (char.IsLowSurrogate(text[start])) start++;
        return text[start..];
    }

    private static bool IsBidi(char c) =>
        c is (char)0x061C or (char)0x200E or (char)0x200F || c is >= (char)0x202A and <= (char)0x202E || c is >= (char)0x2066 and <= (char)0x2069;
}
