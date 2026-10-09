using System.Text;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 3 (code-3-*): the name rules after the repairs of round 2 (410d9a2): what BlankText now lets through, what it still refuses, what PageStore compares, what the limit counts, what a
/// hand-made pick is named. Invented names only; every code point is written as an escape so no file holds a character that cannot be read.
/// </summary>
public sealed class Round3NameTests
{
    private const string Zwj = "\u200D";
    private const string Vs16 = "\uFE0F";

    private static string Cp(int value) => char.ConvertFromUtf32(value);

    private static string TryPage(string name) => PageStore.Default.Create(name, "#112233").Refusal ?? "accepted";

    private static IEnumerable<string> Names(params (string Label, string Text)[] names) => names.Select(n => $"{n.Label} -> {TryPage(n.Text)}");

    // ---- what the repair of code-2-1 to 2-4 holds -------------------------------------------------------------------------------------------

    [Fact]
    public void Held_Emoji_Sequences_Joined_By_A_Joiner_Between_Two_Characters_That_Draw_Are_Taken_As_Page_Names()
    {
        var accepted = new (string, string)[]
        {
            ("man technologist", Cp(0x1F468) + Zwj + Cp(0x1F4BB)),
            ("woman technologist with a skin tone", Cp(0x1F469) + Cp(0x1F3FD) + Zwj + Cp(0x1F4BB)),
            ("family", Cp(0x1F468) + Zwj + Cp(0x1F469) + Zwj + Cp(0x1F467) + Zwj + Cp(0x1F466)),
            ("pirate flag (joiner between a flag and a skull, the skull carries the selector after it)", Cp(0x1F3F4) + Zwj + "\u2620" + Vs16),
            ("two regional indicators (a flag)", Cp(0x1F1F7) + Cp(0x1F1F4)),
            ("keycap", "1" + Vs16 + "\u20E3"),
            ("Persian word with a non-joiner between letters", "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645"),
        };
        var refused = Names(accepted).Where(line => !line.EndsWith("accepted", StringComparison.Ordinal)).ToList();
        Assert.True(refused.Count == 0, string.Join("; ", refused));
    }

    [Fact]
    public void Held_What_Hides_Or_Reverses_Is_Still_Refused_For_Pages()
    {
        var refused = new (string, string)[]
        {
            ("right-to-left override", "\u202EAlpha"),
            ("left-to-right isolate", "Al\u2066pha"),
            ("zero-width space", "Al\u200Bpha"),
            ("byte order mark", "Al\uFEFFpha"),
            ("joiner at the start", Zwj + "Alpha"),
            ("joiner at the end", "Alpha" + Zwj),
            ("two joiners in a row", "A" + Zwj + Zwj + "B"),
            ("joiner next to a space", "A " + Zwj + "B"),
            ("only a joiner and a letter", Zwj + "A"),
            ("a control character", "Al\u0007pha"),
            ("a BMP noncharacter", "Alpha\uFFFF"),
            ("an unassigned code point outside the emoji planes", "Alpha" + Cp(0x2FFFE)),
            ("a real replacement character", "Alpha\uFFFD"),
            ("a lone surrogate", "Alpha\uD800"),
            ("a name that draws as nothing", "\u2800\u3164"),
        };
        var accepted = Names(refused).Where(line => line.EndsWith("accepted", StringComparison.Ordinal)).ToList();
        Assert.True(accepted.Count == 0, string.Join("; ", accepted));
    }

