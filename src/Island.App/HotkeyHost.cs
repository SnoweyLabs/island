using System.Windows.Interop;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>The outcome of asking Windows for one keybind.</summary>
internal sealed record HotkeyResult(string Action, HotkeyCombo Combo, bool Accepted, int LastError);

/// <summary>
/// Registers global keybinds with RegisterHotKey (no keyboard hook), without auto-repeat, on a
/// message-only window. Windows refuses a combination another program already registered; that is
/// recorded per keybind, never forced. It is also the registrar the settings screen uses to change a key
/// (register the new one first, release the old one only after that worked): a combination is held at most once.
/// </summary>
internal sealed class HotkeyHost : IHotkeyRegistrar, IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyCombo> _byId = [];
    private int _nextId = 1;

    public HotkeyHost()
    {
        _source = new HwndSource(new HwndSourceParameters("IslandHotkeys") { ParentWindow = Native.HwndMessage });
        _source.AddHook(WndProc);
    }

    /// <summary>Raised with the combination when a registered keybind is pressed. What it means is decided by the settings in force.</summary>
    public event Action<HotkeyCombo>? Pressed;

    public bool TryRegister(HotkeyCombo combo, out int error)
    {
        error = 0;
        if (_byId.ContainsValue(combo)) return true; // already held by this very app

        var id = _nextId++;
        var ok = Native.RegisterHotKey(_source.Handle, id, (uint)combo.Modifiers | Native.ModNoRepeat, (uint)combo.VirtualKey);
        if (!ok)
        {
            error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            return false;
        }

        _byId[id] = combo;
        return true;
    }

    public void Release(HotkeyCombo combo)
    {
        foreach (var id in _byId.Where(p => p.Value == combo).Select(p => p.Key).ToList())
        {
            Native.UnregisterHotKey(_source.Handle, id);
            _byId.Remove(id);
        }
    }

    public HotkeyResult Register(string action, HotkeyCombo combo)
    {
        var ok = TryRegister(combo, out var error);
        return new HotkeyResult(action, combo, ok, error);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WmHotKey) return IntPtr.Zero;
        handled = true;
        if (_byId.TryGetValue(wParam.ToInt32(), out var combo)) Pressed?.Invoke(combo);
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _byId.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _byId.Clear();
        _source.Dispose();
    }
}
