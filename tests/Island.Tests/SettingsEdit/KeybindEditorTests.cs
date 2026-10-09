using Island.Core;
using Island.Core.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.SettingsEdit;

public class KeybindEditorTests
{
    [Fact]
    public void Captured_Keys_Become_A_Combo()
    {
        // Arrange: presses as a WPF key event would give them (virtual-key code plus held modifiers).
        var cases = new (KeyPress Press, string Expected)[]
        {
            (Press(Q, HotkeyModifiers.Control), "Ctrl+Q"),
            (Press(Space, HotkeyModifiers.Control | HotkeyModifiers.Alt), "Ctrl+Alt+Space"),
            (Press(F5, HotkeyModifiers.Control | HotkeyModifiers.Shift), "Ctrl+Shift+F5"),
            (Press('1', HotkeyModifiers.Control | HotkeyModifiers.Alt), "Ctrl+Alt+1"),
            (Press(0x27, HotkeyModifiers.Alt), "Alt+Right"),
        };

        foreach (var (press, expected) in cases)
        {
            // Act
            var capture = KeybindEditor.Capture(press);

            // Assert
            Assert.Null(capture.Refusal);
            Assert.False(capture.Waiting);
            Assert.Equal(HotkeyCombo.Parse(expected), capture.Combo);
        }
    }

    [Theory]
    [InlineData(0x10)] // Shift
    [InlineData(0x11)] // Ctrl
    [InlineData(0x12)] // Alt
    [InlineData(0xA2)] // left Ctrl
    public void A_Modifier_On_Its_Own_Keeps_The_Screen_Waiting(int vk)
    {
        var capture = KeybindEditor.Capture(Press(vk, HotkeyModifiers.Control));

        Assert.True(capture.Waiting);
        Assert.Null(capture.Combo);
        Assert.Null(capture.Refusal);
    }

    [Fact]
    public void A_Key_The_Settings_File_Cannot_Keep_Is_Refused_In_Plain_Words()
    {
        foreach (var vk in new[] { 0xBA, 0x60, 0xAF }) // a punctuation key, numpad 0, volume up
        {
            var capture = KeybindEditor.Capture(Press(vk, HotkeyModifiers.Control));

            Assert.Null(capture.Combo);
            Assert.Equal("KEY_UNSUPPORTED", capture.Refusal!.Code);
            Assert.False(string.IsNullOrWhiteSpace(capture.Refusal.Message));
        }
    }

    [Fact]
    public void Duplicate_Inside_The_App_Is_Refused()
    {
        // Arrange: the main key is Ctrl+Alt+Space and Media has Ctrl+Alt+1.
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        // Act: give the main key the Media combination, then give Media the main combination.
        var mainToMedia = editor.Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+1"));
        var mediaToMain = editor.Assign(settings, PageIds.Media, Combo("Ctrl+Alt+Space"));

        // Assert: both refused, each message names the action that has the key, both keys unchanged, Windows never asked.
        Assert.True(mainToMedia.Refused);
        Assert.Contains("Media", mainToMedia.Refusal!.Message);
        Assert.True(mediaToMain.Refused);
        Assert.Contains(SettingsText.MainActionName, mediaToMain.Refusal!.Message);
        Assert.Equal(settings, mainToMedia.Settings);
        Assert.Equal(settings, mediaToMain.Settings);
        Assert.Empty(registrar.Log);
    }

    [Fact]
    public void Clear_And_Restore_Defaults()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        // A page key can be cleared, and Windows is told to let it go.
        var cleared = editor.Clear(settings, PageIds.Media);
        Assert.True(cleared.Changed);
        Assert.Null(cleared.Settings.KeyFor(PageIds.Media));
        Assert.DoesNotContain(Combo("Ctrl+Alt+1"), registrar.Held);

        // The main key cannot be cleared: it is the way back to the island.
        var mainCleared = editor.Clear(settings, KeybindEditor.MainId);
        Assert.True(mainCleared.Refused);
        Assert.Equal(settings, mainCleared.Settings);

