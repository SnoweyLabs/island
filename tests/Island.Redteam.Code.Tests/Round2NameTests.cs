using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 2 (code-2-*): what the repair of code-1-16 (page and pick names held to the rule scenes keep) broke or did not reach. Invented names only.
/// </summary>
public sealed class Round2NameTests
{
    private const string Replacement = "\uFFFD";

    private static string PicksJson(string name) =>
        "{ \"schema\": 1, \"picks\": [ { \"id\": \"site:alpha.example.org\", \"kind\": \"site\", \"name\": \"" + name + "\", \"page\": \"browser\", \"host\": \"alpha.example.org\" } ] }";

    [Fact]
    public void A_Picks_File_With_An_Ordinary_Name_Loads_Which_Proves_The_Fixture_Is_Sound()
    {
        var load = PickStore.Parse(PicksJson("Alpha"));
        Assert.True(load.Status == PickStoreStatus.Loaded, load.Detail);
        Assert.Single(load.Store.Picks);
    }

    [Fact]
    public void Defect_PickStore_One_Saved_Name_With_A_Replacement_Character_Makes_The_Whole_Picks_File_Unreadable()
    {
        // code-2-1 (MEDIUM). b34a7d5 put BlankText.HasBrokenCharacter(Name) into Pick.IsStorable. U+FFFD is what a lone surrogate was saved as before the repair (and a real
        // U+FFFD is a character a folder or a file can carry in its name), so a picks.json written by the last build can hold such a name. PickStore.Load calls IsStorable
        // on every pick it reads and answers Unreadable for the whole file when one fails: no pick at all on the island, every Settings change to the picks refused, until the
        // person edits the file by hand. Expected: a name that is already saved loads (only a name being ADDED is held to the new rule).
        var load = PickStore.Parse(PicksJson("Alpha " + Replacement));
        Assert.True(load.Status == PickStoreStatus.Loaded, $"{load.Status}: {load.Detail}");
        Assert.Single(load.Store.Picks);
    }

    [Fact]
    public void Defect_PickStore_Save_Refuses_The_Whole_List_When_One_Pick_That_Was_Loaded_Holds_A_Replacement_Character()
    {
        // code-2-1, second half: the same IsStorable is the guard of PickStore.Save. A store holding one such pick (made in memory by a reader that does not validate, or by a list
        // that was saved before the repair) cannot be saved at all: every later add, move or switch of ANY pick on any page says "could not be saved".
        using var dir = new Scratch();
        var path = dir.Path_("picks.json");
        var store = new PickStore([
            Pick.ForSite("Alpha", "alpha.example.org", PageIds.Browser),
            new Pick("site:beta.example.org", PickKind.Site, "Beta " + Replacement, PageIds.Browser, Host: "beta.example.org"),
        ]);
        Assert.True(store.Save(path), "a list holding one name with U+FFFD is refused as a whole");
    }

    [Fact]
    public void Defect_HandPicks_A_Folder_Whose_Own_Name_Holds_A_Replacement_Character_Cannot_Be_Added_At_All()
    {
        // code-2-1, third half: a hand-made folder pick is named after the folder (HandPicks.NameFrom(leaf)); the person cannot change that name before it is checked, so a folder
        // that carries U+FFFD (or a lone surrogate, which Windows allows in a name) can no longer be put on the island: "That could not be added: name holds a half character."
        // says what is wrong with a name the person never typed and gives no way out. Expected: the name is made storable (the half character left out) and the folder is added.
        var context = new HandContext(null, [], () => 1UL);
        var result = HandPicks.Folder("Q:\\Invented\\Caf" + Replacement + "Alpha", PageIds.Folders, context);
        Assert.True(result.Ok, $"{result.Code}: {result.Message}");
    }

    private static string ScenesJson(string thingName) =>
        "{ \"schema\": 1, \"scenes\": [ { \"id\": \"scene-1\", \"name\": \"Evening\", \"things\": [ { \"id\": \"site:alpha.example.org\", \"kind\": \"site\", \"name\": \"" + thingName + "\", \"page\": \"scene\", \"host\": \"alpha.example.org\" } ] } ] }";

    [Fact]
    public void A_Scenes_File_With_An_Ordinary_Thing_Loads_Which_Proves_The_Fixture_Is_Sound()
    {
        var load = SceneStore.Parse(ScenesJson("Alpha"));
        Assert.True(load.Status == SceneStoreStatus.Loaded, load.Detail);
        Assert.Single(load.Store.Items.Single().Things);
    }

