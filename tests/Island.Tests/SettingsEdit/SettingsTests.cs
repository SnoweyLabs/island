using Island.Core;
using Island.Core.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.SettingsEdit;

public class SettingsTests
{
    [Fact]
    public void Changed_Keybind_Round_Trips()
    {
        // Arrange: change the main key and a page key through the editor, saving as the screen does.
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var registrar = new FakeRegistrar();
        var editor = EditorFor(registrar);
        var first = editor.Assign(Settings.Defaults, KeybindEditor.MainId, Combo("Ctrl+Alt+Space"), s => s.Save(path));
        var second = editor.Assign(first.Settings, PageIds.Vibe, Combo("Ctrl+Alt+4"), s => s.Save(path));

        // Act: start again from the file.
        var load = Settings.Load(path);

        // Assert
        Assert.True(first.Changed && second.Changed);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(Combo("Ctrl+Alt+Space"), load.Settings.ShowHide);
        Assert.Equal(Combo("Ctrl+Alt+4"), load.Settings.KeyFor(PageIds.Vibe));
        Assert.Equal(second.Settings, load.Settings);

        // And a cleared page key and a restored main key stay as they were left.
        var cleared = editor.Clear(load.Settings, PageIds.Vibe, s => s.Save(path));
        var restored = editor.RestoreDefault(cleared.Settings, KeybindEditor.MainId, s => s.Save(path));
        var again = Settings.Load(path);
        Assert.Null(again.Settings.KeyFor(PageIds.Vibe));
        Assert.Equal(Combo("Ctrl+Q"), again.Settings.ShowHide);
        Assert.Equal(restored.Settings, again.Settings);
    }
}
