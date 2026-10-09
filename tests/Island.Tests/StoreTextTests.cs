using System.Text.RegularExpressions;
using Island.Core;

namespace Island.Tests;

/// <summary>The store texts of WORK-ORDER-8 section 6: they keep to the limits the store's pages give, name their source page, and justify every kind of outside action the code knows.</summary>
public class StoreTextTests
{
    private static string Store(string name) => Path.Combine(RepoPaths.Root, "ship", "store", name);

    private static string Read(string name) => File.ReadAllText(Store(name));

    private static IEnumerable<string> TextFiles() =>
        new[] { "listing-en.md", "listing-ro.md", "full-rights-justification.md", "privacy.md", "age-and-category.md" }.Select(Store);

    [Fact]
    public void Every_Outside_Kind_Is_Justified()
    {
        var text = Read("full-rights-justification.md");
        var headings = Regex.Matches(text, @"^### (\w+)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        foreach (var kind in Enum.GetValues<OutsideKind>())
            Assert.Contains(kind.ToString(), headings);
        Assert.Equal(Enum.GetValues<OutsideKind>().Length, headings.Count); // and nothing is justified that does not exist

        // One sentence for each: the line after the heading is one sentence of some length.
        foreach (var kind in Enum.GetValues<OutsideKind>())
        {
            var after = text[(text.IndexOf("### " + kind, StringComparison.Ordinal) + 4 + kind.ToString().Length)..].TrimStart().Split('\n')[0].Trim();
            Assert.True(after.Length > 40 && after.EndsWith('.'), $"{kind} has no sentence under its heading");
        }

        Assert.Contains("global key", text, StringComparison.Ordinal);
        Assert.Contains("tray icon", text, StringComparison.Ordinal);
        Assert.Contains("which windows are open", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_Store_Text_Begins_With_The_Address_Of_The_Page_It_Answers()
    {
        foreach (var file in TextFiles())
        {
            var first = File.ReadLines(file).First();
            Assert.True(first.StartsWith("<!--", StringComparison.Ordinal), Path.GetFileName(file));
            Assert.Contains("https://", first, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("listing-en.md")]
    [InlineData("listing-ro.md")]
    public void The_Listings_Keep_To_The_Limits_Of_The_Store(string file)
    {
        var text = Read(file);

        var shortDescription = Section(text, "Descriere scurtă", "Short description");
        Assert.InRange(shortDescription.Length, 20, 269); // "keep under 270"

        var description = Section(text, "Descriere", "Description");
        Assert.InRange(description.Length, 200, 10_000);
        Assert.DoesNotContain("http", description, StringComparison.OrdinalIgnoreCase); // "Do not include HTML, code snippets, or URLs in the description"
        Assert.DoesNotContain("<", description, StringComparison.Ordinal);

        var features = Regex.Matches(Section(text, "Funcții", "Product features"), @"^\d+\. (.+)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.InRange(features.Count, 1, 20); // up to 20
        Assert.All(features, f => Assert.True(f.Length <= 200, $"a feature has {f.Length} characters"));

        var terms = Section(text, "Termeni de căutare", "Search terms").Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        Assert.InRange(terms.Count, 1, 7); // policy 10.1.3: at most seven

        Assert.True(Section(text, "Ce e nou în această versiune", "What's new in this version").Length <= 1500);
    }

    [Fact]
    public void The_Description_Starts_With_The_Dependency_On_The_Add_On()
    {
        // Policy 10.2.4: a dependency on non-integrated software is disclosed at the beginning of the description.
        var en = Section(Read("listing-en.md"), "Description", "Description");
        Assert.StartsWith("Seeing and switching your browser tabs needs the free Island add-on", en, StringComparison.Ordinal);
        var ro = Section(Read("listing-ro.md"), "Descriere", "Descriere");
        Assert.StartsWith("Ca să vezi și să schimbi filele din browser ai nevoie de extensia gratuită Island", ro, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_Listings_Say_It_Was_Not_Yet_Tried_On_Other_Computers_And_Claim_Nothing_Superlative()
    {
        Assert.Contains("not yet been tried on other computers", Read("listing-en.md"), StringComparison.Ordinal);
        Assert.Contains("nu a fost încă încercat pe alte calculatoare", Read("listing-ro.md"), StringComparison.Ordinal);
        foreach (var word in new[] { "best", "fastest", "#1", "revolutionary", "ultimate", "first-ever" })
            Assert.DoesNotContain(word, Section(Read("listing-en.md"), "Description", "Description"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Privacy_Text_Names_Every_File_The_App_Writes_And_Says_It_Contacts_Nothing()
    {
        var text = Read("privacy.md");
        foreach (var file in new[] { "settings.json", "pages.json", "picks.json", "scenes.json", "island.log" })
            Assert.Contains(file, text, StringComparison.Ordinal);
        Assert.Contains("does not connect to the internet", text, StringComparison.Ordinal);
        Assert.Contains("__OWNER__", text, StringComparison.Ordinal);
        Assert.Contains("never written anywhere", text.Replace("is never written anywhere", "never written anywhere"), StringComparison.Ordinal);
    }

    /// <summary>The text under a "## heading" (either of the two names), up to the next "## ".</summary>
    private static string Section(string text, params string[] headings)
    {
        foreach (var heading in headings)
        {
            var match = Regex.Match(text, @"^## " + Regex.Escape(heading) + @"(?: \([^\n]*\))?[ \t]*\r?\n(.*?)(?=^## |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            if (match.Success) return match.Groups[1].Value.Trim();
        }

        throw new InvalidOperationException("no section " + string.Join(" or ", headings));
    }

    [Theory]
    [InlineData("privacy.md", "PUBLISH FROM HERE", "PUBLISH UNTIL HERE")]
    [InlineData("full-rights-justification.md", "PASTE FROM HERE", "PASTE UNTIL HERE")]
    public void The_Text_To_Publish_Has_Nothing_Internal_In_It(string file, string from, string until)
    {
        // The owner pastes only what lies between the two marks; it must not point at files, tests or work orders of the project.
        var text = Read(file);
        var start = text.IndexOf(from, StringComparison.Ordinal);
        var end = text.IndexOf(until, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the marks are missing");
        var published = text[(start + from.Length)..end];
        foreach (var internalWord in new[] { "ship/", "Test", "WORK-ORDER", "Guard", ".cs", "Claude's own", "CHECKS-FOR-OWNER", "SOURCES" })
            Assert.DoesNotContain(internalWord, published, StringComparison.Ordinal);
    }
}
