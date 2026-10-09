using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.Attack7A.Tests;

/// <summary>Scenes (200 things, 100 scenes, hidden characters, keys), first start, the notice fallback, the mode mark and the new settings fields.</summary>
public class SceneAttackTests
{
    private static Pick Site(int i) => Pick.ForSite("S" + i, $"h{i}.example.com", "apps");

    private static SceneStore WithThings(int scenes, int things)
    {
        var store = SceneStore.Empty;
        for (var s = 0; s < scenes; s++)
        {
            var made = store.Create("Scene " + s);
            Assert.False(made.Refused, made.Refusal);
            store = made.Store;
            var set = store.SetThings(made.Scene!.Id, Enumerable.Range(0, things).Select(Site));
            Assert.False(set.Refused, set.Refusal);
            store = set.Store;
        }

        return store;
    }

    [Fact]
    public void Holds_A_Hundred_Scenes_Of_Two_Hundred_Things_Round_Trip_Through_The_File()
    {
        using var temp = new TempFolder();
        var store = WithThings(100, 200);
        Assert.True(store.Save(temp.File("scenes.json")));
        var load = SceneStore.Load(temp.File("scenes.json"));
        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Null(load.Detail);
        Assert.Equal(100, load.Store.Items.Count);
        Assert.All(load.Store.Items, s => Assert.Equal(200, s.Things.Count));
        Assert.Equal(store.ToJson(), load.Store.ToJson());
    }

    [Fact]
    public void Holds_The_Hundred_And_First_Scene_And_The_Two_Hundred_And_First_Thing_Are_Refused()
    {
        var store = WithThings(100, 1);
        Assert.True(store.Create("One more").Refused);
        var one = WithThings(1, 200);
        var id = one.Items[0].Id;
        Assert.True(one.SetThings(id, Enumerable.Range(0, 201).Select(Site)).Refused);
        Assert.True(one.AddThing(id, Site(500)).Refused);
        Assert.False(one.AddThing(id, Site(3)).Refused); // already there: no change, no refusal
    }

    [Fact]
    public void Holds_A_Hostile_File_Of_Two_Hundred_Scenes_And_Things_Is_Cut_Not_Crashed()
    {
        var things = string.Join(',', Enumerable.Range(0, 250).Select(i => $"{{\"id\":\"site:h{i}.example.com\",\"kind\":\"site\",\"name\":\"S{i}\",\"page\":\"scene\",\"host\":\"h{i}.example.com\"}}"));
        var scenes = string.Join(',', Enumerable.Range(0, 130).Select(i => $"{{\"id\":\"scene-{i}\",\"name\":\"N{i}\",\"things\":[{things}]}}"));
        var load = SceneStore.Parse($"{{\"version\":1,\"scenes\":[{scenes}]}}");
        Assert.Equal(SceneStoreStatus.Loaded, load.Status);
        Assert.Equal(100, load.Store.Items.Count);
        Assert.All(load.Store.Items, s => Assert.True(s.Things.Count <= 200));
        Assert.Contains("left out", load.Detail);
    }

    [Theory]
    [InlineData("\u3164")]
    [InlineData("\u2800")]
    [InlineData("\uFE0F")]
    [InlineData("\u1160")]
    public void Defect_A_Scene_Name_Made_Of_A_Blank_Looking_Character_Is_Accepted(string blank)
    {
        Assert.True(SceneStore.Empty.Create(blank).Refused, "a scene whose name draws as nothing");
    }

    [Fact]
    public void Defect_Two_Scenes_Whose_Names_Look_The_Same_Are_Both_Accepted()
    {
        var first = SceneStore.Empty.Create("caf\u00E9");
        Assert.False(first.Refused);
        Assert.True(first.Store.Create("cafe\u0301").Refused, "precomposed and combining spellings of the same word");
    }

