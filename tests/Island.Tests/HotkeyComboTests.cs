using Island.Core;

namespace Island.Tests;

public class HotkeyComboTests
{
    [Fact]
    public void Default_Combos_Parse()
    {
        var all = Settings.Defaults;
        Assert.Equal('Q', all.ShowHide.VirtualKey);
        Assert.Equal(HotkeyModifiers.Control, all.ShowHide.Modifiers);
        Assert.Equal("Ctrl+Q", all.ShowHide.ToString());
        Assert.All(Pages.BuiltIn, p => Assert.Null(all.KeyFor(p.Id)));
    }

    [Fact]
    public void Ctrl_Q_Is_Accepted()
    {
        Assert.True(HotkeyCombo.TryParse("Ctrl+Q", out var combo, out var error), error);
        Assert.Equal(HotkeyModifiers.Control, combo.Modifiers);
        Assert.Equal('Q', combo.VirtualKey);
        Assert.Equal(combo, HotkeyCombo.Parse("ctrl + q"));
        // A plain key, or Shift alone, would take over typing and is still refused.
        Assert.False(HotkeyCombo.TryParse("Q", out _, out _));
        Assert.False(HotkeyCombo.TryParse("Shift+Q", out _, out _));
    }

    [Theory]
    [InlineData("Win+1")]
    [InlineData("Ctrl+Win+Space")]
    [InlineData("Windows+Shift+A")]
    [InlineData("Super+Alt+X")]
    public void Windows_Key_Combos_Are_Rejected(string text)
    {
        Assert.False(HotkeyCombo.TryParse(text, out _, out var error));
        Assert.Contains("Windows", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl++A")]
    [InlineData("Ctrl+Banana")]
    public void Nonsense_Is_Rejected(string text)
    {
        Assert.False(HotkeyCombo.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parsing_Is_Case_And_Order_Insensitive_And_Round_Trips()
    {
        var a = HotkeyCombo.Parse("shift + alt + ctrl + f5");
        Assert.Equal("Ctrl+Alt+Shift+F5", a.ToString());
        Assert.Equal(a, HotkeyCombo.Parse(a.ToString()));
    }
}
