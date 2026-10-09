using Island.Core;

namespace Island.App;

/// <summary>
/// The picks as the app holds them while it runs: the store, changed by adding, removing and moving, saved to the
/// picks file after every change (unless the file could not be read at start: then it is left exactly as it was).
/// Names and ids only; nothing of what is open is ever saved.
/// </summary>
internal sealed class PickBook(PickStore initial, string? path, bool canSave)
{
    public PickStore Store { get; private set; } = initial;

    /// <summary>Raised on the calling thread after a change, once it is saved.</summary>
    public event Action? Changed;

    /// <summary>Raised when a change could not be written to the picks file (the change stays in force until the app closes).</summary>
    public event Action? SaveFailed;

    public bool Add(Pick pick)
    {
        var next = Store.Add(pick, out var added);
        if (added) Apply(next);
        return added;
    }

    public void Remove(string id) => Apply(Store.Remove(id));

    public void Move(string id, string pageId) => Apply(Store.Move(id, pageId));

    /// <summary>Replaces every pick of one page (restore the starter list of a page).</summary>
    public void ReplacePage(string pageId, IEnumerable<Pick> picks) => Apply(Store.ReplacePage(pageId, picks));

    /// <summary>Takes over a store that was already saved elsewhere (the settings screen saves its own changes).</summary>
    public void Adopt(PickStore saved)
    {
        Store = saved;
        Changed?.Invoke();
    }

    private void Apply(PickStore next)
    {
        Store = next;
        if (canSave && path is not null && !next.Save(path)) SaveFailed?.Invoke();
        Changed?.Invoke();
    }
}
