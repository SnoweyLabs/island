using System.Diagnostics;
using Island.Core;
using Island.Sources.Programs;

// Reads this machine with the real readers and prints counts and yes/no answers only: never a title, a program
// name, a path or a site. The outside gate is in self-test mode, so nothing can be started or brought forward.

var gate = new OutsideGate(selfTest: true);
OutsideGate.Current = gate;

void Say(string label, object value) => Console.WriteLine($"{label}: {value}");

// WORK-ORDER-11 section 2, the probe: does a hidden console window (PseudoConsoleWindow) or a classic console window (ConsoleWindowClass) belong to this very
// process or to one of its ancestors, and does it lead to a terminal window the island lists? Starts nothing and attaches to nothing: it only looks at the windows
// that are already there. Prints, and writes to review/probe-console.md, only yes-or-no answers, one count of steps and the date.
if (args.Length > 0 && args[0] == "probe-console")
{
    var probe = new TerminalProbe().Read();
    var parentOf = probe.Processes.ToDictionary(p => p.Id, p => p.ParentId);
    var chain = new List<int> { Environment.ProcessId };
    while (parentOf.TryGetValue(chain[^1], out var up) && up > 0 && !chain.Contains(up) && chain.Count < 32) chain.Add(up);

    var own = probe.ConsoleWindows.Where(w => chain.Contains(w.OwnerProcessId)).OrderBy(w => chain.IndexOf(w.OwnerProcessId)).ToList();
    var found = own.Count > 0;
    var steps = found ? chain.IndexOf(own[0].OwnerProcessId) : -1;
    using var probeLister = new WindowLister();
    probeLister.WaitForFirstRead(TimeSpan.FromSeconds(30));
    var listed = probeLister.Windows;
    var tables = Island.Core.Terminals.TerminalTables.Default;
    var target = found ? (own[0].ClassName == "ConsoleWindowClass" ? own[0].Handle : own[0].OwnerWindowHandle) : 0;
    var listedWindow = listed.FirstOrDefault(w => w.Handle == target);
    var leadsToListed = found && listedWindow is not null;
    var isTerminal = leadsToListed && Island.Core.Terminals.TerminalClassify.RoleOf(
        new Island.Core.Terminals.TermWindowFact(listedWindow!.Handle, listedWindow.OwnerProcessId, listedWindow.ExeName, listedWindow.PackageFamily, listedWindow.ClassName ?? string.Empty, listedWindow.Title, listedWindow.ZOrder), tables)
        == Island.Core.Terminals.WindowRole.Terminal;
    var pseudoCount = probe.ConsoleWindows.Count(w => w.ClassName == "PseudoConsoleWindow");
    var classicCount = probe.ConsoleWindows.Count(w => w.ClassName == "ConsoleWindowClass");

    Say("consoleWindowOfThisProcessOrAnAncestorFound", found);
    Say("itsWindowIsOneTheIslandLists", leadsToListed);
    Say("thatWindowCountsAsATerminal", isTerminal);
    Say("stepsUpTheChainToTheOwningProcess", steps);
    var text = new System.Text.StringBuilder();
    text.AppendLine("# Probe: console windows (WORK-ORDER-11 section 2)");
    text.AppendLine();
    text.AppendLine($"Run on {DateTime.Now:yyyy-MM-dd}, from the shell this session was started in (`Island.Sources.Programs.Smoke probe-console`). It started nothing and attached to nothing. Only yes-or-no answers and counts are written: no title, no program name, no process id.");
    text.AppendLine();
    text.AppendLine($"- a console window (class PseudoConsoleWindow or ConsoleWindowClass) owned by this process or one of its ancestors was found: **{(found ? "yes" : "no")}**");
    text.AppendLine($"- that window's owner (for a ConsoleWindowClass window: itself) is a window the island lists: **{(leadsToListed ? "yes" : "no")}**");
    text.AppendLine($"- that window counts as a terminal by section 1: **{(isTerminal ? "yes" : "no")}**");
    text.AppendLine($"- steps up the chain from this process to the owning process: **{(found ? steps.ToString() : "none")}**");
    text.AppendLine($"- for the record: {pseudoCount} PseudoConsoleWindow and {classicCount} ConsoleWindowClass windows exist on the computer now; the chain of this process is {chain.Count} processes long.");
    text.AppendLine();
    text.AppendLine(found && leadsToListed && isTerminal
        ? "**The fact holds here**: a hidden console window of a process in this chain leads to a terminal window the island lists."
        : "**UNVERIFIED**: the fact (a helper's console window leads to its terminal's window) was not shown on this laptop by this run. The rule falls back by itself (the window whose title holds the project's name, else the one used last); the page is built all the same.");
    var review = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "review");
    var path = Directory.Exists(review) ? Path.GetFullPath(Path.Combine(review, "probe-console.md")) : Path.Combine(Environment.CurrentDirectory, "probe-console.md");
    File.WriteAllText(path, text.ToString());
    Say("written", "review/probe-console.md");
    return;
}

