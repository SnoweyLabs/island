namespace Island.Core.SettingsEdit;

/// <summary>
/// One key press as the settings screen reads it from an ordinary key event: the Windows virtual-key
/// code, which of Ctrl, Alt and Shift were held, and whether the Windows key was held.
/// </summary>
public readonly record struct KeyPress(int VirtualKey, HotkeyModifiers Modifiers, bool WindowsKey = false)
{
    // The keys that are only modifiers: Shift, Ctrl, Alt (Menu), their left and right forms, and the Windows keys.
    private static readonly int[] ModifierKeys = [0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
    private static readonly int[] WindowsKeys = [0x5B, 0x5C];

    /// <summary>True when the pressed key is a Windows key itself.</summary>
    public bool IsWindowsKey => WindowsKey || Array.IndexOf(WindowsKeys, VirtualKey) >= 0;

    /// <summary>True when only a modifier is down so far: the combination is still being made.</summary>
    public bool IsModifierOnly => Array.IndexOf(ModifierKeys, VirtualKey) >= 0;
}

/// <summary>
/// How the app asks Windows to hold a global key. The real one wraps RegisterHotKey; tests use a pretend.
/// <paramref name="error"/> is the Windows error code when registering fails (1409 means another program holds it).
/// </summary>
public interface IHotkeyRegistrar
{
    bool TryRegister(HotkeyCombo combo, out int error);

    void Release(HotkeyCombo combo);
}
