namespace Island.Core.Terminals;

/// <summary>
/// The Terminals page's own memory (WORK-ORDER-11 sections 1 and 2): each reading in, the tiles out in the order each window was first
/// seen (oldest on the left, a new one on the right, a tile never changes place, a window that is gone is forgotten). Keeps nothing but handles
/// and numbers. Not thread-safe: one reader calls it.
/// </summary>
public sealed class TerminalPage
{
    private readonly TerminalTables tables;
    private readonly FirstSeen<long> windowOrder = new();
    private readonly FirstSeen<long> helperOrder = new();
    private IReadOnlyList<TerminalTile> last = [];

    public TerminalPage(TerminalTables tables)
    {
        this.tables = tables ?? TerminalTables.Empty;
    }

    /// <summary>The tiles of the latest reading, in page order.</summary>
    public IReadOnlyList<TerminalTile> Tiles => last;

    /// <summary>Takes one reading and returns the tiles in page order. Never throws: on a reading that cannot be worked out the last tiles stay.</summary>
    public IReadOnlyList<TerminalTile> Read(TerminalReading? reading)
    {
        try
        {
            var tiles = TerminalTiles.Build(reading, tables, helperOrder);
            var byHandle = tiles.ToDictionary(t => t.WindowHandle);
            last = [.. windowOrder.Order(tiles.Select(t => t.WindowHandle)).Select(h => byHandle[h])];
        }
        catch (Exception)
        {
            // Odd input from outside must never reach the drawing; the tiles of the reading before stay. (Claude)
        }

        return last;
    }
}
