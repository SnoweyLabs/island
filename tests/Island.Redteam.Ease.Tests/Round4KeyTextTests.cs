using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: the refusals of a key, read for what is true when the thing had no key before. Since 6 Oct 2026 no page and no pick has a key by default, so the first key a person gives to anything is the
/// ordinary case, and the refusals all say "Your old key still works". Run on the editor of Island.Core; invented ids only.
/// </summary>
public class Round4KeyTextTests
{
    private sealed class Refusing(int error) : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int e)
        {
            e = error;
            return false;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }

    private static readonly HotkeyCombo Combo = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4A);

    private static Settings WithOnePageWithoutAKey() => Settings.Defaults;

    private static string PageIdWithoutKey(Settings settings)
    {
        var page = Pages.BuiltIn[0].Id;
        Assert.Null(KeybindEditor.KeyOf(settings, page));
        return page;
    }

    /// <summary>The premise, held: a page has no key by default, so the first key given to it is the ordinary case.</summary>
    [Fact]
    public void A_Page_Has_No_Key_Until_The_Person_Gives_It_One()
    {
        PageIdWithoutKey(WithOnePageWithoutAKey());
    }

    /// <summary>
    /// ease-4-10 (LOW). The three refusals of a key that Windows or another program holds, or that cannot be saved, end "Your old key still works" (or say it in the second part). For a page or a pick that had no
    /// key, which is where every page and pick starts, there is no old key: the sentence is false in the commonest case of pressing a first key that is taken. Expected: the sentence is told only when there was an
    /// old key (<c>KeybindEditor.Assign</c> knows <c>old</c>), or the refusal says "Nothing was changed".
    /// </summary>
    [Fact]
    public void Defect_A_First_Key_That_Another_Program_Holds_Is_Refused_With_Your_Old_Key_Still_Works()
    {
        var settings = WithOnePageWithoutAKey();
        var page = PageIdWithoutKey(settings);
        var editor = new KeybindEditor(new Refusing(1409), id => id);
        var change = editor.Assign(settings, page, Combo);
        Assert.True(change.Refused);
        Assert.DoesNotContain("old key", change.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defect_A_First_Key_That_Windows_Refuses_Is_Told_Your_Old_Key_Still_Works()
    {
        var settings = WithOnePageWithoutAKey();
        var page = PageIdWithoutKey(settings);
        var change = new KeybindEditor(new Refusing(5), id => id).Assign(settings, page, Combo);
        Assert.True(change.Refused);
        Assert.DoesNotContain("old key", change.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Defect_A_First_Key_That_Cannot_Be_Saved_Is_Told_Your_Old_Key_Still_Works()
    {
        var settings = WithOnePageWithoutAKey();
        var page = PageIdWithoutKey(settings);
        var accepting = new Accepting();
        var change = new KeybindEditor(accepting, id => id).Assign(settings, page, Combo, save: _ => false);
        Assert.True(change.Refused);
        Assert.DoesNotContain("old key", change.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Held: for the main key, which always has an old one, the same sentence is true (the main key is never empty).</summary>
    [Fact]
    public void For_The_Main_Key_The_Old_Key_Does_Still_Work_And_The_Text_Is_True()
    {
        var settings = Settings.Defaults;
        Assert.NotNull(KeybindEditor.KeyOf(settings, KeybindEditor.MainId));
        var change = new KeybindEditor(new Refusing(1409), id => id).Assign(settings, KeybindEditor.MainId, Combo);
        Assert.True(change.Refused);
        Assert.Contains("old key", change.Refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Accepting : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }
}