var clock = Stopwatch.StartNew();
using var lister = new WindowLister();
var listerReady = lister.WaitForFirstRead(TimeSpan.FromSeconds(30));
var windowsMs = clock.ElapsedMilliseconds;

clock.Restart();
var catalog = new InstalledProgramCatalog();
var catalogReady = catalog.WaitForLoad(TimeSpan.FromSeconds(60));
var catalogMs = clock.ElapsedMilliseconds;
var installed = catalog.Installed;

Say("windowsListerFirstReadDone", listerReady);
Say("windowsListerFirstReadMs", windowsMs);
Say("windowEventHooksInstalled", lister.HooksInstalled);
Say("windowsListed", lister.Windows.Count);
Say("windowsWithExeName", lister.Windows.Count(w => w.ExeName is not null));
Say("windowsWithPackageFamily", lister.Windows.Count(w => w.PackageFamily is not null));
Say("windowsZOrderIsSequence", lister.Windows.Select(w => w.ZOrder).SequenceEqual(Enumerable.Range(0, lister.Windows.Count)));

Say("catalogReadDone", catalogReady);
Say("catalogReadMs", catalogMs);
Say("installedPrograms", installed.Count);
Say("installedWithExeName", installed.Count(p => p.ExeName is not null));
Say("installedPackaged", installed.Count(p => p.PackageFamily is not null));
Say("starterProgramsInList", StarterPicks.Programs.Count);
Say("starterProgramsFound", StarterPicks.CountFound(installed));
Say("starterPicksBuilt", StarterPicks.Build(installed).Count(p => p.Kind == PickKind.Program));

using var icons = new ProgramIcons(catalog);
var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
var explorerIcon = icons.FileIcon(explorer);
Say("explorerIconNonEmpty", explorerIcon is not null && IconChoice.IsUsable(explorerIcon));
Say("explorerIconNotGeneric", explorerIcon is not null && !icons.IsGenericFileIcon(explorerIcon));
Say("explorerIconLargestSize", explorerIcon?.Width ?? 0);
Say("explorerIconAlphaLooksPremultiplied", explorerIcon is not null && IsPremultiplied(explorerIcon));

// The check on the check: a file with a made-up extension must be recognised as generic.
var made = Path.Combine(Path.GetTempPath(), "island-smoke." + "zzqq9");
File.WriteAllBytes(made, []);
try
{
    var generic = icons.FileIcon(made);
    Say("madeUpExtensionIconRead", generic is not null);
    Say("madeUpExtensionIconIsGeneric", generic is not null && icons.IsGenericFileIcon(generic));
}
finally
{
    File.Delete(made);
}

Say("folderIconsRead", Pick.KnownFolders.Count(f => icons.FolderIcon(f) is not null));
Say("folderIconLargestSize", icons.FolderIcon("Downloads")?.Width ?? 0);

