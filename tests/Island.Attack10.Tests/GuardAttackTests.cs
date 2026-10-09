using System.Text.RegularExpressions;

namespace Island.Attack10.Tests;

/// <summary>
/// The two guards of WORK-ORDER-10 section 3 (GuardTests.A_Picks_Path_Is_Named_Only_Where_It_Must_Be and A_Choosing_Window_Always_Has_An_Owner) attacked from outside: the same regular
/// expressions, copied here, run over invented source lines. A guard is a hole when a line it ought to refuse passes. The guards themselves are not touched.
/// </summary>
public class GuardAttackTests
{
    // The guards' own rules (tests/Island.Tests/GuardRules.cs, linked into this project): the very same code the real guards run.
    private static bool PathGuardAccepts(string code) => !Island.Tests.GuardRules.PathReachesASink(code);

    private static bool OwnerGuardAccepts(string code) => Island.Tests.GuardRules.OwnerGuardAccepts(code);

    private static readonly Regex DialogTypes = Island.Tests.GuardRules.DialogTypes;

    // ---- Defects: lines a guard should refuse and does not -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("var place = pick.Location;\n_files.Log(\"opening \" + place);")]
    [InlineData("var real = PickPath.Expand(pick.Location, profile);\n_files.Log($\"opening {real}\");")]
    [InlineData("Debug.WriteLine(pick.Location);")]
    [InlineData("Console.Error.WriteLine($\"{pick.Location}\");")]
    [InlineData("Trace.TraceWarning(pick.Location);")]
    [InlineData("_logger.Warn(pick.Location);")]
    [InlineData("Logger.Info(pick.Location);")]
    [InlineData("throw new InvalidOperationException(\"cannot open \" + pick.Location);")]
    [InlineData("File.AppendAllText(logPath, pick.Location);")]
    [InlineData("report.Add(pick.Location);")]
    public void Defect_The_Path_Guard_Passes_A_Path_That_Reaches_A_Log_Line(string line)
    {
        Assert.False(PathGuardAccepts(line), "the guard lets this through: " + line.Replace("\n", " / "));
    }

    [Theory]
    [InlineData("var d = new OpenFileDialog();\nd.ShowDialog(null);")]
    [InlineData("var d = new OpenFolderDialog();\nd.ShowDialog(default);")]
    [InlineData("var d = new OpenFileDialog();\nWindow? none = null;\nd.ShowDialog(none);")]
    [InlineData("var a = new OpenFileDialog();\nvar b = new OpenFolderDialog();\na.ShowDialog(owner);\nb.ShowDialog(null);")] // one call with an owner satisfies the whole file
    public void Defect_The_Owner_Guard_Passes_A_Dialog_That_Has_No_Owner(string code)
    {
        Assert.False(OwnerGuardAccepts(code), "the guard lets this through: " + code.Replace("\n", " / "));
    }

    [Theory]
    [InlineData("var d = new FileOpenPicker();\nawait d.PickSingleFileAsync();")]
    [InlineData("[DllImport(\"comdlg32.dll\")] static extern bool GetOpenFileName(ref OPENFILENAME o);\nGetOpenFileName(ref o);")]
    [InlineData("var d = new CommonOpenFileDialog();\nd.ShowDialog();")]
    [InlineData("SHBrowseForFolder(ref info);")]
    [InlineData("var d = (IFileOpenDialog)new FileOpenDialog();\nd.Show(IntPtr.Zero);")]
    [InlineData("using WinOpen = Microsoft.Win32.OpenFileDialog;\nvar d = new WinOpen();\nd.ShowDialog();")]
    public void Defect_The_Owner_Guard_Does_Not_Know_Other_Ways_To_Open_A_Choosing_Window(string code)
    {
        // The guard looks only for four type names. These open a choosing window with no owner and the guard does not even look at the file.
        Assert.True(DialogTypes.IsMatch(code), "the guard does not recognise a choosing window here: " + code.Replace("\n", " / "));
    }

    // ---- Holds: what the guards do catch --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("_files.Log($\"opened {pick.Location}\");")]
    [InlineData("Log(pick.Location);")]
    [InlineData("LogWarning(\"x\", item.Location);")]
    [InlineData("Refuse(\"x\", pick.Location);")]
    [InlineData("Notify(pick.Location);")]
    public void Holds_The_Path_Guard_Catches_A_Direct_Log_Call_With_The_Field(string line) => Assert.False(PathGuardAccepts(line));

    [Theory]
    [InlineData("var d = new OpenFileDialog();\nd.ShowDialog();")]
    [InlineData("var d = new OpenFolderDialog();\nd.ShowDialog( );")]
    [InlineData("var d = new OpenFileDialog();")] // never shown: no owner at all
    public void Holds_The_Owner_Guard_Catches_An_Empty_Call_And_A_Missing_Call(string code) => Assert.False(OwnerGuardAccepts(code));

    [Fact]
    public void Holds_The_Real_Source_Has_No_Log_Call_That_Names_A_Path_Under_Any_Name_I_Could_Think_Of()
    {
        // Not a guard of the guard: a plain search of the real sources for the lines above. The guards pass today and so does this.
        var call = new Regex(@"(?:\.Log|Debug\.Write\w*|Trace\.\w+|Console\.\w*Write\w*|AppendAllText)\s*\([^;]*;");
        var plainLiteral = new Regex(@"(?<!\$)""(?:[^""\\]|\\.)*""");
        var names = new Regex(@"(?:\{[^}]*\b(?:real|place|folder|file|path|Location)\b)|(?:[+(,]\s*(?:real|place|folder|file|path)\s*[,)+])|\.Location\b", RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains(sep + "obj" + sep) || file.Contains(sep + "bin" + sep) || file.Contains("Smoke", StringComparison.Ordinal) || Path.GetFileName(file) == "BlurStage.cs") continue; // the blur stage writes its own measurements, not a pick
            foreach (Match m in call.Matches(File.ReadAllText(file)))
            {
                var stripped = plainLiteral.Replace(m.Value, "\"\"");
                if (names.IsMatch(stripped)) offenders.Add(Path.GetFileName(file) + ": " + m.Value.Trim());
            }
        }

        Assert.True(offenders.Count == 0, string.Join(" | ", offenders.Take(5)));
    }

    [Fact]
    public void Holds_The_Only_Real_Choosing_Window_Has_An_Owner_And_Is_Gated()
    {
        var text = Repo.Text("src", "Island.App", "OutsidePlaceChooser.cs");
        Assert.Equal(2, Regex.Matches(text, @"\.ShowDialog\(owner\)").Count);
        Assert.Equal(2, Regex.Matches(text, @"OutsideGate\.Current\.Allow\(OutsideKind\.ChoosePlace\)").Count);
        Assert.Contains("Window owner", text, StringComparison.Ordinal);
        var screen = Repo.Text("src", "Island.App", "SettingsScreen.cs");
        Assert.Contains("new OutsidePlaceChooser(this)", screen, StringComparison.Ordinal);
    }
}
