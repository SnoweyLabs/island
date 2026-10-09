using System.Text.RegularExpressions;

namespace Island.Attack9.Tests;

/// <summary>
/// ATTACK9 on the guard GuardTests.Addon_Log_Never_Names_A_Browser_Or_A_Tab: the same rules applied to text that does what the guard exists to forbid. The first
/// build of the guard read a log hook and let three spellings through (Log!.Invoke, an alias of the hook, DynamicInvoke); version 2 of the work order has the bridge
/// raise events, and the guard was rewritten to count where the event may be named, raised and handed on. The Defect_ tests below pass since.
/// </summary>
public class LogGuardAttackTests
{
    private static string StripComments(string text) => Regex.Replace(Regex.Replace(text, @"/\*[\s\S]*?\*/", ""), @"^\s*//.*$", "", RegexOptions.Multiline);

    /// <summary>True when the guard (its checks on TabBridge.cs, as written in GuardTests.cs) fails on this text.</summary>
    private static bool GuardFails(string source)
    {
        var bridge = StripComments(source);
        var raised = Regex.Matches(bridge, @"\bRaise\(([^;]*)\)\s*;").Select(m => m.Groups[1].Value.Trim()).Where(a => !a.StartsWith("AddonEventKind kind", StringComparison.Ordinal));
        if (raised.Any(a => !Regex.IsMatch(a, @"^AddonEventKind\.\w+(\s*,\s*(\d+|refused))?$"))) return true;
        if (Regex.Matches(bridge, @"new\s+AddonEvent\s*\(").Count != 1) return true;
        if (Regex.Matches(bridge, @"\(\(Action<AddonEvent>\)handler\)\(").Count != 1) return true;
        return Regex.Matches(bridge, @"\bAddonHappened\b").Count != 2; // the declaration and the one place that walks its handlers
    }

    private static string Bridge() => File.ReadAllText(Path.Combine(BridgeAttackTests.FindRoot(), "src", "Island.Bridge", "TabBridge.cs"));

    [Fact]
    public void Holds_The_Replica_Is_The_Guard_And_The_Real_Bridge_Passes_It()
    {
        var guard = File.ReadAllText(Path.Combine(BridgeAttackTests.FindRoot(), "tests", "Island.Tests", "GuardTests.cs"));
        Assert.Contains(@"\bRaise\(([^;]*)\)\s*;", guard, StringComparison.Ordinal);
        Assert.Contains(@"^AddonEventKind\.\w+(\s*,\s*(\d+|refused))?$", guard, StringComparison.Ordinal);
        Assert.Contains(@"\(\(Action<AddonEvent>\)handler\)\(", guard, StringComparison.Ordinal);
        Assert.Contains(@"\bAddonHappened\b", guard, StringComparison.Ordinal);
        Assert.False(GuardFails(Bridge()));
    }

    [Theory]
    [InlineData("Raise(AddonEventKind.Connected + h.Profile);")]
    [InlineData("Raise(tab.Title);")]
    [InlineData("Raise(\n    $\"{h.Browser}\");")]
    [InlineData("Raise(AddonEventKind.Refused, h.Profile.Length);")]
    [InlineData("var e = new AddonEvent(AddonEventKind.Left, tab.Title.Length);")]
    public void Holds_The_Guard_Catches_The_Plain_Ways_To_Put_A_Name_In_The_Log(string line) =>
        Assert.True(GuardFails(Bridge() + "\n" + line));

    [Fact]
    public void Defect_The_Log_Guard_Misses_A_Null_Forgiving_Invoke()
    {
        Assert.True(GuardFails(Bridge() + "\nAddonHappened!.Invoke(new AddonEvent(AddonEventKind.Left, tab.Title.Length));"), "the guard lets a tab's title reach the event through a null-forgiving invoke");
    }

    [Fact]
    public void Defect_The_Log_Guard_Misses_The_Hook_Called_Through_A_Local_Alias()
    {
        Assert.True(GuardFails(Bridge() + "\nvar write = AddonHappened;\nwrite?.Invoke(link.Profile);"), "the guard lets a profile reach the log through an alias of the event");
    }

    [Fact]
    public void Defect_The_Log_Guard_Misses_A_Dynamic_Invoke()
    {
        Assert.True(GuardFails(Bridge() + "\nAddonHappened?.DynamicInvoke(tab.Title);"), "the guard lets a title reach the log through DynamicInvoke");
    }
}
