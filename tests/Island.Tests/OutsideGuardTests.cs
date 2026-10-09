using System.Text;
using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// Source guards for WORK-ORDER-3 (tonight): every call that reaches the outside world is in a file whose
/// name begins "Outside" and asks OutsideGate; the foreground is never forced; the app is never an internet client.
/// Comments are removed before the text is searched, so a word in a comment does not count.
/// </summary>
public class OutsideGuardTests
{
    private static readonly string[] OutsideCalls =
    [
        "Process.Start", "ShellExecute", "CreateProcess", "SetForegroundWindow", "SwitchToThisWindow",
        "TryTogglePlayPauseAsync", "TrySkipNextAsync", "TrySkipPreviousAsync", "TryPlayAsync", "TryPauseAsync",
        "ActivateApplication", "LaunchUriAsync", "LaunchFolderAsync", "LaunchFileAsync",
        "PostMessage", // asking another program's window to close (WORK-ORDER-6 section 5)
    ];

    private static readonly string[] ForcedForeground = ["AttachThreadInput", "AllowSetForegroundWindow", "LockSetForegroundWindow"];

    private static readonly string[] InternetClients = ["HttpClient", "WebClient", "WebRequest", "ClientWebSocket", "TcpClient"];

    internal static List<string> SourceFiles() =>
        new[] { "src", "tools" }
            .Select(d => Path.Combine(RepoPaths.Root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj"))
            .ToList();

    private static string Rel(string f) => Path.GetRelativePath(RepoPaths.Root, f);

    [Fact]
    public void Outside_Actions_Are_Gated()
    {
        var files = SourceFiles();
        Assert.NotEmpty(files);
        var problems = new List<string>();
        foreach (var f in files)
        {
            var code = StripComments(File.ReadAllText(f));
            var hits = OutsideCalls.Where(c => Regex.IsMatch(code, @"(?<![A-Za-z0-9_])" + Regex.Escape(c) + @"(?![A-Za-z0-9_])")).ToList();
            if (hits.Count == 0) continue;

            if (!Path.GetFileName(f).StartsWith("Outside", StringComparison.Ordinal))
                problems.Add($"{Rel(f)} makes an outside call ({string.Join(", ", hits)}) but its name does not begin with Outside");
            else if (!code.Contains("OutsideGate", StringComparison.Ordinal))
                problems.Add($"{Rel(f)} makes an outside call ({string.Join(", ", hits)}) without asking OutsideGate");
        }

        Assert.Empty(problems);
        // The guard is only worth something if the doors it protects exist.
        Assert.Contains(files, f => Path.GetFileName(f) == "OutsideForeground.cs");
    }

    [Fact]
    public void The_Foreground_Is_Never_Forced()
    {
        var hits = from f in SourceFiles()
                   let code = StripComments(File.ReadAllText(f))
                   from word in ForcedForeground
                   where code.Contains(word, StringComparison.Ordinal)
                   select $"{Rel(f)}: {word}";
        Assert.Empty(hits);
    }

    [Fact]
    public void App_Has_No_Internet_Client()
    {
        var files = SourceFiles();
        // The one exception: the pretend add-on of the self-test, a client that may only ever dial this computer.
        const string PretendAddonFile = "PretendAddon.cs";
        var clients = from f in files
                      where Path.GetFileName(f) != PretendAddonFile
                      let code = StripComments(File.ReadAllText(f))
                      from word in InternetClients
                      where Regex.IsMatch(code, @"(?<![A-Za-z0-9_])" + word + @"(?![A-Za-z0-9_])")
                      select $"{Rel(f)}: {word}";
        Assert.Empty(clients);

        foreach (var pretend in files.Where(f => Path.GetFileName(f) == PretendAddonFile))
        {
            var code = StripComments(File.ReadAllText(pretend));
            var addresses = Regex.Matches(code, @"[A-Za-z][A-Za-z0-9+.-]*://[^\s""'/:]*").Select(m => m.Value);
            Assert.All(addresses, a => Assert.Equal("ws://127.0.0.1", a));
            Assert.DoesNotContain("IPAddress.Any", code);
            Assert.DoesNotContain("DnsEndPoint", code);
        }

        // The text "https://" appears in exactly one file, SiteAddress, which builds the address handed to the browser.
        var withHttps = files.Where(f => StripComments(File.ReadAllText(f)).Contains("https://", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).ToList();
        Assert.Equal(["SiteAddress.cs"], withHttps);

        // "http://" is allowed only for this computer.
        var plain = from f in files
                    from m in Regex.Matches(StripComments(File.ReadAllText(f)), @"http://[^\s""'/]*")
                    where !m.Value.StartsWith("http://127.0.0.1", StringComparison.Ordinal) && !m.Value.StartsWith("http://localhost", StringComparison.Ordinal)
                    select $"{Rel(f)}: {m.Value}";
        Assert.Empty(plain);
    }

    [Fact]
    public void Comment_Stripping_Keeps_Strings_And_Drops_Comments()
    {
        var code = StripComments("var a = \"https://x\"; // Process.Start\n/* SetForegroundWindow */ var b = '/'; var c = @\"a//b\";");
        Assert.Contains("https://x", code);
        Assert.DoesNotContain("Process.Start", code);
        Assert.DoesNotContain("SetForegroundWindow", code);
        Assert.Contains("a//b", code);
    }

    /// <summary>Removes // and /* */ comments but keeps the text of string and character literals (regular, verbatim, raw).</summary>
    internal static string StripComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else if (c == '/' && next == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2;
            }
            else if (c == '"' && next == '"' && i + 2 < text.Length && text[i + 2] == '"')
            {
                var start = i;
                var quotes = 0;
                while (i < text.Length && text[i] == '"') { quotes++; i++; }
                var close = new string('"', quotes);
                var end = text.IndexOf(close, i, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + quotes;
                sb.Append(text, start, i - start);
            }
            else if (c == '"' || (c == '@' && next == '"') || (c == '$' && next == '"') || (c == '$' && next == '@') || (c == '@' && next == '$'))
            {
                var start = i;
                var verbatim = false;
                while (i < text.Length && text[i] != '"') { if (text[i] == '@') verbatim = true; i++; }
                i++;
                while (i < text.Length)
                {
                    if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                    if (!verbatim && text[i] == '\\') { i += 2; continue; }
                    if (text[i] == '"') { i++; break; }
                    i++;
                }

                sb.Append(text, start, Math.Min(i, text.Length) - start);
            }
            else if (c == '\'' )
            {
                var start = i;
                i++;
                while (i < text.Length && text[i] != '\'') { if (text[i] == '\\') i++; i++; }
                i++;
                sb.Append(text, start, Math.Min(i, text.Length) - start);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }
}
