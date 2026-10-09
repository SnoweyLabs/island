using System.Text.RegularExpressions;
using Island.Core;
using Island.Core.Agents.Sessions;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: the same rule written in two places. A number in a text on screen, in a refusal or in a document against the constant it speaks of; the limits of the two
/// wires and the command line; the light's numbers against the look's. Nothing here changes a text: a test that fails names a place that disagrees.
/// </summary>
public class DuplicatedRuleTests
{
    [Fact]
    public void The_Numbers_In_The_Words_Of_The_Settings_Screen_Are_The_Constants_They_Speak_Of()
    {
        Assert.Contains($"{Settings.MinIdleSeconds}\\u00A0to\\u00A0{Settings.MaxSetIdleSeconds}\\u00A0seconds", Repo.Text("src", "Island.SettingsUi", "GeneralSection.cs")); // no-break spaces since WORK-ORDER-13 (Dan's P22), written as escapes
        Assert.Contains($"({NoticeQueue.MinSeconds} to {NoticeQueue.MaxSeconds})", Repo.Text("src", "Island.SettingsUi", "GeneralSection.cs"));
        Assert.Contains($"{NoticeQueue.MinSeconds} to {NoticeQueue.MaxSeconds}", Repo.Text("src", "Island.Core", "Settings.cs"));
        Assert.Equal(9, PageStore.MaxPages);
        Assert.Contains("nine pages", SettingsText.PageLimit);
        Assert.Equal(6, Pages.BuiltIn.Count);
        Assert.Contains("six pages", SettingsText.BuiltInPageCannotBeDeleted);
        Assert.Equal($"There can be at most {Scenes.MaxScenes} scenes.", SceneText.SceneLimit);
        Assert.Equal($"A scene can hold at most {Scenes.MaxThingsPerScene} things.", SceneText.ThingLimit);
        Assert.Equal(Scenes.MaxNameLength, int.Parse(Regex.Match(SceneText.NameTooLong, @"\d+").Value));
        Assert.Equal(SettingsText.MaxPageNameLength, int.Parse(Regex.Match(SettingsText.PageNameTooLong, @"\d+").Value));
        Assert.Equal(ModeSettingsJson.MaxNeverOver, 200);
    }

    [Fact]
    public void The_Digit_Keys_And_The_Pages_Agree_Whatever_The_Number_Of_Pages()
    {
        Assert.Equal(PageStore.MaxPages, 9);
        Assert.Equal("Key 1 changes page while the island is open.", SettingsText.DigitHint(1));
        Assert.Equal("Keys 1 to 9 change page while the island is open.", SettingsText.DigitHint(9));
        Assert.Equal("Keys 1 to 9 change page while the island is open.", SettingsText.DigitHint(40));
        var machine = new IslandMachine(5, [.. Enumerable.Range(0, 12).Select(i => new Page("p" + i, "P" + i, "#112233", "dot", null, false))]);
        Assert.Equal(9, machine.DigitKeyCount);
        Assert.Equal("p8", machine.PageIdAt(8));
    }

    [Fact]
    public void The_Limits_Of_The_Two_Wires_And_The_Command_Line_Do_Not_Cut_What_The_Other_Side_Sends()
    {
        // The command line's --event goes into version 2's "e"; --agent into "a"; the hook's own fields are cut by the sender before they are written.
        Assert.True(NotifyArguments.MaxEventChars <= SessionLimits.MaxEventChars);
        Assert.True(NotifyArguments.MaxAgentChars <= SessionLimits.MaxHelperChars);
        Assert.Equal(AgentPipe.MaxChain, SessionLimits.MaxChain);
        Assert.Equal(AgentPipe.MaxMessageBytes, SessionLimits.MaxMessageBytes);
        Assert.Equal(AgentPipe.MaxFolderChars, SessionLimits.MaxFolderChars);
        // version 1 carries an event of at most 32 characters, version 2 of 48: a 40-character event is legal in one wire and cut in the other (version 1 is Claude Code's own and its longest is 18)
        Assert.True(AgentPipe.MaxEventChars < SessionLimits.MaxEventChars);
        // the stdin limit is a length of bytes and the message limit a length of bytes: a full hook input never fits a message, only its first fields do
        Assert.True(AgentPipe.StdinLimitBytes > AgentPipe.MaxMessageBytes);
    }

