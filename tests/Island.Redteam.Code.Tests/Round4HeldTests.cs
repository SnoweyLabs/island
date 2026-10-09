using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 4: the coverage statement for the repairs of round 3 (code-3-2 to code-3-8) tried with other inputs, other orders and other threads than round 3 used. Every test here passes.
/// Invented names only; every code point is built from its number.
/// </summary>
public sealed class Round4HeldTests
{
    private static string U(params int[] values) => string.Concat(values.Select(char.ConvertFromUtf32));

    private static bool Taken(string name) => PageStore.Default.Create(name, "#112233").Refusal is null;

    // ---- code-3-2: the joiner rule with the sequences it names and the ones it does not ----------------------------------------------------------------

    [Fact]
    public void Held_Sequences_That_Use_A_Joiner_Or_A_Selector_Beside_It_Are_Taken()
    {
        var taken = new (string, string)[]
        {
            ("rainbow flag", U(0x1F3F3, 0xFE0F, 0x200D, 0x1F308)),
            ("heart on fire", U(0x2764, 0xFE0F, 0x200D, 0x1F525)),
            ("woman lifting weights", U(0x1F3CB, 0xFE0F, 0x200D, 0x2640, 0xFE0F)),
            ("couple with heart and two skin tones", U(0x1F469, 0x1F3FB, 0x200D, 0x2764, 0xFE0F, 0x200D, 0x1F468, 0x1F3FF)),
            ("Hindi conjunct", U(0x915, 0x94D, 0x200D, 0x937)),
            ("Malayalam chillu at the end", U(0xD28, 0xD4D, 0x200D)),
            ("Persian non-joiner", U(0x645, 0x6CC, 0x200C, 0x62E, 0x648, 0x627, 0x647, 0x645)),
            ("Arabic joiner between letters", U(0x628, 0x200D, 0x646)),
            ("keycap", "1" + U(0xFE0F, 0x20E3)),
        };
        var refused = taken.Where(t => !Taken(t.Item2)).Select(t => t.Item1).ToList();
        Assert.True(refused.Count == 0, string.Join(", ", refused));
    }

    [Fact]
    public void Held_A_Joiner_That_Does_Not_Join_Anything_Is_Still_Refused()
    {
        var refused = new (string, string)[]
        {
            ("at the start", U(0x200D) + "Alpha"),
            ("after a letter at the end", "Alpha" + U(0x200D)),
            ("before a space", "Alpha" + U(0x200D) + " Beta"),
            ("after a space", "Alpha " + U(0x200D) + "Beta"),
            ("two joiners", "Al" + U(0x200D, 0x200D) + "pha"),
            ("a joiner and a non-joiner", "Al" + U(0x200D, 0x200C) + "pha"),
            ("a joiner by an override", "Al" + U(0x200D, 0x202E) + "pha"),
            ("a lone joiner", U(0x200D)),
            ("a joiner between two marks only", U(0x301, 0x200D, 0x301)),
            ("a noncharacter of the emoji planes", "A" + U(0x1FFFE)),
            ("the other noncharacter", "A" + U(0x1FFFF)),
            ("a noncharacter of the second plane", "A" + U(0x2FFFE)),
            ("a Basic Multilingual Plane noncharacter", "A" + U(0xFDD0)),
            ("a private use character", "A" + U(0xE000)),
            ("a control", "A" + U(0x7)),
            ("a tag character with no flag before it", "A" + U(0xE0067)),
            ("a lone tag cancel", U(0xE007F)),
        };
        var taken = refused.Where(t => Taken(t.Item2)).Select(t => t.Item1).ToList();
        Assert.True(taken.Count == 0, "taken: " + string.Join(", ", taken));
    }