    [Fact]
    public void Holds_Names_With_Controls_Zero_Width_Direction_Lone_Surrogates_And_Length_Are_Refused_And_Spaces_Are_Tidied()
    {
        foreach (var bad in new[] { "a\0b", "a\u200Bb", "\u202Eabc", "a\uD83D", "\uDE00b", new string('x', 25), "", "   ", "\t", "a\u00ADb", "\uFEFFa", "a\uE000" })
            Assert.True(SceneStore.Empty.Create(bad).Refused, bad);
        var ok = SceneStore.Empty.Create("  Work   mode \t ");
        Assert.Equal("Work mode", ok.Scene!.Name);
        Assert.True(ok.Store.Create("WORK MODE").Refused);
        Assert.False(SceneStore.Empty.Create(new string('x', 24)).Refused);
        Assert.False(SceneStore.Empty.Create(string.Concat(Enumerable.Repeat("😀", 12))).Refused);
    }

    [Fact]
    public void Holds_A_Thing_That_Is_A_Site_A_Program_Or_A_Folder_Is_Planned_In_Order_And_Never_Closed()
    {
        var program = Pick.ForProgram("Alpha", "apps", "alpha.exe", null);
        var gone = Pick.ForProgram("Gone", "apps", "gone.exe", null);
        var folder = Pick.ForFolder("Downloads", "apps");
        var site = Pick.ForSite("Site", "example.org", "apps");
        var made = SceneStore.Empty.Create("Mix");
        var set = made.Store.SetThings(made.Scene!.Id, [program, gone, folder, site, program]);
        var scene = set.Scene!;
        Assert.Equal(4, scene.Things.Count);
        var installed = ScenePlans.InstalledIn([new InstalledProgram("Alpha", "alpha.exe", null, "x")]);
        var plan = ScenePlans.For(scene, OpenSnapshot.Empty, installed);
        Assert.Equal(scene.Things.Select(t => t.Id), plan.Steps.Select(s => s.Thing.Id));
        Assert.Equal(SceneAction.Skip, plan.Steps[1].Action);
        Assert.Equal(["Gone"], plan.SkippedNames);
        Assert.Equal(3, plan.OpenedCount);
        Assert.Equal(["Open", "BringForward", "Skip"], Enum.GetNames<SceneAction>().OrderBy(n => n == "Open" ? 0 : n == "BringForward" ? 1 : 2));
    }

    [Fact]
    public void Holds_A_Scene_Built_Directly_With_Two_Hundred_And_One_Things_And_Repeats_Plans_At_Most_Two_Hundred_Distinct()
    {
        var things = Enumerable.Range(0, 201).Select(i => Scenes.ThingFrom(Site(i))!).Concat(Enumerable.Range(0, 50).Select(i => Scenes.ThingFrom(Site(i))!)).ToList();
        var plan = ScenePlans.For(new Scene("scene-1", "Big", things), OpenSnapshot.Empty, _ => true);
        Assert.Equal(200, plan.Steps.Count);
        Assert.Equal(200, plan.Steps.Select(s => s.Thing.Id).Distinct().Count());
    }

    [Fact]
    public void Holds_The_Missing_Message_Stays_Under_The_Balloon_Limit_For_Any_Names()
    {
        var r = new Random(11);
        string[] pool = ["", "A", "Spotify", new string('x', 300), "\u202Eevil", "a\u200Bb", "<n> <names> <scene>", "😀😀😀😀", "x\0y", "  a   b  ", "\uD83D", new string('é', 120)];
        for (var round = 0; round < 3000; round++)
        {
            var names = Enumerable.Range(0, r.Next(1, 201)).Select(_ => pool[r.Next(pool.Length)]).ToList();
            var refusal = SceneRefusals.PartMissing(pool[r.Next(pool.Length)] + pool[r.Next(pool.Length)], names)!;
            Assert.True(refusal.Message.Length <= SceneRefusals.MaxMessageLength + 3, $"{refusal.Message.Length}: {refusal.Message}");
            Assert.DoesNotContain(refusal.Message, c => char.IsControl(c) || c is '\u202E' or '\u200B');
        }

        Assert.Null(SceneRefusals.PartMissing("x", []));
    }

