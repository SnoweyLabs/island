using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Island.Tests;

/// <summary>
/// The scanner of WORK-ORDER-8 section 2: looks in files (and inside every .msix and .zip, entry names and unpacked bytes) for the traces of the person and
/// of this computer: the account name, the profile folder's path and its name alone, the computer's name, the working directory's path, and each word of the
/// account's full name as a whole word in text files. Case is ignored for letters A to Z; text is looked for as plain bytes and as the two-byte text Windows
/// programs use, with \, / and doubled \\ between the parts of a path. What it looks for is read from the running system and is written in no file; what it
/// reports is the file and the kind, never the thing.
/// </summary>
internal sealed class TraceScanner(IReadOnlyList<(string Kind, string Text, bool IsPath)> needles)
{
    private static readonly string[] TextExtensions = [".md", ".txt", ".xml", ".json", ".cmd", ".html", ".htm", ".manifest", ".csv", ".yml", ".yaml", ".js", ".css", ".config", ".resx", ".props", ".xaml", ".svg"];

    // The words of the full name are looked for as whole words in text files only: in programs, three letters are in everything.
    private readonly List<(string Kind, byte[] Pattern)> _patterns = [.. needles.Where(n => n.Kind != FullNameWord).SelectMany(Patterns)];
    // The same words are also looked for, as whole words, in Island's own programs (not in Microsoft's runtime, where three letters are in everything).
    private readonly List<byte[]> _wordBytes = [.. needles.Where(n => !n.IsPath && n.Kind == FullNameWord).SelectMany(n => new[] { Encoding.UTF8.GetBytes(n.Text.ToLowerInvariant()), Encoding.Unicode.GetBytes(n.Text.ToLowerInvariant()) })];
    private readonly List<(string Kind, Regex Word)> _words = [.. needles.Where(n => !n.IsPath && n.Kind == FullNameWord).Select(n => (n.Kind, new Regex(@"\b" + Regex.Escape(n.Text) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))];

    public const string FullNameWord = "a word of the account's full name";

    public int FilesScanned { get; private set; }

    /// <summary>How many words of the account's full name Windows gave (zero when it holds none: then the word search has nothing to look for, and the guard says so).</summary>
    public int WordCount => needles.Count(n => n.Kind == FullNameWord);

    /// <summary>The needles of this computer, from the running system.</summary>
    public static TraceScanner ForThisComputer()
    {
        var list = new List<(string Kind, string Text, bool IsPath)>();
        void Add(string kind, string? text, bool isPath = false)
        {
            if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 3) list.Add((kind, text.Trim(), isPath));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("the Windows account name", Environment.UserName);
        Add("the profile folder's path", profile, isPath: true);
        Add("the profile folder's name", string.IsNullOrEmpty(profile) ? null : Path.GetFileName(profile.TrimEnd('\\', '/')));
        Add("this computer's name", Environment.MachineName);
        Add("the working directory's path", Directory.GetCurrentDirectory(), isPath: true);
        Add("the working directory's path", RepoPaths.Root, isPath: true);
        Add("the name of the working folder's vault", "Paradise" + " Gate"); // the project's own folder name: it says where the work was done
        foreach (var word in FullNameWords()) Add(FullNameWord, word);
        return new TraceScanner([.. list.DistinctBy(n => (n.Kind, n.Text.ToLowerInvariant()))]);
    }

    /// <summary>The same needles but the words of the account's full name (WORK-ORDER-14: the person's name may appear in the public copy).</summary>
    public static TraceScanner ForThisComputerWithoutTheName() => new([.. ForThisComputer().Needles.Where(n => n.Kind != FullNameWord)]);

    private IReadOnlyList<(string Kind, string Text, bool IsPath)> Needles => needles;

    /// <summary>The words (three letters or more) of the account's full name as Windows holds it; empty when Windows has none.</summary>
    private static IEnumerable<string> FullNameWords()
    {
        var names = new List<string>();
        try
        {
            var buffer = new StringBuilder(256);
            var size = (uint)buffer.Capacity;
            if (GetUserNameEx(3, buffer, ref size)) names.Add(buffer.ToString()); // NameDisplay
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // no answer from Windows: no words
        }

        return names.SelectMany(n => Regex.Split(n, @"[^\p{L}\p{N}]+")).Where(w => w.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder name, ref uint size);

    private static IEnumerable<(string Kind, byte[] Pattern)> Patterns((string Kind, string Text, bool IsPath) needle)
    {
        var forms = new HashSet<string> { needle.Text.ToLowerInvariant() };
        if (needle.IsPath)
        {
            var text = needle.Text.ToLowerInvariant();
            forms.Add(text.Replace('\\', '/'));
            forms.Add(text.Replace("\\", "\\\\"));
            forms.Add(text.Replace('/', '\\'));
        }

        foreach (var form in forms)
        {
            yield return (needle.Kind, Encoding.UTF8.GetBytes(form));
            yield return (needle.Kind, Encoding.Unicode.GetBytes(form));
        }
    }

    /// <summary>Scans one file's bytes (and what is inside it when it is a zip or a package). Returns one finding per file and kind: "path: kind".</summary>
    public List<string> ScanFile(string path, string displayName)
    {
        var findings = new List<string>();
        var bytes = File.ReadAllBytes(path);
        ScanBytes(displayName, bytes, findings);
        if (displayName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) || displayName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ScanArchive(displayName, bytes, findings);
        return findings;
    }

    public void ScanBytes(string name, byte[] bytes, List<string> findings)
    {
        FilesScanned++;
        var seen = new HashSet<string>();
        var lowerBytes = Lower(bytes);
        foreach (var (kind, pattern) in _patterns)
            if (lowerBytes.AsSpan().IndexOf(pattern) >= 0) Report(findings, seen, name, kind);

        if (_wordBytes.Count > 0 && IsOwnProgram(name) && ContainsWholeWord(lowerBytes, _wordBytes)) Report(findings, seen, name, FullNameWord);

        // Third-party texts are copied as their authors wrote them: a word of a name can be a word of theirs; every other kind of trace is still looked for in them.
        if (_words.Count > 0 && !name.Contains("/third-party/", StringComparison.OrdinalIgnoreCase) && TextExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            var text = Encoding.UTF8.GetString(bytes);
            if (_words.Any(w => w.Word.IsMatch(text))) Report(findings, seen, name, FullNameWord);
        }

        // The name of the file itself is looked at too (entries of a package carry paths).
        var lowered = Encoding.UTF8.GetBytes(name.ToLowerInvariant());
        foreach (var (kind, pattern) in _patterns)
            if (lowered.AsSpan().IndexOf(pattern) >= 0) Report(findings, seen, name + " (its name)", kind);
    }

    private static bool IsOwnProgram(string name)
    {
        var file = name[(name.LastIndexOf('/') + 1)..];
        return file.StartsWith("Island.", StringComparison.OrdinalIgnoreCase) && (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A word found in the bytes with no letter or digit (in the same width) on either side of it.</summary>
    private static bool ContainsWholeWord(byte[] lowered, List<byte[]> words)
    {
        foreach (var word in words)
        {
            var wide = word.Length >= 2 && word[1] == 0;
            var step = wide ? 2 : 1;
            for (var from = 0; from <= lowered.Length - word.Length;)
            {
                var at = lowered.AsSpan(from).IndexOf(word);
                if (at < 0) break;
                at += from;
                from = at + 1;
                if (wide && at % 2 != 0) continue; // two-byte text is aligned
                var before = at - step >= 0 && IsWordChar(lowered, at - step, wide);
                var after = at + word.Length + step <= lowered.Length && IsWordChar(lowered, at + word.Length, wide);
                if (!before && !after) return true;
            }
        }

        return false;
    }

    private static bool IsWordChar(byte[] bytes, int at, bool wide) =>
        (!wide || bytes[at + 1] == 0) && (bytes[at] is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_' || bytes[at] >= 0x80 && !wide);

    private void ScanArchive(string displayName, byte[] bytes, List<string> findings)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var inner = displayName + "!" + entry.FullName;
                ScanBytes(inner, copy.ToArray(), findings);
                if (entry.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)) ScanArchive(inner, copy.ToArray(), findings);
            }
        }
        catch (InvalidDataException)
        {
            findings.Add($"{displayName}: could not be opened as an archive");
        }
    }

    private static byte[] Lower(byte[] bytes)
    {
        var copy = new byte[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            copy[i] = b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
        }

        return copy;
    }

    private static void Report(List<string> findings, HashSet<string> seen, string name, string kind)
    {
        if (seen.Add(name + "|" + kind)) findings.Add($"{name}: {kind}");
    }
}