    // ---- code-3-4: the limit and the file ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Held_Twenty_Four_Emoji_Are_A_Name_And_Twenty_Five_Are_Not_And_The_Long_Name_Survives_The_File()
    {
        var emoji = U(0x1F600);
        var twentyFour = string.Concat(Enumerable.Repeat(emoji, 24));
        var made = PageStore.Default.Create(twentyFour, "#112233");
        Assert.NotNull(made.Store);
        var load = PageStore.Parse(made.Store!.ToJson());
        Assert.True(load.Status == PageStoreStatus.Loaded, load.Detail);
        Assert.Equal(twentyFour, load.Store.Pages[^1].Name);
        Assert.NotNull(PageStore.Default.Create(twentyFour + emoji, "#112233").Refusal);
        Assert.NotNull(SceneStore.Empty.Create(twentyFour + emoji).Refusal);
        var scene = SceneStore.Empty.Create(twentyFour);
        Assert.Null(scene.Refusal);
        Assert.Equal(SceneStoreStatus.Loaded, SceneStore.Parse(scene.Store.ToJson()).Status);
    }

    [Fact]
    public void Held_A_Page_File_Saved_Before_The_Count_Changed_With_A_Name_Of_Twenty_Four_Units_Loads()
    {
        var name = new string('W', 24);
        var made = PageStore.Default.Create(name, "#112233");
        Assert.True(PageStore.Parse(made.Store!.ToJson()).Status == PageStoreStatus.Loaded);
    }

    [Fact]
    public void Held_The_Balloon_Of_A_Scene_Named_With_Twenty_Four_Emoji_Stays_Under_The_Limit_Of_A_Balloon()
    {
        // Scene names can now be 48 UTF-16 units long; the balloon shortens the scene's name to 24 units itself (SceneRefusals.Shorten), so the message of 250 characters holds.
        var scene = string.Concat(Enumerable.Repeat(U(0x1F600), 24));
        var skipped = Enumerable.Range(0, 40).Select(i => string.Concat(Enumerable.Repeat(U(0x1F600), 24)) + i).ToList();
        var message = SceneRefusals.PartMissing(scene, skipped)!.Message;
        Assert.True(message.Length <= SceneRefusals.MaxMessageLength, $"{message.Length} characters");
        Assert.True(SceneRefusals.PartMissing(scene, skipped)!.WhatHappened.Length <= SceneRefusals.MaxMessageLength);
        Assert.DoesNotContain(((char)0xD83D).ToString() + "…", message); // never half a character before the ellipsis
    }

    // ---- code-3-5: a hand-made pick's name -----------------------------------------------------------------------------------------------------------

    private static readonly HandContext Context = new("C:" + U(0x5C) + "Users" + U(0x5C) + "Invented", [], () => 1UL);

    private static string FolderName(string leaf) => HandPicks.Folder("Q:" + U(0x5C) + "Invented" + U(0x5C) + leaf, PageIds.Folders, Context).Pick?.Name ?? "(refused)";

    [Fact]
    public void Held_A_Hand_Made_Name_Of_Zero_Width_Characters_Or_An_Override_Gets_The_Plain_Fallback_Or_Loses_The_Override()
    {
        Assert.Equal("Folder", FolderName(U(0x200B, 0x200B)));
        Assert.Equal("Folder", FolderName(U(0xFEFF)));
        Assert.Equal("Folder", FolderName(U(0x3164)));
        Assert.Equal("invoicegpj", FolderName("invoice" + U(0x202E) + "gpj"));
        Assert.Equal("Alpha", FolderName(U(0x202E) + "Alpha"));
    }

    [Fact]
    public void Held_A_Long_Hand_Made_Name_Is_Cut_At_A_Hundred_Units_Without_Splitting_An_Emoji()
    {
        var odd = "a" + string.Concat(Enumerable.Repeat(U(0x1F600), 60)); // 121 units: the hundredth unit is the first half of a pair
        var name = FolderName(odd);
        Assert.True(name.Length <= 100);
        Assert.False(BlankText.HasBrokenCharacter(name));
        Assert.True(name.Length >= 99);
    }

