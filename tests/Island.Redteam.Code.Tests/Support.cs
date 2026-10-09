using System.Text;

// Some tests of this project measure milliseconds or open many pipes at once: none runs beside another, so a busy neighbour never makes a timing line fail.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Island.Redteam.Code.Tests;

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
}

/// <summary>A folder of its own under the temp folder, with invented file names, removed at the end.</summary>
internal sealed class Scratch : IDisposable
{
    public Scratch() => Folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "island-redteam-code-" + Guid.NewGuid().ToString("N"))).FullName;

    public string Folder { get; }

    public string Path_(string name) => Path.Combine(Folder, name);

    public string Write(string name, string text)
    {
        var path = Path_(name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a file that is still held by a test: the temp folder is cleaned by Windows later
        }
    }
}
