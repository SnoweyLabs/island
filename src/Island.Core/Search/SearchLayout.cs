namespace Island.Core;

/// <summary>
/// The shape of the capsule while search is open (WORK-ORDER-7 section 3, choice 9A): in place of the page's chip and picks, a text field 40 high
/// (corner radius 20, at least 120 wide, text 14), then the matches as tiles, then the text block. The width follows the text and the matches
/// through the width spring.
/// </summary>
public static class SearchLayout
{
    public const double FieldHeight = 40;
    public const double FieldRadius = 20;
    public const double FieldMinWidth = 120;
    public const double FieldMaxWidth = 240;

    /// <summary>The room one typed character is given (text 14), plus the field's own padding on both sides.</summary>
    private const double CharWidth = 8;
    private const double FieldPadding = 28;

    /// <summary>The width of the field for this many characters of text: 120 at least, 240 at most.</summary>
    public static double FieldWidth(int characters) =>
        Math.Clamp(FieldPadding + Math.Max(0, characters) * CharWidth, FieldMinWidth, FieldMaxWidth);

    /// <summary>How many matches show at once; more slide (the capsule never gets wider than for this many).</summary>
    public static int ShownTiles(int tileCount) => Math.Clamp(tileCount, 0, ChoiceConstants.MaxVisibleTiles);

    /// <summary>The capsule's width: the field, the tiles that show, and the text block, with the same insets and gaps as the page's capsule.</summary>
    public static double Width(int characters, int tileCount)
    {
        var shown = ShownTiles(tileCount);
        return LookConstants.WidthLeadingInset
            + FieldWidth(characters)
            + LookConstants.WidthChipGap
            + (shown > 0 ? shown * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap : 0)
            + LookConstants.WidthItemsToTextGap
            + LookConstants.WidthTextBlock
            + LookConstants.WidthTrailingInset;
    }
}
