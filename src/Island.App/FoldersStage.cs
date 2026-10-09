using Island.Core;
using Island.Sources.Folders;

namespace Island.App;

/// <summary>
/// WORK-ORDER-3 section 6 checks, logic only, with simulated lists: a folder pick reads open or closed with a count
/// from the Explorer windows, and a click brings an open one forward or opens a closed one, through the outside door
/// (recorded here, nothing is opened). The live reading of File Explorer records only a count; whether the real
/// windows come forward is NO-VERIFIER here and a check for a person.
/// </summary>
internal sealed class FoldersStage(SelfTestReport report, TimeSpan hangLimit)
{
    public async Task RunAsync()
    {
        var pretend = new PretendWorld();
        var recording = new RecordingOutside();
        var world = AppWorld.Pretend(pretend, recording);
        var store = new PickStore([Pick.ForFolder("Downloads", PageIds.Folders), Pick.ForFolder("Documents", PageIds.Folders)]);
        var icon = new IconImage(1, 1, [255, 0, 0, 255]);
        pretend.Icons["Downloads"] = icon;
        var pages = new PickPages(() => store, world, synchronousIcons: true, plusTile: false);
        var folders = Pages.Get(PageIds.Folders);

        var closed = pages.ItemsOf(folders);
        report.Check("two folder picks read closed when no Explorer window is open", closed.Count == 2 && closed.All(i => i.IsClosed && i.Subtitle == "closed"), "simulated list");
        report.Check("a folder pick carries the folder's own icon when Windows gave one", closed[0].Icon is not null && closed[1].Icon is null, "Downloads has one, Documents has none (letters)");

        var closedClick = pages.Click(folders, 1);
        report.Check("a click on a closed folder opens it through the outside door", closedClick?.Kind == ClickKind.OpenFolder && recording.Calls.Contains("folder:Documents"), "recorded, nothing opened");

        pretend.FolderWindows = [new FolderWindow(501, "Downloads", "pretend", 0), new FolderWindow(501, "Downloads", "pretend", 0), new FolderWindow(502, "Downloads", "pretend", 1)];
        pretend.Raise();
        var open = pages.ItemsOf(folders);
        report.Check("with Explorer windows on Downloads the pick reads open with the number of windows", open[0] is { IsClosed: false, Count: 3 } && open[0].Subtitle == "3 windows" && open[1].IsClosed, open[0].Subtitle);

        var first = pages.Click(folders, 0);
        var second = pages.Click(folders, 0);
        report.Check("a click on an open folder brings an Explorer window forward and the next click goes to the next one",
            first is { Kind: ClickKind.BringForward } && second is { Kind: ClickKind.BringForward } && first.Target != second.Target
            && recording.Calls.Count(c => c.StartsWith("bring:", StringComparison.Ordinal)) == 2,
            string.Join(", ", recording.Calls.Where(c => c.StartsWith("bring:", StringComparison.Ordinal))));

        // The live reading: a count of Explorer entries, never a path.
        using var reader = new ExplorerWindowReader();
        reader.Start();
        reader.WaitForFirstRead(hangLimit);
        // WORK-ORDER-6 section 6: while the island is hidden the reader does not look at all; it looks again at once when the island is summoned.
        using var resting = new ExplorerWindowReader(TimeSpan.FromMilliseconds(150));
        resting.Start();
        resting.WaitForFirstRead(hangLimit);
        resting.SetQuiet(true);
        await Task.Delay(400);
        var restingAt = resting.PollCount;
        await Task.Delay(1200);
        report.Check("a quiet Explorer reader does not look while the island is hidden", resting.PollCount == restingAt, $"{resting.PollCount - restingAt} look(s) in 1.2 s");
        resting.SetQuiet(false);
        var looked = await Waiter.UntilAsync(() => resting.PollCount > restingAt, "the Explorer reader looking again when the island is summoned", hangLimit, report);
        report.Check("it looks again at once when the island is summoned", looked, "woken by the summon");

        report.Info["explorerEntries"] = reader.Windows.Count;
        report.Info["explorerEntriesWithKnownFolder"] = reader.Windows.Count(w => w.KnownFolder is not null);
        report.Info["foldersLiveCheck"] = "NO-VERIFIER: the self-test does not touch File Explorer; only a count is recorded";
    }
}
