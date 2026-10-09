using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 4: what the stores accept as a name against what they read back. A name the page store or the scene store ACCEPTS when it is typed must be a name the same store READS when the file is
/// loaded at the next start (otherwise the person's pages or scenes are unreadable after a restart). Random names over a pool of awkward characters, built from code points, never typed as text.
/// </summary>
public sealed class Round4NameFuzzTests
{
    private static string U(params int[] values) => string.Concat(values.Select(char.ConvertFromUtf32));

    private static readonly string[] Pool =
    [
        "a", "A", "e", "i", "I", "k", "K", "s", " ", "ss",
        U(0x301), U(0x345), U(0x399), U(0x3B1), U(0x391), U(0x3B9), U(0x200D), U(0x200C), U(0xDF), U(0x1E9E), U(0x212B), U(0xC5), U(0x212A), U(0x130), U(0x131), U(0x17F),
        U(0x1F468), U(0x1F4BB), U(0xFE0F), U(0x1F3FD), U(0x915), U(0x94D), U(0x937), U(0x2800), U(0x3164), U(0xE0100), U(0x1F3F4), U(0xE0067), U(0xE007F), U(0x1C5), U(0x1C4), U(0x1C6),
    ];

    private static string RandomName(Random rng) => string.Concat(Enumerable.Range(0, rng.Next(1, 8)).Select(_ => Pool[rng.Next(Pool.Length)]));

    private static string Esc(string text) => string.Concat(text.EnumerateRunes().Select(r => r.Value is >= 0x20 and < 0x7F ? r.ToString() : $"<{r.Value:X}>"));

    [Fact]
    public void Defect_PageStore_Two_Names_That_Are_Accepted_Together_Are_Read_Back_As_The_Same_Name_And_The_Whole_Pages_File_Is_Unreadable()
    {
        // code-4-1 (LOW, the whole page list when it happens). PageStore.CheckName compares BlankText.SameName (joiners taken out, then FormC) with OrdinalIgnoreCase; PageStore.Parse (the
        // load at the next start) compares the RAW names with OrdinalIgnoreCase. .NET's OrdinalIgnoreCase folds U+0345 (the Greek combining iota below) to U+0399 (capital iota), but FormC
        // joins U+0345 with the letter before it (alpha + U+0345 becomes U+1FB3): so the alpha with the iota below and alpha followed by a capital iota are two names to CheckName
        // and one name to the loader. Both are accepted now; the next start reads pages.json as unreadable ("Two pages have the same name."): every custom page is out of reach
        // and Settings refuses every change of the pages until the file is edited by hand. The same holds for eta, omega and capital alpha followed by U+0345 / U+0399.
        // Expected: what the store accepts when typed, it reads back.
        var rng = new Random(20261007);
        var bases = new[] { U(0x3B1), U(0x391), U(0x3B7), U(0x3C9) };
        var failures = new List<string>();
        foreach (var b in bases)
        {
            var first = PageStore.Default.Create(b + U(0x345), "#112233");
            var second = first.Store?.Create(b + U(0x399), "#112233");
            if (second is null or { Refusal: not null }) continue; // refused: the store is consistent
            var load = PageStore.Parse(second.Store.ToJson());
            if (load.Status != PageStoreStatus.Loaded) failures.Add($"{Esc(b)}: accepted together, read back as {load.Status}: {load.Detail}");
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
        Assert.NotNull(rng);
    }

    [Fact]
    public void Held_Random_Page_Names_That_Are_Accepted_One_By_One_Are_Read_Back_Except_The_Case_Folding_Pairs()
    {
        // The wide net behind code-4-1: 60,000 pairs of random names from a pool of 40 awkward pieces. Every pair the store accepts together must load; the only ones that do not are the Greek
        // pairs of the defect test above (a name with U+0345 beside one with U+0399), which are left out here so that this test is the coverage statement for everything else.
        var rng = new Random(4);
        var checkedPairs = 0;
        var problems = new List<string>();
        for (var i = 0; i < 60_000 && problems.Count < 5; i++)
        {
            var a = RandomName(rng);
            var b = RandomName(rng);
            if ((a + b).Contains(U(0x345), StringComparison.Ordinal) && (a + b).Contains(U(0x399), StringComparison.Ordinal)) continue;
            var first = PageStore.Default.Create(a, "#112233");
            if (first.Refusal is not null) continue;
            var second = first.Store.Create(b, "#445566");
            if (second.Refusal is not null) continue;
            checkedPairs++;
            var load = PageStore.Parse(second.Store.ToJson());
            if (load.Status != PageStoreStatus.Loaded) problems.Add($"{Esc(a)} / {Esc(b)}: {load.Detail}");
            else if (!load.Store.Pages.Select(p => p.Name).SequenceEqual(second.Store.Pages.Select(p => p.Name))) problems.Add($"{Esc(a)} / {Esc(b)}: names changed on the way through the file");
        }

        Assert.True(problems.Count == 0, string.Join(" | ", problems));
        Assert.True(checkedPairs > 2000, $"only {checkedPairs} accepted pairs");
    }

    [Fact]
    public void Held_Random_Scene_Names_That_Are_Accepted_Together_Are_Read_Back()
    {
        // Scenes read a name with the very function that accepts it (SceneStoreFile.TryReadScene calls CheckName), so the pair above cannot happen there.
        var rng = new Random(5);
        var checkedPairs = 0;
        var problems = new List<string>();
        for (var i = 0; i < 40_000 && problems.Count < 5; i++)
        {
            var a = RandomName(rng);
            var b = RandomName(rng);
            var first = SceneStore.Empty.Create(a);
            if (first.Refusal is not null) continue;
            var second = first.Store.Create(b);
            if (second.Refusal is not null) continue;
            checkedPairs++;
            var load = SceneStore.Parse(second.Store.ToJson());
            if (load.Status != SceneStoreStatus.Loaded) problems.Add($"{Esc(a)} / {Esc(b)}: {load.Detail}");
        }

        Assert.True(problems.Count == 0, string.Join(" | ", problems));
        Assert.True(checkedPairs > 1000, $"only {checkedPairs} accepted pairs");
    }
}
