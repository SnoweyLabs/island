using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Tests.SettingsEdit;

/// <summary>A pretend Windows that keeps a log of what it was asked, in order, and can have combinations held by "another program".</summary>
internal sealed class FakeRegistrar : IHotkeyRegistrar
{
    public List<string> Log { get; } = [];

    public HashSet<HotkeyCombo> Held { get; } = [];

    public HashSet<HotkeyCombo> HeldByOthers { get; } = [];

    public int ErrorCode { get; set; } = 1409;

    public bool TryRegister(HotkeyCombo combo, out int error)
    {
        Log.Add($"register {combo}");
        error = 0;
        if (HeldByOthers.Contains(combo))
        {
            error = ErrorCode;
            return false;
        }

        Held.Add(combo);
        return true;
    }

    public void Release(HotkeyCombo combo)
    {
        Log.Add($"release {combo}");
        Held.Remove(combo);
    }
}

internal static class KeyFixtures
{
    public const int Q = 'Q';
    public const int Space = 0x20;
    public const int F5 = 0x74;

    public static KeyPress Press(int vk, HotkeyModifiers mods, bool windowsKey = false) => new(vk, mods, windowsKey);

    public static HotkeyCombo Combo(string text) => HotkeyCombo.Parse(text);

    public static KeybindEditor EditorFor(FakeRegistrar registrar, IEnumerable<Page>? pages = null)
    {
        var all = (pages ?? Pages.BuiltIn).ToList();
        return new KeybindEditor(registrar, id => id == KeybindEditor.MainId ? SettingsText.MainActionName : all.FirstOrDefault(p => p.Id == id)?.Name ?? (id.Contains(':') ? id.Split(':')[1] : id));
    }

    public const string Spotify = "program:spotify";
    public const string Notepad = "program:notepad";

    /// <summary>Settings with the main key and the Media page key set, and the registrar holding both.</summary>
    public static (Settings Settings, FakeRegistrar Registrar) WithKeys(string main = "Ctrl+Alt+Space", string media = "Ctrl+Alt+1")
    {
        var registrar = new FakeRegistrar();
        var settings = Settings.Defaults with { ShowHide = Combo(main) };
        settings = settings.WithPageKey(PageIds.Media, Combo(media));
        registrar.Held.Add(Combo(main));
        registrar.Held.Add(Combo(media));
        return (settings, registrar);
    }
}