    [Fact]
    public void Held_HasBrokenCharacter_Is_Exactly_A_Lone_Surrogate_For_Every_String_Of_Up_To_Five_Units_Over_Six_Kinds()
    {
        // A lone surrogate is a half character: the strictest reference is the UTF-8 encoder that throws. 6^1..6^5 = 9330 strings.
        char[] alphabet = ['a', '\uFFFD', '\uD83D', '\uDE00', '\uD800', '\uDFFF'];
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        var checkedCount = 0;
        for (var length = 1; length <= 5; length++)
        {
            var total = (int)Math.Pow(alphabet.Length, length);
            for (var n = 0; n < total; n++)
            {
                var chars = new char[length];
                for (int i = 0, rest = n; i < length; i++, rest /= alphabet.Length) chars[i] = alphabet[rest % alphabet.Length];
                var text = new string(chars);
                var wellFormed = true;
                try
                {
                    strict.GetBytes(text);
                }
                catch (EncoderFallbackException)
                {
                    wellFormed = false;
                }

                Assert.True(BlankText.HasBrokenCharacter(text) == !wellFormed, string.Join(' ', chars.Select(c => ((int)c).ToString("X4"))));
                checkedCount++;
            }
        }

        Assert.Equal(9330, checkedCount);
    }

    [Fact]
    public void Held_A_Pick_Named_With_A_Real_Replacement_Character_Is_Kept_And_One_Named_With_A_Lone_Surrogate_Is_Not_Storable()
    {
        var real = new Pick("site:alpha.example.org", PickKind.Site, "Alpha \uFFFD", PageIds.Browser, Host: "alpha.example.org");
        Assert.True(real.IsStorable(out var why), why);
        var back = PickStore.Parse(new PickStore([real]).ToJson());
        Assert.Equal(real, Assert.Single(back.Store.Picks));

        var half = real with { Name = "Alpha \uD800" };
        Assert.False(half.IsStorable(out _));
    }

    [Fact]
    public void Held_Names_That_Are_One_Name_After_Composition_Are_One_Page_And_A_Page_May_Be_Renamed_To_Its_Own_Other_Spelling()
    {
        var withE = PageStore.Default.Create("Caf\u00E9", "#112233").Store;
        Assert.NotNull(withE.Create("Cafe\u0301", "#445566").Refusal);
        Assert.NotNull(withE.Create("CAF\u00C9", "#445566").Refusal); // capitals ignored after the composition
        var page = withE.Pages.Last();
        Assert.Null(withE.Rename(page.Id, "Cafe\u0301").Refusal);
    }

    // ---- code-3-2: the joiner rule lets through only the plainest sequences ---------------------------------------------------------------------

    [Fact]
    public void Defect_BlankText_An_Emoji_Sequence_With_A_Variation_Selector_Before_The_Joiner_Is_Refused_As_Hidden()
    {
        // code-3-2 (LOW). BlankText.HasHiddenCharacters lets U+200C/U+200D through only when the runes on both sides are not IsBlankLooking, and IsBlankLooking is true for every
        // NonSpacingMark: that includes U+FE0F, the emoji presentation selector, which stands directly before the joiner in many sequences: the rainbow flag (U+1F3F3 U+FE0F U+200D
        // U+1F308, the very example code-2-3 named), the heart on fire (U+2764 U+FE0F U+200D U+1F525), the woman lifting weights (U+1F3CB U+FE0F U+200D U+2640 U+FE0F). A page named with any
        // of them is refused with "no hidden or control characters" although it draws as one picture. Expected: accepted (a joiner after a variation selector that follows a character that
        // draws is not hidden).
        var names = new (string, string)[]
        {
            ("rainbow flag", Cp(0x1F3F3) + Vs16 + Zwj + Cp(0x1F308)),
            ("heart on fire", "\u2764" + Vs16 + Zwj + Cp(0x1F525)),
            ("woman lifting weights", Cp(0x1F3CB) + Vs16 + Zwj + "\u2640" + Vs16),
        };
        var refused = Names(names).Where(line => !line.EndsWith("accepted", StringComparison.Ordinal)).ToList();
        Assert.True(refused.Count == 0, string.Join("; ", refused));
    }

