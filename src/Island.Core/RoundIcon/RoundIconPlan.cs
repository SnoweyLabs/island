namespace Island.Core;

/// <summary>What a pick's round tile shows (WORK-ORDER-10 §1).</summary>
public enum RoundIconKind
{
    /// <summary>No opaque pixel, or nothing readable: the two-letter tile.</summary>
    Letters,

    /// <summary>The opaque pixels fill a square: drawn as it is on a disc of its main colour.</summary>
    Plate,

    /// <summary>One colour, not a plate: drawn white on a disc of that colour.</summary>
    FlatShape,

    /// <summary>One nearly white colour, not a plate: drawn as it is on the dark neutral disc.</summary>
    NearlyWhiteFlatShape,

    /// <summary>Several colours, not a plate: drawn as it is on a disc moved until the icon stays visible.</summary>
    Other,
}

/// <summary>An 8-bit sRGB colour. (<see cref="Rgb"/> is the double-valued colour of the look; a disc is a whole-number colour.)</summary>
public readonly record struct RoundIconColour(byte R, byte G, byte B)
{
    public Rgb ToRgb() => new(R, G, B);
}

/// <summary>The disc colour and how to draw the icon on it. <see cref="DrawnWhite"/>: every pixel white, its own alpha kept.</summary>
public readonly record struct RoundIconPlan(RoundIconKind Kind, RoundIconColour Disc, bool DrawnWhite)
{
    /// <summary>The two-letter tile; the disc colour means nothing here.</summary>
    public static RoundIconPlan Letters { get; } = new(RoundIconKind.Letters, RoundIconConstants.DarkNeutral, false);
}
