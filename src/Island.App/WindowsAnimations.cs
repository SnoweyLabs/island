using System.Windows;
using Island.Core;

namespace Island.App;

/// <summary>
/// Whether Windows is set to show animations ("Show animations in Windows", Settings, Accessibility, Visual effects). Dan's P1 (WORK-ORDER-13): with it off the island and the settings screen come and go
/// without spring, delay, fade or light that moves. A self-test always has them on: its pictures and numbers are of the moving island.
/// </summary>
internal static class WindowsAnimations
{
    public static bool On => OutsideGate.Current.SelfTest || SystemParameters.ClientAreaAnimation;
}
