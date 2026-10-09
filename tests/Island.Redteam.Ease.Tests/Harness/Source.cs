using System.IO;

namespace Island.Redteam.Ease.Tests.Harness;

/// <summary>Reads the app's own source files of this worktree (source scans: what is written in the code, not what it does when it runs).</summary>
internal static class Source
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Island.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Island.sln was not found above the test folder.");
        }
    }

    public static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The .cs files under a folder of src (never obj or bin), as (path relative to the root, text).</summary>
    public static IEnumerable<(string Path, string Text)> All(string folder)
    {
        var start = Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar));
        foreach (var file in Directory.EnumerateFiles(start, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}")) continue;
            yield return (Path.GetRelativePath(Root, file).Replace('\\', '/'), File.ReadAllText(file));
        }
    }
}
