namespace Island.Core;

/// <summary>
/// How the moving light on the island's edge is drawn. There is no setting for it any more (WORK-ORDER-13, Dan's answer Q1, 8 Oct 2026): the island picks the kind itself, the graphics-card
/// light where the compositor can be had and the fixed-rate light where it cannot (Dan's P25). <see cref="AsBefore"/> is what every picture of the self-test is drawn with.
/// </summary>
public enum LightKind
{
    /// <summary>The light is drawn and moved by the system's compositor; the app does no work per frame for it.</summary>
    GraphicsCard,

    /// <summary>The light as it was drawn before, its place changing at most <see cref="LightClock.FixedRateHz"/> times a second while nothing but the edge moves.</summary>
    FixedRate,

    /// <summary>The old drawing, on every frame. What every picture of the self-test runs on.</summary>
    AsBefore,
}
