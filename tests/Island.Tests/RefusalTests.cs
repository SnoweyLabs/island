using Island.Core;

namespace Island.Tests;

public class RefusalTests
{
    [Fact]
    public void Every_Refusal_Names_A_Next_Action()
    {
        Assert.Equal(["HOTKEY_TAKEN", "SETTINGS_UNREADABLE", "SETTINGS_FROM_NEWER_VERSION", "ALREADY_RUNNING", "BLUR_UNAVAILABLE", "STARTUP_PATH_TOO_LONG", "STARTUP_OFF_IN_WINDOWS", "CLOSE_NEEDS_ADMIN", "HOOKS_FILE_UNREADABLE", "SCENE_PART_MISSING", "PICK_TARGET_MISSING", "NOT_A_SITE", "TERMINALS_PAGE_NO_ROOM"], Refusals.All.Select(r => r.Code).ToArray());
        foreach (var r in Refusals.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened), r.Code);
            Assert.False(string.IsNullOrWhiteSpace(r.Why), r.Code);
            Assert.False(string.IsNullOrWhiteSpace(r.NextAction), r.Code);
            Assert.Contains(r.NextAction, r.Message);
        }

        Assert.StartsWith("Pick another combination", Refusals.HotkeyTaken.NextAction);
        Assert.StartsWith("Close Island, delete settings.json", Refusals.SettingsUnreadable.NextAction); // WORK-ORDER-13 (Dan's P12): it said "Fix the file, or delete it"
        Assert.StartsWith("Use the tray icon", Refusals.AlreadyRunning.NextAction);
        Assert.StartsWith("Turn on Transparency effects", Refusals.BlurUnavailable.NextAction);
        Assert.StartsWith("Close it from its own window", CloseRefusals.NeedsAdmin.NextAction);
    }

    [Fact]
    public void No_Refusal_Looks_Like_A_Stack_Trace()
    {
        // HotkeyTaken and the close refusal are templates; what the user sees is the filled-in version.
        var messages = Refusals.All.Where(r => r.Code is not ("HOTKEY_TAKEN" or "CLOSE_NEEDS_ADMIN")).Select(r => r.Message)
            .Append(Refusals.ForHotkeyTaken(HotkeyCombo.Parse("Ctrl+Alt+Shift+1"), "Media").Message)
            .Append(CloseRefusals.ForName("Notepad").Message);
        foreach (var m in messages)
        {
            Assert.DoesNotContain("Exception", m);
            Assert.DoesNotContain("   at ", m);
            Assert.DoesNotContain("StackTrace", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".cs:line", m);
            Assert.DoesNotContain("0x", m);
            Assert.DoesNotContain("{", m);
        }
    }

    [Fact]
    public void Hotkey_Taken_Names_The_Combo_And_The_Category()
    {
        var m = Refusals.ForHotkeyTaken(HotkeyCombo.Parse("Ctrl+Alt+Shift+2"), "Folders").Message;
        Assert.Contains("Ctrl+Alt+Shift+2", m);
        Assert.Contains("Folders has no keybind", m);
    }

    [Fact]
    public void A_Notify_Path_The_Hook_Cannot_Hold_Is_Told_With_Its_Three_Parts_And_Not_As_An_Unreadable_File()
    {
        var r = AgentRefusals.NotifyPathInvalid;
        Assert.Equal(HookInstaller.NotifyPathInvalid, r.Code);
        Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened) || string.IsNullOrWhiteSpace(r.Why) || string.IsNullOrWhiteSpace(r.NextAction));
        Assert.DoesNotContain("could not be read", r.Message);
        Assert.True(r.Message.Length <= 255, "a balloon holds 255 characters");
    }
}
