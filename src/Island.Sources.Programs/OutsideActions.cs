using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Island.Core;

namespace Island.Sources.Programs;

/// <summary>
/// The real <see cref="IOutsideActions"/>: the only code of this project that starts a program, opens a folder
/// or an address, or asks for the foreground. Every method first asks <see cref="OutsideGate.Current"/> and does
/// nothing when it refuses (under the self-test it refuses all but its own windows). It never logs what it was
/// asked to open, and it never forces anything: if Windows will not give the foreground, the answer is false.
/// Call it from the click or key handler, on the thread that received the input, so Windows still counts the
/// click as the last input and agrees to the foreground change (Research/windows-apis.md section 3).
/// </summary>
public sealed class OutsideActions(IProgramCatalog catalog) : IOutsideActions
{
    private const int ShowRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    public bool BringForward(long windowHandle)
    {
        var hwnd = (nint)windowHandle;
        if (!IsWindow(hwnd)) return false;

        GetWindowThreadProcessId(hwnd, out var owner); // reading only: the gate wants to know whose window it is
        if (!OutsideGate.Current.Allow(OutsideKind.BringForward, (int)owner)) return false;

        if (IsIconic(hwnd)) ShowWindow(hwnd, ShowRestore);
        return SetForegroundWindow(hwnd);
    }

    public bool StartProgram(Pick pick)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.StartProgram)) return false;
        if (pick.Kind != PickKind.Program) return false;
        // A program the person browsed to is started from its own place (a shortcut too: the shell opens it as a double-click would); any other from the catalog.
        if (pick.Location is not null) return PickPath.Expand(pick.Location, ProfileFolder) is { } place && Open(place);
        return Resolve(pick) is { } target && Open(target);
    }

    private static string ProfileFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public bool OpenFolderAt(string realPath)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenFolder)) return false;
        return Directory.Exists(realPath) && Open(realPath);
    }

    public bool OpenFile(string realPath)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenFile)) return false;
        return File.Exists(realPath) && Open(realPath);
    }

    public bool OpenFolder(string knownFolder)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenFolder)) return false;
        return KnownFolders.PathOf(knownFolder) is { } path && Open(path);
    }

    public bool OpenSite(string host)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenAddress)) return false;
        return !string.IsNullOrWhiteSpace(host) && Open(SiteAddress.For(host));
    }

    public bool OpenSearch(SearchService service, string text)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.OpenAddress)) return false;
        return SiteAddress.ForSearch(service, text) is { } address && Open(address);
    }

    /// <summary>What to start for a pick, from the catalog in memory: by executable file name first, then by package family.</summary>
    private string? Resolve(Pick pick)
    {
        var installed = catalog.Installed;
        var found = installed.FirstOrDefault(p => pick.ExeName is not null && string.Equals(p.ExeName, pick.ExeName, StringComparison.OrdinalIgnoreCase))
                    ?? installed.FirstOrDefault(p => pick.PackageFamily is not null && string.Equals(p.PackageFamily, pick.PackageFamily, StringComparison.OrdinalIgnoreCase));
        return found?.LaunchTarget;
    }

    /// <summary>Hands a path, a shell:AppsFolder id or an address to the shell, as a double-click would.</summary>
    private static bool Open(string target)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
