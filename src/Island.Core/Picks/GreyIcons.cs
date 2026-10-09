using System.Runtime.CompilerServices;

namespace Island.Core;

/// <summary>
/// WORK-ORDER-5 §2: the grey copy of an icon for a closed pick — no colour, the same transparency, a little darker
/// (<see cref="ChoiceConstants.ClosedBrightness"/>). It works on raw pixel values; the copy of an icon is made once and
/// remembered, so the drawing thread never has to make it (the icon cache makes it when the icon arrives).
/// </summary>
public static class GreyIcons
{
    private static readonly ConditionalWeakTable<IconImage, IconImage> Made = new();

    public static IconImage Of(IconImage icon) => Made.GetValue(icon, Make);

    /// <summary>A new picture with every pixel's red, green and blue equal; alpha untouched. The pixels are straight (not premultiplied), as Windows gives them.</summary>
    public static IconImage Make(IconImage icon)
    {
        var pixels = (byte[])icon.Bgra.Clone();
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var luma = 0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2];
            pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)Math.Clamp(Math.Round(luma * ChoiceConstants.ClosedBrightness), 0, 255);
        }

        return new IconImage(icon.Width, icon.Height, pixels);
    }
}