        // Restore default for the main key brings back Ctrl+Q and gives Ctrl+Alt+Space back to Windows.
        var restored = editor.RestoreDefault(settings, KeybindEditor.MainId);
        Assert.Equal(Combo("Ctrl+Q"), restored.Settings.ShowHide);
        Assert.Contains(Combo("Ctrl+Q"), registrar.Held);
        Assert.DoesNotContain(Combo("Ctrl+Alt+Space"), registrar.Held);

        // Restore all: the original keys come back, idle time and glass stay.
        var (custom, otherRegistrar) = WithKeys();
        var withGlass = custom with { Glass = GlassKind.Darker, IdleSeconds = 12 };
        var all = EditorFor(otherRegistrar).RestoreDefaults(withGlass);
        Assert.Equal(Combo("Ctrl+Q"), all.Settings.ShowHide);
        Assert.All(Pages.BuiltIn, p => Assert.Null(all.Settings.KeyFor(p.Id)));
        Assert.Equal(GlassKind.Darker, all.Settings.Glass);
        Assert.Equal(12, all.Settings.IdleSeconds);
        Assert.Equal([Combo("Ctrl+Q")], otherRegistrar.Held.ToList());
    }

    [Fact]
    public void Refused_New_Key_Leaves_The_Old_One_In_Force()
    {
        // Arrange: another program holds Ctrl+Alt+K.
        var (settings, registrar) = WithKeys();
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+K"));
        var editor = EditorFor(registrar);

        // Act
        var change = editor.Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+K"));

        // Assert: HOTKEY_TAKEN, settings untouched, the old key was never released.
        Assert.True(change.Refused);
        Assert.Equal(Refusals.HotkeyTaken.Code, change.Refusal!.Code);
        Assert.Contains("Ctrl+Alt+K", change.Refusal.Message);
        Assert.Contains("old key still works", change.Refusal.Message);
        Assert.Equal(settings, change.Settings);
        Assert.Contains(Combo("Ctrl+Alt+Space"), registrar.Held);
        Assert.DoesNotContain(registrar.Log, line => line.StartsWith("release", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_Windows_Refusal_Is_Also_Safe_And_Says_Its_Code()
    {
        var (settings, registrar) = WithKeys();
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+K"));
        registrar.ErrorCode = 87;

        var change = EditorFor(registrar).Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+K"));

        Assert.True(change.Refused);
        Assert.Contains("87", change.Refusal!.Message);
        Assert.Equal(settings, change.Settings);
        Assert.Contains(Combo("Ctrl+Alt+Space"), registrar.Held);
    }

    [Fact]
    public void New_Key_Is_Registered_Before_The_Old_One_Is_Released()
    {
        // Arrange
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);
        bool Save(Settings next)
        {
            registrar.Log.Add("save");
            return true;
        }

        // Act
        var change = editor.Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+K"), Save);

        // Assert: register the new one, save it, and only then let the old one go.
        Assert.True(change.Changed);
        Assert.Equal(Combo("Ctrl+Alt+K"), change.Settings.ShowHide);
        Assert.Equal(["register Ctrl+Alt+K", "save", "release Ctrl+Alt+Space"], registrar.Log);
        Assert.Equal([Combo("Ctrl+Alt+1"), Combo("Ctrl+Alt+K")], registrar.Held.OrderBy(c => c.ToString()).ToList());
    }

    [Fact]
    public void A_Key_That_Cannot_Be_Saved_Is_Not_Taken()
    {
        var (settings, registrar) = WithKeys();

        var change = EditorFor(registrar).Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+K"), _ => false);

        Assert.True(change.Refused);
        Assert.Equal(settings, change.Settings);
        Assert.Equal(["register Ctrl+Alt+K", "release Ctrl+Alt+K"], registrar.Log);
        Assert.Contains(Combo("Ctrl+Alt+Space"), registrar.Held);
        Assert.DoesNotContain(Combo("Ctrl+Alt+K"), registrar.Held);
    }

    [Fact]
    public void Pressing_The_Key_It_Already_Has_Changes_Nothing()
    {
        var (settings, registrar) = WithKeys();

        var change = EditorFor(registrar).AssignPress(settings, KeybindEditor.MainId, Press(Space, HotkeyModifiers.Control | HotkeyModifiers.Alt));

        // Nothing changes, but Windows is asked again: the key may have been refused at the start and be free now (the registrar answers true for one it holds).
        Assert.False(change.Changed);
        Assert.False(change.Refused);
        Assert.Equal(["register Ctrl+Alt+Space"], registrar.Log);
    }

    [Fact]
    public void A_Refused_Press_Never_Reaches_Windows()
    {
        var (settings, registrar) = WithKeys();

        var change = EditorFor(registrar).AssignPress(settings, KeybindEditor.MainId, Press(Q, HotkeyModifiers.None));

        Assert.True(change.Refused);
        Assert.Equal(settings, change.Settings);
        Assert.Empty(registrar.Log);
    }

    [Fact]
    public void A_Pressed_Combination_Is_Mapped_To_Its_Action_From_The_Current_Settings()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        Assert.Equal(KeybindEditor.MainId, KeybindEditor.ActionFor(settings, Combo("Ctrl+Alt+Space")));
        Assert.Equal(PageIds.Media, KeybindEditor.ActionFor(settings, Combo("Ctrl+Alt+1")));
        Assert.Null(KeybindEditor.ActionFor(settings, Combo("Ctrl+Alt+K")));

        // After a change the same lookup follows it with no other bookkeeping.
        var changed = editor.Assign(settings, KeybindEditor.MainId, Combo("Ctrl+Alt+K")).Settings;
        Assert.Equal(KeybindEditor.MainId, KeybindEditor.ActionFor(changed, Combo("Ctrl+Alt+K")));
        Assert.Null(KeybindEditor.ActionFor(changed, Combo("Ctrl+Alt+Space")));
    }

    // ---- WORK-ORDER-6 section 4: a key for a page, a key for a pick -----------------

    [Fact]
    public void Item_Keybind_Round_Trips_And_Obeys_The_Same_Rules()
    {
        // Arrange: a pick is given a key the way the screen does it, by a press.
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);
        string? saved = null;
        bool Save(Settings next)
        {
            saved = next.ToJson();
            return true;
        }

        // Act + Assert: the key is taken, registered, saved, and read back from the file as it was.
        var given = editor.AssignPress(settings, Spotify, Press('S', HotkeyModifiers.Control | HotkeyModifiers.Alt), Save);
        Assert.True(given.Changed);
        Assert.Equal(Combo("Ctrl+Alt+S"), given.Settings.PickKeyFor(Spotify));
        Assert.Contains(Combo("Ctrl+Alt+S"), registrar.Held);
        var reread = Settings.Parse(saved!).Settings;
        Assert.Equal(given.Settings, reread);
        Assert.Equal(Spotify, KeybindEditor.ActionFor(reread, Combo("Ctrl+Alt+S")));

        // The same rules as the main key: no Windows key, no key the file cannot keep, no bare key, nothing another program holds.
        Assert.Equal("KEY_WINDOWS", editor.AssignPress(given.Settings, Spotify, Press('S', HotkeyModifiers.Control, windowsKey: true)).Refusal!.Code);
        Assert.Equal("KEY_UNSUPPORTED", editor.AssignPress(given.Settings, Spotify, Press(0xBA, HotkeyModifiers.Control)).Refusal!.Code);
        Assert.True(editor.AssignPress(given.Settings, Spotify, Press('S', HotkeyModifiers.None)).Refused);
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+K"));
        var taken = editor.Assign(given.Settings, Spotify, Combo("Ctrl+Alt+K"));
        Assert.Equal(Refusals.HotkeyTaken.Code, taken.Refusal!.Code);
        Assert.Equal(given.Settings, taken.Settings);
        Assert.Contains(Combo("Ctrl+Alt+S"), registrar.Held); // the old key was never let go

        // A key that cannot be saved is not taken; changing a key lets the old one go only after the new one is held and saved.
        var unsaved = editor.Assign(given.Settings, Spotify, Combo("Ctrl+Alt+T"), _ => false);
        Assert.True(unsaved.Refused);
        Assert.DoesNotContain(Combo("Ctrl+Alt+T"), registrar.Held);
        var changed = editor.Assign(given.Settings, Spotify, Combo("Ctrl+Alt+T"), Save);
        Assert.Equal(Combo("Ctrl+Alt+T"), changed.Settings.PickKeyFor(Spotify));
        Assert.DoesNotContain(Combo("Ctrl+Alt+S"), registrar.Held);

        // Cleared: empty, and Windows has the key back.
        var cleared = editor.Clear(changed.Settings, Spotify, Save);
        Assert.Null(cleared.Settings.PickKeyFor(Spotify));
        Assert.Empty(cleared.Settings.PickKeys);
        Assert.DoesNotContain(Combo("Ctrl+Alt+T"), registrar.Held);
        Assert.Equal(cleared.Settings, Settings.Parse(saved!).Settings);
    }

    [Fact]
    public void A_Page_Key_Obeys_The_Same_Rules()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        // Any page, not only the ones that already have a key, can be given one by a press.
        var given = editor.AssignPress(settings, PageIds.Apps, Press('3', HotkeyModifiers.Control | HotkeyModifiers.Alt));
        Assert.True(given.Changed);
        Assert.Equal(Combo("Ctrl+Alt+3"), given.Settings.KeyFor(PageIds.Apps));
        Assert.Equal(PageIds.Apps, KeybindEditor.ActionFor(given.Settings, Combo("Ctrl+Alt+3")));
        Assert.Equal(given.Settings, Settings.Parse(given.Settings.ToJson()).Settings);

        // The same refusals as the main key, each leaving what was in force.
        Assert.Equal("KEY_WINDOWS", editor.AssignPress(given.Settings, PageIds.Apps, Press('3', HotkeyModifiers.Control, windowsKey: true)).Refusal!.Code);
        Assert.True(editor.AssignPress(given.Settings, PageIds.Apps, Press('3', HotkeyModifiers.None)).Refused);
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+K"));
        var taken = editor.Assign(given.Settings, PageIds.Apps, Combo("Ctrl+Alt+K"));
        Assert.Equal(Refusals.HotkeyTaken.Code, taken.Refusal!.Code);
        Assert.Equal(given.Settings, taken.Settings);
        Assert.Contains(Combo("Ctrl+Alt+3"), registrar.Held);

        // Restore default means: empty.
        var restored = editor.RestoreDefault(given.Settings, PageIds.Apps);
        Assert.Null(restored.Settings.KeyFor(PageIds.Apps));
        Assert.DoesNotContain(Combo("Ctrl+Alt+3"), registrar.Held);
    }

    [Fact]
    public void No_Two_Things_Share_A_Key()
    {
        // The main key, a page and a pick: whichever holds the combination first, the other two are refused in plain words that name
        // both, and nothing changes and Windows is never asked.
        string[] things = [KeybindEditor.MainId, PageIds.Apps, Spotify];
        foreach (var first in things)
        {
            foreach (var second in things.Where(t => t != first))
            {
                var registrar = new FakeRegistrar();
                var editor = EditorFor(registrar);
                var settings = Settings.Defaults;
                var held = editor.Assign(settings, first, Combo("Ctrl+Alt+J"));
                Assert.True(held.Changed);
                registrar.Log.Clear();

                var refused = editor.Assign(held.Settings, second, Combo("Ctrl+Alt+J"));

                Assert.True(refused.Refused);
                Assert.Equal("KEY_ALREADY_USED_HERE", refused.Refusal!.Code);
                Assert.Equal(held.Settings, refused.Settings);
                Assert.Empty(registrar.Log);
            }
        }

        // The words name what has the key and what was not changed.
        var message = SettingsText.KeyTakenInside(Combo("Ctrl+Alt+J"), "Spotify", "Apps").Message;
        Assert.Contains("Ctrl+Alt+J", message);
        Assert.Contains("\"Spotify\"", message);
        Assert.Contains("\"Apps\"", message);
        Assert.Contains("was not changed", message);

        // A settings file that gives one combination to two things is not accepted.
        var twice = Settings.Parse("""{ "hotkeys": { "apps": "Ctrl+Alt+J" }, "pickKeys": { "program:spotify": "Ctrl+Alt+J" } }""");
        Assert.Equal(SettingsStatus.Unreadable, twice.Status);
    }

    [Fact]
    public void Removing_A_Pick_Releases_Its_Key()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);
        var withTwo = editor.Assign(editor.Assign(settings, Spotify, Combo("Ctrl+Alt+S")).Settings, Notepad, Combo("Ctrl+Alt+N")).Settings;
        Assert.Contains(Combo("Ctrl+Alt+S"), registrar.Held);

        // Spotify is removed (dragged off, switched off, or its page removed): its key goes back to Windows, the other pick keeps its own.
        var after = editor.ReleaseKeysOfMissingPicks(withTwo, id => id == Notepad);

        Assert.True(after.Changed);
        Assert.Null(after.Settings.PickKeyFor(Spotify));
        Assert.Equal(Combo("Ctrl+Alt+N"), after.Settings.PickKeyFor(Notepad));
        Assert.DoesNotContain(Combo("Ctrl+Alt+S"), registrar.Held);
        Assert.Contains(Combo("Ctrl+Alt+N"), registrar.Held);

        // Nothing is missing: nothing is done. The key is free for another thing at once.
        Assert.False(editor.ReleaseKeysOfMissingPicks(after.Settings, id => id == Notepad).Changed);
        Assert.True(editor.Assign(after.Settings, PageIds.Apps, Combo("Ctrl+Alt+S")).Changed);

        // A save that fails keeps the key (and says so); the next try releases it.
        var stuck = editor.ReleaseKeysOfMissingPicks(after.Settings, _ => false, _ => false);
        Assert.True(stuck.Refused);
        Assert.Equal(Combo("Ctrl+Alt+N"), stuck.Settings.PickKeyFor(Notepad));
        Assert.True(editor.ReleaseKeysOfMissingPicks(stuck.Settings, _ => false).Changed);
    }

    [Fact]
    public void Default_For_Page_And_Pick_Keys_Is_Empty()
    {
        Assert.All(Pages.BuiltIn, p => Assert.Null(Settings.Defaults.KeyFor(p.Id)));
        Assert.Empty(Settings.Defaults.PickKeys);
        Assert.Null(Settings.Defaults.PickKeyFor(Spotify));

        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);
        var withPick = editor.Assign(settings, Spotify, Combo("Ctrl+Alt+S")).Settings;

        // "Restore default" on one pick's or one page's key: empty. "Restore the original keys": every one of them is empty again.
        var one = editor.RestoreDefault(withPick, Spotify);
        Assert.Null(one.Settings.PickKeyFor(Spotify));
        Assert.DoesNotContain(Combo("Ctrl+Alt+S"), registrar.Held);

        var (custom, other) = WithKeys();
        var otherEditor = EditorFor(other);
        var many = otherEditor.Assign(custom, Spotify, Combo("Ctrl+Alt+S")).Settings;
        var all = otherEditor.RestoreDefaults(many);
        Assert.Empty(all.Settings.PickKeys);
        Assert.Equal([Combo("Ctrl+Q")], other.Held.ToList());
    }

    [Fact]
    public void A_Mode_Key_Obeys_The_Same_Rules()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        var given = editor.AssignPress(settings, KeybindEditor.ModeNextId, Press('M', HotkeyModifiers.Control | HotkeyModifiers.Alt));
        Assert.Equal(Combo("Ctrl+Alt+M"), given.Settings.ModeKey);
        Assert.Equal(KeybindEditor.ModeNextId, KeybindEditor.ActionFor(given.Settings, Combo("Ctrl+Alt+M")));
        Assert.Equal(given.Settings, Settings.Parse(given.Settings.ToJson()).Settings);

        // Nobody else may take it, and it may not take what others hold.
        Assert.True(editor.Assign(given.Settings, PageIds.Apps, Combo("Ctrl+Alt+M")).Refused);
        Assert.True(editor.Assign(given.Settings, KeybindEditor.ModeNextId, Combo("Ctrl+Alt+Space")).Refused);
        Assert.True(editor.AssignPress(given.Settings, KeybindEditor.ModeNextId, Press('M', HotkeyModifiers.None)).Refused);

        // Empty is the default, and restoring all keys clears it.
        Assert.Null(Settings.Defaults.ModeKey);
        var all = editor.RestoreDefaults(given.Settings);
        Assert.Null(all.Settings.ModeKey);
        Assert.DoesNotContain(Combo("Ctrl+Alt+M"), registrar.Held);
    }

    [Fact]
    public void A_Scene_Key_Obeys_The_Same_Rules()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);
        var rest = KeybindEditor.SceneActionId("scene-1");
        var party = KeybindEditor.SceneActionId("scene-2");

        // A scene's key is a pick key under a prefixed id: it is kept, parsed back and found by the key listener.
        Assert.True(PickKeysJson.IsPickId(rest));
        var given = editor.AssignPress(settings, rest, Press('R', HotkeyModifiers.Control | HotkeyModifiers.Alt));
        Assert.Equal(Combo("Ctrl+Alt+R"), KeybindEditor.KeyOf(given.Settings, rest));
        Assert.Equal(rest, KeybindEditor.ActionFor(given.Settings, Combo("Ctrl+Alt+R")));
        Assert.True(KeybindEditor.IsSceneAction(rest));
        Assert.Equal("scene-1", KeybindEditor.SceneIdOf(rest));
        Assert.Equal(given.Settings, Settings.Parse(given.Settings.ToJson()).Settings);

        // Nobody else may take it, and it may not take what others hold: the main key, a page key, another scene's key.
        Assert.True(editor.Assign(given.Settings, party, Combo("Ctrl+Alt+R")).Refused);
        Assert.True(editor.Assign(given.Settings, PageIds.Apps, Combo("Ctrl+Alt+R")).Refused);
        Assert.True(editor.Assign(given.Settings, rest, Combo("Ctrl+Alt+Space")).Refused);
        Assert.True(editor.Assign(given.Settings, rest, Combo("Ctrl+Alt+1")).Refused);
        Assert.True(editor.AssignPress(given.Settings, rest, Press('R', HotkeyModifiers.None)).Refused);
        Assert.True(editor.AssignPress(given.Settings, rest, Press('R', HotkeyModifiers.Control, windowsKey: true)).Refused);

        // Windows holding it for another program refuses it too, and the old key stays.
        registrar.HeldByOthers.Add(Combo("Ctrl+Alt+T"));
        var refused = editor.Assign(given.Settings, rest, Combo("Ctrl+Alt+T"));
        Assert.True(refused.Refused);
        Assert.Contains(Combo("Ctrl+Alt+R"), registrar.Held);

        // Restoring all keys clears it, as it clears a pick's.
        var all = editor.RestoreDefaults(given.Settings);
        Assert.Null(KeybindEditor.KeyOf(all.Settings, rest));
        Assert.DoesNotContain(Combo("Ctrl+Alt+R"), registrar.Held);
    }

    [Fact]
    public void Deleting_A_Scene_Releases_Its_Key()
    {
        using var dir = new TempDir();
        var registrar = new FakeRegistrar();
        var files = SessionFixtures.Files(dir);
        var session = SessionFixtures.Open(files, registrar: registrar);

        Assert.True(session.CreateScene("Evening").Ok);
        var id = session.Scenes.Items[0].Id;
        var action = KeybindEditor.SceneActionId(id);
        Assert.True(session.PressKey(action, Press('E', HotkeyModifiers.Control | HotkeyModifiers.Alt)).Ok);
        Assert.Contains(Combo("Ctrl+Alt+E"), registrar.Held);
        Assert.Equal("Evening", session.ActionName(action));

        var result = session.DeleteScene(id);

        Assert.True(result.Ok);
        Assert.Empty(session.Scenes.Items);
        Assert.DoesNotContain(Combo("Ctrl+Alt+E"), registrar.Held); // given back to Windows at once
        Assert.Null(KeybindEditor.KeyOf(session.Settings, action));
        Assert.Empty(SceneStore.Load(files.ScenesPath!).Store.Items);
        Assert.Null(Settings.Load(files.SettingsPath, Pages.BuiltIn, bindIdleTime: false).Settings.PickKeyFor(action));

        // A key pressed for a scene that is gone is refused in plain words.
        Assert.False(session.PressKey(action, Press('E', HotkeyModifiers.Control | HotkeyModifiers.Alt)).Ok);
    }
}