    [Fact]
    public void Holds_The_Held_Key_Guard_Runs_Once_For_A_Hold_And_Again_After_A_Pause_Or_A_Release()
    {
        var g = new HeldKeyGuard();
        Assert.True(g.Accept("scene-1", 0));
        for (var t = 100; t < 3_600_000; t += 100) Assert.False(g.Accept("scene-1", t));
        Assert.True(g.Accept("scene-1", 3_600_000 + 1100));
        g.Released("scene-1");
        Assert.True(g.Accept("scene-1", 3_600_000 + 1101));
        Assert.True(g.Accept("scene-2", 3_600_000 + 1102)); // another scene's key is its own
        Assert.True(g.Accept("scene-1", 0)); // a clock that went backwards counts as a new press
    }

    [Fact]
    public void Defect_The_Held_Key_Guard_Swallows_A_New_Press_When_The_Clock_Difference_Overflows()
    {
        var g = new HeldKeyGuard();
        Assert.True(g.Accept("scene-1", -1));
        Assert.True(g.Accept("scene-1", long.MaxValue));
    }

    // ---- keys through the session --------------------------------------------------

    private static readonly HotkeyCombo[] Pool = [.. new[] { "Ctrl+Alt+A", "Ctrl+Alt+B", "Ctrl+Alt+C", "Ctrl+Alt+D", "Ctrl+Alt+E", "Ctrl+Alt+Shift+1" }.Select(HotkeyCombo.Parse)];

