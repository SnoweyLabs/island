using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Island.Tests;

/// <summary>The installer's script (WORK-ORDER-14 section 3), read as text: what it promises a person who runs it.</summary>
public class InstallerScriptTests
{
    private static readonly string[] Lines = File.ReadAllLines(RepoPaths.File("ship", "installer", "island.iss"));

    // The directives outside a test copy's own lines: what the real installer is compiled from.
    private static IEnumerable<string> RealLines()
    {
        var skipping = new Stack<bool>();
        foreach (var raw in Lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#ifdef TestCopy", StringComparison.OrdinalIgnoreCase)) { skipping.Push(true); continue; }
            if (line.StartsWith("#ifndef TestCopy", StringComparison.OrdinalIgnoreCase)) { skipping.Push(false); continue; }
            if (line.StartsWith("#else", StringComparison.OrdinalIgnoreCase)) { skipping.Push(!skipping.Pop()); continue; }
            if (line.StartsWith("#endif", StringComparison.OrdinalIgnoreCase)) { skipping.Pop(); continue; }
            if (skipping.Any(s => s) || line.Length == 0 || line.StartsWith(';')) continue;
            yield return line;
        }
    }

    private static string? Directive(string name) =>
        RealLines().Select(l => Regex.Match(l, $@"^{name}\s*=\s*(.*)$", RegexOptions.IgnoreCase)).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).SingleOrDefault();

    private static string Define(string name) =>
        Lines.Select(l => Regex.Match(l.Trim(), $"^#define {name} \"([^\"]*)\"$")).Single(m => m.Success).Groups[1].Value;

    [Fact]
    public void It_Installs_Per_User_Without_Administrator_Rights()
    {
        Assert.Equal("lowest", Directive("PrivilegesRequired"));
        Assert.Null(Directive("PrivilegesRequiredOverridesAllowed")); // no other install mode is offered
        Assert.StartsWith("{userpf}", Directive("DefaultDirName"));

        // Nothing is written where only an administrator may write: no common or machine-wide folder, no HKLM.
        string[] machineWide = ["{commonpf", "{commonprograms}", "{commondesktop}", "{commonappdata}", "{autopf}", "{autoprograms}", "{autodesktop}", "{pf}", "{pf32}", "{pf64}", "{sys}", "{win}", "HKLM", "HKEY_LOCAL_MACHINE", "HKA"];
        foreach (var line in RealLines())
            foreach (var place in machineWide)
                Assert.DoesNotContain(place, line, StringComparison.OrdinalIgnoreCase);

        // In the Start menu for this user, and on the desktop only when the person ticks the box.
        Assert.Contains(RealLines(), l => l.StartsWith("Name: \"{userprograms}\\", StringComparison.Ordinal));
        Assert.Contains(RealLines(), l => l.StartsWith("Name: \"desktopicon\"", StringComparison.Ordinal) && l.Contains("Flags: unchecked", StringComparison.Ordinal));
        Assert.Contains(RealLines(), l => l.StartsWith("Name: \"{userdesktop}\\", StringComparison.Ordinal) && l.Contains("Tasks: desktopicon", StringComparison.Ordinal));

        // A running Island is found by its own one-copy lock, and the person is asked; nothing is closed by itself.
        Assert.Equal(@"Local\Island.Snowey.Running", Directive("AppMutex"));
        Assert.Equal(Island.Core.InstanceNames.For(selfTest: false).Running, Directive("AppMutex"));
        Assert.Equal("no", Directive("CloseApplications"));

        // The uninstaller first runs the clean-up, then removes the program's own folder; the person's settings (roaming AppData) are not named.
        Assert.Contains(RealLines(), l => l.Contains("Parameters: \"" + Island.Core.UninstallCleanup.Argument + "\"", StringComparison.Ordinal) && l.Contains("waituntilterminated", StringComparison.Ordinal));
        Assert.Contains("Type: filesandordirs; Name: \"{localappdata}\\Island\"", RealLines());
        Assert.DoesNotContain(RealLines(), l => l.Contains("{userappdata}", StringComparison.OrdinalIgnoreCase) || l.Contains("{appdata}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Its_Version_Is_The_Builds_Version()
    {
        var props = XDocument.Load(RepoPaths.File("Directory.Build.props"));
        var version = props.Descendants("Version").Single().Value;

        Assert.Equal(version, Define("AppFileVersion"));
        Assert.Equal(string.Join('.', version.Split('.').Take(3)), Define("AppVersion"));
        Assert.Equal("{#AppVersion}", Directive("AppVersion"));
        Assert.Equal("{#AppFileVersion}", Directive("VersionInfoVersion"));
        Assert.Equal(props.Descendants("Company").Single().Value, Define("AppPublisher"));
    }

    [Fact]
    public void The_Test_Copy_Has_No_Uninstall_Entry_No_Shortcut_No_Start_And_No_Clean_Up()
    {
        // The lines that only the test copy gets, and the lines the test copy loses.
        var text = string.Join('\n', Lines);
        Assert.Matches(new Regex(@"#ifdef TestCopy\s*\nUninstallable=no\s*\n#else\s*\nAppMutex=", RegexOptions.Multiline), text);
        foreach (var section in new[] { "[Tasks]", "[Icons]", "[Run]" })
        {
            var at = Array.FindIndex(Lines, l => l.Trim() == section);
            Assert.Equal("#ifndef TestCopy", Lines[at + 1].Trim());
        }

        var uninstallRun = Array.FindIndex(Lines, l => l.Trim() == "[UninstallRun]");
        Assert.Equal("#ifndef TestCopy", Lines[uninstallRun - 1].Trim());
    }
}
