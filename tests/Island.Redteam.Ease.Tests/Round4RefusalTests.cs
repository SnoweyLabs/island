using System.IO;
using Island.Core;
using Island.Core.SettingsEdit;
using Island.Redteam.Ease.Tests.Harness;

namespace Island.Redteam.Ease.Tests;

/// <summary>
/// Round 4: the refusals and names that bf24c4d (code-3-5, code-3-8) wrote or changed, read as a person who just pressed Disconnect, or named a folder with a family emoji, would meet them. Run on Island.Core and
/// by reading the connectors (Island.App cannot be loaded by a test). Invented names only.
/// </summary>
public class Round4RefusalTests
{
    private static readonly string[] Hedges = ["may", "might", "usually", "often", "most likely", "for example", "such as", "perhaps", "can be"];

    private static string Between(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{from} was not found");
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"{to} was not found after {from}");
        return text[start..end];
    }

    /// <summary>Held: the two new refusals have three parts, each a sentence, and the Codex wording replaces "Claude Code" in them (so Codex's person is never told about Claude Code's file).</summary>
    [Fact]
    public void The_Two_New_Connector_Refusals_Have_Three_Sentences_And_Do_Not_Name_The_Wrong_Program_For_Codex()
    {
        foreach (var refusal in new[] { AgentRefusals.HooksFileNotWritten, AgentRefusals.NotifyNotCopied })
        {
            Assert.EndsWith(".", refusal.WhatHappened);
            Assert.EndsWith(".", refusal.Why);
            Assert.EndsWith(".", refusal.NextAction);
            Assert.False(string.IsNullOrWhiteSpace(refusal.Code));
        }

        var codex = Source.Read("src/Island.App/OutsideCodexSettings.cs");
        Assert.Contains("Replace(\"Claude Code\", \"Codex\")", codex);
    }

    /// <summary>
    /// ease-4-6 (LOW). Disconnect writes the settings file too, and when that fails it returns HOOKS_FILE_NOT_WRITTEN (bf24c4d): "Claude Code's settings file could not be written, so Island did not connect.
    /// ... then press Connect again." A person who pressed "Yes, disconnect" is told that Island did not connect and to press Connect, which is the opposite of what he is doing (the helper is still connected).
    /// The older refusal for a file that cannot be read has the same words in the same place. Expected: the refusal given by Disconnect does not say connect.
    /// </summary>
    [Fact]
    public void Defect_The_Refusal_Of_A_Failed_Disconnect_Says_Island_Did_Not_Connect_And_To_Press_Connect()
    {
        var disconnect = Between(Source.Read("src/Island.App/OutsideClaudeSettings.cs"), "public ConnectorResult Disconnect()", "/// <summary>The text of the file");
        Assert.Contains("HooksFileNotWritten", disconnect);
        var text = AgentRefusals.HooksFileNotWrittenForDisconnect; // repaired (ease-4-6): a failed Disconnect has its own words
        Assert.Contains("HooksFileNotWrittenForDisconnect", disconnect);
        Assert.DoesNotContain("did not connect", text.WhatHappened);
        Assert.DoesNotContain("Connect again", text.NextAction);
    }

    /// <summary>
    /// ease-4-7 (LOW). NOTIFY_NOT_COPIED states its cause as a fact ("A hook that is running right now holds the old copy") and tells the person to wait and press Connect again. <c>CopyRefusal</c> picks it for every
    /// failed copy as soon as Island.Notify is beside Island: a read-only or full folder, a permission refused, a file held by a virus scanner all end here, and no wait mends them. The sibling refusal for the settings
    /// file hedges ("may be read-only or held open"). Expected: the cause is said as a likely one, and the next step does not promise that waiting is enough.
    /// </summary>
    [Fact]
    public void Defect_The_Cause_Given_For_A_Failed_Copy_Of_Island_Notify_Is_Told_As_A_Fact_Whatever_Failed()
    {
        var why = AgentRefusals.NotifyNotCopied.Why;
        Assert.Contains(Hedges, h => why.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ease-4-8 (LOW). The repair of code-3-5 drops every character of category Format from a name taken from a file or folder: that includes the zero-width joiner, so a folder called with a family emoji (three
    /// people joined) becomes a pick of three separate people, and in Persian or Hindi the joiner that spells the word is cut out. Page and scene names keep a joiner that sits between two letters or emoji
    /// (round 2, round 3): two rules for one question. Expected: a joiner between two visible characters is kept in a pick's name as it is in a page's.
    /// </summary>
    [Fact]
    public void Defect_A_Folder_Named_With_A_Family_Emoji_Becomes_A_Pick_Of_Three_Separate_People()
    {
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        var context = new HandContext(@"Q:\Invented\Profile", []);
        var result = HandPicks.Folder(@"Q:\Invented\Profile\" + family + " Trips", PageIds.Folders, context);
        Assert.NotNull(result.Pick);
        Assert.Contains(family, result.Pick!.Name, StringComparison.Ordinal);
    }

    /// <summary>The premise of ease-4-8, held: a joiner between two letters is accepted in a page's name (so the two rules do disagree).</summary>
    [Fact]
    public void A_Page_Name_Keeps_A_Joiner_Between_Two_Emoji()
    {
        using var fixture = Fixture.Make(withHandAdded: false);
        Assert.True(fixture.Session.CreatePage("\U0001F468\u200D\U0001F469 Home", "#7CE04A").Ok);
        Assert.Contains("\u200D", fixture.Session.Pages.Pages[^1].Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// ease-4-9 (LOW). <c>RestoreAllKeys</c> forgets the keys kept for the picks switched off (<c>_keysOfSwitchedOff.Clear()</c>) BEFORE it knows that the restore worked. When the settings file cannot be
    /// saved the restore is refused and nothing is restored, and the kept keys are gone all the same: the person is told the keys were not restored, switches his pick on, and the key he set for it does not
    /// come back, with no word. Expected: the kept keys are forgotten only when the restore went through.
    /// </summary>
    [Fact]
    public void Defect_A_Restore_Of_The_Keys_That_Could_Not_Be_Saved_Forgets_The_Key_Kept_For_A_Pick_That_Is_Off()
    {
        using var fixture = Fixture.Make();
        var session = fixture.Session;
        var alpha = session.Picks.Picks.Single(p => p.Name == "Alpha");
        Assert.True(session.PressKey(alpha.Id, new KeyPress(0x4A, HotkeyModifiers.Control | HotkeyModifiers.Alt, false)).Changed);
        Assert.True(session.PressKey(session.Pages.Pages[^1].Id, new KeyPress(0x4B, HotkeyModifiers.Control | HotkeyModifiers.Alt, false)).Changed); // a key that a restore has to clear
        var page = session.Pages.Pages[^1].Id;
        session.SetPick(session.PickRows(page).Single(r => r.Pick.Id == alpha.Id), on: false);

        File.Delete(fixture.Files.SettingsPath);
        Directory.CreateDirectory(fixture.Files.SettingsPath); // the settings file cannot be saved now
        var restore = session.RestoreAllKeys();
        Assert.False(restore.Ok, "the premise: the restore is refused because the file could not be saved");
        Directory.Delete(fixture.Files.SettingsPath);

        var on = session.SetPick(session.PickRows(page).Single(r => r.Pick.Id == alpha.Id), on: true);

        Assert.True(on.Ok || on.Changed);
        Assert.NotNull(session.Settings.PickKeyFor(alpha.Id));
    }
}
