using System.Windows;
using Island.Core;
using Island.Core.SettingsEdit;
using Microsoft.Win32;

namespace Island.App;

/// <summary>
/// The real way to Windows' own windows for choosing a file or a folder (WORK-ORDER-10 §3): <c>OpenFileDialog</c> and <c>OpenFolderDialog</c> of Microsoft.Win32
/// (PresentationFramework; OpenFolderDialog exists from .NET 8, Microsoft Learn, page ms.date 2025-07-01, read 2026-10-07). It cannot be made without the window that owns it, and
/// every window it opens is shown with that owner (<c>ShowDialog(Window)</c>), so it always opens in front of the settings screen, which is itself on top of everything.
/// It asks <see cref="OutsideGate"/> first: under the self-test the gate refuses and nothing opens. What was chosen is returned in memory and never logged.
/// </summary>
internal sealed class OutsidePlaceChooser(Window owner) : IPlaceChooser
{
    public string? ChooseFile(PlaceChoice what)
    {
        if (!OutsideGate.Current.Allow(OutsideKind.ChoosePlace)) return null;
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = false, // a shortcut is kept as the shortcut's own file, as the work order says
            Title = what == PlaceChoice.Program ? "Choose a program or a shortcut" : "Choose a file",
            Filter = what == PlaceChoice.Program ? "Programs and shortcuts|*.exe;*.lnk|All files|*.*" : "All files|*.*",
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? ChooseFolder()
    {
        if (!OutsideGate.Current.Allow(OutsideKind.ChoosePlace)) return null;
        var dialog = new OpenFolderDialog { Multiselect = false, Title = "Choose a folder" };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }
}