    [Fact]
    public void Held_Every_Hand_Made_Name_That_Is_Made_Is_Storable_And_Visible_Over_A_Random_Pool_With_The_Joiners_And_Separators_In_It()
    {
        var pool = new[]
        {
            "a", "B", " ", U(0x200B), U(0x200D), U(0x200C), U(0x202E), U(0x2066), U(0xFEFF), U(0x2028), U(0x2029), U(0x85), U(0x1F600), U(0x1F3F4), U(0xE0067), U(0xE007F), U(0xFE0F), U(0x301),
            U(0x3164), U(0x2800), U(0xD7FF), U(0xFFFD), U(0x1FACD), U(0x915), U(0x94D),
        };
        var rng = new Random(7);
        var made = 0;
        for (var i = 0; i < 50_000; i++)
        {
            var leaf = string.Concat(Enumerable.Range(0, rng.Next(1, 10)).Select(_ => pool[rng.Next(pool.Length)]));
            var result = HandPicks.Folder("Q:" + U(0x5C) + "Invented" + U(0x5C) + leaf, PageIds.Folders, Context);
            if (result.Pick is not { } pick) continue;
            made++;
            Assert.True(pick.IsStorable(out var why), why);
            Assert.True(BlankText.HasVisible(pick.Name), "a pick named with nothing that draws");
            Assert.False(BlankText.HasBrokenCharacter(pick.Name));
        }

        Assert.True(made > 10_000);
    }

    // ---- code-3-6: the writer into another program's folder -----------------------------------------------------------------------------------------

    [Fact]
    public void Held_A_Refused_Write_Leaves_Nothing_Behind_Whatever_Made_It_Fail_And_Never_Throws()
    {
        using var dir = new Scratch();
        var file = dir.Write("hooks.json", "{}");

        // the file is read-only
        File.SetAttributes(file, FileAttributes.ReadOnly);
        Assert.False(HelperFileEditor.Write(file, "{ \"a\": 1 }", freshSecondCopy: false));
        File.SetAttributes(file, FileAttributes.Normal);
        Assert.False(File.Exists(file + HelperFileEditor.TemporarySuffix));
        Assert.Equal("{}", File.ReadAllText(file));

        // a folder stands where the temporary file goes: the write fails, the folder is left alone (it is not the writer's)
        Directory.CreateDirectory(file + HelperFileEditor.TemporarySuffix);
        Assert.False(HelperFileEditor.Write(file, "{ \"a\": 2 }", freshSecondCopy: true));
        Assert.True(Directory.Exists(file + HelperFileEditor.TemporarySuffix));
        Assert.Equal("{}", File.ReadAllText(file));
        Directory.Delete(file + HelperFileEditor.TemporarySuffix);

        // the first copy cannot be made (a folder stands where it goes): another file, the same writer
        var second = dir.Write("settings.json", "{}");
        Directory.CreateDirectory(second + HelperFileEditor.FirstCopySuffix);
        Assert.False(HelperFileEditor.Write(second, "{ \"a\": 3 }", freshSecondCopy: false));
        Assert.False(File.Exists(second + HelperFileEditor.TemporarySuffix));
        Assert.Equal("{}", File.ReadAllText(second));
    }

    [Fact]
    public void Held_A_Write_Beside_A_File_That_Is_Held_Open_By_An_Editor_Fails_Plainly_And_Leaves_No_Temporary_File()
    {
        using var dir = new Scratch();
        var file = dir.Write("hooks.json", "{}");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) // an editor that holds the file without letting it be replaced
        {
            Assert.False(HelperFileEditor.Write(file, "{ \"a\": 1 }", freshSecondCopy: false));
        }

        Assert.False(File.Exists(file + HelperFileEditor.TemporarySuffix));
        Assert.Equal("{}", File.ReadAllText(file));
    }

    // ---- code-3-1: by reading, the reset in the one place every hide passes through --------------------------------------------------------------------

    [Fact]
    public void Held_The_Words_Of_The_Island_Are_Forgotten_In_Detach_After_The_Guard_And_Before_The_Event_That_The_Island_Left()
    {
        var source = Repo.Text("src", "Island.App", "IslandController.cs");
        var start = source.IndexOf("private void Detach()", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = source[start..source.IndexOf("Left?.Invoke();", start, StringComparison.Ordinal)];
        Assert.Contains("if (!Attached) return;", body);
        Assert.True(body.IndexOf("if (!Attached) return;", StringComparison.Ordinal) < body.IndexOf("_describedKey = default", StringComparison.Ordinal));
        Assert.Contains("_view.ForgetWords();", body);
        Assert.Contains("Detach()", source[(source.IndexOf("public void Dispose()", StringComparison.Ordinal))..]); // a dispose forgets as well
        Assert.Contains("public void Forget() => _description = Name;", Repo.Text("src", "Island.App", "Visuals", "IslandRoot.cs"));
    }
}
