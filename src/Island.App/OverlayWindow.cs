using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Island.App;

/// <summary>
/// A borderless, transparent, topmost window that never takes focus and is absent
/// from the taskbar and Alt+Tab. Placed in physical pixels at the top of the primary monitor.
/// </summary>
internal class OverlayWindow : Window
{
    private readonly bool _ignoresMouse;

    public OverlayWindow(bool ignoresMouse)
    {
        _ignoresMouse = ignoresMouse;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        SnapsToDevicePixels = true;
    }

    public IntPtr Handle { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        Handle = source.Handle;

        var style = Native.GetExStyle(Handle) | Native.WsExToolWindow | Native.WsExNoActivate;
        if (_ignoresMouse) style |= Native.WsExTransparent;
        Native.SetExStyle(Handle, style);

        source.AddHook(WndProc);
    }

    /// <summary>
    /// A key pressed while this window has the keyboard, as its virtual-key code with Shift, Ctrl, Alt and Windows as they were when the message was sent and whether it is
    /// a repeat of a key held down (bit 30 of lParam of WM_KEYDOWN, the key's previous state). An ordinary window message: no hook on other programs' keys.
    /// The key is swallowed, never passed on.
    /// </summary>
    public event Action<Island.Core.KeyInput>? RawKey;

    /// <summary>
    /// Text typed into the island's own window (WM_CHAR, which Windows sends to the window that has the keyboard): a character or a surrogate
    /// pair. Control characters and the digits are left to <see cref="RawKey"/>. No hook on other programs' keys.
    /// </summary>
    public event Action<string>? RawText;

    private char _highSurrogate;

    /// <summary>The screens, their scaling or the work area may have changed (the island re-reads them; WORK-ORDER-6 §1).</summary>
    public event Action? SystemChanged;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WmMouseActivate:
                handled = true;
                return new IntPtr(Native.MaNoActivate);
            case Native.WmDisplayChange or Native.WmDpiChanged or Native.WmSettingChange:
                SystemChanged?.Invoke();
                return IntPtr.Zero; // not handled: the framework still sees it
            case Native.WmChar:
                handled = true;
                TextFrom((char)wParam.ToInt32());
                return IntPtr.Zero;
            case Native.WmKeyDown or Native.WmSysKeyDown:
                handled = true;
                RawKey?.Invoke(new Island.Core.KeyInput(
                    wParam.ToInt32(),
                    Shift: Down(Native.VkShift),
                    Ctrl: Down(Native.VkControl),
                    Alt: Down(Native.VkMenu),
                    Win: Down(Native.VkLWin) || Down(Native.VkRWin),
                    IsRepeat: (lParam.ToInt64() & (1L << 30)) != 0));
                return IntPtr.Zero;
            default:
                return IntPtr.Zero;
        }
    }

    // The key's state right now, from the keyboard itself. GetKeyState reads this window's own queue, which never saw the Ctrl of Ctrl+Q (that key went to another program's queue):
    // it can stay "down" there, and then every arrow would count as Ctrl+arrow and do nothing.
    private static bool Down(int virtualKey) => (Native.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void TextFrom(char c)
    {
        if (char.IsHighSurrogate(c))
        {
            _highSurrogate = c;
            return;
        }

        var high = _highSurrogate;
        _highSurrogate = '\0';
        if (char.IsLowSurrogate(c))
        {
            if (high != '\0') RawText?.Invoke(new string([high, c]));
            return;
        }

        if (char.IsControl(c) || c is >= '0' and <= '9') return; // keys are RawKey's; digits go by their key, once
        RawText?.Invoke(c.ToString());
    }

    /// <summary>Puts the window at this rectangle in real pixels, without activating it and without changing which window is on top.</summary>
    public void MoveTo(Island.Core.PixelRect rect) =>
        Native.SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, (int)rect.Width, (int)rect.Height, Native.SwpNoActivate | Native.SwpNoZOrder);

    /// <summary>The window's real rectangle now, in pixels.</summary>
    public Island.Core.PixelRect RealRectangle
    {
        get
        {
            Native.GetWindowRect(Handle, out var r);
            return new Island.Core.PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    /// <summary>Sets the size in device-independent pixels and the position (top centre of the primary monitor).</summary>
    public void PlaceAtTopCentre(double widthDip, double heightDip)
    {
        var monitor = Native.PrimaryMonitorBounds();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var widthPx = (int)Math.Round(widthDip * scale);
        var heightPx = (int)Math.Round(heightDip * scale);
        var x = monitor.Left + ((monitor.Right - monitor.Left) - widthPx) / 2;
        Native.SetWindowPos(Handle, Native.HwndTopmost, x, monitor.Top, widthPx, heightPx, Native.SwpNoActivate);
    }
}
