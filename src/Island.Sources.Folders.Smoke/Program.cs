using System.Diagnostics;
using Island.Sources.Folders;

// Reads File Explorer once and prints COUNTS ONLY: no path, no name. No window is shown.
var clock = Stopwatch.StartNew();
using var reader = new ExplorerWindowReader();
reader.Start();
var done = reader.WaitForFirstRead(TimeSpan.FromSeconds(10));
clock.Stop();

var list = reader.Windows;
Console.WriteLine($"firstReadFinished: {done}");
Console.WriteLine($"explorerFrames: {list.Select(w => w.Handle).Distinct().Count()}");
Console.WriteLine($"entries: {list.Count}");
Console.WriteLine($"withKnownFolder: {list.Count(w => w.KnownFolder is not null)}");
Console.WriteLine($"firstReadMs: {clock.ElapsedMilliseconds}");
Console.WriteLine($"withPath: {list.Count(w => w.PathInMemory is not null)}");
Console.WriteLine($"withShellParsingName: {list.Count(w => w.PathInMemory?.StartsWith("::{", StringComparison.Ordinal) == true)}");

Console.WriteLine($"knownFolderPathsRead: {KnownFolderPaths.Read().Count(p => !p.Path.StartsWith("::{", StringComparison.Ordinal))} of 6");

// Raw look (internal), counts only: does the shell give every tab its own window handle?
var sta = new Thread(() =>
{
    var raw = ExplorerRawRead.Read();
    Console.WriteLine($"rawEntries: {raw?.Entries.Count ?? -1}");
    Console.WriteLine($"rawEntriesWithTabHandle: {raw?.Entries.Count(e => e.TabHandle != 0) ?? -1}");
    Console.WriteLine($"rawFramesWithTabWindows: {raw?.TabsByFrame.Count(f => f.Value.Count > 0) ?? -1}");
    Console.WriteLine($"rawTabWindows: {raw?.TabsByFrame.Sum(f => f.Value.Count) ?? -1}");
});
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();
return done ? 0 : 1;