    private static HashSet<HotkeyCombo> AllKeys(Settings s)
    {
        var set = new HashSet<HotkeyCombo> { s.ShowHide };
        foreach (var k in s.PageKeys) if (k.Combo is { } c) set.Add(c);
        foreach (var k in s.PickKeys) set.Add(k.Combo);
        if (s.ModeKey is { } m) set.Add(m);
        return set;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Holds_Scene_Keys_Mixed_With_Page_Pick_Main_And_Mode_Keys_Stay_Unique_Held_And_On_Disk(int seed)
    {
        using var temp = new TempFolder();
        var reg = new PretendRegistrar();
        reg.TryRegister(Settings.Defaults.ShowHide, out _); // the app holds the main key from its start
        var alpha = Make.Program("Alpha");
        var session = Make.Session(temp, reg, [alpha]);
        var r = new Random(seed);
        for (var step = 0; step < 1500; step++)
        {
            var sceneIds = session.Scenes.Items.Select(s => s.Id).ToList();
            var pickId = r.Next(5) == 0 ? "scene:ghost" : sceneIds.Count > 0 && r.Next(3) > 0 ? KeybindEditor.SceneActionId(sceneIds[r.Next(sceneIds.Count)]) : null;
            var combo = Pool[r.Next(Pool.Length)];
            switch (r.Next(11))
            {
                case 0 or 1: session.CreateScene("S" + r.Next(40)); break;
                case 2 when sceneIds.Count > 0: session.DeleteScene(sceneIds[r.Next(sceneIds.Count)]); break;
                case 3 or 4 when pickId is not null: session.PressKey(pickId, new KeyPress(combo.VirtualKey, combo.Modifiers)); break;
                case 5: session.PressKey(PageIds.Apps, new KeyPress(combo.VirtualKey, combo.Modifiers)); break;
                case 6: session.PressKey(alpha.Id, new KeyPress(combo.VirtualKey, combo.Modifiers)); break;
                case 7: session.PressKey(KeybindEditor.MainId, new KeyPress(combo.VirtualKey, combo.Modifiers)); break;
                case 8: session.PressKey(KeybindEditor.ModeNextId, new KeyPress(combo.VirtualKey, combo.Modifiers)); break;
                case 9: if (r.Next(4) == 0) session.RestoreAllKeys(); else if (pickId is not null) session.ClearKey(pickId); break;
                case 10 when sceneIds.Count > 0: session.RenameScene(sceneIds[r.Next(sceneIds.Count)], "R" + r.Next(40)); break;
            }

            var s = session.Settings;
            var all = s.PageKeys.Select(k => k.Combo).Concat(s.PickKeys.Select(k => (HotkeyCombo?)k.Combo)).Append(s.ModeKey).Append(s.ShowHide).Where(c => c is not null).ToList();
            Assert.True(all.Count == all.Distinct().Count(), $"seed {seed} step {step}: a combination is shared");
            Assert.True(AllKeys(s).SetEquals(reg.Held), $"seed {seed} step {step}: Windows holds {reg.Held.Count}, settings say {AllKeys(s).Count}");
            foreach (var k in s.PickKeys.Where(k => KeybindEditor.IsSceneAction(k.PickId)))
                Assert.NotNull(session.Scenes.ById(KeybindEditor.SceneIdOf(k.PickId)));
            var disk = Settings.Load(temp.File("settings.json"));
            Assert.Equal(SettingsStatus.Loaded, disk.Status);
            Assert.Equal(s, disk.Settings);
            var scenes = SceneStore.Load(temp.File("scenes.json"));
            Assert.Equal(session.Scenes.ToJson(), scenes.Store.ToJson());
        }
    }

    [Fact]
    public void Defect_A_Deleted_Scenes_Key_That_Could_Not_Be_Given_Back_Goes_To_The_Next_Scene_That_Reuses_The_Id()
    {
        using var temp = new TempFolder();
        var reg = new PretendRegistrar();
        var session = Make.Session(temp, reg);
        Assert.True(session.CreateScene("First").Ok);
        var id = session.Scenes.Items[0].Id;
        var combo = Pool[0];
        Assert.True(session.PressKey(KeybindEditor.SceneActionId(id), new KeyPress(combo.VirtualKey, combo.Modifiers)).Ok);

        File.SetAttributes(temp.File("settings.json"), FileAttributes.ReadOnly); // the settings file cannot be saved now
        var deleted = session.DeleteScene(id);
        Assert.True(deleted.Changed); // the scene is gone; the answer says only that a key is still held
        Assert.Empty(session.Scenes.Items);

        Assert.True(session.CreateScene("Second").Ok);
        var created = session.Scenes.Items[0];
        // A scene nobody gave a key to must not already have one: ids are reused ("scene-1"), the old key entry is still in the settings.
        Assert.Null(session.Settings.PickKeyFor(KeybindEditor.SceneActionId(created.Id)));
    }

    [Fact]
    public void Holds_An_Unreadable_Scenes_File_Is_Never_Overwritten_And_Edits_Are_Refused()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("scenes.json"), "{ this is not json");
        var files = temp.Files();
        var session = new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), new PageStoreLoad(PageStore.Default, PageStoreStatus.Loaded, null),
            new PickStoreLoad(new PickStore([]), PickStoreStatus.Loaded, null), new PretendRegistrar(), () => [], () => false, scenes: SceneStore.Load(temp.File("scenes.json")));
        Assert.False(session.CreateScene("A").Ok);
        Assert.False(session.RenameScene("scene-1", "B").Ok);
        Assert.False(session.SetSceneThing("scene-1", Site(1), true).Ok);
        Assert.False(session.DeleteScene("scene-1").Ok);
        Assert.Equal("{ this is not json", File.ReadAllText(temp.File("scenes.json")));
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public void Holds_Without_A_Scenes_Path_Or_With_A_Folder_In_The_Way_Nothing_Changes_And_The_Answer_Says_So()
    {
        using var temp = new TempFolder();
        var s1 = Make.Session(temp, new PretendRegistrar());
        Directory.CreateDirectory(temp.File("scenes.json")); // a folder where the file should be
        var r1 = s1.CreateScene("A");
        Assert.False(r1.Ok);
        Assert.Contains("nothing was changed", r1.Refusal);
        Assert.Empty(s1.Scenes.Items);
    }

    [Fact]
    public void Holds_A_Scenes_File_With_Hidden_Names_Repeated_Names_Or_A_Newer_Version_Is_Unreadable_Not_Half_Read()
    {
        foreach (var json in new[]
                 {
                     "{\"version\":2,\"scenes\":[]}", "{\"scenes\":[{\"id\":\"a\",\"name\":\"x\\u200By\",\"things\":[]}]}", "{\"scenes\":[{\"id\":\"a\",\"name\":\"X\",\"things\":[]},{\"id\":\"b\",\"name\":\"x\",\"things\":[]}]}",
                     "{\"scenes\":[{\"id\":\"A!\",\"name\":\"X\",\"things\":[]}]}", "{\"scenes\":[{\"id\":\"a\",\"name\":\"X\",\"things\":[{\"id\":\"program:x\",\"kind\":\"program\",\"name\":\"X\",\"page\":\"scene\",\"exe\":\"C:\\\\x.exe\"}]}]}",
                     "[]", "null", "", "{\"scenes\":{}}",
                 })
            Assert.Equal(SceneStoreStatus.Unreadable, SceneStore.Parse(json).Status);
    }

    // ---- first start, fallback, mark, settings -------------------------------------------

    [Fact]
    public void Holds_First_Start_Runs_Only_For_A_Fresh_Normal_Launch()
    {
        foreach (var existed in new[] { false, true })
        foreach (var autostart in new[] { false, true })
        foreach (var self in new[] { false, true })
            Assert.Equal(!existed && !autostart && !self, FirstStart.ShouldRun(existed, autostart, self));
        Assert.Equal("Start", FirstStart.ButtonText(0));
        Assert.Equal("Done", FirstStart.ButtonText(FirstStart.Steps.Count - 1));
        Assert.Equal(7, FirstStart.Steps.Count); // five until WORK-ORDER-13 added the step Try it (Dan's tutorial), six until Dan's step Chrome (version 1.0.1)
    }

    [Fact]
    public void Holds_Notice_Fallback_Storm_Sound_Once_Never_Outside_Focus_And_Idle_After_Done()
    {
        var r = new Random(9);
        for (var round = 0; round < 3000; round++)
        {
            var state = NoticeWait.Arrive(DateTimeOffset.UnixEpoch);
            var sounds = 0;
            var mode = (Mode)r.Next(0, 3);
            for (var step = 0; step < 400; step++)
            {
                var now = DateTimeOffset.UnixEpoch.AddSeconds(step * 2.0 + (r.Next(5) == 0 ? -5 : 0));
                var facts = new NoticeFacts(r.Next(40) == 0 ? (Mode)r.Next(3, 9) : mode, r.Next(60) == 0 ? (FrontState)r.Next(4, 9) : (FrontState)r.Next(0, 4), r.Next(4) == 0, now);
                var next = NoticeFallback.Next(state, facts);
                if (next.Action == NoticeAction.PlaySound) { sounds++; Assert.Equal(Mode.Focus, facts.Mode); Assert.Equal(state.Phase, NoticePhase.Waiting); }
                if (facts.Mode == Mode.DND) Assert.True(next.Action is NoticeAction.Drop or NoticeAction.ShowHere or NoticeAction.None);
                if (next.Action is NoticeAction.ShowHere or NoticeAction.ShowOnOtherScreen or NoticeAction.Drop or NoticeAction.None) Assert.False(next.NeedsTimer);
                if (next.Action is NoticeAction.Wait or NoticeAction.PlaySound) Assert.True(next.NeedsTimer);
                if (next.Action == NoticeAction.ShowHere) Assert.Equal(ShowAnswer.Show, ShowDecision.Decide(facts.Front, Appearer.Notice, ShowOrigin.ByItself, facts.Mode));
                state = next.State;
                if (!state.NeedsTimer) break;
            }

            Assert.True(sounds <= 1);
        }
    }

    [Fact]
    public void Holds_Mode_Mark_Stays_In_Range_For_Any_Time_And_Blend_Never_Leaves_Its_Two_Ends()
    {
        foreach (var t in new[] { 0, 0.5, 1.1, 2.2, -7.3, 1e6, 1e15, 1e300, 1e307, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var b = ModeMark.Breath(t);
            Assert.InRange(b, ModeMark.BreathLow - 1e-9, 1 + 1e-9);
            foreach (var mode in new[] { Mode.Focus, Mode.Vibe, Mode.DND, (Mode)77 })
            foreach (var pill in new[] { false, true })
            {
                var look = ModeMark.LookOf(mode, t, pill);
                foreach (var v in new[] { look.Glow, look.MovingLight, look.PageRim, look.DashedRim }) Assert.InRange(v, 0.0, 1.0 + 1e-9);
            }
        }

        Assert.Equal(1, ModeMark.Breath(0), 12);
        Assert.Equal(ModeMark.Look.Approved, ModeMark.LookOf(Mode.Focus, 5));
        foreach (var ms in new[] { -1e9, -1, 0, 1, 175, 349.999, 350, 1e9, double.NaN, double.PositiveInfinity })
        {
            var look = ModeMark.Blend(ModeMark.LookOf(Mode.Focus, 0), ModeMark.LookOf(Mode.DND, 0), ms);
            foreach (var v in new[] { look.Glow, look.MovingLight, look.PageRim, look.DashedRim }) Assert.InRange(v, -1e-9, 1 + 1e-9);
        }
    }

    [Fact]
    public void Defect_Breath_Is_Not_A_Number_For_A_Time_Near_The_Top_Of_The_Range()
    {
        Assert.True(double.IsFinite(ModeMark.Breath(double.MaxValue)), "t / 2.2 * 2 * pi overflows to infinity and the cosine of infinity is NaN");
    }

    [Fact]
    public void Holds_New_Settings_Round_Trip_For_Every_Mode_Notice_Time_Pill_Never_Over_And_Mode_Key()
    {
        using var temp = new TempFolder();
        foreach (var mode in Enum.GetValues<Mode>())
        foreach (var seconds in new[] { 3.0, 6, 17, 30 })
        {
            var settings = Settings.Defaults with
            {
                Mode = mode, NoticeSeconds = seconds, ShowPill = seconds > 10, ModeKey = Pool[1],
                NeverOver = NeverOverList.From([new NeverOverEntry("Alpha", "alpha.exe"), new NeverOverEntry("Beta", "BETA.EXE"), new NeverOverEntry("Dup", "alpha.exe")]),
            };
            Assert.True(settings.Save(temp.File("s.json")));
            var back = Settings.Load(temp.File("s.json"));
            Assert.Equal(SettingsStatus.Loaded, back.Status);
            Assert.Equal(settings, back.Settings);
            Assert.Equal(2, back.Settings.NeverOver.Entries.Count);
        }
    }

    [Fact]
    public void Defect_Save_Writes_A_Notice_Time_Or_A_Mode_That_The_Next_Start_Calls_Unreadable()
    {
        using var temp = new TempFolder();
        var zero = Settings.Defaults with { NoticeSeconds = 0 };
        var saved = zero.Save(temp.File("zero.json"));
        Assert.False(saved && Settings.Load(temp.File("zero.json")).Status == SettingsStatus.Unreadable, "saved a notice time of 0 that reads back as unreadable");
        var odd = Settings.Defaults with { Mode = (Mode)9 };
        var savedOdd = odd.Save(temp.File("odd.json"));
        Assert.False(savedOdd && Settings.Load(temp.File("odd.json")).Status == SettingsStatus.Unreadable, "saved a mode that reads back as unreadable");
    }

    [Fact]
    public void Holds_Never_Over_List_Refuses_Paths_Hidden_Parts_And_Is_Immutable()
    {
        var list = NeverOverList.Empty;
        foreach (var bad in new[] { "a\\b.exe", "C:\\x.exe", "a/b.exe", "x.com", "x.exe ", " x.exe", "a\0.exe", "a*.exe", "", "   ", "a:b.exe" })
            Assert.Same(list, list.With(new NeverOverEntry("Name", bad)));
        var one = list.With(new NeverOverEntry("A", "game.exe"));
        Assert.Empty(list.Entries);
        Assert.True(one.Contains("GAME.EXE"));
        Assert.Equal(FrontState.ExclusiveFullscreen, one.Apply(FrontState.FullscreenProgram, "Game.exe"));
        Assert.Equal(FrontState.Presentation, one.Apply(FrontState.Presentation, "game.exe"));
        Assert.Equal(FrontState.Clear, one.Apply(FrontState.Clear, "game.exe"));
        Assert.False(one.Without("GAME.exe").Contains("game.exe"));
        Assert.Equal(FrontState.FullscreenProgram, one.Apply(FrontState.FullscreenProgram, null));
    }
}
