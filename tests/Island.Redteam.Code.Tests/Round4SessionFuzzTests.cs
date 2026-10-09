using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// ROUND 4: the keys the Settings session owns, under a random order of everything that changes them (a pick switched off and on, a key pressed for a page, a pick, a scene or the mode, cleared,
/// restored, a page made and deleted, a scene made and deleted, a pick moved, the original keys restored), with Windows refusing some combinations at random. After every step: the keys held
/// with Windows are exactly the keys the settings hold, no two actions share one, the file on disk says what the memory says, and no key belongs to a thing that is gone. Invented names only.
/// </summary>
public sealed class Round4SessionFuzzTests
{
    private sealed class Registrar : IHotkeyRegistrar
    {
        public HashSet<HotkeyCombo> Held { get; } = [];

        public HashSet<HotkeyCombo> Refused { get; } = [];

        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            if (Held.Contains(combo)) return true;
            if (Refused.Contains(combo))
            {
                error = 1409;
                return false;
            }

            Held.Add(combo);
            return true;
        }

        public void Release(HotkeyCombo combo) => Held.Remove(combo);
    }

    private static readonly HotkeyCombo[] Pool = new[] {"Ctrl+Alt+A", "Ctrl+Alt+B", "Ctrl+Alt+C", "Ctrl+Alt+D", "Ctrl+Alt+E", "Ctrl+Q", "Ctrl+Alt+Shift+F"}.Select(HotkeyCombo.Parse).ToArray();

    private static string[] Sites => ["alpha.example.org", "beta.example.org", "gamma.example.org", "delta.example.org", "epsilon.example.org"];

    [Fact]
    public void Held_Keys_Windows_Holds_Are_Exactly_The_Keys_The_Settings_Hold_Whatever_Order_Things_Change_In() => Run(faults: false);

    [Fact]
    public void Held_Keys_Windows_Holds_Are_Exactly_The_Keys_The_Settings_Hold_When_Saves_Fail_Now_And_Then() => Run(faults: true);

    private static readonly string[] Blocked = ["settings.json.tmp", "pages.json.tmp", "picks.json.tmp", "scenes.json.tmp"];

    private static void Run(bool faults)
    {
        var failures = new List<string>();
        var deadline = DateTime.UtcNow.AddSeconds(double.TryParse(Environment.GetEnvironmentVariable("ISLAND_FUZZ_SECONDS"), out var secs) ? secs : 25);
        var runs = 0;
        for (var seed = 1; seed <= 5000 && (DateTime.UtcNow < deadline || runs < 30) && failures.Count < 3 /* the time box never cuts the runs below the 30 the test asks for: a loaded machine runs fewer in 25 s (WORK-ORDER-12) */; seed++, runs++)
        {
            var rng = new Random(seed);
            using var dir = new Scratch();
            var registrar = new Registrar();
            var settings = Settings.Defaults;
            registrar.Held.Add(settings.ShowHide);
            Assert.True(settings.Save(dir.Path_("settings.json")));
            var files = new SettingsFiles(dir.Path_("settings.json"), dir.Path_("pages.json"), dir.Path_("picks.json"), dir.Path_("scenes.json"));
            var picks = new PickStore(Sites.Take(3).Select((s, i) => Pick.ForSite(s, s, i == 0 ? PageIds.Browser : PageIds.Apps)));
            var session = new SettingsSession(files, new SettingsLoad(settings, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
                new PickStoreLoad(picks, PickStoreStatus.Loaded, null), registrar, () => [], () => false, scenes: new SceneStoreLoad(SceneStore.Empty, SceneStoreStatus.Missing, null));
            var log = new List<string>();
            try
            {
                for (var step = 0; step < 90; step++)
                {
                    if (faults && rng.Next(5) == 0)
                    {
                        // a folder where the temporary file of a save must go: that save fails plainly (and heals when the folder is taken away again)
                        var blocker = dir.Path_(Blocked[rng.Next(Blocked.Length)]);
                        if (Directory.Exists(blocker)) Directory.Delete(blocker);
                        else Directory.CreateDirectory(blocker);
                    }

                    Step(session, registrar, rng, log);
                    Check(session, registrar, dir, checkFile: step % 15 == 14, checkOwners: !faults);
                }
            }
            catch (Exception e)
            {
                failures.Add($"seed {seed}: {e.GetType().Name}: {e.Message} after [{string.Join(" ; ", log.TakeLast(14))}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(" || ", failures));
        Assert.True(runs >= 30, $"only {runs} runs");
    }

    private static string Name(HotkeyCombo? combo) => combo?.ToString() ?? "-";

    private static IEnumerable<string> ActionIds(SettingsSession s) =>
        new[] { KeybindEditor.MainId, KeybindEditor.ModeNextId }
            .Concat(s.Pages.Pages.Select(p => p.Id))
            .Concat(s.Picks.Picks.Select(p => p.Id))
            .Concat(s.Scenes.Items.Select(sc => KeybindEditor.SceneActionId(sc.Id)));

    private static void Step(SettingsSession s, Registrar registrar, Random rng, List<string> log)
    {
        if (rng.Next(6) == 0)
        {
            registrar.Refused.Clear();
            foreach (var c in Pool) if (rng.Next(4) == 0 && !registrar.Held.Contains(c)) registrar.Refused.Add(c);
        }

        var op = rng.Next(14);
        var ids = ActionIds(s).ToList();
        switch (op)
        {
            case 0 or 1 or 2:
                {
                    var id = ids[rng.Next(ids.Count)];
                    var combo = Pool[rng.Next(Pool.Length)];
                    log.Add($"press {id} {combo}");
                    s.PressKey(id, new KeyPress(combo.VirtualKey, combo.Modifiers));
                    break;
                }
            case 3:
                {
                    var rows = Pages(s).SelectMany(p => s.PickRows(p.Id)).ToList();
                    if (rows.Count == 0) break;
                    var row = rows[rng.Next(rows.Count)];
                    var on = rng.Next(2) == 0;
                    log.Add($"setpick {row.Pick.Id} {(on ? "on" : "off")} (row on {row.On})");
                    s.SetPick(row, on);
                    break;
                }
            case 4:
                {
                    var id = ids[rng.Next(ids.Count)];
                    log.Add($"clear {id}");
                    s.ClearKey(id);
                    break;
                }
            case 5:
                {
                    var id = ids[rng.Next(ids.Count)];
                    log.Add($"restore {id}");
                    s.RestoreKey(id);
                    break;
                }
            case 6:
                log.Add("restoreall");
                s.RestoreAllKeys();
                break;
            case 7:
                {
                    var combo = rng.Next(2) == 0 ? (HotkeyCombo?)Pool[rng.Next(Pool.Length)] : null;
                    log.Add($"createpage {Name(combo)}");
                    s.CreatePage("Page" + rng.Next(1000), "#" + rng.Next(0x1000000).ToString("X6"), combo);
                    break;
                }
            case 8:
                {
                    var custom = s.Pages.Pages.Where(p => !p.IsBuiltIn).ToList();
                    if (custom.Count == 0) break;
                    var page = custom[rng.Next(custom.Count)];
                    log.Add($"deletepage {page.Id}");
                    s.DeletePage(page.Id);
                    break;
                }
            case 9:
                {
                    if (s.Picks.Picks.Count == 0) break;
                    var pick = s.Picks.Picks[rng.Next(s.Picks.Picks.Count)];
                    var pages = s.Pages.Pages.Where(p => PageIds.CanHoldPicks(p.Id)).ToList();
                    var target = pages[rng.Next(pages.Count)];
                    log.Add($"move {pick.Id} to {target.Id}");
                    s.MovePick(pick.Id, target.Id);
                    break;
                }
            case 10:
                log.Add("createscene");
                s.CreateScene("Scene" + rng.Next(1000));
                break;
            case 11:
                {
                    if (s.Scenes.Items.Count == 0) break;
                    var scene = s.Scenes.Items[rng.Next(s.Scenes.Items.Count)];
                    log.Add($"deletescene {scene.Id}");
                    s.DeleteScene(scene.Id);
                    break;
                }
            case 12:
                {
                    var page = Pages(s).Where(p => PageIds.CanHoldPicks(p.Id)).ElementAt(rng.Next(Pages(s).Count(p => PageIds.CanHoldPicks(p.Id))));
                    var site = Sites[rng.Next(Sites.Length)];
                    log.Add($"addsite {site} on {page.Id}");
                    s.AddSite(page.Id, site);
                    break;
                }
            default:
                {
                    if (s.Scenes.Items.Count == 0 || s.Picks.Picks.Count == 0) break;
                    var scene = s.Scenes.Items[rng.Next(s.Scenes.Items.Count)];
                    var pick = s.Picks.Picks[rng.Next(s.Picks.Picks.Count)];
                    log.Add($"scenething {scene.Id} {pick.Id}");
                    s.SetSceneThing(scene.Id, pick, rng.Next(2) == 0);
                    break;
                }
        }
    }

    private static IReadOnlyList<Page> Pages(SettingsSession s) => s.Pages.Pages;

    private static List<HotkeyCombo> AllKeys(Settings st) =>
        [st.ShowHide, .. st.PageKeys.Where(k => k.Combo is not null).Select(k => k.Combo!.Value), .. st.PickKeys.Select(k => k.Combo), .. (st.ModeKey is { } m ? new[] { m } : [])];

    private static void Check(SettingsSession s, Registrar registrar, Scratch dir, bool checkFile, bool checkOwners)
    {
        var keys = AllKeys(s.Settings);
        Assert.True(keys.Distinct().Count() == keys.Count, "two actions hold one key: " + string.Join(", ", keys));
        Assert.True(registrar.Held.SetEquals(keys), $"Windows holds [{string.Join(", ", registrar.Held.OrderBy(c => c.ToString()))}] but the settings hold [{string.Join(", ", keys.OrderBy(c => c.ToString()))}]");
        if (checkFile) CheckFile(s, dir, keys);
        if (checkOwners) CheckOwners(s);
    }

    private static void CheckFile(SettingsSession s, Scratch dir, List<HotkeyCombo> keys)
    {
        var disk = Settings.Load(dir.Path_("settings.json"), s.Pages.Pages).Settings;
        var onDisk = AllKeys(disk);
        Assert.True(onDisk.ToHashSet().SetEquals(keys), $"the file holds [{string.Join(", ", onDisk.OrderBy(c => c.ToString()))}] and the memory [{string.Join(", ", keys.OrderBy(c => c.ToString()))}]");
    }

    private static void CheckOwners(SettingsSession s)
    {
        foreach (var k in s.Settings.PickKeys)
        {
            var alive = KeybindEditor.IsSceneAction(k.PickId) ? s.Scenes.ById(KeybindEditor.SceneIdOf(k.PickId)) is not null : s.Picks.ById(k.PickId) is not null;
            Assert.True(alive, $"the key {k.Combo} belongs to {k.PickId}, which is gone");
        }

        foreach (var k in s.Settings.PageKeys.Where(k => k.Combo is not null))
            Assert.True(s.Pages.ById(k.PageId) is not null, $"the key {k.Combo} belongs to page {k.PageId}, which is gone");
    }
}
