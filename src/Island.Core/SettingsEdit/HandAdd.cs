namespace Island.Core.SettingsEdit;

/// <summary>What a choosing window is asked for (WORK-ORDER-10 §3). Windows' own windows; the session never opens one itself.</summary>
public enum PlaceChoice
{
    /// <summary>A program or a shortcut that is not in the list of installed programs.</summary>
    Program,

    /// <summary>Any file.</summary>
    AnyFile,
}

/// <summary>
/// The one small interface through which the settings screen reaches Windows' own windows for choosing a file or a folder. The app's real one lives in a file whose name
/// begins "Outside", asks <see cref="OutsideGate"/> first (it refuses under the self-test) and cannot be made without the window that owns it; the self-test and every
/// test use <see cref="PretendChooser"/>. A choosing window is never opened by a test, a self-test stage or a subagent.
/// </summary>
public interface IPlaceChooser
{
    /// <summary>The full path of the file the person chose, or null when they cancelled. In memory only.</summary>
    string? ChooseFile(PlaceChoice what);

    /// <summary>The full path of the folder the person chose, or null when they cancelled. In memory only.</summary>
    string? ChooseFolder();
}

/// <summary>A chooser that answers at once with what it was told, for tests and for the self-test. It opens nothing.</summary>
public sealed class PretendChooser(string? file = null, string? folder = null) : IPlaceChooser
{
    public string? File { get; set; } = file;

    public string? Folder { get; set; } = folder;

    /// <summary>How many times it was asked (a window that was never opened still counts as asked).</summary>
    public int Asked { get; private set; }

    public string? ChooseFile(PlaceChoice what)
    {
        Asked++;
        return File;
    }

    public string? ChooseFolder()
    {
        Asked++;
        return Folder;
    }
}

/// <summary>
/// What "Add…" did. <see cref="Added"/> is true when the thing is on the island now; otherwise <see cref="Message"/> says why in the register's words or a plain
/// reason, and <see cref="AlreadyOn"/> names the page a thing that is already on the island is on.
/// </summary>
public sealed record HandAddResult(bool Added, string? Message, Pick? Pick = null, string? AlreadyOn = null, bool Cancelled = false)
{
    public static HandAddResult Cancel { get; } = new(false, null, Cancelled: true);
}
