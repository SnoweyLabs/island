namespace Island.Core;

/// <summary>
/// The registry as the start-with-Windows switch needs it: one value, read, written, removed. The names are
/// deliberately not the framework's own writing members, so the guard that finds registry writes (they may
/// appear in one file whose name begins "Outside") never mistakes this logic for one.
/// </summary>
public interface IStartupRegistry
{
    /// <summary>The text of the one value, or null when it is not there or cannot be read. Never throws.</summary>
    string? Read();

    /// <summary>Stores <paramref name="commandLine"/> as the value (a REG_SZ). False when it was not stored; never throws.</summary>
    bool Write(string commandLine);

    /// <summary>True when the value is not there afterwards (it also was not there before counts). False when it could not be removed; never throws.</summary>
    bool Remove();
}

/// <summary>
/// Where the value lives and how long it may be, every one taken from Microsoft Learn,
/// "Run and RunOnce Registry Keys" (win32/setupapi/run-and-runonce-registry-keys, page updated 2026-02-21).
/// </summary>
public static class StartupKey
{
    /// <summary>Under HKEY_CURRENT_USER: the page lists HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The page says entries have the form description-string=commandline; this is Island's description-string.</summary>
    public const string ValueName = "Island";

    /// <summary>"The data value for a key is a command line no longer than 260 characters."</summary>
    public const int MaxCommandLineLength = 260;
}

/// <summary>A registry for tests and for the self-test: the value lives in memory and every call is counted.</summary>
public sealed class MemoryStartupRegistry : IStartupRegistry
{
    private string? _value;

    public MemoryStartupRegistry(string? initialValue = null) => _value = initialValue;

    /// <summary>When true, Write and Remove fail and change nothing, as a locked-down profile would.</summary>
    public bool RefuseChanges { get; set; }

    public int Writes { get; private set; }

    public int Removes { get; private set; }

    public string? Value => _value;

    public string? Read() => _value;

    public bool Write(string commandLine)
    {
        Writes++;
        if (RefuseChanges) return false;
        _value = commandLine;
        return true;
    }

    public bool Remove()
    {
        Removes++;
        if (RefuseChanges) return false;
        _value = null;
        return true;
    }
}
