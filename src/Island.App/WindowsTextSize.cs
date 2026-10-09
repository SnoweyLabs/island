using Island.Core;
using Windows.UI.ViewManagement;

namespace Island.App;

/// <summary>
/// Reads Windows' text size ("Make text bigger", 100% to 225%) into <see cref="TextScale"/> and follows it while the app runs (Dan's P2, WORK-ORDER-13). A self-test never reads it: its pictures are at 100%.
/// </summary>
internal static class WindowsTextSize
{
    private static UISettings? _settings;

    public static void Start()
    {
        if (OutsideGate.Current.SelfTest || _settings is not null) return;
        try
        {
            _settings = new UISettings();
            TextScale.Factor = _settings.TextScaleFactor;
            _settings.TextScaleFactorChanged += (s, _) => TextScale.Factor = s.TextScaleFactor; // comes on another thread; the settings screen takes it the next time it is built
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or PlatformNotSupportedException)
        {
            TextScale.Factor = 1; // the system would not say: the type stays as it is
        }
    }
}
