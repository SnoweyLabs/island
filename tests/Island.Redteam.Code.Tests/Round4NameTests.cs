using System.Globalization;
using System.Text;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 4 (code-4-*): the name rules after the repairs of round 3 (bf24c4d, 956ac18): how the limit counts, what the same-name check folds, what a hand-made pick keeps of a file's name, and what
/// a scenes file saved by an earlier build still has to be. Invented names only; every code point is built from its number (U(...)), so no file holds a character that cannot be read.
/// </summary>
public sealed class Round4NameTests
{
    private static string U(params int[] values) => string.Concat(values.Select(char.ConvertFromUtf32));

    private static string Esc(string text) => string.Concat(text.EnumerateRunes().Select(r => r.Value is >= 0x20 and < 0x7F ? r.ToString() : $"<{r.Value:X}>"));

    private static readonly HandContext Context = new(null, [], () => 1UL);

    private static string FolderName(string leaf) => HandPicks.Folder("Q:" + U(0x5C) + "Invented" + U(0x5C) + leaf, PageIds.Folders, Context).Pick?.Name ?? "(refused)";

    // ---- code-4-2: the limit counts code points; a person counts what he sees -----------------------------------------------------------------------

    [Fact]
    public void Held_Thirteen_Single_Code_Point_Emoji_Are_Taken_As_A_Page_Name_And_A_Scene_Name_Since_Round_3()
    {
        var name = string.Concat(Enumerable.Repeat(U(0x1F600), 13));
        Assert.Null(PageStore.Default.Create(name, "#112233").Refusal);
        Assert.Null(SceneStore.Empty.Create(name).Refusal);
    }

    [Fact]
    public void Defect_PageStore_And_SceneStore_Thirteen_Emoji_With_A_Skin_Tone_Are_Refused_As_Longer_Than_Twenty_Four_Characters()
    {
        // code-4-2 (LOW; the repair of code-3-4 is incomplete). BlankText.CountCharacters counts Unicode code points ("an emoji is one" in the commit). Most emoji a person picks are more
        // than one: a thumbs up with a skin tone is two, a flag two, a family seven, a keycap three. Thirteen thumbs up with a skin tone are thirteen characters to the person who typed them
        // (26 code points); both stores answer "A page name can have at most 24 characters." / "A scene name can have at most 24 characters." Five families (five characters) are 35 code points
        // and are refused with the same sentence. Expected: the limit counts what a person counts (StringInfo.LengthInTextElements), or the sentence does not say characters.
        var thumbs = string.Concat(Enumerable.Repeat(U(0x1F44D, 0x1F3FD), 13));
        var families = string.Concat(Enumerable.Repeat(U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x200D, 0x1F466), 5));
        var visible = new[] { thumbs, families }.Select(n => new StringInfo(n).LengthInTextElements).ToList();
        Assert.All(visible, count => Assert.True(count <= 24, $"{count} characters as a person counts them")); // the premise
        var refused = new List<string>();
        foreach (var (label, name) in new[] { ("13 thumbs", thumbs), ("5 families", families) })
        {
            if (PageStore.Default.Create(name, "#112233").Refusal is { } page) refused.Add($"{label} as a page: {page}");
            if (SceneStore.Empty.Create(name).Refusal is { } scene) refused.Add($"{label} as a scene: {scene}");
        }

