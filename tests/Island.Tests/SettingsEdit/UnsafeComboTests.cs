using Island.Core;
using Island.Core.SettingsEdit;
using static Island.Tests.SettingsEdit.KeyFixtures;

namespace Island.Tests.SettingsEdit;

/// <summary>EVALS K7: one case per kind of unsafe combination, each refused with a message that says why.</summary>
public class UnsafeComboTests
{
    private const HotkeyModifiers Ctrl = HotkeyModifiers.Control;
    private const HotkeyModifiers Alt = HotkeyModifiers.Alt;
    private const HotkeyModifiers Shift = HotkeyModifiers.Shift;

    public static TheoryData<string, KeyPress, string> Cases => new()
    {
        { "windows key held", Press('Q', Ctrl, windowsKey: true), "Windows" },
        { "windows key pressed", Press(0x5B, HotkeyModifiers.None), "Windows" },
        { "plain key", Press('Q', HotkeyModifiers.None), "needs at least one of Ctrl, Alt or Shift" },
        { "plain function key", Press(0x74, HotkeyModifiers.None), "needs at least one of Ctrl, Alt or Shift" },
        { "shift alone", Press('Q', Shift), "Shift alone" },
        { "alt f4", Press(0x73, Alt), "Windows keeps for itself" },
        { "alt tab", Press(0x09, Alt), "Windows keeps for itself" },
        { "ctrl esc", Press(0x1B, Ctrl), "Windows keeps for itself" },
        { "ctrl shift esc", Press(0x1B, Ctrl | Shift), "Windows keeps for itself" },
        { "ctrl alt delete", Press(0x2E, Ctrl | Alt), "Windows keeps for itself" },
        { "copy", Press('C', Ctrl), "copy, paste" },
        { "paste", Press('V', Ctrl), "copy, paste" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Unsafe_Combos_Are_Rejected(string kind, KeyPress press, string reasonPart)
    {
        var capture = KeybindEditor.Capture(press);

        Assert.True(capture.Combo is null, $"{kind} must not become a combination");
        Assert.NotNull(capture.Refusal);
        Assert.Contains(reasonPart, capture.Refusal!.Message);
        Assert.False(string.IsNullOrWhiteSpace(capture.Refusal.Why));
        Assert.False(string.IsNullOrWhiteSpace(capture.Refusal.NextAction));
    }

    [Fact]
    public void A_Refused_Press_Is_Not_Registered_By_The_Editor_Either()
    {
        var (settings, registrar) = WithKeys();
        var editor = EditorFor(registrar);

        var results = Cases.Select(c => editor.AssignPress(settings, KeybindEditor.MainId, (KeyPress)c[1]!)).ToList();

        Assert.All(results, r => Assert.True(r.Refused));
        Assert.All(results, r => Assert.Equal(settings, r.Settings));
        Assert.Empty(registrar.Log);
    }
}
