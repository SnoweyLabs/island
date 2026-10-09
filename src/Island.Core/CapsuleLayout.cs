namespace Island.Core;

public readonly record struct CapsuleSize(double Width, double Height, double Radius);

public static class CapsuleLayout
{
    public static double Width(int itemCount, bool isMedia) =>
        LookConstants.WidthLeadingInset
        + LookConstants.WidthChip
        + LookConstants.WidthChipGap
        + (itemCount > 0 ? itemCount * LookConstants.WidthItemPitch - LookConstants.WidthItemTrailingGap : 0)
        + LookConstants.WidthItemsToTextGap
        + LookConstants.WidthTextBlock
        + LookConstants.WidthTextToControlsGap
        + (isMedia ? LookConstants.WidthMediaControls : LookConstants.WidthOtherControls)
        + LookConstants.WidthTrailingInset;

    public static CapsuleSize SizeFor(PageContents contents) =>
        new(Width(StripLayout.ShownTiles(contents.Items), contents.Page.IsMedia),
            LookConstants.CapsuleHeight,
            LookConstants.CapsuleCornerRadius);

    public static CapsuleSize Ball { get; } =
        new(LookConstants.BallSize, LookConstants.BallSize, LookConstants.BallSize / 2);
}
