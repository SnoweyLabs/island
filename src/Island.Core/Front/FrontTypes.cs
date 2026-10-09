namespace Island.Core;

/// <summary>What is in front of the person, as far as the island can tell (WORK-ORDER-6 section 2).</summary>
public enum FrontState
{
    Clear,
    FullscreenProgram,
    ExclusiveFullscreen,
    Presentation,
}

/// <summary>What wants to appear (WORK-ORDER-7 section 1).</summary>
public enum Appearer
{
    Island,
    Pill,
    Notice,
}

/// <summary>"Asked" means the person pressed a key or clicked; "by itself" means the pill, the notice or the island's own start-up appearance.</summary>
public enum ShowOrigin
{
    Asked,
    ByItself,
}

/// <summary>One mode is always on.</summary>
public enum Mode
{
    Focus,
    Vibe,
    DND,
}

/// <summary>The answer of the table. <see cref="StayAway"/> is the zero value on purpose: an unset answer never shows anything.</summary>
public enum ShowAnswer
{
    StayAway,
    Show,
}
