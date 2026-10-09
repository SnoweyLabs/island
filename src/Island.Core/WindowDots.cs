namespace Island.Core;

/// <summary>WORK-ORDER-5 §3: how many dots show under a tile for the windows (or tabs) of a pick.</summary>
public static class WindowDots
{
    /// <summary>None for no window or one; two to five windows show that many; six or more show <see cref="ChoiceConstants.MaxDots"/>.</summary>
    public static int For(int windows) => windows < 2 ? 0 : Math.Min(windows, ChoiceConstants.MaxDots);
}
