namespace Island.Core.Terminals;

/// <summary>
/// Keeps the order things were first seen in, across readings (WORK-ORDER-11 section 1): a new key goes on the right, a key never changes
/// place, keys first seen in the same reading take the order that reading gives them, and a key that is missing from a reading is forgotten
/// (if it comes back it is new). Not thread-safe: one reader calls it.
/// </summary>
public sealed class FirstSeen<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, long> seen = [];
    private long next;

    /// <summary>How many keys are remembered.</summary>
    public int Count => seen.Count;

    /// <summary>The distinct keys of this reading (a repeated key counts once, at its first place), in the order they were first seen.</summary>
    public IReadOnlyList<TKey> Order(IEnumerable<TKey>? keys)
    {
        var current = new List<TKey>();
        var present = new HashSet<TKey>();
        foreach (var key in keys ?? [])
        {
            if (key is null || !present.Add(key)) continue;
            current.Add(key);
            if (!seen.ContainsKey(key)) seen[key] = next++;
        }

        foreach (var gone in seen.Keys.Where(k => !present.Contains(k)).ToList()) seen.Remove(gone);
        return [.. current.OrderBy(k => seen[k])];
    }
}
