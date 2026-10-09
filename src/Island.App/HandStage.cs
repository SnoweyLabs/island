using System.IO;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.App;

/// <summary>
/// WORK-ORDER-10 §3, through the code the settings screen calls, with a temporary picks file, a pretend chooser that answers at once (no real choosing window is ever opened here)
/// and invented things inside the self-test's own temporary folder: a folder is added and appears on the page; a file is added and a click on it asks the gate to open it, which the
/// gate refuses and counts; an address typed with a page after the site's name becomes one site pick; the same thing twice says where it is; a thing that was deleted turns grey,
/// says "not found" and a click shows PICK_TARGET_MISSING. The picks file holds a place only in its one field and never the account's name. Nothing here is logged or snapshotted
/// with a path.
/// </summary>
internal sealed class HandStage(SelfTestReport report, string tempRoot)
{
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

    public Task RunAsync()
    {
        var dir = Path.Combine(tempRoot, "hand-stage");
        Directory.CreateDirectory(dir);
        var files = new SettingsFiles(Path.Combine(dir, "settings.json"), Path.Combine(dir, "pages.json"), Path.Combine(dir, "picks.json"), Path.Combine(dir, "scenes.json"));
        Settings.Defaults.Save(files.SettingsPath);
        var inventedFolder = Path.Combine(dir, "Invented Folder");
        var inventedFile = Path.Combine(dir, "invented note.txt");
        Directory.CreateDirectory(inventedFolder);
        File.WriteAllText(inventedFile, "invented");

        var installed = new[] { new InstalledProgram("Alpha Tool", "alpha.exe", null, "launch-alpha"), new InstalledProgram("Beta Tool", "beta.exe", null, "launch-beta") };
        var chooser = new PretendChooser(file: inventedFile, folder: inventedFolder);
        var session = new SettingsSession(files, new SettingsLoad(Settings.Defaults, SettingsStatus.Loaded, null), PageStore.Load(files.PagesPath), new PickStoreLoad(PickStore.Empty, PickStoreStatus.Loaded, null),
            new AcceptAll(), () => installed, () => false)
        {
            Chooser = chooser,
            HandContextSource = () => new HandContext(PickPages.ProfileFolder, []),
        };

        // A folder, added and shown on the page.
        var folder = session.AddFolder(PageIds.Apps);
        report.Check("a folder chosen through the pretend chooser is added to the page", folder.Added && folder.Pick is { Kind: PickKind.Folder }, folder.Message ?? "added");

        // A file, added; a click on it asks the gate to open the file; the gate refuses and counts.
        var file = session.AddFile(PageIds.Apps);
        report.Check("a file chosen through the pretend chooser is added", file.Added && file.Pick is { Kind: PickKind.File }, file.Message ?? "added");

        var pretend = new PretendWorld();
        var world = AppWorld.Pretend(pretend, new OutsideSelfTestActions());
        var pages = new PickPages(() => session.Picks, world, synchronousIcons: true, plusTile: false);
        string? refusedCode = null;
        pages.Refused += r => refusedCode = r.Code;
        var apps = Pages.Get(PageIds.Apps);
        var items = pages.ItemsOf(apps);
        var folderItem = items.FirstOrDefault(i => i.PickId == folder.Pick?.Id);
        var fileItem = items.FirstOrDefault(i => i.PickId == file.Pick?.Id);
        report.Check("both appear on the page, the file always bright and reading \"file\"", folderItem is not null && fileItem is { IsClosed: false, Subtitle: "file" }, $"{items.Count} tiles");

        var openRefusedBefore = OutsideGate.Current.Refused(OutsideKind.OpenFile);
        var plan = fileItem is null ? null : pages.Click(apps, items.ToList().IndexOf(fileItem));
        report.Check("a click on the file asks the gate to open it, which refuses and counts it (nothing is opened)",
            plan?.Kind == ClickKind.OpenFile && OutsideGate.Current.Refused(OutsideKind.OpenFile) == openRefusedBefore + 1, $"plan {plan?.Kind}");

        // The picks file: the place only in its one field, written with the profile folder unexpanded, and the account's name nowhere.
        var saved = File.ReadAllText(files.PicksPath);
        report.Check("the picks file holds the places only in the field made for them, and never the account's name",
            saved.Contains("\"location\"", StringComparison.Ordinal) && !saved.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) && !saved.Contains(PickPages.ProfileFolder, StringComparison.OrdinalIgnoreCase),
            "a place under the profile folder is written with %USERPROFILE%");

        // A typed address with a page after the site's name is one site pick, whose name is the host.
        var site = session.AddSite(PageIds.Media, "www.Example.org/watch/something?x=1");
        report.Check("an address typed with a page after the site's name becomes one site pick, kept by its host only",
            site.Added && site.Pick is { Kind: PickKind.Site, Host: "example.org" } && !File.ReadAllText(files.PicksPath).Contains("watch", StringComparison.Ordinal), site.Pick?.Host ?? site.Message ?? "");
        var nonsense = session.AddSite(PageIds.Media, "not a site at all");
        report.Check("something that is not a site's name is refused with a reason and nothing is added", !nonsense.Added && nonsense.Message is not null && session.Picks.Picks.Count(p => p.Kind == PickKind.Site) == 1, "NOT_A_SITE");

        // A program from the list: no path.
        var program = session.AddProgramFromList(PageIds.Apps, installed[0]);
        report.Check("a program chosen from the list is added and stores no path", program.Added && program.Pick is { Location: null, ExeName: "alpha.exe" }, program.Message ?? "added");

        // The same thing twice says where it is.
        var again = session.AddFolder(PageIds.Media);
        report.Check("the same folder put on the island twice is refused and the answer says where it already is", !again.Added && again.AlreadyOn is not null, again.Message ?? "");

        // A folder that was deleted: grey, "not found", and a click shows PICK_TARGET_MISSING; nothing is removed by itself.
        Directory.Delete(inventedFolder);
        var changed = pages.ProbeNow(new PickTargetProbe());
        pages.Invalidate();
        var after = pages.ItemsOf(apps);
        var gone = after.FirstOrDefault(i => i.PickId == folder.Pick?.Id);
        report.Check("a folder that was deleted turns grey and says \"not found\"", changed && gone is { IsClosed: true, Subtitle: "not found" }, gone?.Subtitle ?? "no tile");
        var clickPlan = gone is null ? null : pages.Click(apps, after.ToList().IndexOf(gone));
        report.Check("a click on it answers PICK_TARGET_MISSING and removes nothing", clickPlan?.Kind == ClickKind.TargetMissing && refusedCode == "PICK_TARGET_MISSING" && session.Picks.ById(folder.Pick!.Id) is not null, refusedCode ?? "no refusal");

        report.Check("the chooser that answered was the pretend one, asked only for what the checks asked", chooser.Asked == 3, $"{chooser.Asked} questions");
        return Task.CompletedTask;
    }
}
