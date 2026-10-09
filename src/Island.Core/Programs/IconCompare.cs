namespace Island.Core;

/// <summary>Compares two icons of possibly different sizes, to recognise the generic icon Windows gives a file that has none of its own.</summary>
public static class IconCompare
{
    /// <summary>Chosen by B1: the mean difference per colour channel (0..255) below which two icons count as the same picture.</summary>
    public const double SameThreshold = 12.0;

    /// <summary>
    /// The mean absolute difference per channel, after the second picture is sampled to the size of the first
    /// (nearest pixel). Large when the pictures differ; 0 when they are identical.
    /// </summary>
    public static double MeanDifference(IconImage a, IconImage b)
    {
        if (!IconChoice.IsUsable(a) || !IconChoice.IsUsable(b)) return double.MaxValue;

        long total = 0;
        for (var y = 0; y < a.Height; y++)
        {
            var by = Math.Min(b.Height - 1, (int)((y + 0.5) * b.Height / a.Height));
            for (var x = 0; x < a.Width; x++)
            {
                var bx = Math.Min(b.Width - 1, (int)((x + 0.5) * b.Width / a.Width));
                var ai = (y * a.Width + x) * 4;
                var bi = (by * b.Width + bx) * 4;
                for (var c = 0; c < 4; c++) total += Math.Abs(a.Bgra[ai + c] - b.Bgra[bi + c]);
            }
        }

        return total / (double)(a.Width * a.Height * 4);
    }

    public static bool AreAlike(IconImage a, IconImage b) => MeanDifference(a, b) < SameThreshold;
}
