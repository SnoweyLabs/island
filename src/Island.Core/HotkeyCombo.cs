namespace Island.Core;

/// <summary>
/// Modifier bits as RegisterHotKey expects them. The Windows key is left out on
/// purpose: combinations with it belong to Windows, so they are refused at parse time.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
}

/// <summary>A global keybind: modifiers plus one Windows virtual-key code.</summary>
public readonly record struct HotkeyCombo(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["Tab"] = 0x09,
        ["Enter"] = 0x0D,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
    };

    private static readonly string[] WindowsKeyNames = ["win", "windows", "super", "meta", "cmd"];

    public static HotkeyCombo Parse(string text) =>
        TryParse(text, out var combo, out var error)
            ? combo
            : throw new FormatException(error);

    public static bool TryParse(string? text, out HotkeyCombo combo, out string error)
    {
        combo = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The combination is empty.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;

        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                error = $"'{text}' has an empty part.";
                return false;
            }

            if (Array.Exists(WindowsKeyNames, n => n.Equals(part, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Combinations with the Windows key are reserved by Windows.";
                return false;
            }

            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"'{part}' appears twice.";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (key is not null)
            {
                error = $"'{text}' has more than one key.";
                return false;
            }

            if (!TryKey(part, out var vk))
            {
                error = $"Unknown key '{part}'.";
                return false;
            }

            key = vk;
        }

        if (key is null)
        {
            error = "No key given, only modifiers.";
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            error = "A keybind needs at least one of Ctrl, Alt or Shift, or it would take over normal typing.";
            return false;
        }

        var candidate = new HotkeyCombo(modifiers, key.Value);
        if (UnsafeReason(candidate) is { } reason)
        {
            error = reason;
            return false;
        }

        combo = candidate;
        return true;
    }

    // Shortcuts that Windows keeps for itself or that nearly every program relies on: taking them
    // silently breaks those programs (RegisterHotKey grants most of them).
    private static readonly (HotkeyModifiers Modifiers, int Key)[] Reserved =
    [
        (HotkeyModifiers.Alt, 0x73),                                     // Alt+F4 closes a window
        (HotkeyModifiers.Alt, 0x09),                                     // Alt+Tab switches window
        (HotkeyModifiers.Alt, 0x20),                                     // Alt+Space window menu
        (HotkeyModifiers.Alt, 0x1B),                                     // Alt+Esc
        (HotkeyModifiers.Control, 0x1B),                                 // Ctrl+Esc opens Start
        (HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x1B),         // Ctrl+Shift+Esc task manager
        (HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x2E),           // Ctrl+Alt+Delete
    ];

    private const string EditingKeys = "ACVXYZ";

    private static string? UnsafeReason(HotkeyCombo c)
    {
        if (c.Modifiers == HotkeyModifiers.Shift)
            return "Shift alone types capital letters and symbols, so this would take over normal typing.";

        if (Array.Exists(Reserved, r => r.Modifiers == c.Modifiers && r.Key == c.VirtualKey))
            return $"{c} is a shortcut Windows keeps for itself, so Island would break it.";

        if (c.Modifiers == HotkeyModifiers.Control && c.VirtualKey is >= 'A' and <= 'Z' && EditingKeys.Contains((char)c.VirtualKey))
            return $"{c} is copy, paste, cut, undo or select all in nearly every program, so Island would break them.";

        return null;
    }

    private static bool TryKey(string name, out int vk)
    {
        vk = 0;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            vk = char.ToUpperInvariant(name[0]);
            return true;
        }

        if (NamedKeys.TryGetValue(name, out vk)) return true;

        // Plain spelling only: F1 to F24, no spaces, signs or leading zeros.
        if (name.Length is >= 2 and <= 3 && (name[0] is 'F' or 'f') && name[1] != '0'
            && name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            && int.Parse(name.AsSpan(1)) is var n and >= 1 and <= 24)
        {
            vk = 0x70 + n - 1;
            return true;
        }

        return false;
    }

    /// <summary>The canonical text, for example Ctrl+Alt+Shift+Space.</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName());
        return string.Join('+', parts);
    }

    private string KeyName()
    {
        foreach (var pair in NamedKeys)
            if (pair.Value == VirtualKey && pair.Key is not "Escape")
                return pair.Key;
        if (VirtualKey is >= 0x70 and <= 0x87) return $"F{VirtualKey - 0x70 + 1}";
        return ((char)VirtualKey).ToString();
    }
}
