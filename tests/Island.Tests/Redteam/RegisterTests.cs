using System.Text.RegularExpressions;

namespace Island.Tests.Redteam;

/// <summary>
/// WORK-ORDER-12 section 4: the register of what the three attackers found (<c>review/redteam/REGISTER.md</c>). Only rows marked <c>repaired</c> are read, and only for one thing: that the test
/// the row names is there. A row names a test as <c>Class.Method</c> (or <c>Project/Class.Method</c>, several separated by commas or semicolons; backticks are ignored). A register that
/// is not there yet passes.
/// </summary>
public class RegisterTests
{
    private static string? RegisterPath => RepoPaths.File("review", "redteam", "REGISTER.md") is { } p && File.Exists(p) ? p : null;

    /// <summary>The rows of the register's tables: the cells of each line that starts with a bar, without the header and the rule under it.</summary>
    internal static IEnumerable<string[]> Rows(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim();
            if (!t.StartsWith('|') || !t.EndsWith('|')) continue;
            var cells = t.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.All(c => Regex.IsMatch(c, @"^:?-{2,}:?$"))) continue; // the rule under a header
            yield return cells;
        }
    }

    /// <summary>All the test names (<c>Class.Method</c>, optionally behind a project and a slash) written in a cell.</summary>
    internal static IReadOnlyList<(string Class, string Method)> TestNames(string cell) =>
        [.. Regex.Matches(cell.Replace("`", ""), @"(?:[\w.]+/)?(?<class>[A-Za-z_]\w*)\.(?<method>[A-Za-z_]\w*)").Select(m => (m.Groups["class"].Value, m.Groups["method"].Value))];

    /// <summary>True when a file under tests/ declares the class and the method.</summary>
    internal static bool TestExists(string className, string method)
    {
        var root = RepoPaths.File("tests");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var code = File.ReadAllText(file);
            if (Regex.IsMatch(code, $@"\bclass\s+{Regex.Escape(className)}\b") && Regex.IsMatch(code, $@"\b{Regex.Escape(method)}\s*\(")) return true;
        }

        return false;
    }

    [Fact]
    public void Every_Repaired_Row_Names_A_Test_That_Exists()
    {
        if (RegisterPath is not { } path) return; // not written yet

        var text = File.ReadAllText(path);
        var header = Rows(text).FirstOrDefault(r => r.Any(c => c.Equals("state", StringComparison.OrdinalIgnoreCase)));
        if (header is null) return;
        var stateAt = Array.FindIndex(header, c => c.Equals("state", StringComparison.OrdinalIgnoreCase));
        var testAt = Array.FindIndex(header, c => c.StartsWith("test", StringComparison.OrdinalIgnoreCase));
        Assert.True(testAt >= 0, "the register has a state column and no test column");

        var problems = new List<string>();
        foreach (var row in Rows(text))
        {
            if (row.Length <= Math.Max(stateAt, testAt) || row == header) continue;
            if (!row[stateAt].StartsWith("repaired", StringComparison.OrdinalIgnoreCase)) continue;
            var names = TestNames(row[testAt]);
            if (names.Count == 0) problems.Add($"{row[0]}: a repaired row names no test");
            foreach (var (cls, method) in names)
                if (!TestExists(cls, method)) problems.Add($"{row[0]}: {cls}.{method} is not in any test project");
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Theory]
    [InlineData("`HelperAttackTests.Defect_Two_Helpers`, Island.Attack11.Tests/PagesAttackTests.Holds_X", 2)]
    [InlineData("none", 0)]
    [InlineData("A.B; C.D", 2)]
    public void A_Cell_Is_Read_For_Its_Test_Names(string cell, int count) => Assert.Equal(count, TestNames(cell).Count);

    [Fact]
    public void A_Test_That_Is_There_Is_Found_And_One_That_Is_Not_Is_Not()
    {
        Assert.True(TestExists("RegisterTests", "Every_Repaired_Row_Names_A_Test_That_Exists"));
        Assert.False(TestExists("RegisterTests", "No_Such_Method_Anywhere"));
        Assert.False(TestExists("NoSuchClassAnywhere", "Every_Repaired_Row_Names_A_Test_That_Exists"));
    }

    [Fact]
    public void A_Table_Is_Read_Without_Its_Rule()
    {
        var rows = Rows("| id | state |\n|---|---|\n| a | open |\n\ntext\n").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[1][0]);
    }
}
