namespace Island.Core.SettingsEdit;

/// <summary>One glass the app has, with the plain words the settings screen shows for it.</summary>
public sealed record GlassOption(GlassKind Kind, string Name, string Description);

/// <summary>The glasses the app has. Blur is listed only while the caller says it is available.</summary>
public static class GlassChoice
{
    public static IReadOnlyList<GlassOption> All { get; } =
    [
        new(GlassKind.Approved, "Approved", "The standard glass: dark and clear."),
        new(GlassKind.Darker, "Darker", "The same glass with a deeper tint, easier to read on a bright desktop."),
        new(GlassKind.Blur, "Blur", "The desktop behind the island is blurred, like frosted glass."),
    ];

    /// <summary>The glasses that can be chosen now.</summary>
    public static IReadOnlyList<GlassOption> Offered(bool blurAvailable) =>
        [.. All.Where(o => o.Kind != GlassKind.Blur || blurAvailable)];

    public static bool IsOffered(GlassKind kind, bool blurAvailable) =>
        kind != GlassKind.Blur || blurAvailable;

    /// <summary>The glass to draw with: the chosen one, or Approved when Blur was chosen but is not available now. The saved choice is not changed.</summary>
    public static GlassKind Effective(GlassKind chosen, bool blurAvailable) =>
        IsOffered(chosen, blurAvailable) ? chosen : GlassKind.Approved;
}
