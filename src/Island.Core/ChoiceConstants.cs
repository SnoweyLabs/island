namespace Island.Core;

/// <summary>
/// The numbers of the looks Dan chose on 6 Oct 2026 (WORK-ORDER-5.md). Kept apart from <see cref="LookConstants"/>, which pins the
/// approved look; each is pinned by the tests of its own section. Those marked (Claude) were chosen by the author of the work
/// order to make a picture buildable and are listed under OWNER DECISIONS REQUIRED.
/// </summary>
public static class ChoiceConstants
{
    // ---- §2 A closed pick is grey (choice 2B) ----
    /// <summary>Picture: filter brightness(.95).</summary>
    public const double ClosedBrightness = 0.95;

    /// <summary>Picture: opacity .85.</summary>
    public const double ClosedOpacity = 0.85;

    /// <summary>(Claude) The cross-fade when a pick opens or closes.</summary>
    public const double ClosedFadeMs = 200;

    // ---- The drag's own movements (Dan's P22, WORK-ORDER-13: they were literals in DragView) ----
    /// <summary>(Claude) The drop zone fades in.</summary>
    public const double DropZoneFadeInMs = 150;

    /// <summary>The drop zone fades out in the time the capsule's own dismiss fade takes (it was 120, ten apart from 110: one time for the same kind of movement).</summary>
    public const double DropZoneFadeOutMs = LookConstants.DismissFadeMs;

    /// <summary>(Claude) The lifted tile comes back to its place by a back-ease of this length and this amplitude.</summary>
    public const double LiftedReturnMs = 260;

    public const double LiftedReturnAmplitude = 0.6;

    // ---- §3 Several windows show as dots (choice 3B) ----
    /// <summary>Picture: class n-dots, each dot 4 across.</summary>
    public const double DotSize = 4;

    /// <summary>Picture: 3 apart.</summary>
    public const double DotGap = 3;

    /// <summary>The centre of the dots is this far below the tile's lower edge.</summary>
    public const double DotCentreBelowTile = 7;

    /// <summary>The soft glow around each dot, in the page colour (CSS box-shadow blur 4).</summary>
    public const double DotGlow = 4;

    /// <summary>(Claude) Six windows or more show five dots; the text line already says the number.</summary>
    public const int MaxDots = 5;

    // ---- §4 A long page slides sideways (choice 6A) ----
    /// <summary>(Claude, from the picture) The most picks a page shows at once; the + tile comes after them.</summary>
    public const int MaxVisibleTiles = 7;

    /// <summary>(Claude) One full wheel notch moves the picks by this many tiles.</summary>
    public const double TilesPerNotch = 1;

    /// <summary>Picture: class fadeR, the sliding part fades out over its last 22% on a side where more picks are hidden.</summary>
    public const double StripFadeShare = 0.22;

    /// <summary>Picture: class arrow, the ‹ or › is 16 high text at 85%.</summary>
    public const double ArrowGlyphSize = 16;

    public const double ArrowAlpha = 0.85;

    /// <summary>(Claude) The area around an arrow that belongs to the arrow and not to the tile under it: 24 wide, 40 high.</summary>
    public const double ArrowAreaWidth = 24;

    public const double ArrowAreaHeight = 40;

    // ---- §5 The + opens a second row (choice 4B) ----
    /// <summary>Picture: class cap two, two rows of 74 and the border: the open capsule is 150 high (one row is 76).</summary>
    public const double TwoRowHeight = 150;

    /// <summary>Picture: .rowx height.</summary>
    public const double RowHeight = 74;

    /// <summary>Picture: the line between the rows is the top pixel of the second row, white at 14%.</summary>
    public const double RowLineAlpha = 0.14;

    /// <summary>Picture: the words "Open now" are 12 high, white at 85%.</summary>
    public const double RowLabelSize = 12;

    public const double RowLabelAlpha = 0.85;

    /// <summary>Picture: class badge-plus, a white disc 16 across ...</summary>
    public const double BadgeSize = 16;

    /// <summary>... reaching this far outside the tile's edge at its upper right.</summary>
    public const double BadgeReach = 5;

    /// <summary>(Claude) The second row's contents fade in over this long once the capsule starts growing, and out over <see cref="LookConstants.DismissFadeMs"/>.</summary>
    public const double RowFadeInMs = 200;
}
