namespace Island.Core;

/// <summary>The refusal WORK-ORDER-11 adds to the register, in the order's own words.</summary>
public static class TerminalsPageRefusals
{
    public static Refusal NoRoom { get; } = new(
        "TERMINALS_PAGE_NO_ROOM",
        "The Terminals page could not be added, because the island already has as many pages as it can hold.",
        "Nothing of yours was changed.",
        "Delete a page you do not use; the Terminals page appears the next time Island starts.");
}
