using System.IO;
using System.Net;
using System.Net.Sockets;
using Island.Core;

namespace Island.App;

/// <summary>
/// What keeps the self-test from fighting for the things Dan's own copy holds: its own lock names
/// (<see cref="InstanceNames"/>), no add-on listener on the real ports (the self-test's bridge uses a free port of its
/// own), and a stand-in main key when Windows says another program holds the real one.
/// </summary>
internal static class SelfTestIsolation
{
    /// <summary>True once a self-test copy found the real main key taken and used a stand-in key instead.</summary>
    public static bool StandInUsed { get; private set; }

    /// <summary>
    /// Under the self-test, before any key is registered: when Windows refuses the main key in the (temporary) settings
    /// file, the first stand-in it accepts is written into that file. Nothing changes when the real key is free.
    /// </summary>
    public static void UseStandInKeyIfTaken(string settingsPath, IReadOnlyList<Page> pages)
    {
        using var probe = new HotkeyHost();
        if (StandInKey.Prepare(settingsPath, pages, combo => probe.TryRegister(combo, out _))) StandInUsed = true;
    }
}

/// <summary>
/// <c>--selftest-hold</c>: a helper process of the self-test. It tries to take the real lock, the real main key and the
/// first real port of the add-on, writes what it found ("held" by itself, or "taken" by another copy, which is Dan's)
/// into <c>hold.txt</c> in the folder it was given, accepts no connection, and ends by itself after a short while (the
/// self-test ends it sooner).
/// </summary>
internal static class SelfTestHold
{
    public const string FileName = "hold.txt";

    /// <summary>The helper's own window (off the screen), standing in for "somebody else's window" in the close checks; its handle is written here before <see cref="FileName"/>.</summary>
    public const string WindowFileName = "window.txt";
    private static readonly TimeSpan StayAlive = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(string? folder)
    {
        if (folder is null) return;

        string lockState, keyState, portState;
        Mutex? mutex = null;
        HotkeyHost? keys = null;
        TcpListener? listener = null;
        try
        {
            mutex = new Mutex(true, InstanceNames.For(selfTest: false).Running, out var created);
            lockState = created ? "held" : "taken";

            keys = new HotkeyHost();
            keyState = keys.TryRegister(Settings.Defaults.ShowHide, out _) ? "held" : "taken";

            try
            {
                listener = new TcpListener(IPAddress.Loopback, TabProtocol.Port) { ExclusiveAddressUse = true };
                listener.Start(1); // listens; nothing is ever accepted
                portState = "held";
            }
            catch (SocketException)
            {
                portState = "taken";
            }

            var window = new System.Windows.Window
            {
                Title = "Island self-test: stand-in for somebody else's window",
                Width = 200,
                Height = 80,
                Left = -4000,
                Top = -4000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
            };
            window.Show();
            File.WriteAllText(Path.Combine(folder, WindowFileName), new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle().ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));

            var temporary = Path.Combine(folder, FileName + ".tmp");
            File.WriteAllText(temporary, $"lock={lockState}\nkey={keyState}\nport={portState}\n");
            File.Move(temporary, Path.Combine(folder, FileName), overwrite: true);

            await Task.Delay(StayAlive);
        }
        finally
        {
            listener?.Stop();
            keys?.Dispose();
            mutex?.Dispose();
        }
    }
}
