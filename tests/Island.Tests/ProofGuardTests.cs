using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// Source guards for promises that had no proof (WORK-ORDER-12 section 5, <c>review/coverage.md</c>): what the app and the self-test do not do. Each one reads the source of <c>src/</c> with the
/// comments left in (the words are looked for as code and as text alike) and names the one place that is allowed.
/// </summary>
public class ProofGuardTests
{
    internal static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(RepoPaths.File("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj"));

    internal static string Rel(string f) => Path.GetRelativePath(RepoPaths.Root, f).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void No_Package_Reference_In_The_App_Projects()
    {
        // WORK-ORDER.md: "Install nothing". Every project of src/ is built from the SDK and the projects of this solution alone.
        var projects = Directory.EnumerateFiles(RepoPaths.File("src"), "*.csproj", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        Assert.NotEmpty(projects);
        Assert.Empty(projects.Where(p => File.ReadAllText(p).Contains("PackageReference", StringComparison.Ordinal)).Select(Rel));
    }

    [Fact]
    public void No_Screen_Capture_In_Source()
    {
        // The self-test draws the island's own visuals into a bitmap (RenderTargetBitmap); nothing in src/ copies the screen, a window as Windows shows it, or a monitor's output.
        var words = new[] { "CopyFromScreen", "PrintWindow", "GraphicsCapture", "DesktopDuplication", "IDXGIOutputDuplication", "BitBlt" };
        var hits = Sources().SelectMany(f => words.Where(w => File.ReadAllText(f).Contains(w, StringComparison.Ordinal)).Select(w => $"{Rel(f)}: {w}")).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void No_Brand_Icon_Or_Logo_Is_Shipped_Beside_The_Code()
    {
        // EVALS I9: a pick the computer has never seen shows letters, never a borrowed logo. The only pictures in src/ and extension/ are Island's own.
        var roots = new[] { RepoPaths.File("src"), RepoPaths.File("extension") };
        var pictures = roots.Where(Directory.Exists).SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
            .Where(f => Regex.IsMatch(f, @"\.(png|ico|svg|jpg|jpeg|gif|bmp)$", RegexOptions.IgnoreCase))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj" or "node_modules"))
            .Select(Rel).Order().ToList();
        Assert.Equal(["extension/icons/icon128.png", "extension/icons/icon16.png", "extension/icons/icon32.png", "extension/icons/icon48.png", "src/Island.App/Island.ico"], pictures);
    }

    [Fact]
    public void The_Speed_Run_Writes_Only_Its_Table_And_Reads_The_Icons_Of_Two_Files()
    {
        // WORK-ORDER-12 section 1: "draws and writes nothing of what it reads"; row 8 asks the real icon reader for Windows' own explorer.exe and the app's own file, nothing else.
        var text = File.ReadAllText(RepoPaths.File("src", "Island.App", "SpeedStage.cs"));
        var writes = Regex.Matches(text, @"File\.(?:Write|Create|Copy|Move|Delete|Append)\w*|FileStream|StreamWriter").Select(m => m.Value).ToList();
        Assert.Equal(["File.WriteAllText"], writes);
        Assert.Contains("File.WriteAllText(path, PerfSections.Upsert(", text);
        var asked = Regex.Matches(text, @"PlaceIcon\(").Count;
        Assert.Equal(1, asked);
        Assert.Contains("new[] { explorer, own }", text);
    }
}

/// <summary>EVALS I8 names this class.</summary>
public class IconStoreTests
{
    [Fact]
    public void Only_Icons_Of_Picks_Are_Saved()
    {
        // EVALS I8 names this test. The design saves no icon at all: an icon is read, made into its disc in memory, and thrown away with the process. So the guard is stronger than the name:
        // no file of src/ that handles an icon's pixels writes a file, but the tool that makes the Store's pictures and a smoke program of the programs source.
        var allowed = new[] { "src/Island.App/ShipScreenshots.cs", "src/Island.Sources.Programs.Smoke/Program.cs" };
        var writes = new Regex(@"File\.(?:Write|Create|Open)\w*|FileStream|StreamWriter|\.Save\(");
        // The self-test's stages (Island.App/*Stage.cs) save pictures of what they draw and files of their own into temporary folders, and only run under the self-test: they are not the app.
        var hits = ProofGuardTests.Sources().Where(f => !f.EndsWith("Stage.cs", StringComparison.Ordinal)).Select(f => (f, text: File.ReadAllText(f)))
            .Where(x => Regex.IsMatch(x.text, @"IconImage|Bgra") && writes.IsMatch(x.text))
            .Select(x => ProofGuardTests.Rel(x.f)).Where(f => !allowed.Contains(f)).ToList();
        Assert.Empty(hits);
    }
}

/// <summary>EVALS X6: the one rule about games: the app never injects into another program.</summary>
public class InjectionGuardTests
{
    [Fact]
    public void Nothing_Of_The_App_Reaches_Into_Another_Programs_Memory_Or_Threads()
    {
        // Beside the guard of hooks and fake input: the calls a program would need to put code or keys into a game. A window of its own, drawn on top, is all the island is.
        var words = new[] { "CreateRemoteThread", "WriteProcessMemory", "VirtualAllocEx", "QueueUserAPC", "SetThreadContext", "NtCreateThreadEx", "ReadProcessMemory", "LoadLibraryEx", "SetWindowsHookEx", "DebugActiveProcess" };
        var hits = ProofGuardTests.Sources().SelectMany(f => words.Where(w => File.ReadAllText(f).Contains(w, StringComparison.Ordinal)).Select(w => $"{ProofGuardTests.Rel(f)}: {w}")).ToList();
        Assert.Empty(hits.Where(h => !h.Contains("LoadLibraryEx") || !h.Contains("Island.Glass"))); // LoadLibraryEx of the app's own libraries (the compositor's) is not injection
    }
}

/// <summary>WORK-ORDER-12 section 2: the graphics-card light behaves under Windows' "show animations" setting exactly as the old light does: neither reads it.</summary>
public class AnimationSettingGuardTests
{
    [Fact]
    public void Windows_Show_Animations_Is_Read_In_One_File_And_Neither_Light_Reads_It()
    {
        var readers = ProofGuardTests.Sources().Where(f => File.ReadAllText(f).Contains("ClientAreaAnimation", StringComparison.Ordinal)).Select(ProofGuardTests.Rel).Order().ToList();
        Assert.Equal(["src/Island.App/Visuals/TileView.cs", "src/Island.App/WindowsAnimations.cs"], readers); // the working ring (WORK-ORDER-11) and WindowsAnimations, which the island, its light and the settings screen ask (WORK-ORDER-13, Dan's P1)
    }
}
