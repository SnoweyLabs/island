using Island.Core;

namespace Island.Tests;

/// <summary>WORK-ORDER-11 section 4, step 1: what the work wrote into review/perf.md is not lost when the ordinary self-test rewrites that file.</summary>
public class PerfTableTests
{
    private const string Rewritten = "# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 1 MB |\n";

    private const string Before = "## What the open island costs (WORK-ORDER-11 §4) — before\n\n| Page | Mode | % |\n|---|---|---|\n| Apps | Vibe | 60 |\n";
    private const string Causes = "## What the open island costs (WORK-ORDER-11 §4) — where it goes\n\n1. every layer is drawn again on every frame: fixed\n";
    private const string After = "## What the open island costs (WORK-ORDER-11 §4) — after\n\n| Page | Mode | % |\n|---|---|---|\n| Apps | Vibe | 9 |\n";

    private static string Earlier => Rewritten.TrimEnd('\n') + "\n\n" + PerfSections.HiddenCost + "\n\nhidden: 0.9%\n\n" + Before + "\n" + Causes + "\n" + After;

    [Fact]
    public void What_Section_Four_Wrote_Is_Kept_When_Perf_Md_Is_Rewritten()
    {
        var text = PerfSections.WithKept("# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 2 MB |\n", Earlier);

        Assert.Contains("| Memory | 2 MB |", text); // the new rewrite
        Assert.DoesNotContain("| Memory | 1 MB |", text);
        Assert.Contains(PerfSections.HiddenCost, text); // the hidden cost of WORK-ORDER-6 stays
        Assert.Contains("hidden: 0.9%", text);
        Assert.Contains(Before.TrimEnd('\n'), text); // and every heading of section 4: before, where it goes, after
        Assert.Contains(Causes.TrimEnd('\n'), text);
        Assert.Contains(After.TrimEnd('\n'), text);
        Assert.Equal(4, PerfSections.Kept(text).Count);

        // A second rewrite changes nothing more.
        Assert.Equal(text, PerfSections.WithKept("# Performance records — Island\n\n| Measure | Value |\n|---|---|\n| Memory | 2 MB |\n", text));
    }

    [Fact]
    public void A_File_With_Nothing_To_Keep_Is_Just_The_Rewrite()
    {
        Assert.Equal(Rewritten, PerfSections.WithKept(Rewritten, null));
        Assert.Equal(Rewritten, PerfSections.WithKept(Rewritten, "# something else\n\n## Another heading\n\ntext\n"));
        Assert.Empty(PerfSections.Kept(""));
    }

    [Fact]
    public void A_Section_Ends_At_The_Next_Heading_And_Windows_Line_Endings_Are_Read()
    {
        var earlier = Earlier.Replace("\n", "\r\n") + "\r\n## A heading of someone else\r\n\r\nnot ours\r\n";
        var kept = PerfSections.Kept(earlier);

        Assert.Equal(4, kept.Count);
        Assert.DoesNotContain(kept, k => k.Contains("not ours"));
        Assert.All(kept, k => Assert.DoesNotContain('\r', k));
    }

    [Fact]
    public void Upsert_Replaces_A_Section_Of_The_Same_Heading_And_Otherwise_Adds_It()
    {
        var replaced = PerfSections.Upsert(Earlier, After.Replace("| Apps | Vibe | 9 |", "| Apps | Vibe | 7 |"));
        Assert.Contains("| Apps | Vibe | 7 |", replaced);
        Assert.DoesNotContain("| Apps | Vibe | 9 |", replaced);
        Assert.Equal(1, replaced.Split(PerfSections.OpenCostPrefix).Length - 1 - 2); // before, where it goes and after: three headings, none doubled

        var added = PerfSections.Upsert(Rewritten, Before);
        Assert.EndsWith(Before, added);
        Assert.StartsWith("# Performance records", added);
    }
}