var starterIcons = StarterPicks.Programs
    .Select(s => StarterPicks.Find(s, installed))
    .OfType<InstalledProgram>()
    .Select(p => icons.ProgramIcon(p.ExeName, p.PackageFamily))
    .ToList();
Say("starterIconsRead", starterIcons.Count(i => i is not null));
Say("starterIconsGeneric", starterIcons.Count(i => i is not null && icons.IsGenericFileIcon(i)));
Say("starterIconsLargestSize", starterIcons.Max(i => i?.Width ?? 0));

var unfound = StarterPicks.Programs.Where(s => StarterPicks.Find(s, installed) is null).ToList();
Say("starterProgramsNotFound", unfound.Count);
Say("notFoundButNameContainsHint", unfound.Count(s => installed.Any(p => s.NameHints.Any(h => p.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))));
Say("notFoundButExeStemContainsCandidate", unfound.Count(s => installed.Any(p => p.ExeName is not null && s.ExeCandidates.Any(c => p.ExeName.Contains(Path.GetFileNameWithoutExtension(c), StringComparison.OrdinalIgnoreCase)))));

Say("installedWithUpdaterExeName", installed.Count(p => p.ExeName is not null && (p.ExeName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) || p.ExeName.Contains("unins", StringComparison.OrdinalIgnoreCase) || p.ExeName.Contains("setup", StringComparison.OrdinalIgnoreCase) || p.ExeName.Contains("launcher", StringComparison.OrdinalIgnoreCase))));
Say("starterFoundButExeIsNotACandidate", StarterPicks.Programs.Count(s => StarterPicks.Find(s, installed) is { ExeName: not null } f && !s.ExeCandidates.Contains(f.ExeName, StringComparer.OrdinalIgnoreCase)));
Say("runningStarterExeButNotFound", unfound.Count(s => lister.Windows.Any(w => s.ExeCandidates.Contains(w.ExeName ?? "?", StringComparer.OrdinalIgnoreCase))));

var sample = installed.Where(p => p.ExeName is not null || p.PackageFamily is not null).Take(60).Select(p => icons.ProgramIcon(p.ExeName, p.PackageFamily)).ToList();
Say("sampleOfInstalledChecked", sample.Count);
Say("sampleIconsRead", sample.Count(i => i is not null));
Say("sampleIconsGeneric", sample.Count(i => i is not null && icons.IsGenericFileIcon(i)));

var actions = new OutsideActions(catalog);
var pick = Pick.ForProgram("Alpha", PageIds.Apps, "alpha.exe", null);
var aWindow = lister.Windows.FirstOrDefault();
Say("startProgramReturned", actions.StartProgram(pick));
Say("openFolderReturned", actions.OpenFolder("Downloads"));
Say("openSiteReturned", actions.OpenSite("example.org"));
Say("bringForwardReturned", aWindow is not null && actions.BringForward(aWindow.Handle));
Say("gateAllowed", gate.AllowedCount);
Say("gateRefusedTotal", gate.RefusedTotal);
Say("gateRefusedStart", gate.Refused(OutsideKind.StartProgram));
Say("gateRefusedFolder", gate.Refused(OutsideKind.OpenFolder));
Say("gateRefusedAddress", gate.Refused(OutsideKind.OpenAddress));
Say("gateRefusedBringForward", gate.Refused(OutsideKind.BringForward));

static bool IsPremultiplied(IconImage icon)
{
    // Premultiplied pixels never have a colour channel larger than their alpha; only half-see-through pixels can tell.
    for (var i = 0; i + 3 < icon.Bgra.Length; i += 4)
    {
        var a = icon.Bgra[i + 3];
        if (a is > 0 and < 255 && (icon.Bgra[i] > a || icon.Bgra[i + 1] > a || icon.Bgra[i + 2] > a)) return false;
    }
    return true;
}
