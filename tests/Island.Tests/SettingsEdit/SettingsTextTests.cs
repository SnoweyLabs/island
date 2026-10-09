using Island.Core;
using Island.Core.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.SettingsEdit;

public class SettingsTextTests
{
    [Fact]
    public void Private_Shortcut_Warning_Is_Present()
    {
        var text = SettingsText.PrivateShortcutWarning;

        // EVALS K9: one plain sentence that says what Windows cannot tell an app.
        Assert.Contains("Windows does not tell an app when another program uses the same keys privately as its own shortcut", text);
        Assert.Single(text.Split(". ", StringSplitOptions.RemoveEmptyEntries));
        Assert.EndsWith(".", text);
    }

    [Fact]
    public void Every_Refusal_Keeps_Its_Three_Parts()
    {
        var refusals = new[]
        {
            SettingsText.KeyHasWindowsKey,
            SettingsText.KeyUnknown,
            SettingsText.KeyUnsafe("because"),
            SettingsText.KeyTakenInside(Combo("Ctrl+Alt+1"), "Media", "Apps"),
            SettingsText.KeyTakenByAnotherProgram(Combo("Ctrl+Alt+1")),
            SettingsText.KeyRefusedByWindows(Combo("Ctrl+Alt+1"), 5),
            SettingsText.MainKeyCannotBeEmpty,
            SettingsText.KeyNotSaved,
        };

        Assert.All(refusals, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Code));
            Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened));
            Assert.False(string.IsNullOrWhiteSpace(r.Why));
            Assert.False(string.IsNullOrWhiteSpace(r.NextAction));
        });
        Assert.Equal(Refusals.HotkeyTaken.Code, SettingsText.KeyTakenByAnotherProgram(Combo("Ctrl+Alt+1")).Code);
    }

    [Fact]
    public void The_Digit_Hint_Follows_The_Page_Count_And_Stops_At_Nine()
    {
        Assert.Equal("Keys 1 to 5 change page while the island is open.", SettingsText.DigitHint(5));
        Assert.Equal("Keys 1 to 9 change page while the island is open.", SettingsText.DigitHint(12));
    }
}