    [Fact]
    public void Defect_BlankText_A_Flag_Of_England_Written_With_Tag_Characters_Is_Refused_As_Hidden()
    {
        // code-3-2, second half. The flags of England, Scotland and Wales are U+1F3F4 followed by tag characters (U+E0067 ... U+E007F, category Format) and nothing else; the repair of
        // code-2-3 named tag characters in its report ("and tag characters") but lets through only U+200C and U+200D. Expected: accepted.
        var england = Cp(0x1F3F4) + Cp(0xE0067) + Cp(0xE0062) + Cp(0xE0065) + Cp(0xE006E) + Cp(0xE0067) + Cp(0xE007F);
        Assert.Equal("accepted", TryPage(england));
    }

    [Fact]
    public void Defect_BlankText_A_Joiner_After_A_Virama_Is_Refused_As_Hidden_Although_The_Rule_Names_Hindi()
    {
        // code-3-2, third half. The comment of HasHiddenCharacters says the joiners are let through for "a Persian or Hindi word". In Hindi (and Malayalam, Sinhala, Khmer) the joiner and the
        // non-joiner stand directly after the virama (U+094D, NonSpacingMark, so IsBlankLooking) and the rule only allows a joiner between two characters that are NOT blank looking:
        // U+0915 U+094D U+200D U+0937 (a ra-less conjunct) and the Malayalam chillu U+0D28 U+0D4D U+200D are refused. Expected: accepted.
        var names = new (string, string)[]
        {
            ("Hindi: ka, virama, joiner, ssa", "\u0915\u094D\u200D\u0937"),
            ("Hindi: ka, virama, non-joiner, ssa", "\u0915\u094D\u200C\u0937"),
            ("Malayalam chillu n (joiner after the virama, at the end)", "\u0D28\u0D4D\u200D"),
        };
        var refused = Names(names).Where(line => !line.EndsWith("accepted", StringComparison.Ordinal)).ToList();
        Assert.True(refused.Count == 0, string.Join("; ", refused));
    }

    // ---- code-3-3: the carve-outs are wider than the reason for them --------------------------------------------------------------------------------

    [Fact]
    public void Defect_PageStore_Two_Names_That_Differ_Only_By_A_Joiner_Between_Two_Letters_Are_Two_Pages()
    {
        // code-3-3 (LOW). The repair lets a joiner or non-joiner stand between any two characters that draw, in any script. PageStore.CheckName (and SceneStore.CheckName) compare names after
        // FormC, and FormC keeps U+200D. So "Work" and "Wo<U+200D>rk" are two pages that look the same in the strip, in Settings and in the key list (the thing the rule that scenes keep,
        // "names that draw the same are the same", is for), and the hidden character is back inside a name. Expected: names that differ only by joiners and non-joiners are one name (compare
        // with them taken out), or a joiner is allowed only between characters that need one (emoji, and letters of the scripts that use it).
        var store = PageStore.Default.Create("Work", "#112233").Store;
        Assert.NotNull(store.Create("Wo" + Zwj + "rk", "#445566").Refusal);
    }

    [Fact]
    public void Defect_BlankText_A_Noncharacter_Of_The_Emoji_Planes_Is_Taken_As_A_Page_Name_That_Draws_As_A_Box()
    {
        // code-3-3, second half. The repair of code-2-2 lets every unassigned code point of U+1F000 to U+1FFFF through, to carry emoji newer than the runtime's table. That range also holds the
        // two noncharacters U+1FFFE and U+1FFFF (never assigned, never drawn as a picture) and the many unassigned points that are not emoji. A page named only with one of them is accepted
        // (HasVisible is true: it is not "blank looking"). Expected: refused. The smallest repair is to leave out the noncharacters (the last two points of each plane) from the carve-out.
        Assert.NotEqual("accepted", TryPage(Cp(0x1FFFE)));
        Assert.NotEqual("accepted", TryPage("Alpha " + Cp(0x1FFFF)));
    }

