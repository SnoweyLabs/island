using System.Diagnostics;
using System.Text;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: every file the app reads and keeps (settings.json with its new "movingLight", pages.json, picks.json, scenes.json): unreadable, half written, huge, older, newer,
/// with the new field wrong; and the way each is written back. Invented names only.
/// </summary>
public class FileEdgeTests
{
    private static readonly string PicksText = new PickStore([Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null), Pick.ForSite("Example Site", "example.org", PageIds.Media), Pick.ForFolder("Downloads", PageIds.Folders)]).ToJson();

    private static readonly string ScenesText = SceneText();

    private static string SceneText()
    {
        var created = SceneStore.Empty.Create("Work");
        var withThing = created.Store.SetThings(created.Scene!.Id, [Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null)]);
        return withThing.Store.ToJson();
    }

    // ---- the old field (gone in WORK-ORDER-13) ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\"graphicscard\"")]
    [InlineData("\"HalfRate\"")]
    [InlineData("\"ASBEFORE\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("\"compositor\"")]
    public void MovingLight_Is_Not_A_Setting_Any_More_Whatever_An_Old_File_Holds_The_File_Loads_And_Nothing_Else_Changes(string value)
    {
        var load = Settings.Parse("{\"movingLight\": " + value + ", \"idleSeconds\": 20}");
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(20, load.Settings.IdleSeconds);
    }

    [Fact]
    public void MovingLight_A_Save_Does_Not_Write_It()
    {
        using var s = new Scratch();
        var path = s.Write("settings.json", "{\"hotkeys\": {\"showHide\": \"Ctrl+Q\"}, \"idleSeconds\": 8, \"movingLight\": \"asbefore\"}");
        var load = Settings.Load(path);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.True(load.Settings.Save(path));
        Assert.DoesNotContain("movingLight", File.ReadAllText(path));
        Assert.Equal(load.Settings, Settings.Load(path).Settings);
    }

    private static SettingsSession SessionIn(Scratch s)
    {
        var settingsPath = s.Path_("settings.json");
        Assert.True(Settings.Defaults.Save(settingsPath));
        var files = new SettingsFiles(settingsPath, s.Path_("pages.json"), s.Path_("picks.json"), s.Path_("scenes.json"));
        return new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null), new AcceptAll(), () => [], () => false, null, () => [], null, new SceneStoreLoad(SceneStore.Empty, SceneStoreStatus.Loaded, null), null, null);
    }

    private sealed class AcceptAll : IHotkeyRegistrar
    {
        public bool TryRegister(HotkeyCombo combo, out int error)
        {
            error = 0;
            return true;
        }

        public void Release(HotkeyCombo combo)
        {
        }
    }

    // ---- half written ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_Proper_Prefix_Of_Every_File_Is_Unreadable_And_Never_Throws()
    {
        var texts = new (string Name, string Text, Func<string, bool> Loaded)[]
        {
            ("settings", Settings.Defaults.ToJson(), t => Settings.Parse(t).Status == SettingsStatus.Loaded),
            ("pages", PageStore.Default.ToJson(), t => PageStore.Parse(t).Status == PageStoreStatus.Loaded),
            ("picks", PicksText, t => PickStore.Parse(t).Status == PickStoreStatus.Loaded),
            ("scenes", ScenesText, t => SceneStore.Parse(t).Status == SceneStoreStatus.Loaded),
        };
        foreach (var (name, text, loaded) in texts)
        {
            Assert.True(loaded(text), name + " itself must load");
            for (var i = 0; i < text.Length; i++)
            {
                var prefix = text[..i];
                bool ok = false;
                var ex = Record.Exception(() => ok = loaded(prefix));
                Assert.Null(ex);
                Assert.False(ok, $"{name}: the first {i} characters loaded as a whole file");
            }
        }
    }

    [Fact]
    public void A_File_With_A_Zero_Filled_Tail_As_NTFS_Leaves_After_A_Crash_Is_Unreadable_And_Is_Left_Exactly_As_It_Was()
    {
        using var s = new Scratch();
        var good = Settings.Defaults.ToJson();
        var bytes = Encoding.UTF8.GetBytes(good).Concat(new byte[300]).ToArray();
        var path = s.Path_("settings.json");
        File.WriteAllBytes(path, bytes);
        var load = Settings.Load(path);
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.Equal(bytes, File.ReadAllBytes(path)); // loading never writes
        Assert.Equal(Settings.Defaults, load.Settings);
    }

    [Fact]
    public void Newer_Schema_Is_Refused_By_Every_Store_With_The_Newer_Words_And_The_File_Is_Never_Written()
    {
        using var s = new Scratch();
        var cases = new (string File, string Text, Func<string, (bool Newer, bool Unreadable)> Load)[]
        {
            ("settings.json", "{\"schema\": 2, \"idleSeconds\": 9}", p => { var l = Settings.Load(p); return (FileSchema.IsNewer(l.Detail), l.Status == SettingsStatus.Unreadable); }),
            ("pages.json", "{\"schema\": 2, \"pages\": []}", p => { var l = PageStore.Load(p); return (FileSchema.IsNewer(l.Detail), l.Status == PageStoreStatus.Unreadable); }),
            ("picks.json", "{\"schema\": 3, \"picks\": []}", p => { var l = PickStore.Load(p); return (FileSchema.IsNewer(l.Detail), l.Status == PickStoreStatus.Unreadable); }),
            ("scenes.json", "{\"version\": 2, \"scenes\": []}", p => { var l = SceneStore.Load(p); return (FileSchema.IsNewer(l.Detail), l.Status == SceneStoreStatus.Unreadable); }),
        };
        foreach (var (file, text, load) in cases)
        {
            var path = s.Write(file, text);
            var (newer, unreadable) = load(path);
            Assert.True(newer && unreadable, file);
            Assert.Equal(text, File.ReadAllText(path));
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("1e400")]
    [InlineData("null")]
    [InlineData("2147483648")]
    public void A_Schema_That_Is_Not_A_Whole_Number_From_One_Is_Unreadable_Not_Newer_Not_Loaded(string value)
    {
        var load = Settings.Parse("{\"schema\": " + value + "}");
        Assert.Equal(SettingsStatus.Unreadable, load.Status);
        Assert.False(FileSchema.IsNewer(load.Detail) && value is "0" or "-1" or "1.5");
    }

    [Fact]
    public void Deep_Nesting_Garbage_And_Odd_Roots_Are_Unreadable_Without_An_Exception()
    {
        var deep = new string('[', 5000) + new string(']', 5000);
        foreach (var text in new[] { deep, "[" + deep + "]", "{\"hotkeys\":" + deep + "}", "null", "7", "\"x\"", "{", "}", "\0", "﻿", "{\"a\":1,}x", "/* c */ {}" })
        {
            Assert.Null(Record.Exception(() => Settings.Parse(text)));
            Assert.Null(Record.Exception(() => PageStore.Parse(text)));
            Assert.Null(Record.Exception(() => PickStore.Parse(text)));
            Assert.Null(Record.Exception(() => SceneStore.Parse(text)));
        }
    }

    [Fact]
    public void A_Very_Large_Settings_File_With_Unknown_Fields_Loads_In_Reasonable_Time()
    {
        using var s = new Scratch();
        var path = s.Path_("settings.json");
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            w.Write("{\"idleSeconds\": 12, \"junk\": \"");
            var chunk = new string('x', 1 << 20);
            for (var i = 0; i < 40; i++) w.Write(chunk);
            w.Write("\"}");
        }

        var clock = Stopwatch.StartNew();
        var load = Settings.Load(path);
        Assert.Equal(SettingsStatus.Loaded, load.Status);
        Assert.Equal(12, load.Settings.IdleSeconds);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    [Fact]
    public void A_Directory_In_The_Way_Is_Missing_On_Load_And_False_On_Save_Never_An_Exception()
    {
        using var s = new Scratch();
        var path = s.Path_("settings.json");
        Directory.CreateDirectory(path);
        Assert.Equal(SettingsStatus.Missing, Settings.Load(path).Status);
        Assert.False(Settings.EnsureExists(path));
        Assert.False(Settings.Defaults.Save(path));
        Assert.True(Directory.Exists(path));
    }

    // ---- how each file is written back ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Defect_Settings_Save_Writes_The_Live_File_In_Place_So_A_Crash_Or_A_Reader_Meets_A_Half_Written_File()
    {
        // code-1-3 (MEDIUM): pages.json, picks.json and scenes.json are written to "<name>.tmp" and moved over the file (a reader sees the old file or the new, never half). settings.json
        // (Settings.Save, called by the tray's mode and glass, the settings screen and the first-start) is written with File.WriteAllText straight onto the live file, which truncates it first.
        // A reader that has the file open (an editor, a backup or sync program, the next start after a power cut) can meet an empty or cut file, which loads as Unreadable and drops every key.
        // Expected: the way pages.json does it. Shown here without any timing: a handle opened before the save still reads the OLD, complete text after an atomic replace; after an in-place
        // write it reads the new text of the same file object (the file was changed under it).
        using var s = new Scratch();
        var path = s.Path_("settings.json");
        Assert.True(Settings.Defaults.Save(path));
        var old = File.ReadAllText(path);
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var changed = Settings.Defaults with { IdleSeconds = 33 };
        Assert.True(changed.Save(path));
        using var text = new StreamReader(reader);
        Assert.Equal(old, text.ReadToEnd());
    }

    [Fact]
    public void Pages_Picks_And_Scenes_Write_The_Whole_New_Text_And_Leave_No_Temporary_File()
    {
        using var s = new Scratch();
        var pages = s.Path_("pages.json");
        var picks = s.Path_("picks.json");
        var scenes = s.Path_("scenes.json");
        Assert.True(PageStore.Default.Rename(PageIds.Media, "Tunes").Store.Save(pages));
        Assert.True(new PickStore([Pick.ForFolder("Documents", PageIds.Folders)]).Save(picks));
        Assert.True(SceneStore.Empty.Create("Work").Store.Save(scenes));
        Assert.Contains("Tunes", File.ReadAllText(pages));
        Assert.Contains("folder:documents", File.ReadAllText(picks));
        Assert.Contains("Work", File.ReadAllText(scenes));
        Assert.Empty(Directory.GetFiles(s.Folder, "*.tmp"));
    }

    [Fact]
    public async Task Defect_Stores_Give_Up_At_The_First_Sharing_Violation_Of_A_Reader_That_Lets_Go_A_Moment_Later()
    {
        // code-1-6 (LOW, safe improvement): pages.json, picks.json and scenes.json are moved over with File.Move(temp, path, overwrite: true) once; a program that has the live file open for
        // a moment (a virus scanner reading the file it was told changed, a backup or search indexer, an editor) makes the move fail with a sharing violation, Save says false, and the change
        // stays in memory only: a pick dragged off the island is only logged ("the picks could not be saved") and is back after the next start. Windows' own advice for a replace is to retry
        // a few times a few tens of milliseconds apart. Expected: a reader that lets go after 150 ms does not lose the save (the store retries for a short bounded time).
        using var s = new Scratch();
        var failed = new List<string>();
        foreach (var (name, first, save) in new (string, Func<string, bool>, Func<string, bool>)[]
        {
            ("pages.json", p => PageStore.Default.Save(p), p => PageStore.Default.Rename(PageIds.Media, "Tunes").Store.Save(p)),
            ("picks.json", p => PickStore.Empty.Save(p), p => new PickStore([Pick.ForFolder("Documents", PageIds.Folders)]).Save(p)),
            ("scenes.json", p => SceneStore.Empty.Save(p), p => SceneStore.Empty.Create("Work").Store.Save(p)),
        })
        {
            var path = s.Path_(name);
            Assert.True(first(path));
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Run(async () =>
            {
                await Task.Delay(150);
                held.Dispose();
            });
            if (!save(path)) failed.Add(name);
            await release;
        }

        Assert.True(failed.Count == 0, "gave up at the first sharing violation: " + string.Join(", ", failed));
    }

    [Fact]
    public void Picks_Failed_Save_Leaves_No_Temporary_File_Behind()
    {
        using var s = new Scratch();
        var path = s.Write("picks.json", PicksText);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.False(new PickStore([Pick.ForFolder("Documents", PageIds.Folders)]).Save(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(PicksText, File.ReadAllText(path));
    }

    [Fact]
    public void Defect_Pages_Failed_Save_Leaves_A_Temporary_File_Behind_Which_Picks_Delete()
    {
        // code-1-4 (LOW): PickStore.Save deletes "<name>.tmp" when the move fails (WORK-ORDER-5 attack A5-13: "the list that was refused must not stay behind in a file of its own");
        // PageStore.Save was written the same way before that repair and never given it. Failing input: pages.json read-only (a sync program, a person) -> Save returns false, pages.json.tmp stays
        // with the whole refused page list. Expected: no pages.json.tmp.
        using var s = new Scratch();
        var path = s.Path_("pages.json");
        Assert.True(PageStore.Default.Save(path));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.False(PageStore.Default.Create("Extra", "#112233").Store.Save(path));
        Assert.False(File.Exists(path + ".tmp"), "pages.json.tmp was left behind");
    }

    [Fact]
    public void Defect_Scenes_Failed_Save_Leaves_A_Temporary_File_Behind_Which_Picks_Delete()
    {
        // code-1-5 (LOW): the same for scenes.json: SceneStore.Save has no delete of "<name>.tmp" on a failed move. Expected: no scenes.json.tmp.
        using var s = new Scratch();
        var path = s.Path_("scenes.json");
        Assert.True(SceneStore.Empty.Save(path));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.False(SceneStore.Empty.Create("Work").Store.Save(path));
        Assert.False(File.Exists(path + ".tmp"), "scenes.json.tmp was left behind");
    }

    [Fact]
    public void Settings_Save_On_A_Read_Only_File_Says_False_And_Keeps_The_File()
    {
        using var s = new Scratch();
        var path = s.Path_("settings.json");
        Assert.True(Settings.Defaults.Save(path));
        var before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.False((Settings.Defaults with { IdleSeconds = 40 }).Save(path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Settings_Round_Trip_Keeps_Every_Field_Including_The_New_One()
    {
        using var s = new Scratch();
        var path = s.Path_("settings.json");
        var all = Settings.Defaults with { Mode = Mode.DND, NoticeSeconds = 12, ShowPill = false, Glass = GlassKind.Darker, IdleSeconds = 17, StartWithWindows = true };
        Assert.True(all.Save(path));
        Assert.Equal(all, Settings.Load(path).Settings);
    }
}
