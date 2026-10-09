namespace Island.Attack10.Tests;

/// <summary>Finds the worktree root (the folder with Island.sln) so a test can read source text. Read only.</summary>
internal static class Repo
{
    public static string Root { get; } = Find();

    public static string Text(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Island.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Island.sln not found above the test folder");
    }

    /// <summary>The text of one method or property body in a source file: from the line that holds <paramref name="signature"/> to the matching close brace.</summary>
    public static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("no such member: " + signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
    }
}