    [Fact]
    public void Held_A_Name_Of_A_Newer_Emoji_Than_The_Runtime_Knows_Is_Taken_And_A_New_Script_Outside_The_Emoji_Planes_Is_Not()
    {
        // The carve-out of the repair of code-2-2 is exactly the emoji planes. Orca (U+1FACD, Emoji 17.0) is unassigned to the runtime and is taken. A script added after the runtime was built
        // (U+1E6C0 is not assigned in the runtime's table) is still refused: the rule's reason (a name that hides) does not apply to it; it is the residue the main session accepted.
        Assert.Equal(System.Globalization.UnicodeCategory.OtherNotAssigned, Rune.GetUnicodeCategory(new Rune(0x1FACD)));
        Assert.Equal("accepted", TryPage("Orca " + Cp(0x1FACD)));
        if (Rune.GetUnicodeCategory(new Rune(0x1E6C0)) == System.Globalization.UnicodeCategory.OtherNotAssigned)
            Assert.NotEqual("accepted", TryPage("Alpha " + Cp(0x1E6C0)));
    }

    // ---- code-3-4: the limit counts units, the text says characters ----------------------------------------------------------------------------

    [Fact]
    public void Defect_PageStore_A_Name_Of_Thirteen_Emoji_Is_Refused_As_Longer_Than_Twenty_Four_Characters()
    {
        // code-3-4 (LOW). PageStore.CheckName compares trimmed.Length (UTF-16 units) with MaxPageNameLength (24), and the refusal says "A page name can have at most 24 characters." Thirteen emoji
        // are thirteen characters and 26 units: refused with a sentence that is false. The same in SceneStore (Scenes.MaxNameLength 24, SceneText.NameTooLong) and, for the repair of
        // code-1-7's kind of fault, the half of the limit that code-2-2 and 2-3 just made more likely (emoji names are welcome now). Expected: the limit counts characters (text elements or
        // code points), as the add-on's titles do since code-1-7; or the text says units.
        var thirteen = string.Concat(Enumerable.Repeat(Cp(0x1F600), 13));
        Assert.Equal(26, thirteen.Length);
        Assert.Equal("accepted", TryPage(thirteen));
    }

    [Fact]
    public void Defect_SceneStore_A_Name_Of_Thirteen_Emoji_Is_Refused_As_Longer_Than_Twenty_Four_Characters()
    {
        // code-3-4, the same for scenes.
        var thirteen = string.Concat(Enumerable.Repeat(Cp(0x1F600), 13));
        Assert.Null(SceneStore.Empty.Create(thirteen).Refusal);
    }

    [Fact]
    public void Held_A_Name_Of_Twenty_Four_Letters_Is_Taken_And_One_Of_Twenty_Five_Is_Refused()
    {
        Assert.Equal("accepted", TryPage(new string('a', 24)));
        Assert.Equal(SettingsText.PageNameTooLong, TryPage(new string('a', 25)));
    }

    // ---- code-3-5: the name of a hand-made pick is the name of the file or folder, whatever it holds -------------------------------------------

    private static readonly HandContext Context = new(null, [], () => 1UL);

    [Fact]
    public void Defect_HandPicks_A_Folder_Named_With_Zero_Width_Characters_Makes_A_Pick_With_No_Visible_Name()
    {
        // code-3-5 (LOW). HandPicks.NameFrom takes the leaf of the path as the pick's name, drops half characters and cuts it; it never asks whether the name draws. A folder named with
        // zero-width spaces (NTFS allows them; "a name that draws as nothing" is what PageStore and SceneStore refuse) becomes a pick whose tile title, row in Settings and balloon
        // entry are empty. Expected: the name falls back to "Folder" (or "File", "Program") as it does for an empty leaf, when no character of it draws.
        var result = HandPicks.Folder("Q:\\Invented\\\u200B\u200B", PageIds.Folders, Context);
        Assert.True(result.Ok, result.Message);
        Assert.True(BlankText.HasVisible(result.Pick!.Name), "the pick's name draws as nothing");
    }

