using System.IO;
using Island.Core;
using Microsoft.Win32;

namespace Island.App;

/// <summary>
/// The only file of the whole program that writes the Windows registry: the one value of the person's own start-up list
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>) that the settings screen's switch sets and removes. Every write and
/// delete asks <see cref="OutsideGate"/> first, and the gate refuses them under the self-test (no test, no self-test stage and no
/// subagent opens the real registry for writing). Reading needs no gate and changes nothing.
/// </summary>
internal sealed class OutsideStartupRegistry : IStartupRegistry
{
    public string? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey.KeyPath);
            return key?.GetValue(StartupKey.ValueName) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // unreadable counts as not there
        }
    }

    public bool Write(string commandLine)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.WriteStartupValue)) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey.KeyPath);
            key.SetValue(StartupKey.ValueName, commandLine, RegistryValueKind.String);
            return true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public bool Remove()
    {
        if (!OutsideGate.Current.Allow(OutsideKind.WriteStartupValue)) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupKey.KeyPath, writable: true);
            key?.DeleteValue(StartupKey.ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