        Assert.True(refused.Count == 0, string.Join(" | ", refused));
    }

    // ---- code-4-3: names that draw the same, still two names ------------------------------------------------------------------------------------------

    [Fact]
    public void Held_A_Joiner_Twin_Is_Still_The_Same_Name_As_The_Plain_Name()
    {
        var made = PageStore.Default.Create("Work", "#112233");
        Assert.True(made.Store!.Create("Wo" + U(0x200D) + "rk", "#445566").Refused);
    }

    [Fact]
    public void Defect_PageStore_A_Name_Followed_By_Characters_That_Draw_As_Nothing_Is_A_Second_Page_That_Looks_Like_The_First()
    {
        // code-4-3 (LOW; the same "names that draw the same are the same" as ease-3-11, for the characters it did not take out). SameName takes out U+200C and U+200D and composes; it keeps a
        // braille blank (U+2800), a Hangul filler (U+3164), the combining grapheme joiner (U+034F), a variation selector of the supplement (U+E0100) and a plain variation selector after
        // a letter (U+FE0E): each is accepted as a name's last character (HasHiddenCharacters lets marks and fillers through, HasVisible only needs one other character) and draws as nothing
        // after "Alpha". "Alpha" and "Alpha<U+2800>" are two pages that look alike in the strip, in Settings and in the key list; so are the others. Expected: refused as the name already taken.
        var first = PageStore.Default.Create("Alpha", "#112233").Store!;
        var accepted = new List<string>();
        foreach (var extra in new[] { 0x2800, 0x3164, 0x34F, 0xE0100, 0xFE0E })
        {
            var twin = first.Create("Alpha" + U(extra), "#445566");
            if (!twin.Refused) accepted.Add($"U+{extra:X4}");
        }

        Assert.True(accepted.Count == 0, "accepted as a second page named like the first: " + string.Join(", ", accepted));
    }

    [Fact]
    public void Defect_PageStore_Tag_Characters_After_A_Black_Flag_Are_Taken_Whatever_They_Spell()
    {
        // code-4-3b (LOW; the carve-out of code-3-2 is wider than its reason). The rule lets tag characters (U+E0020 to U+E007F) follow U+1F3F4 or another tag: the reason is the flags of England,
        // Scotland and Wales (three exact sequences). Any run is taken, so a page named "A" + black flag + tag characters that spell eighteen invisible ASCII letters and the cancel tag is
        // accepted: text a person cannot see, in a name that the screen reader reads and the key list shows. Expected: only the three sequences are taken.
        var spelled = "A" + U(0x1F3F4) + string.Concat("hidden text goes here".Select(c => U(0xE0000 + c))) + U(0xE007F);
        Assert.True(PageStore.Default.Create(spelled, "#112233").Refused, "a black flag followed by invisible text is a page name");
    }

    [Fact]
    public void Held_The_Three_Flags_Written_With_Tag_Characters_Are_Taken()
    {
        foreach (var (label, region) in new[] { ("England", "gbeng"), ("Scotland", "gbsct"), ("Wales", "gbwls") })
        {
            var flag = U(0x1F3F4) + string.Concat(region.Select(c => U(0xE0000 + c))) + U(0xE007F);
            Assert.True(PageStore.Default.Create(flag, "#112233").Refusal is null, label);
        }
    }

    // ---- code-4-4: the name of a hand-made pick keeps what draws ----------------------------------------------------------------------------------------

    [Fact]
    public void Held_A_Folder_Named_Plainly_Keeps_Its_Name()
    {
        Assert.Equal("Alpha notes", FolderName("Alpha notes"));
    }

    [Fact]
    public void Defect_HandPicks_A_Folder_Named_In_Persian_Or_With_An_Emoji_Sequence_Loses_The_Joiner_That_Is_Part_Of_The_Name()
    {
        // code-4-4 (LOW; the repair of code-3-5 takes out more than it should). HandPicks.DropHidden takes out every character of the category Format, Control, PrivateUse and unassigned: the
        // zero-width non-joiner of a Persian word, the joiner of an emoji sequence, the tag characters of a flag, the joiner after a virama, and an emoji newer than the runtime's table all
        // go. BlankText.HasHiddenCharacters, the rule for page and scene names, lets exactly these through "because they draw". So a folder called with the Persian word for "books" (with its
        // non-joiner) becomes a pick named as the misspelled word; a folder with the man technologist becomes a man and a laptop; the flag of England becomes a black flag; the rainbow flag
        // a white flag and a rainbow. Expected: the same rule as for page names (a joiner between two characters that draw, the tags of a flag, an emoji the table lacks) is kept.
        var persian = U(0x6A9, 0x62A, 0x627, 0x628, 0x200C, 0x647, 0x627);
        var technologist = U(0x1F468, 0x200D, 0x1F4BB) + " notes";
        var england = U(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F) + " cup";
        var hindi = U(0x915, 0x94D, 0x200D, 0x937);
        var newest = U(0x1FACD) + " orca";
        var lost = new List<string>();
        foreach (var (label, leaf) in new[] { ("persian", persian), ("technologist", technologist), ("england", england), ("hindi", hindi), ("new emoji", newest) })
        {
            var name = FolderName(leaf);
            if (name != leaf) lost.Add($"{label}: {Esc(leaf)} became {Esc(name)}");
        }

        Assert.True(lost.Count == 0, string.Join(" | ", lost));
    }

    [Fact]
    public void Defect_HandPicks_A_Folder_Named_With_A_Line_Separator_Keeps_It_And_The_Tile_Title_Breaks_Its_Line()
    {
        // code-4-4b (LOW; UNVERIFIED how WPF draws it: a TextBlock breaks a line at U+2028). DropHidden leaves U+2028 and U+2029 (categories LineSeparator and ParagraphSeparator are not on its
        // list) and NameFrom trims only the ends: NTFS allows U+2028 in a name. Page and scene names go through a tidy that makes every run of white space one space; a pick's name does not.
        // Expected: no line or paragraph separator in a pick's name.
        var name = FolderName("Alpha" + U(0x2028) + "Beta");
        Assert.DoesNotContain(U(0x2028), name);
    }

    [Fact]
    public void Defect_SceneRefusals_The_Balloon_Takes_The_Joiner_Out_Of_A_Scene_Name_That_The_Scene_Was_Allowed_To_Have()
    {
        // code-4-4c (LOW; the same fault in the balloon of a scene that could not open a thing, SceneRefusals.Tidy). Round 2 let a joiner stand in a scene name; the balloon's own tidy takes out
        // every Format character, the joiner of the sequence among them: a scene named with the man technologist is told in the balloon as a man and a laptop. Expected: the name told is the name.
        var scene = U(0x1F468, 0x200D, 0x1F4BB) + " Work";
        Assert.Null(SceneStore.Empty.Create(scene).Refusal); // the premise: the scene may have this name
        var message = SceneRefusals.PartMissing(scene, ["Alpha"])!.Message;
        Assert.Contains(scene, message, StringComparison.Ordinal);
    }

    // ---- code-4-5: a scenes file written by the build between the two repairs ---------------------------------------------------------------------------

    private static string ScenesJson(params string[] names) =>
        "{ \"schema\": 1, \"scenes\": [ " + string.Join(", ", names.Select((n, i) => $"{{ \"id\": \"scene-{i + 1}\", \"name\": \"{n}\", \"things\": [] }}")) + " ] }";

    [Fact]
    public void Held_A_Scenes_File_With_Two_Different_Names_Loads()
    {
        Assert.Equal(SceneStoreStatus.Loaded, SceneStore.Parse(ScenesJson("Work", "Play")).Status);
    }

    [Fact]
    public void Defect_SceneStore_A_Scenes_File_Saved_Before_The_Joiner_Twin_Rule_Is_Unreadable_As_A_Whole_When_It_Holds_Two_Twins()
    {
        // code-4-5 (LOW; likelihood tiny, effect the whole file). SceneStoreFile.TryReadScene asks SceneStore.CheckName about every name it reads, and since 956ac18 CheckName folds joiners.
        // The build between 410d9a2 (a joiner between two letters is let through) and 956ac18 could save "Work" and "Wo<U+200D>rk" as two scenes (ease-3-11); that file is now unreadable as a
        // whole: no scene, no scene key, until the file is edited by hand. Round 2 settled this for picks ("only a name being ADDED is held to the new rule"); scenes read their own names
        // through the rule that adds them. Expected: a file that was valid when it was written loads (the twin is kept, the person renames it).
        var load = SceneStore.Parse(ScenesJson("Work", "Wo" + U(0x200D) + "rk"));
        Assert.True(load.Status == SceneStoreStatus.Loaded, $"{load.Status}: {load.Detail}");
    }

    // ---- code-4-6, 4-7, 4-8: the connectors' refusals and the source of SameName ---------------------------------------------------------------------

    private static string MethodBody(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, signature + " not found");
        var to = source.IndexOf("\n    }", from, StringComparison.Ordinal);
        Assert.True(to > from, "the end of " + signature + " not found");
        return source[from..to];
    }

    [Fact]
    public void Defect_The_Connectors_Tell_A_Disconnect_That_Failed_As_A_Connect_That_Did_Not_Happen()
    {
        // code-4-6 (LOW; false text, in two places: HooksFileNotWritten of bf24c4d and the older HooksFileUnreadable). Both connectors' Disconnect() answer a refusal whose words are written for
        // Connect: "<helper>'s settings file could not be written, so Island did not connect. ... then press Connect again." A person who pressed Disconnect on a read-only file is told that
        // Island did not connect and to press Connect. SettingsSession.DisconnectAgent shows result.Refusal.Message as it is. Expected: a Disconnect that fails has words for disconnecting.
        var told = new List<string>();
        foreach (var file in new[] { "OutsideClaudeSettings.cs", "OutsideCodexSettings.cs" })
        {
            var body = MethodBody(Repo.Text("src", "Island.App", file), "public ConnectorResult Disconnect()");
            var used = System.Text.RegularExpressions.Regex.Matches(body, @"AgentRefusals\.(\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(used);
            foreach (var name in used)
            {
                var refusal = (Refusal)typeof(AgentRefusals).GetProperty(name)!.GetValue(null)!;
                if (refusal.Message.Contains("did not connect", StringComparison.OrdinalIgnoreCase) || refusal.Message.Contains("press Connect", StringComparison.OrdinalIgnoreCase))
                    told.Add($"{file}: Disconnect() answers {name}: {refusal.Message}");
            }
        }

        Assert.True(told.Count == 0, string.Join(" || ", told));
    }

    [Fact]
    public void Defect_The_Connectors_Tell_Every_Failed_Copy_Of_Island_Notify_As_A_Hook_That_Holds_The_Old_Copy()
    {
        // code-4-7 (LOW; the repair of code-3-8c names one cause as the only one). OutsideAgentConnector.CopyRefusal answers NotifyNotCopied ("A hook that is running right now holds the old
        // copy, and a hook lives only a fraction of a second. Wait a moment and press Connect again.") whenever HelperFileEditor.NotifyIsBeside is true and CopyNotify is false. CopyNotify is
        // also false when Island.Core.dll (the first file it copies) is missing beside the island, when the folder it copies into cannot be made, is read-only or the disk is full: for those
        // the sentence is false and "wait a moment" does nothing. Run here: the program beside, the library missing: CopyNotify false, NotifyIsBeside true, so the person is told a hook holds it.
        var source = Repo.Text("src", "Island.App", "OutsideClaudeSettings.cs");
        Assert.Contains("HelperFileEditor.NotifyIsBeside(AppContext.BaseDirectory) ? AgentRefusals.NotifyNotCopied : AgentRefusals.NotifyMissing", source); // the app's own choice, as read

        using var dir = new Scratch();
        var from = Directory.CreateDirectory(dir.Path_("beside")).FullName;
        foreach (var name in new[] { "Island.Notify.exe", "Island.Notify.dll", "Island.Notify.deps.json", "Island.Notify.runtimeconfig.json" }) File.WriteAllText(Path.Combine(from, name), "x");
        var to = dir.Path_("into");
        Assert.False(HelperFileEditor.CopyNotify(from, to)); // Island.Core.dll is not beside: a copy that fails for no hook
        Assert.True(HelperFileEditor.NotifyIsBeside(from));
        var told = HelperFileEditor.NotifyIsBeside(from) ? AgentRefusals.NotifyNotCopied : AgentRefusals.NotifyMissing;
        Assert.DoesNotContain("running right now", told.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Defect_BlankText_SameName_Holds_Two_Invisible_Characters_As_Literals_In_Its_Source()
    {
        // code-4-8 (LOW, a safe improvement: the code is right today). The line of BlankText.SameName is written with the characters U+200C and U+200D themselves between the quotes (bytes E2 80 8C
        // and E2 80 8D), which no editor shows. A tool or an editor that strips invisible characters, or a copy through a chat, turns them into Replace("", ...), which throws ArgumentException on
        // every page and scene name (the same line of the same commit wrote U+FFFD as an escape). Expected: backslash-u-200C and backslash-u-200D written as escapes, as in the line above it.
        var line = Repo.Text("src", "Island.Core", "BlankText.cs").Split('\n').Single(l => l.Contains("public static string SameName", StringComparison.Ordinal));
        // Repaired in WORK-ORDER-12: compared ordinal (a culture-sensitive Contains ignores zero-width characters, so the old call found them in any line).
        Assert.DoesNotContain(U(0x200C), line, StringComparison.Ordinal);
        Assert.DoesNotContain(U(0x200D), line, StringComparison.Ordinal);
    }
}

public sealed class Round4DocTests
{
    [Fact]
    public void Defect_The_Summary_Of_CopyNotify_Sits_On_The_Method_Added_Above_It_And_One_File_Has_Two_Summaries_In_A_Row()
    {
        // code-4-8b (LOW, a safe improvement: a comment only). bf24c4d put NotifyIsBeside (HelperFileEditor) and CopyRefusal (OutsideAgentConnector) between the XML summary of CopyNotify and
        // CopyNotify itself: the text "Copies the program the hook runs ..." now documents NotifyIsBeside, CopyNotify has none, and OutsideClaudeSettings.cs holds two summaries one after the other
        // above CopyRefusal. Expected: each summary on its own member.
        var editor = Repo.Text("src", "Island.Core", "Agents", "Connect", "HelperFileEditor.cs");
        var at = editor.IndexOf("public static bool NotifyIsBeside", StringComparison.Ordinal);
        Assert.True(at > 0);
        var above = editor[Math.Max(0, at - 700)..at];
        Assert.DoesNotContain("Copies the program the hook runs", above);

        var outside = Repo.Text("src", "Island.App", "OutsideClaudeSettings.cs");
        var refusal = outside.IndexOf("internal static Refusal CopyRefusal", StringComparison.Ordinal);
        Assert.True(refusal > 0);
        var lines = outside[..refusal].Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(2).ToList();
        Assert.False(lines.All(l => l.StartsWith("///", StringComparison.Ordinal) && l.Contains("<summary>", StringComparison.Ordinal)), "two summaries in a row above CopyRefusal");
    }
}
