using System.Text.RegularExpressions;

namespace Island.Tests.Redteam;

/// <summary>
/// WORK-ORDER-12 section 5: <c>review/coverage.md</c>, the table of every promise and its proof. A unit test named there as <c>`Project` `Class.Method`</c> must be in a test project; a self-test check named
/// there as <c>check "text" (a stage file)</c> must have its text in that stage's source; no row may be left without a proof. A table that is not there yet passes.
/// </summary>
public class CoverageTests
{
    private static string? CoveragePath => RepoPaths.File("review", "coverage.md") is { } p && File.Exists(p) ? p : null;

    [Fact]
    public void Every_Proof_Named_In_The_Coverage_Table_Exists()
    {
        if (CoveragePath is not { } path) return; // not written yet

        var text = File.ReadAllText(path);
        var problems = new List<string>();
        var units = 0;
        var checks = 0;

        foreach (Match m in Regex.Matches(text, @"`(Island\.[\w.]*Tests(?:\.Ui)?)`\s*/?\s*`(\w+)\.(\w+)`"))
        {
            units++;
            if (!RegisterTests.TestExists(m.Groups[2].Value, m.Groups[3].Value)) problems.Add($"{m.Groups[2].Value}.{m.Groups[3].Value} is named for {m.Groups[1].Value} and is not in any test project");
        }

        foreach (Match m in Regex.Matches(text, "check \"([^\"]+)\" \\((\\w+Stage\\.cs)\\)"))
        {
            checks++;
            var file = RepoPaths.File("src", "Island.App", m.Groups[2].Value);
            if (!File.Exists(file)) { problems.Add($"{m.Groups[2].Value} does not exist"); continue; }
            if (!File.ReadAllText(file).Contains(m.Groups[1].Value, StringComparison.Ordinal)) problems.Add($"the check \"{m.Groups[1].Value}\" is not in {m.Groups[2].Value}");
        }

        Assert.True(units > 40 && checks > 30, $"the table names {units} unit tests and {checks} self-test checks: it should name many more");
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void No_Row_Of_The_Coverage_Table_Is_Left_Without_A_Proof()
    {
        if (CoveragePath is not { } path) return;

        var rows = File.ReadAllLines(path)
            .Where(l => l.TrimStart().StartsWith('|') && l.TrimEnd().EndsWith('|'))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim().Trim('*').Trim('`').Trim()).ToArray())
            .ToList();
        var bare = rows.Where(r => r.Any(c => c == "NO PROOF")).Select(r => r[0]).ToList();
        Assert.Empty(bare);
        Assert.True(rows.Count > 400, $"{rows.Count} rows read: the table is shorter than it should be");
    }

    [Theory]
    [InlineData("`Island.Tests` `GuardTests.No_Account_Name_In_Source`", 1)]
    [InlineData("`Island.Tests` / `SpringTests.A` and `Island.Attack7B.Tests` `PipeAttackTests.B`", 2)]
    [InlineData("a check in ShellStage.cs", 0)]
    public void A_Unit_Test_Is_Read_From_A_Row_As_A_Project_And_A_Class_And_A_Method(string row, int expected) =>
        Assert.Equal(expected, Regex.Matches(row, @"`(Island\.[\w.]*Tests(?:\.Ui)?)`\s*/?\s*`(\w+)\.(\w+)`").Count);
}