    [Fact]
    public void Defect_HandPicks_A_File_Named_With_A_Right_To_Left_Override_Makes_A_Pick_Whose_Name_Is_Drawn_Reversed()
    {
        // code-3-5, second half. A file name that begins with U+202E (the trick of "invoice<RLO>gpj.exe") is kept whole as the pick's name: the tile and the row draw the text after it
        // reversed. Page and scene names refuse the override for exactly this reason. Expected: the override and the other format characters that reorder text are left out of a name that
        // is made from a file's name (the file name itself is not changed).
        var result = HandPicks.File("Q:\\Invented\\\u202Egpj.Alpha", PageIds.Folders, Context);
        Assert.True(result.Ok, result.Message);
        Assert.DoesNotContain('\u202E', result.Pick!.Name);
    }

    [Fact]
    public void Held_A_Folder_With_A_Lone_Surrogate_In_Its_Name_Is_Refused_With_Words_That_Name_No_Path_And_A_Real_Replacement_Character_Is_Added()
    {
        var half = HandPicks.Folder("Q:\\Invented\\Caf\uD800", PageIds.Folders, Context);
        Assert.False(half.Ok);
        Assert.DoesNotContain("Invented", half.Message);

        var real = HandPicks.Folder("Q:\\Invented\\Caf\uFFFD", PageIds.Folders, Context);
        Assert.True(real.Ok, real.Message);
        Assert.Equal(real.Pick, Assert.Single(PickStore.Parse(new PickStore([real.Pick!]).ToJson()).Store.Picks));
    }

    [Fact]
    public void Held_Two_Hundred_Thousand_Random_Hand_Made_Picks_That_Are_Accepted_Are_Saved_And_Loaded_Back_Equal()
    {
        // The promise of PickStore.MaxPicks's comment ("what is saved can always be loaded again") for the picks made by hand, over a pool of 46 awkward characters (joiners, overrides,
        // lone halves, marks, NEL, line separators, controls). The accepted ones come back equal; the refused ones are told in words (the nine kinds are in the report).
        char[] pool =
        [
            'a', 'B', '1', ' ', '.', '-', '_', '\u00E9', '\u0301', '\u200B', '\u200D', '\u200C', '\u202E', '\u2066', '\uFFFD', '\uFEFF', '\u2800', '\u3164', '\u00A0', '\u2028', '\u0007', '\u007F', '\u0085',
            '\uD83D', '\uDE00', '\uD800', '\uDC00', '\uFE0F', '\u0915', '\u094D', '\u05D0', '\u0645', '~', '%', '\'', '&', '$', '#', '(', ')', '\u30FB', '\u00DF', '\u0130', '\u0131', '\u03A3', '\u03C2',
        ];
        var rng = new Random(20261007);
        var context = new HandContext("C:\\Users\\Invented", [], () => 0xABCDEFUL);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var made = 0;
        for (var round = 0; round < 200_000 && DateTime.UtcNow < deadline; round++)
        {
            var leaf = new string(Enumerable.Range(0, rng.Next(1, 12)).Select(_ => pool[rng.Next(pool.Length)]).ToArray());
            var result = rng.Next(4) switch
            {
                0 => HandPicks.Folder("Q:\\Invented\\" + leaf, PageIds.Folders, context),
                1 => HandPicks.File("Q:\\Invented\\" + leaf + ".txt", PageIds.Folders, context),
                2 => HandPicks.BrowsedProgram("Q:\\Invented\\" + leaf + ".exe", PageIds.Apps, context),
                _ => HandPicks.FromInstalledProgram(new InstalledProgram(leaf, "alpha.exe", null, "alpha"), PageIds.Apps),
            };
            if (!result.Ok) continue;
            made++;
            var load = PickStore.Parse(new PickStore([result.Pick!]).ToJson());
            Assert.True(load.Status == PickStoreStatus.Loaded && load.Store.Picks.Count == 1 && load.Store.Picks[0] == result.Pick, string.Join(' ', leaf.Select(c => ((int)c).ToString("X4"))));
        }

        Assert.True(made > 1000, $"only {made} picks were accepted");
    }
}