    [Fact]
    public void The_Lights_Numbers_Are_The_Looks_Numbers_And_The_Old_Drawing_Uses_The_Same_Constants()
    {
        var spec = LightSpec.For(Rgb.FromHex(LookConstants.AppsColor), ModeMark.Look.Approved)!;
        Assert.Equal(LookConstants.RimInset, spec.Inset);
        Assert.Equal((LookConstants.BaseRimWidth, LookConstants.BaseRimAlpha), (spec.BaseRim!.Value.Width, spec.BaseRim.Value.Alpha));
        Assert.Equal((LookConstants.ArcWidth, LookConstants.ArcFraction), (spec.FirstArc!.Value.Stroke.Width, spec.FirstArc.Value.Fraction));
        Assert.Equal((LookConstants.SecondArcWidth, LookConstants.SecondArcFraction, LookConstants.SecondArcPhase, LookConstants.SecondArcAlpha),
            (spec.SecondArc!.Value.Stroke.Width, spec.SecondArc.Value.Fraction, spec.SecondArc.Value.EndAheadOfHead, spec.SecondArc.Value.Stroke.Alpha));
        Assert.Equal((LookConstants.BloomBaseWidth, LookConstants.BloomBaseAlpha), (spec.BloomRing.Width, spec.BloomRing.Alpha));
        Assert.Equal(LookConstants.BloomArcWidth, spec.BloomArc!.Value.Stroke.Width);
        Assert.Equal(LookConstants.BloomBlurCss, spec.BloomBlurSigma);
        Assert.Equal(LookConstants.BloomLayerAlpha, spec.GlowOpacity);
        Assert.Equal(LookConstants.ArcSpeedPerSecond, spec.SpeedPerSecond);
        Assert.Equal(1 / LookConstants.ArcSpeedPerSecond, spec.LapSeconds, 12);
        // the old drawing's pens, read from its source: the same names
        var layers = Repo.Text("src", "Island.App", "Visuals", "Layers.cs");
        foreach (var name in new[] { "BaseRimWidth", "BaseRimAlpha", "ArcWidth", "ArcFraction", "SecondArcWidth", "SecondArcFraction", "SecondArcPhase", "SecondArcAlpha", "BloomBaseWidth", "BloomBaseAlpha", "BloomArcWidth", "BloomBlurCss", "BloomLayerAlpha", "RimInset" })
            Assert.Contains("LookConstants." + name, layers);
    }

    [Fact] // made true in WORK-ORDER-13 (Dan said yes to it)
    public void Defect_The_Old_Rim_Has_A_Blur_Of_Its_Own_Which_The_Lights_Numbers_Do_Not_Carry()
    {
        // code-1-15 (LOW, a look: needs a person; written as a source fact): RimLayer's constructor sets Effect = Units.Blur(Units.RadiusForFilterBlur(LookConstants.FrontRimBlurCss)): the whole old rim,
        // the base line and both arcs, is drawn with a 0.6 px blur (the approved reference's own softening). LightSpec / LightSpec.For (whose text says "the numbers are the ones the old drawing
        // gives its pens") has no such number and MovingLight draws the rim sharp; LightStage compares only the pens. So with the graphics-card light the rim is a little sharper than "As
        // before" (and the doc says the lights are compared by their numbers). Expected: the number is carried (and the compositor given an equivalent) or the difference is recorded for Dan
        // under OWNER DECISIONS. The test fails until LightSpec names the constant.
        Assert.Contains("FrontRimBlurCss", Repo.Text("src", "Island.App", "Visuals", "Layers.cs"));
        Assert.Contains("FrontRimBlurCss", Repo.Text("src", "Island.Core", "Light", "LightSpec.cs"));
    }

    [Fact]
    public void The_Refusals_Of_The_Register_All_Have_Three_Parts_And_Fit_A_Balloon()
    {
        foreach (var r in Refusals.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.WhatHappened) || string.IsNullOrWhiteSpace(r.Why) || string.IsNullOrWhiteSpace(r.NextAction), r.Code);
            if (r.Code is not ("HOTKEY_TAKEN" or "CLOSE_NEEDS_ADMIN")) Assert.True(r.Message.Length <= 255, $"{r.Code}: {r.Message.Length} characters");
        }

        Assert.Equal(Refusals.All.Count, Refusals.All.Select(r => r.Code).Distinct().Count());
    }

}
