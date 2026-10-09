namespace Island.Tests;

/// <summary>Finds files in the repository from the test's output folder.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string File(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "Island.sln"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Island.sln not found above the test output folder.");
    }
}
