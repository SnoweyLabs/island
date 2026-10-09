namespace Island.Core;

/// <summary>A program the island never appears over: a name to show and an executable file name to match. Never a path.</summary>
public sealed record NeverOverEntry(string Name, string ExeFileName);

/// <summary>
/// The "never over this" list (WORK-ORDER-7 section 1). A program on it counts as exclusive fullscreen whenever it
/// is in front and fullscreen. Stored like a pick: a name and an executable file name, so an entry that carries a
/// path, or has no file name, is refused. Immutable: adding and removing give a new list.
/// </summary>
public sealed class NeverOverList
{
    public static NeverOverList Empty { get; } = new([]);

    private readonly IReadOnlyList<NeverOverEntry> _entries;

    private NeverOverList(IReadOnlyList<NeverOverEntry> entries) => _entries = entries;

    public IReadOnlyList<NeverOverEntry> Entries => _entries;

    /// <summary>The list of the given entries; invalid ones and repeats of an executable are left out.</summary>
    public static NeverOverList From(IEnumerable<NeverOverEntry?> entries) =>
        entries.Aggregate(Empty, (list, e) => e is null ? list : list.With(e));

    public static bool IsValid(NeverOverEntry? entry) =>
        entry is not null
        && !string.IsNullOrWhiteSpace(entry.Name)
        && !string.IsNullOrWhiteSpace(entry.ExeFileName)
        && entry.ExeFileName == entry.ExeFileName.Trim()
        && entry.ExeFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) // what the front reader hands over; anything else could never match
        && entry.ExeFileName.IndexOfAny(['\\', '/', ':']) < 0
        && entry.ExeFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    public NeverOverList With(NeverOverEntry entry) =>
        !IsValid(entry) || Contains(entry.ExeFileName) ? this : new([.. _entries, entry]);

    public NeverOverList Without(string exeFileName) =>
        new([.. _entries.Where(e => !SameExe(e.ExeFileName, exeFileName))]);

    /// <summary>True when the executable file name is on the list. Case is ignored, as Windows ignores it; null or empty is never on it.</summary>
    public bool Contains(string? exeFileName) =>
        !string.IsNullOrWhiteSpace(exeFileName) && _entries.Any(e => SameExe(e.ExeFileName, exeFileName));

    /// <summary>
    /// The state the table should be asked about: a fullscreen program that is on the list counts as exclusive fullscreen.
    /// Nothing else changes (a presentation is not a program, and an unknown executable is never on the list).
    /// </summary>
    public FrontState Apply(FrontState state, string? frontExeFileName) =>
        state == FrontState.FullscreenProgram && Contains(frontExeFileName) ? FrontState.ExclusiveFullscreen : state;

    private static bool SameExe(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
