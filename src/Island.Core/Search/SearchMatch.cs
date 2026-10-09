using System.Globalization;
using System.Text;

namespace Island.Core;

/// <summary>
/// One thing search can offer, as plain values. The caller maps its picks and its "open and not picked" things onto this.
/// </summary>
/// <param name="Name">What the tile is called; what the typed text is compared with.</param>
/// <param name="Key">Opaque to search: what the caller needs to find the thing again (a pick id, a window handle).</param>
/// <param name="IsPick">True for a pick (from any page), false for something that is merely open.</param>
/// <param name="IsClosed">True for a pick that is not open; search only carries it through, the tile is drawn grey.</param>
public sealed record SearchCandidate(string Name, string Key, bool IsPick, bool IsClosed);

/// <summary>
/// The matching and the order of search (WORK-ORDER-7.md section 3, EVALS F1). Pure: no screen, no clock, no log; the
/// typed text is never stored, only compared.
/// </summary>
public static class SearchMatch
{
    // Letters that Unicode does not decompose, so accent-stripping alone would miss them.
    private static readonly (string From, string To)[] Extras =
    [
        ("ß", "ss"), ("ø", "o"), ("ł", "l"), ("đ", "d"), ("æ", "ae"), ("œ", "oe"), ("ı", "i"),
    ];

    /// <summary>
    /// The matches, in the order search shows them: picks first, then open things that are not picks; inside each group a
    /// name that starts with the text before one that only contains it; otherwise the order the caller gave.
    /// Text that is empty or blank (or only combining marks, which fold to nothing) matches nothing: with no text there
    /// is nothing to look for, and the strip never has to cap a list of everything. The text is trimmed, so a space typed
    /// after a word does not hide the word's matches. Never throws, whatever the input.
    /// </summary>
    public static IReadOnlyList<SearchCandidate> Rank(IEnumerable<SearchCandidate> candidates, string? text)
    {
        var needle = Fold(text?.Trim());
        if (needle.Length == 0) return [];

        var scored = new List<(SearchCandidate Item, int Order, int Group)>();
        var order = 0;
        foreach (var item in candidates)
        {
            var name = Fold(item.Name);
            if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var starts = name.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
            scored.Add((item, order++, (item.IsPick ? 0 : 2) + (starts ? 0 : 1)));
        }

        return [.. scored.OrderBy(s => s.Group).ThenBy(s => s.Order).Select(s => s.Item)];
    }

    /// <summary>
    /// The text without case-sensitive detail: compatibility-decomposed (so ligatures and full-width letters become plain
    /// ones), accents and other combining marks removed, lower case. Lone surrogates, which Normalize refuses, are
    /// replaced first.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var clean = ReplaceLoneSurrogates(text);
        string decomposed;
        try
        {
            decomposed = clean.Normalize(NormalizationForm.FormKD);
        }
        catch (ArgumentException)
        {
            decomposed = clean; // cannot happen after the replacement; kept so odd input can never throw into the drawing thread
        }

        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }

        var folded = sb.ToString().ToLowerInvariant();
        foreach (var (from, to) in Extras) folded = folded.Replace(from, to, StringComparison.Ordinal);
        return folded;
    }

    /// <summary>Every high or low surrogate that has no partner becomes U+FFFD.</summary>
    internal static string ReplaceLoneSurrogates(string text)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var paired = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (paired)
            {
                sb?.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                sb ??= new StringBuilder(text.Length).Append(text, 0, i);
                sb.Append('�');
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb?.ToString() ?? text;
    }
}
