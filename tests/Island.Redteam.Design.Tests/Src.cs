using System.Text.RegularExpressions;

namespace Island.Redteam.Design.Tests;

/// <summary>Reads the app's own source files (this worktree's, never the main folder's) and the approved references, as text.</summary>
internal static class Src
{
    public static string? WorktreeRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Island.sln")) && Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
        return null;
    }

    public static string Read(string relativeUnderSrc)
    {
        var root = WorktreeRoot() ?? throw new InvalidOperationException("the worktree's root was not found above the test");
        return File.ReadAllText(Path.Combine(root, "src", relativeUnderSrc.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Every .cs file under src/Island.App, Island.SettingsUi, Island.Glass and Island.Core except obj, bin and the self-test stages ("...Stage.cs").</summary>
    public static IEnumerable<(string Name, string Text)> Visual(bool includeStages = false)
    {
        var root = WorktreeRoot() ?? throw new InvalidOperationException("the worktree's root was not found above the test");
        foreach (var project in new[] { "Island.App", "Island.SettingsUi", "Island.Glass", "Island.Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                if (!includeStages && file.EndsWith("Stage.cs", StringComparison.Ordinal)) continue;
                yield return (Path.GetRelativePath(Path.Combine(root, "src"), file).Replace('\\', '/'), File.ReadAllText(file));
            }
    }

    public static string? Reference(string file)
    {
        var root = Pic.Root();
        if (root is null) return null;
        var path = Path.Combine(root, "reference", file);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

/// <summary>The few things a test needs from a style sheet: the body of a rule and the numbers of one property.</summary>
internal static class Css
{
    public static string? Rule(string css, string selector)
    {
        var m = Regex.Match(css, @"(?:^|[}\s,;])" + Regex.Escape(selector) + @"\s*\{([^}]*)\}", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string? Prop(string? body, string property)
    {
        if (body is null) return null;
        var m = Regex.Match(body, @"(?:^|;)\s*" + Regex.Escape(property) + @"\s*:\s*([^;]+)");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static double[] Numbers(string? value) =>
        value is null ? [] : [.. Regex.Matches(value, @"-?\d*\.?\d+").Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))];
}
