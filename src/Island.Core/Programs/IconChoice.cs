namespace Island.Core;

/// <summary>
/// What to draw for one item: a real picture, or the two-letter tile. <see cref="Undersized"/> is set when
/// the best picture is smaller than the space it is drawn in: it is then used as it is, never stretched, and
/// the caller flags the item (EVALS I5).
/// </summary>
public sealed record IconPick(IconImage? Image, bool UseLetters, bool Undersized)
{
    public static IconPick Letters { get; } = new(null, true, false);
}

/// <summary>The rules of EVALS I4, I5 and I9: which picture an item gets, if any.</summary>
public static class IconChoice
{
    /// <param name="available">Every picture the source could read, of any size; null entries are ignored.</param>
    /// <param name="drawnPixels">How many device pixels across the icon is drawn at the current display scaling.</param>
    /// <param name="seenOnThisComputer">
    /// False for a starter pick whose program was never found on disk or whose site was never open here (I9):
    /// it shows letters even if some picture is offered, so no borrowed logo ever appears.
    /// </param>
    public static IconPick Choose(IReadOnlyList<IconImage?>? available, int drawnPixels, bool seenOnThisComputer = true)
    {
        if (!seenOnThisComputer) return IconPick.Letters;

        var best = available?
            .Where(IsUsable)
            .OrderByDescending(i => i!.Width)
            .ThenByDescending(i => i!.Height)
            .FirstOrDefault();
        if (best is null) return IconPick.Letters;

        return new IconPick(best, UseLetters: false, Undersized: best.Width < drawnPixels || best.Height < drawnPixels);
    }

    /// <summary>A picture is usable when it has pixels, its byte count matches its size, and it is not blank (every pixel fully transparent).</summary>
    public static bool IsUsable(IconImage? image)
    {
        if (image is null || image.Width <= 0 || image.Height <= 0 || image.Bgra is null) return false;
        if (image.Bgra.Length != (long)image.Width * image.Height * 4) return false;
        for (var i = 3; i < image.Bgra.Length; i += 4)
            if (image.Bgra[i] != 0) return true;
        return false;
    }

    /// <summary>
    /// The tile text: the first letters of the first two words in capitals ("YouTube Music" gives "YM"), or
    /// the first two letters of a single word ("Spotify" gives "Sp"). "?" when the name has no letter or digit.
    /// </summary>
    public static string TwoLetterMark(string? name)
    {
        var words = (name ?? string.Empty)
            .Split([' ', '.', '-', '_', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsLetterOrDigit)]))
            .Where(w => w.Length > 0)
            .ToList();
        if (words.Count == 0) return "?";
        if (words.Count > 1) return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));

        var word = words[0];
        return word.Length == 1
            ? char.ToUpperInvariant(word[0]).ToString()
            : string.Concat(char.ToUpperInvariant(word[0]), char.ToLowerInvariant(word[1]));
    }
}
