using System.Text;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: the texts and the rules that the repairs of round 3 (956ac18, bf24c4d) touched, tried by another input: the limit on a name, names that look alike, the new refusals of SetPick, the key that is
/// asked for while Enter is held. Run on Island.Core. Invented names only.
/// </summary>
public class Round4TextTests
{
    private static string Repeat(string unit, int times) => new StringBuilder().Insert(0, unit, times).ToString();

    private const string Flag = "\U0001F1F7\U0001F1F4"; // a flag drawn from two regional indicators: one thing to look at, two code points
    private const string ThumbsUpMedium = "\U0001F44D\U0001F3FD"; // a thumb with a skin tone: one thing to look at, two code points

    // ---- the limit on a name counts what a person sees ---------------------------------------------------------------------------------------

    /// <summary>Held: thirteen plain emoji (two UTF-16 units each) are one character each and pass the limit of 24 (the repair of code-3-4).</summary>
    [Fact]
    public void Thirteen_Plain_Emoji_Are_Accepted_As_A_Page_Name()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        Assert.True(fixture.Session.CreatePage(Repeat("\U0001F600", 13), "#7CE04A").Ok);
    }

    /// <summary>
    /// ease-4-2 (LOW). The repair of code-3-4 counts code points, so a flag or a thumb with a skin tone, each ONE thing to the eye, count two. Thirteen flags are refused with "A page name can have at most 24
    /// characters." to a person who typed thirteen: the text is false in the same way it was for thirteen plain emoji (the same defect by another input). The same holds for a scene name. Expected: the limit is
    /// counted in what a person sees (<c>StringInfo.LengthInTextElements</c>), or the text says what it counts.
    /// </summary>
    [Fact]
    public void Defect_Thirteen_Flags_Are_Refused_As_More_Than_Twenty_Four_Characters()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        var result = fixture.Session.CreatePage(Repeat(Flag, 13), "#7CE04A");
        Assert.True(result.Ok, result.Refusal);
    }

    [Fact]
    public void Defect_Thirteen_Thumbs_With_A_Skin_Tone_Are_Refused_As_A_Scene_Name_Of_More_Than_Twenty_Four_Characters()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        var result = fixture.Session.CreateScene(Repeat(ThumbsUpMedium, 13));
        Assert.True(result.Ok, result.Refusal);
    }

    // ---- names that look alike are two names -------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-4-3 (LOW). The repair of ease-3-11 strips the two joiners (U+200C, U+200D) before comparing names. Two other characters that draw nothing on their own and are let through because they are
    /// "marks" (category Mn) do the same trick: the variation selector U+FE0F after an emoji (text or picture form) and the combining grapheme joiner U+034F. "Wo" + U+034F + "rk" reads and looks as "Work" and
    /// is a second page; so is a heart and a heart with U+FE0F. Expected: SameName leaves out U+FE0E, U+FE0F and U+034F as well (the mark after an emoji is kept in the name that is stored).
    /// </summary>
    [Fact]
    public void Defect_A_Page_Named_With_A_Combining_Grapheme_Joiner_Is_The_Same_Page_As_The_Plain_Name()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        Assert.True(fixture.Session.CreatePage("Work", "#7CE04A").Ok);
        var second = fixture.Session.CreatePage("Wo\u034Frk", "#E0A04A");
        Assert.False(second.Ok, "a page that looks and reads as Work was made");
    }

    [Fact]
    public void Defect_A_Heart_And_A_Heart_With_The_Picture_Selector_Are_Two_Pages()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        Assert.True(fixture.Session.CreatePage("\u2764", "#7CE04A").Ok);
        var second = fixture.Session.CreatePage("\u2764\uFE0F", "#E0A04A");
        Assert.False(second.Ok, "a second heart that looks the same was made");
    }

    /// <summary>Held: the joiner of round 3 (ease-3-11) is refused as the plain name, with the existing page's own name told.</summary>
    [Fact]
    public void A_Joiner_Between_Letters_Is_Told_As_The_Page_That_Is_There()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        Assert.True(fixture.Session.CreatePage("Work", "#7CE04A").Ok);
        var second = fixture.Session.CreatePage("Wo\u200Drk", "#E0A04A");
        Assert.False(second.Ok);
        Assert.Contains("Work", second.Refusal);
        // the same for a scene
        Assert.True(fixture.Session.CreateScene("Mix").Ok);
        Assert.False(fixture.Session.CreateScene("M\u200Dix").Ok);
    }

    // ---- SetPick, the new refusal ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ease-4-4 (LOW). The refusal the repair of ease-3-14 gives ("X is already on the island under another name, so this one cannot be switched on again. Its key, if it had one, is gone with it.") says what
    /// happened and why, and nothing about what to do or where the thing is: the Add panel's refusal for the same case names the page ("X is already on the island, on the page Y."), and the person cannot
    /// look for a thing whose page he is not told. "Under another name" is also false when the other pick has the same name (a program added twice with the same name on another page). Expected: the page is named.
    /// </summary>
    [Fact]
    public void Defect_The_Refusal_For_A_Pick_That_Is_Already_On_The_Island_Does_Not_Say_On_Which_Page()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        session.Chooser = new PretendChooser(file: @"Q:\Invented\Profile\Apps\beta.exe");
        session.HandContextSource = () => new HandContext(@"Q:\Invented\Profile", []);
        var page = session.Pages.Pages[^1].Id;
        Assert.True(session.AddProgramByBrowsing(page).Added);
        var hand = session.Picks.Picks.Single(p => p.ExeName == "beta.exe");
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Id == hand.Id), on: false);
        var elsewhere = session.Pages.Pages[0].Id;
        Assert.True(session.AddProgramFromList(elsewhere, new InstalledProgram("Beta", "beta.exe", null, "launch-Beta")).Added);

        var result = session.SetPick(session.PickRows(page).Single(r => r.Pick.Id == hand.Id), on: true);

        Assert.NotNull(result.Refusal);
        Assert.Contains(session.Pages.ById(elsewhere)!.Name, result.Refusal);
    }

    // ---- the key that is asked for while Enter is held ---------------------------------------------------------------------------------------

    /// <summary>The premise of ease-4-5: a bare Enter is refused as a key (so the person who holds Enter a moment too long is told so at once, with the screen still waiting).</summary>
    [Fact]
    public void A_Bare_Enter_Is_Refused_As_A_Key()
    {
        var capture = KeybindEditor.Capture(new KeyPress(0x0D, 0, false));
        Assert.NotNull(capture.Refusal);
    }

    /// <summary>
    /// ease-4-5 (LOW). Enter on a key button starts the wait on its KeyDown (the template is a Button: Enter clicks on the way down). While the wait is on, <c>SettingsView.Capture</c> takes every key,
    /// including the auto-repeat of the Enter that is still held: a person who cannot lift the finger at once (a tremor, a slow hand: the people the keyboard is for) is shown the refusal "That combination
    /// cannot be used" before he has pressed any key at all. Expected: a repeat of the key that started the wait is ignored (<c>e.IsRepeat</c>).
    /// </summary>
    [Fact]
    public void Defect_The_Repeat_Of_The_Enter_That_Started_The_Wait_For_A_Key_Is_Taken_As_The_Key()
    {
        var capture = Source.Read("src/Island.SettingsUi/SettingsView.cs");
        var start = capture.IndexOf("protected override void OnPreviewKeyDown", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = capture.IndexOf("// ---- Finding things", start, StringComparison.Ordinal);
        Assert.Contains("IsRepeat", capture[start..end]);
    }
}
