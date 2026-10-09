using System.Runtime.InteropServices;

namespace Island.App;

/// <summary>
/// How often the screen a window is on refreshes (WORK-ORDER-12 section 2): the monitor the window is on (MonitorFromWindow, nearest), its device name (GetMonitorInfo, the
/// extended form), and the frequency of that display's current mode (EnumDisplaySettings with ENUM_CURRENT_SETTINGS: DEVMODE.dmDisplayFrequency, "the frequency, in hertz,
/// of a display device in its current mode"; Microsoft Learn, read 2026-10-07). 0 when it cannot be had or the driver says 0 or 1 (the hardware's default): the island then
/// draws on every frame callback, as it did before. Reads only a number; nothing is kept or written.
/// </summary>
internal static class ScreenRefresh
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EnumCurrentSettings = -1;
    private const int Cchdevicename = 32, Cchformname = 32;
    private const int DevModeSize = 220; // sizeof(DEVMODEW) on every Windows this runs on

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public int Left, Top, Right, Bottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cchdevicename)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cchdevicename)]
        public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TtOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Cchformname)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(string deviceName, int mode, ref DevMode devMode);

    /// <summary>The refresh rate, in hertz, of the screen the window is on; 0 when it is not known.</summary>
    public static double HzOf(IntPtr window)
    {
        try
        {
            if (Marshal.SizeOf<DevMode>() != DevModeSize || window == IntPtr.Zero) return 0;
            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfoEx(monitor, ref info)) return 0;
            var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettings(info.Device, EnumCurrentSettings, ref mode)) return 0;
            return mode.DisplayFrequency > 1 ? mode.DisplayFrequency : 0;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return 0;
        }
    }
}