    [Fact]
    public void Defect_SceneStore_One_Saved_Thing_Whose_Name_Holds_A_Replacement_Character_Makes_Every_Scene_Unreadable()
    {
        // code-2-1, fourth half: a scene keeps copies of picks ("things"), and SceneStore reads them with PickStore.Parse ("the one reader of picks"), so the new rule of
        // Pick.IsStorable reaches scenes.json too: one thing named with U+FFFD (copied from a pick that held it before the repair) and the whole scenes file is Unreadable: no
        // scene, no scene key, until the file is edited by hand. Expected: loads.
        var load = SceneStore.Parse(ScenesJson("Alpha " + Replacement));
        Assert.True(load.Status == SceneStoreStatus.Loaded, $"{load.Status}: {load.Detail}");
    }

    [Fact]
    public void A_Folder_With_An_Ordinary_Name_Is_Added_Which_Proves_The_Fixture_Is_Sound()
    {
        var result = HandPicks.Folder("Q:\\Invented\\Alpha", PageIds.Folders, new HandContext(null, [], () => 1UL));
        Assert.True(result.Ok, $"{result.Code}: {result.Message}");
    }

    // ---- pages already saved with a name the new rule refuses -------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_Built_In_Page_Id_Written_By_The_App_Loads_With_A_Legacy_Hidden_Name_Beside_It()
    {
        var made = PageStore.Default.Create("Work", "#FFAA33");
        var json = made.Store.ToJson().Replace("\"Work\"", "\"Work\\u200B\"");
        var load = PageStore.Parse(json);
        Assert.True(load.Status == PageStoreStatus.Loaded, load.Detail);
        var page = load.Store.Pages[^1];
        Assert.Equal("Work\u200B", page.Name);
        Assert.False(load.Store.Rename(page.Id, "Work again").Refused);
    }

    // ---- the rule itself ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_PageStore_Refuses_A_Name_That_Holds_An_Emoji_Added_To_Unicode_After_The_Runtime_Was_Built()
    {
        // code-2-2 (LOW). BlankText.HasHiddenCharacters refuses OtherNotAssigned: "unassigned" is read from the table of the .NET runtime (10.0.12 here: Unicode 16). Emoji 17.0
        // (September 2025: U+1FACD orca, U+1FAC8 hairy creature, U+1FAEF fight cloud, U+1FA8A trombone; listed as E17.0 in unicode.org's emoji-data.txt) are OtherNotAssigned to it,
        // so a page (and a scene) named with the newest emoji is refused with "no hidden or control characters" although it draws. Expected: accepted.
        Assert.Equal(System.Globalization.UnicodeCategory.OtherNotAssigned, System.Text.Rune.GetUnicodeCategory(new System.Text.Rune(0x1FACD))); // the premise: this runtime does not know it yet
        var made = PageStore.Default.Create("Orca \U0001FACD", "#FFAA33");
        Assert.False(made.Refused, made.Refusal);
    }

    [Fact]
    public void Defect_PageStore_Refuses_An_Emoji_Sequence_Joined_By_A_Zero_Width_Joiner_As_A_Hidden_Character()
    {
        // code-2-3 (LOW). A page named "man + laptop" (U+1F468 U+200D U+1F4BB, one visible picture) holds U+200D (category Format) and is refused with "no hidden or control
        // characters": the person sees nothing hidden. The same for the rainbow flag and every family or profession emoji, and, for text, the zero-width non-joiner that Persian and
        // Hindi spelling needs. The right-to-left override that the rule exists for is a different character (U+202E). Expected: a joiner between two characters that draw is kept.
        var made = PageStore.Default.Create("\U0001F468\u200D\U0001F4BB Work", "#FFAA33");
        Assert.False(made.Refused, made.Refusal);
    }

    [Fact]
    public void The_Right_To_Left_Override_And_A_Name_That_Draws_As_Nothing_Are_Still_Refused()
    {
        foreach (var bad in new[] { "Work\u202E", "\u200B", "\u3164", "Wo\u0000rk", "\u0301" })
            Assert.True(PageStore.Default.Create(bad, "#FFAA33").Refused, $"U+{(int)bad[0]:X4}...");
    }

    [Fact]
    public void Defect_PageStore_Two_Names_That_Draw_The_Same_Through_Canonical_Equivalence_Are_Two_Pages()
    {
        // code-2-4 (LOW). code-1-16 was repaired as "held to the rule scenes keep", but one part of that rule was left behind: SceneStore.CheckName compares names after
        // NormalizationForm.FormC ("names that draw the same are the same: the precomposed and the combining spelling of one letter are one name"); PageStore.CheckName compares
        // the raw text. "Caf" + U+00E9 and "Cafe" + U+0301 (text pasted from a file that came from a Mac is the second form) make two pages that look the same in the strip,
        // in Settings and in the page key list. Expected: the second is refused as taken.
        var first = PageStore.Default.Create("Caf\u00E9", "#FFAA33");
        Assert.False(first.Refused, first.Refusal);
        var second = first.Store.Create("Cafe\u0301", "#33AAFF");
        Assert.True(second.Refused, "two pages named alike were made");
    }
}
